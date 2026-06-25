namespace Soenneker.Redis.Dump.Models;

internal sealed class RedisDiskCloneEntry
{
    public string Value { get; set; } = null!;

    public long? TimeToLiveMilliseconds { get; set; }
}
