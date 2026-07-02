---
id: coding-no-bare-except
title: Kein stilles Verschlucken von Exceptions
domain: coding
type: Guideline
priority: High
tags: [python, exceptions, error-handling]
concepts: [except, pass, exception, error-handling, swallow]
appliesTo: [python]
author: system
requires: [world-security-principles]
validatorScript: |
  import json, sys, re

  data = json.load(sys.stdin)
  code = data.get("code", "")
  rule_id = data.get("rule_id", "coding-no-bare-except")

  PATTERNS = [
      (r'except\b[^:\n]*:\s*(#[^\n]*)?\n\s*pass\b',
       "Silently swallowed exception (except ...: pass) — handle or log it", "error"),
      (r'except\s*:',
       "Bare except catches everything, incl. SystemExit/KeyboardInterrupt", "warning"),
  ]

  violations = []
  seen_lines = set()
  for pattern, message, severity in PATTERNS:
      for match in re.finditer(pattern, code):
          line = code[:match.start()].count('\n') + 1
          if line in seen_lines:
              continue
          seen_lines.add(line)
          violations.append({
              "rule_id": rule_id,
              "message": message,
              "severity": severity,
              "line": line,
              "suggestion": "Catch a specific exception type and handle it (log, re-raise, or recover)."
          })

  json.dump({"pass": len(violations) == 0, "violations": violations}, sys.stdout)
---

## Kein stilles Verschlucken von Exceptions

Ein `except: pass` (oder ein nacktes `except:`) verschluckt Fehler lautlos: Bugs bleiben unsichtbar,
und ein nacktes `except` fängt sogar `SystemExit` und `KeyboardInterrupt` ab.

### Warum

- **Versteckte Bugs:** Eine verschluckte Exception maskiert die eigentliche Fehlerursache — die
  Fehlersuche beginnt an der falschen Stelle.
- **Nicht abbrechbar:** `except:` ohne Typ fängt auch `KeyboardInterrupt`/`SystemExit` — das Programm
  lässt sich nicht mehr sauber beenden.
- **Zu breit:** Man will fast nie *jede* Exception behandeln, sondern eine erwartete.

### Korrekt

```python
try:
    value = int(raw)
except ValueError as exc:
    logger.warning("Ungültige Zahl %r: %s", raw, exc)
    value = 0
```

### Falsch

```python
try:
    value = int(raw)
except:
    pass

try:
    do_work()
except Exception:
    pass
```
