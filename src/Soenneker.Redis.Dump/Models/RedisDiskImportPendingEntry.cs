using System;
using System.Threading.Tasks;

namespace Soenneker.Redis.Dump.Models;

internal sealed class RedisDiskImportPendingEntry
{
    public RedisDiskImportPendingEntry(string redisKey, byte[] value, TimeSpan? timeToLive)
    {
        RedisKey = redisKey;
        Value = value;
        TimeToLive = timeToLive;
    }

    public string RedisKey { get; }

    public byte[] Value { get; }

    public TimeSpan? TimeToLive { get; }

    public Task<bool>? DeleteTask { get; set; }

    public Task? RestoreTask { get; set; }

    public bool DeleteSucceeded { get; set; }
}
