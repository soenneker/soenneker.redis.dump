using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Soenneker.Enums.JsonOptions;
using Soenneker.Redis.Client.Abstract;
using Soenneker.Redis.Dump.Abstract;
using Soenneker.Tests.HostedUnit;
using Soenneker.Utils.Json;
using StackExchange.Redis;

namespace Soenneker.Redis.Dump.Tests;

[ClassDataSource<Host>(Shared = SharedType.PerTestSession)]
public sealed class RedisDumpUtilTests : HostedUnitTest
{
    private readonly IRedisDumpUtil _util;
    private readonly IRedisClient _redisClient;
    private readonly string _connectionString;

    public RedisDumpUtilTests(Host host) : base(host)
    {
        _util = Resolve<IRedisDumpUtil>(true);
        _redisClient = Resolve<IRedisClient>(true);

        IConfiguration config = Resolve<IConfiguration>(true);
        _connectionString = config.GetValue<string>("Azure:Redis:ConnectionString")!;
    }

    [Test]
    public async Task CloneToDisk_should_write_redis_keys_to_file()
    {
        var cancellationToken = CancellationToken.None;
        string redisKey = $"test:{Faker.Random.AlphaNumeric(20)}";
        string value = Faker.Random.AlphaNumeric(20);
        string filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        IDatabase db = await GetDb(cancellationToken);

        try
        {
            await db.StringSetAsync(redisKey, value);

            int count = await _util.CloneToDisk(filePath, _connectionString, cancellationToken);

            count.Should().BeGreaterThan(0);
            File.Exists(filePath).Should().BeTrue();

            string json = await File.ReadAllTextAsync(filePath, cancellationToken);
            json.Should().Contain(redisKey);
        }
        finally
        {
            await db.KeyDeleteAsync(redisKey);

            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    [Test]
    public async Task ImportFromDisk_should_restore_cloned_keys()
    {
        var cancellationToken = CancellationToken.None;
        string redisKey = $"test:{Faker.Random.AlphaNumeric(20)}";
        string value = Faker.Random.AlphaNumeric(20);
        string filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        IDatabase db = await GetDb(cancellationToken);

        try
        {
            await db.StringSetAsync(redisKey, value);
            await _util.CloneToDisk(filePath, _connectionString, cancellationToken);
            await KeepOnlyRedisKey(filePath, redisKey, cancellationToken);

            await db.KeyDeleteAsync(redisKey);

            RedisValue removedValue = await db.StringGetAsync(redisKey);
            removedValue.IsNull.Should().BeTrue();

            int count = await _util.ImportFromDisk(filePath, _connectionString, cancellationToken);

            count.Should().Be(1);

            RedisValue result = await db.StringGetAsync(redisKey);
            result.ToString().Should().Be(value);
        }
        finally
        {
            await db.KeyDeleteAsync(redisKey);

            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    private async ValueTask<IDatabase> GetDb(CancellationToken cancellationToken)
    {
        ConnectionMultiplexer connection = await _redisClient.Get(_connectionString, cancellationToken);
        return connection.GetDatabase();
    }

    private static async Task KeepOnlyRedisKey(string filePath, string redisKey, CancellationToken cancellationToken)
    {
        string json = await File.ReadAllTextAsync(filePath, cancellationToken);
        JsonObject? node = JsonUtil.Deserialize<JsonObject>(json);

        if (node?["keyValues"] is not JsonObject keyValues)
            throw new InvalidOperationException("Redis clone file did not contain keyValues.");

        foreach (string key in keyValues.Select(item => item.Key).Where(item => item != redisKey).ToArray())
            keyValues.Remove(key);

        string filteredJson = JsonUtil.Serialize(node, JsonOptionType.Pretty)!;
        await File.WriteAllTextAsync(filePath, filteredJson, cancellationToken);
    }
}
