using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.Redis.Dump.Abstract;

/// <summary>
/// Redis database export, import, and copy utilities for .NET
/// </summary>
public interface IRedisDumpUtil
{
    /// <summary>
    /// Clones all keys from the specified Redis connection string to a single JSON file on disk.
    /// </summary>
    /// <param name="filePath">The destination file path.</param>
    /// <param name="connectionString">The Redis connection string to clone from.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the asynchronous operation to complete.</param>
    /// <returns>The number of keys written to disk.</returns>
    ValueTask<int> CloneToDisk(string filePath, string connectionString, CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports keys from a JSON file created by a clone operation into the specified Redis connection string.
    /// </summary>
    /// <param name="filePath">The source file path.</param>
    /// <param name="connectionString">The Redis connection string to import into.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the asynchronous operation to complete.</param>
    /// <returns>The number of keys imported into Redis.</returns>
    ValueTask<int> ImportFromDisk(string filePath, string connectionString, CancellationToken cancellationToken = default);
}
