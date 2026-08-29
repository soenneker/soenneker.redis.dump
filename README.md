[![](https://img.shields.io/nuget/v/soenneker.redis.dump.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.redis.dump/)
[![](https://img.shields.io/github/actions/workflow/status/soenneker/soenneker.redis.dump/publish-package.yml?style=for-the-badge)](https://github.com/soenneker/soenneker.redis.dump/actions/workflows/publish-package.yml)
[![](https://img.shields.io/nuget/dt/soenneker.redis.dump.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.redis.dump/)

# Soenneker.Redis.Dump

Redis database export, import, and copy utilities for .NET.

## Install

```bash
dotnet add package Soenneker.Redis.Dump
```

## Quick start

```csharp
using Soenneker.Redis.Dump.Registrars;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
var result = services.AddRedisDumpUtilAsSingleton();
```

Adds `IRedisDumpUtil` as a singleton service.

## What you get

- `IRedisDumpUtil` — Redis database export, import, and copy utilities for .NET.
- `RedisDumpUtilRegistrar` — Redis database export, import, and copy utilities for .NET.

## API at a glance

| API | What it does | Result / important behavior |
| --- | --- | --- |
| `IRedisDumpUtil.CloneToDisk(filePath, connectionString, cancellationToken)` | Clones all keys from the specified Redis connection string to a single JSON file on disk. | The number of keys written to disk. |
| `IRedisDumpUtil.ImportFromDisk(filePath, connectionString, cancellationToken)` | Imports keys from a JSON file created by a clone operation into the specified Redis connection string. | The number of keys imported into Redis. |
| `RedisDumpUtilRegistrar.AddRedisDumpUtilAsSingleton(services)` | Adds `IRedisDumpUtil` as a singleton service. | The same service collection, so additional registrations can be chained. |
| `RedisDumpUtilRegistrar.AddRedisDumpUtilAsScoped(services)` | Adds `IRedisDumpUtil` as a scoped service. | The same service collection, so additional registrations can be chained. |

## Practical notes

- Cancellation stops pending work; it does not undo work that has already completed.
