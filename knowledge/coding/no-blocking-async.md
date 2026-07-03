---
id: coding-no-blocking-async
title: Kein blockierendes Warten auf async-Code
domain: coding
type: Guideline
priority: High
tags: [csharp, async, await, deadlock, threadpool]
concepts: [async, await, Result, Wait, GetAwaiter, deadlock]
appliesTo: [csharp]
author: system
requires: [coding-async-await-patterns]
validatorScript: |
  import json, sys, re

  data = json.load(sys.stdin)
  code = data.get("code", "")
  rule_id = data.get("rule_id", "coding-no-blocking-async")

  PATTERNS = [
      (r'\.GetAwaiter\(\)\.GetResult\(\)', "Blocking .GetAwaiter().GetResult() on async code"),
      (r'\.Result\b', "Blocking .Result on a Task"),
      (r'\.Wait\(\)', "Blocking .Wait() on a Task"),
  ]

  violations = []
  for pattern, message in PATTERNS:
      for match in re.finditer(pattern, code):
          line = code[:match.start()].count('\n') + 1
          violations.append({
              "rule_id": rule_id,
              "message": message,
              "severity": "warning",
              "line": line,
              "suggestion": "await the call instead — blocking on async code can deadlock and starves the thread pool."
          })

  json.dump({"pass": len(violations) == 0, "violations": violations}, sys.stdout)
validatorFixtures:
  pass:
    - |
      var content = await httpClient.GetStringAsync(url, ct);
  fail:
    - |
      var content = httpClient.GetStringAsync(url).Result;
---

## Kein blockierendes Warten auf async-Code

`.Result`, `.Wait()` und `.GetAwaiter().GetResult()` blockieren den aufrufenden Thread, bis die
`Task` fertig ist. In Umgebungen mit `SynchronizationContext` (ASP.NET-Klassik, UI) führt das zu
Deadlocks; überall sonst wird ein Thread-Pool-Thread nutzlos belegt.

### Warum

- **Deadlock:** Blockiert der aufrufende Thread auf dem Context, kann die Fortsetzung der `Task`
  nicht auf denselben Context zurückkehren — beide warten aufeinander.
- **Thread-Pool-Hunger:** Jeder blockierte Aufruf hält einen Worker-Thread; unter Last kippt der Pool.
- **Verschluckte Exceptions:** `.Result`/`.Wait()` verpacken Fehler in eine `AggregateException`.

### Korrekt

```csharp
// async durch alle Layer
var content = await httpClient.GetStringAsync(url, ct);
var rows = await dbContext.QueryAsync(sql, ct);
```

### Falsch

```csharp
// VERBOTEN — blockiert, kann deadlocken
var content = httpClient.GetStringAsync(url).Result;
task.Wait();
var rows = repository.QueryAsync(sql).GetAwaiter().GetResult();
```
