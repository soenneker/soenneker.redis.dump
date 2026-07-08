using Microsoft.Extensions.Logging;
using Soenneker.Enums.JsonOptions;
using Soenneker.Extensions.String;
using Soenneker.Extensions.Task;
using Soenneker.Extensions.ValueTask;
using Soenneker.Redis.Client.Abstract;
using Soenneker.Redis.Dump.Abstract;
using Soenneker.Redis.Dump.Models;
using Soenneker.Utils.Json;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.Redis.Dump;

/// <inheritdoc cref="IRedisDumpUtil"/>
public sealed class RedisDumpUtil : IRedisDumpUtil
{
    private const int _diskCloneVersion = 1;
    private const int _keyScanPageSize = 1000;
    private const int _diskImportBatchSize = 1000;

    private readonly ILogger<RedisDumpUtil> _logger;
    private readonly IRedisClient _redisClient;

    public RedisDumpUtil(ILogger<RedisDumpUtil> logger, IRedisClient redisClient)
    {
        _logger = logger;
        _redisClient = redisClient;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ValueTask<ConnectionMultiplexer> GetConnection(string connectionString, CancellationToken ct) =>
        _redisClient.Get(connectionString, ct);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ValueTask<IDatabase> GetDb(string connectionString, CancellationToken ct)
    {
        ValueTask<ConnectionMultiplexer> vt = GetConnection(connectionString, ct);

        if (vt.IsCompletedSuccessfully)
            return new ValueTask<IDatabase>(vt.Result.GetDatabase());

        return AwaitSlow(vt);

        static async ValueTask<IDatabase> AwaitSlow(ValueTask<ConnectionMultiplexer> vt) =>
            (await vt.NoSync()).GetDatabase();
    }

    public async ValueTask<int> CloneToDisk(string filePath, string connectionString, CancellationToken cancellationToken = default)
    {
        if (connectionString.IsNullOrEmpty())
        {
            _logger.LogError(">> REDIS: Skipping CloneToDisk because the connection string is null or empty");
            return 0;
        }

        if (filePath.IsNullOrEmpty())
        {
            _logger.LogError(">> REDIS: Skipping CloneToDisk because the file path is null or empty");
            return 0;
        }

        try
        {
            _logger.LogInformation(">> REDIS: Starting disk clone to {filePath}", filePath);

            ConnectionMultiplexer connection = await GetConnection(connectionString, cancellationToken).NoSync();
            IDatabase db = connection.GetDatabase();
            List<IServer> servers = GetWritableServers(connection);

            _logger.LogInformation(">> REDIS: Scanning Redis database {database} across {serverCount} endpoint(s) for disk clone", db.Database,
                servers.Count);

            var keyValues = new Dictionary<string, RedisDiskCloneEntry>(StringComparer.Ordinal);
            var seenKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (IServer server in servers)
            {
                IAsyncEnumerable<RedisKey> keys = server.KeysAsync(database: db.Database, pageSize: _keyScanPageSize);
                var pageKeys = new List<RedisKey>(_keyScanPageSize);
                var pageNumber = 0;
                var scannedKeys = 0;
                long? currentCursor = null;
                IScanningCursor? scanningCursor = keys as IScanningCursor;

                await foreach (RedisKey redisKey in keys.ConfigureAwait(false)
                                                        .WithCancellation(cancellationToken))
                {
                    try
                    {
                        if (scanningCursor is not null && currentCursor != scanningCursor.Cursor)
                        {
                            await CloneKeyPage(db, pageKeys, keyValues, seenKeys, server.EndPoint, pageNumber, cancellationToken).NoSync();
                            pageKeys.Clear();

                            currentCursor = scanningCursor.Cursor;
                            pageNumber++;

                            _logger.LogInformation(
                                ">> REDIS: Reading disk clone key page {pageNumber} from endpoint {endpoint}. Cursor: {cursor}. Page size: {pageSize}. Keys scanned so far on endpoint: {scannedKeys}",
                                pageNumber, server.EndPoint, scanningCursor.Cursor, scanningCursor.PageSize, scannedKeys);
                        }
                        else if (scanningCursor is null && scannedKeys % _keyScanPageSize == 0)
                        {
                            await CloneKeyPage(db, pageKeys, keyValues, seenKeys, server.EndPoint, pageNumber, cancellationToken).NoSync();
                            pageKeys.Clear();

                            pageNumber++;

                            _logger.LogInformation(
                                ">> REDIS: Reading disk clone key page {pageNumber} from endpoint {endpoint}. Page size: {pageSize}. Keys scanned so far on endpoint: {scannedKeys}",
                                pageNumber, server.EndPoint, _keyScanPageSize, scannedKeys);
                        }

                        scannedKeys++;
                        pageKeys.Add(redisKey);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, ">> REDIS: Error cloning key to disk: {key}", redisKey.ToString());
                    }
                }

                await CloneKeyPage(db, pageKeys, keyValues, seenKeys, server.EndPoint, pageNumber, cancellationToken).NoSync();
            }

            if (keyValues.Count == 0)
                _logger.LogWarning(">> REDIS: Disk clone found no keys to write to {filePath}", filePath);

            var clone = new RedisDiskClone
            {
                Version = _diskCloneVersion,
                KeyValues = keyValues
            };

            string? directory = Path.GetDirectoryName(filePath);

            if (!directory.IsNullOrEmpty())
                Directory.CreateDirectory(directory);

            await JsonUtil.SerializeToFile(clone, filePath, JsonOptionType.Pretty, cancellationToken: cancellationToken).NoSync();

            _logger.LogInformation(">> REDIS: Completed disk clone to {filePath}. Keys cloned: {count}", filePath, keyValues.Count);

            return keyValues.Count;
        }
        catch (Exception e)
        {
            _logger.LogError(e, ">> REDIS: Error cloning keys to disk: {filePath}", filePath);
            return 0;
        }
    }

    private async ValueTask<int> CloneKeyPage(IDatabase db, IReadOnlyList<RedisKey> redisKeys, Dictionary<string, RedisDiskCloneEntry> keyValues,
        HashSet<string> seenKeys, EndPoint endpoint, int pageNumber, CancellationToken cancellationToken)
    {
        if (redisKeys.Count == 0)
            return 0;

        var pendingEntries = new List<RedisDiskClonePendingEntry>(redisKeys.Count);

        foreach (RedisKey redisKey in redisKeys)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string redisKeyStr = redisKey.ToString();

            if (!seenKeys.Add(redisKeyStr))
                continue;

            pendingEntries.Add(new RedisDiskClonePendingEntry(redisKeyStr, db.KeyTimeToLiveAsync(redisKey), db.KeyDumpAsync(redisKey)));
        }

        if (pendingEntries.Count == 0)
            return 0;

        var clonedCount = 0;

        foreach (RedisDiskClonePendingEntry pendingEntry in pendingEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await Task.WhenAll(pendingEntry.TimeToLiveTask, pendingEntry.ValueTask).WaitAsync(cancellationToken).NoSync();

                TimeSpan? ttl = pendingEntry.TimeToLiveTask.Result;
                long? ttlMilliseconds = null;

                if (ttl is { } timeToLive)
                {
                    if (timeToLive <= TimeSpan.Zero)
                        continue;

                    ttlMilliseconds = (long)Math.Ceiling(timeToLive.TotalMilliseconds);
                }

                byte[]? value = pendingEntry.ValueTask.Result;

                if (value is null || value.Length == 0)
                    continue;

                keyValues[pendingEntry.RedisKey] = new RedisDiskCloneEntry
                {
                    Value = Convert.ToBase64String(value),
                    TimeToLiveMilliseconds = ttlMilliseconds
                };

                clonedCount++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                _logger.LogError(e, ">> REDIS: Error cloning key to disk: {key}", pendingEntry.RedisKey);
            }
        }

        _logger.LogInformation(
            ">> REDIS: Completed disk clone key page {pageNumber} from endpoint {endpoint}. Keys queued: {queuedCount}. Keys cloned: {clonedCount}. Total cloned: {totalCloned}",
            pageNumber, endpoint, pendingEntries.Count, clonedCount, keyValues.Count);

        return clonedCount;
    }

    public async ValueTask<int> ImportFromDisk(string filePath, string connectionString, CancellationToken cancellationToken = default)
    {
        if (connectionString.IsNullOrEmpty())
        {
            _logger.LogError(">> REDIS: Skipping ImportFromDisk because the connection string is null or empty");
            return 0;
        }

        if (filePath.IsNullOrEmpty())
        {
            _logger.LogError(">> REDIS: Skipping ImportFromDisk because the file path is null or empty");
            return 0;
        }

        if (!File.Exists(filePath))
        {
            _logger.LogError(">> REDIS: Skipping ImportFromDisk because the file does not exist: {filePath}", filePath);
            return 0;
        }

        try
        {
            var fileInfo = new FileInfo(filePath);
            _logger.LogInformation(">> REDIS: Starting disk import from {filePath}. File size: {bytes} bytes", filePath, fileInfo.Length);

            RedisDiskClone? clone = await JsonUtil.DeserializeFromFile<RedisDiskClone>(filePath, _logger, cancellationToken).NoSync();

            if (clone is null)
            {
                _logger.LogWarning(">> REDIS: Disk import file could not be deserialized: {filePath}", filePath);
                return 0;
            }

            if (clone.KeyValues is null || clone.KeyValues.Count == 0)
            {
                _logger.LogWarning(">> REDIS: Disk import file contains no keys: {filePath}", filePath);
                return 0;
            }

            if (clone.Version != _diskCloneVersion)
                _logger.LogWarning(">> REDIS: Importing Redis disk clone version {version}; expected {expectedVersion}", clone.Version,
                    _diskCloneVersion);

            _logger.LogInformation(">> REDIS: Restoring {count} keys from disk clone version {version}", clone.KeyValues.Count, clone.Version);

            IDatabase db = await GetDb(connectionString, cancellationToken).NoSync();
            var count = 0;
            var failureCount = 0;
            var batchNumber = 0;
            int totalBatches = (clone.KeyValues.Count + _diskImportBatchSize - 1) / _diskImportBatchSize;
            var batch = new List<KeyValuePair<string, RedisDiskCloneEntry>>(_diskImportBatchSize);

            foreach (KeyValuePair<string, RedisDiskCloneEntry> keyValue in clone.KeyValues)
            {
                cancellationToken.ThrowIfCancellationRequested();

                batch.Add(keyValue);

                if (batch.Count < _diskImportBatchSize)
                    continue;

                batchNumber++;
                (int imported, int failed) = await ImportKeyBatch(db, batch, batchNumber, totalBatches, cancellationToken).NoSync();
                count += imported;
                failureCount += failed;
                batch.Clear();
            }

            if (batch.Count > 0)
            {
                batchNumber++;
                (int imported, int failed) = await ImportKeyBatch(db, batch, batchNumber, totalBatches, cancellationToken).NoSync();
                count += imported;
                failureCount += failed;
            }

            if (failureCount > 0)
                _logger.LogWarning(">> REDIS: Completed disk import from {filePath}. Keys imported: {count}. Failures: {failureCount}", filePath,
                    count, failureCount);
            else
                _logger.LogInformation(">> REDIS: Completed disk import from {filePath}. Keys imported: {count}", filePath, count);

            return count;
        }
        catch (Exception e)
        {
            _logger.LogError(e, ">> REDIS: Error importing keys from disk: {filePath}", filePath);
            return 0;
        }
    }

    private async ValueTask<(int importedCount, int failureCount)> ImportKeyBatch(IDatabase db,
        IReadOnlyList<KeyValuePair<string, RedisDiskCloneEntry>> keyValues, int batchNumber, int totalBatches, CancellationToken cancellationToken)
    {
        if (keyValues.Count == 0)
            return (0, 0);

        var pendingEntries = new List<RedisDiskImportPendingEntry>(keyValues.Count);
        var failureCount = 0;

        foreach (KeyValuePair<string, RedisDiskCloneEntry> keyValue in keyValues)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (keyValue.Key.IsNullOrEmpty())
                    continue;

                string value = keyValue.Value.Value;

                if (value.IsNullOrEmpty())
                    continue;

                byte[] dumpedValue = Convert.FromBase64String(value);
                TimeSpan? ttl = null;

                if (keyValue.Value.TimeToLiveMilliseconds is > 0)
                    ttl = TimeSpan.FromMilliseconds(keyValue.Value.TimeToLiveMilliseconds.Value);

                pendingEntries.Add(new RedisDiskImportPendingEntry(keyValue.Key, dumpedValue, ttl));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                failureCount++;
                _logger.LogError(e, ">> REDIS: Error preparing key for disk import: {key}", keyValue.Key);
            }
        }

        if (pendingEntries.Count == 0)
            return (0, failureCount);

        _logger.LogInformation(">> REDIS: Starting disk import batch {batchNumber}/{totalBatches}. Keys queued: {queuedCount}", batchNumber,
            totalBatches, pendingEntries.Count);

        foreach (RedisDiskImportPendingEntry pendingEntry in pendingEntries)
            pendingEntry.DeleteTask = db.KeyDeleteAsync(pendingEntry.RedisKey);

        var deleteFailureCount = 0;

        foreach (RedisDiskImportPendingEntry pendingEntry in pendingEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                _ = await pendingEntry.DeleteTask!.WaitAsync(cancellationToken).NoSync();
                pendingEntry.DeleteSucceeded = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                deleteFailureCount++;
                _logger.LogError(e, ">> REDIS: Error deleting key before disk import restore: {key}", pendingEntry.RedisKey);
            }
        }

        foreach (RedisDiskImportPendingEntry pendingEntry in pendingEntries)
        {
            if (pendingEntry.DeleteSucceeded)
                pendingEntry.RestoreTask = db.KeyRestoreAsync(pendingEntry.RedisKey, pendingEntry.Value, pendingEntry.TimeToLive);
        }

        var importedCount = 0;
        var restoreFailureCount = 0;

        foreach (RedisDiskImportPendingEntry pendingEntry in pendingEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!pendingEntry.DeleteSucceeded)
                continue;

            try
            {
                await pendingEntry.RestoreTask!.WaitAsync(cancellationToken).NoSync();
                importedCount++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                restoreFailureCount++;
                _logger.LogError(e, ">> REDIS: Error restoring key from disk import: {key}", pendingEntry.RedisKey);
            }
        }

        failureCount += deleteFailureCount + restoreFailureCount;

        if (failureCount > 0)
        {
            _logger.LogWarning(
                ">> REDIS: Completed disk import batch {batchNumber}/{totalBatches}. Keys queued: {queuedCount}. Keys imported: {importedCount}. Failures: {failureCount}",
                batchNumber, totalBatches, pendingEntries.Count, importedCount, failureCount);
        }
        else
        {
            _logger.LogInformation(
                ">> REDIS: Completed disk import batch {batchNumber}/{totalBatches}. Keys queued: {queuedCount}. Keys imported: {importedCount}",
                batchNumber, totalBatches, pendingEntries.Count, importedCount);
        }

        return (importedCount, failureCount);
    }

    private List<IServer> GetWritableServers(ConnectionMultiplexer connection)
    {
        EndPoint[] endpoints = connection.GetEndPoints();

        if (endpoints.Length == 0)
            throw new InvalidOperationException("Redis ConnectionMultiplexer returned no endpoints.");

        var servers = new List<IServer>(endpoints.Length);

        foreach (EndPoint endpoint in endpoints)
        {
            try
            {
                IServer server = connection.GetServer(endpoint);

                if (!server.IsConnected || server.IsReplica)
                    continue;

                servers.Add(server);
            }
            catch (Exception e)
            {
                _logger.LogDebug(e, ">> REDIS: Unable to inspect Redis endpoint: {endpoint}", endpoint);
            }
        }

        if (servers.Count > 0)
            return servers;

        _logger.LogWarning(">> REDIS: No writable Redis endpoints were identified; falling back to the first connected endpoint for disk clone");

        foreach (EndPoint endpoint in endpoints)
        {
            IServer server = connection.GetServer(endpoint);

            if (server.IsConnected)
            {
                servers.Add(server);
                break;
            }
        }

        if (servers.Count == 0)
            throw new InvalidOperationException("Redis ConnectionMultiplexer returned no connected endpoints.");

        return servers;
    }

}
