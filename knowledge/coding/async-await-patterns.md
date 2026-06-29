---
id: coding-async-await-patterns
title: Async/Await Best Practices (.NET)
domain: coding
type: Guideline
priority: Medium
tags: [csharp, async, await, deadlock, performance]
concepts: [async, await, task, deadlock, ConfigureAwait]
author: system
requires: [tool-python-code-interpreter, world-oop-principles]
---

## Async/Await Best Practices (.NET)

### ConfigureAwait(false) in Library-Code

In Library-Projekten (nicht in ASP.NET Core-Handlern) `ConfigureAwait(false)` verwenden, um Deadlocks zu verhindern:

```csharp
// Korrekt in Library-Code (Core, Agent, AKG, Security, ...)
var result = await someService.DoWorkAsync(ct).ConfigureAwait(false);

// In ASP.NET Core-Handlern (Gateway): nicht nötig, da kein SynchronizationContext
var result = await someService.DoWorkAsync(ct);
```

### Kein .Result / .Wait() auf async-Code

```csharp
// VERBOTEN — führt zu Deadlocks
var result = someTask.Result;
someTask.Wait();

// Korrekt — async durch alle Layer
var result = await someTask;
```

### CancellationToken immer propagieren

```csharp
// Korrekt
public async Task<string> ProcessAsync(CancellationToken ct)
{
    var data = await _db.QueryAsync(sql, ct).ConfigureAwait(false);
    return await _processor.TransformAsync(data, ct).ConfigureAwait(false);
}

// Falsch — Token geht verloren
public async Task<string> ProcessAsync(CancellationToken ct)
{
    var data = await _db.QueryAsync(sql, CancellationToken.None); // ← schlecht
    return await _processor.TransformAsync(data);                  // ← token vergessen
}
```

### ValueTask für hot paths

```csharp
// Für häufig synchron abgeschlossene Operationen:
public ValueTask<string?> GetCachedAsync(string key)
{
    if (_cache.TryGetValue(key, out var value))
        return ValueTask.FromResult<string?>(value);      // kein Alloc

    return new ValueTask<string?>(LoadFromDbAsync(key));
}
```
