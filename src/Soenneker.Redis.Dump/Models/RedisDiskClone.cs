using System;
using System.Collections.Generic;

namespace Soenneker.Redis.Dump.Models;

internal sealed class RedisDiskClone
{
    public int Version { get; set; }

    public Dictionary<string, RedisDiskCloneEntry> KeyValues { get; set; } = new(StringComparer.Ordinal);
}
