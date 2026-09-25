using Soenneker.Enums.JsonOptions;
using Soenneker.Redis.Dump.Models;
using StackExchange.Redis;
using System.Collections.Generic;
using System.Net;
using System.Text.Json.Serialization.Metadata;
using System.Text.Json.Serialization;
using System.Text.Json;
using System;

namespace Soenneker.Redis.Dump;

[JsonSourceGenerationOptions(JsonSerializerDefaults.General, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(RedisDiskClone))]
internal partial class LibraryJsonContext : JsonSerializerContext
{
    internal static JsonTypeInfo<T> Get<T>() =>
        (JsonTypeInfo<T>)(Default.GetTypeInfo(typeof(T)) ?? throw new NotSupportedException($"No generated JSON metadata for {typeof(T)}."));
}
