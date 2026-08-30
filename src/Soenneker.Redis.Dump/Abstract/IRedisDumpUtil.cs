using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.Redis.Dump.Abstract;

/// <summary>
/// Exports Redis keys to disk and restores them into a Redis database.
/// </summary>
public interface IRedisDumpUtil
{
    /// <summary>
    /// Exports keys and their positive TTLs from the specified Redis connection to a JSON file.
    /// </summary>
    /// <param name="filePath">The destination file path.</param>
    /// <param name="connectionString">The Redis connection string to clone from.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the asynchronous operation to complete.</param>
    /// <returns>The number of keys written to disk.</returns>
    /// <remarks>The destination file is replaced only after the complete export has been serialized.</remarks>
    ValueTask<int> CloneToDisk(string filePath, string connectionString, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores keys from an export file, atomically replacing each matching destination key.
    /// </summary>
    /// <param name="filePath">The source file path.</param>
    /// <param name="connectionString">The Redis connection string to import into.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the asynchronous operation to complete.</param>
    /// <returns>The number of keys restored successfully.</returns>
    /// <remarks>This operation is atomic per key, not across the entire file, and does not remove destination-only keys.</remarks>
    ValueTask<int> ImportFromDisk(string filePath, string connectionString, CancellationToken cancellationToken = default);
}
