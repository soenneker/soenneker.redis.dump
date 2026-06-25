using System;
using System.Threading.Tasks;

namespace Soenneker.Redis.Dump.Models;

internal sealed class RedisDiskClonePendingEntry
{
    public RedisDiskClonePendingEntry(string redisKey, Task<TimeSpan?> timeToLiveTask, Task<byte[]?> valueTask)
    {
        RedisKey = redisKey;
        TimeToLiveTask = timeToLiveTask;
        ValueTask = valueTask;
    }

    public string RedisKey { get; }

    public Task<TimeSpan?> TimeToLiveTask { get; }

    public Task<byte[]?> ValueTask { get; }
}
