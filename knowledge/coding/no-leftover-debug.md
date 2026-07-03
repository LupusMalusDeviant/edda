---
id: coding-no-leftover-debug
title: Kein vergessenes Debug-Logging im Code
domain: coding
type: Guideline
priority: Medium
tags: [javascript, typescript, debugging, logging, cleanup]
concepts: [console.log, debugger, debug, logging, cleanup]
appliesTo: [javascript, typescript]
author: system
requires: []
validatorScript: |
  import json, sys, re

  data = json.load(sys.stdin)
  code = data.get("code", "")
  rule_id = data.get("rule_id", "coding-no-leftover-debug")

  PATTERNS = [
      (r'\bconsole\.(?:log|debug|info)\s*\(',
       "Leftover console.log/debug/info — use a real logger or remove it"),
      (r'\bdebugger\b',
       "Leftover debugger statement — remove before committing"),
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
              "suggestion": "Remove debug output or route it through the project's logging library."
          })

  json.dump({"pass": len(violations) == 0, "violations": violations}, sys.stdout)
validatorFixtures:
  pass:
    - |
      logger.info("Order processed", { orderId });
  fail:
    - |
      console.log("hier bin ich", user);
---

## Kein vergessenes Debug-Logging im Code

`console.log`/`console.debug`/`console.info` und `debugger`-Statements sind fast immer beim Debuggen
vergessene Reste. Sie verrauschen die Konsole, können sensible Daten leaken und blockieren im Fall von
`debugger` die Ausführung, wenn die DevTools offen sind.

### Warum

- **Rauschen & Leaks:** Debug-Ausgaben landen in der Produktion, verwässern echte Logs und können
  Nutzer- oder Systemdaten preisgeben.
- **`debugger` hält an:** Ein vergessenes `debugger` unterbricht die App, sobald die DevTools offen sind.
- **Kein Log-Level:** `console.log` kennt keine Stufen/Struktur — echtes Logging schon.

### Korrekt

```javascript
import { logger } from "./logging";
logger.info("Order processed", { orderId });
// console.error/console.warn für echte Fehler bleiben zulässig
```

### Falsch

```javascript
console.log("hier bin ich", user);
console.debug(payload);
debugger;
```
