[![](https://img.shields.io/nuget/v/soenneker.redis.dump.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.redis.dump/)
[![](https://img.shields.io/github/actions/workflow/status/soenneker/soenneker.redis.dump/publish-package.yml?style=for-the-badge)](https://github.com/soenneker/soenneker.redis.dump/actions/workflows/publish-package.yml)
[![](https://img.shields.io/nuget/dt/soenneker.redis.dump.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.redis.dump/)
[![](https://img.shields.io/github/actions/workflow/status/soenneker/soenneker.redis.dump/build-and-test.yml?label=build%20and%20test&style=for-the-badge)](https://github.com/soenneker/soenneker.redis.dump/actions/workflows/build-and-test.yml)
[![](https://img.shields.io/github/actions/workflow/status/soenneker/soenneker.redis.dump/codeql.yml?label=CodeQL&style=for-the-badge)](https://github.com/soenneker/soenneker.redis.dump/actions/workflows/codeql.yml)

# Soenneker.Redis.Dump

Exports Redis keys and TTLs to a portable JSON file and restores that file into a Redis database.

## Installation

```bash
dotnet add package Soenneker.Redis.Dump
```

## Registration

```csharp
using Soenneker.Redis.Dump.Registrars;

services.AddRedisDumpUtilAsSingleton();
```

Both registrars use a singleton `IRedisClient`, so a scoped dump utility can be destroyed without closing the application's shared Redis connections.

## Export and import

```csharp
using Soenneker.Redis.Dump.Abstract;

IRedisDumpUtil dump = serviceProvider.GetRequiredService<IRedisDumpUtil>();

int exported = await dump.CloneToDisk(
    "backups/redis.json",
    sourceConnectionString,
    cancellationToken);

int imported = await dump.ImportFromDisk(
    "backups/redis.json",
    destinationConnectionString,
    cancellationToken);
```

Export scans writable endpoints, stores Redis's serialized value for each key, and preserves positive TTLs. The destination file is replaced only after the complete JSON document has been written.

Import overwrites matching keys using atomic per-key `RESTORE ... REPLACE` operations. It does not delete destination-only keys, and the overall import is not a cross-key transaction. The return value is the number of successfully restored keys; malformed individual entries are logged and skipped. Connection, file, cancellation, and unsupported-format failures are thrown to the caller.

The Redis account needs permission for `SCAN`, `DUMP`, `PTTL`, and `RESTORE`. Configure access accordingly; this package does not enable administrative commands automatically.
