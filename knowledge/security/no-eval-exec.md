---
id: security-no-eval-exec
title: Kein eval/exec auf dynamischem Code
domain: security
type: Constraint
priority: Critical
tags: [security, injection, rce, eval, exec]
concepts: [eval, exec, code-injection, rce, sandbox]
appliesTo: [python, javascript, typescript]
author: system
requires: [world-security-principles]
validatorScript: |
  import json, sys, re

  data = json.load(sys.stdin)
  code = data.get("code", "")
  rule_id = data.get("rule_id", "security-no-eval-exec")

  # (?<!\.) so obj.eval(...) / regex.exec(...) (method calls) are not flagged — only the
  # dangerous globals eval()/exec() that execute arbitrary source at runtime.
  PATTERNS = [
      (r'(?<![\w.])eval\s*\(', "eval() executes arbitrary code — a code-injection / RCE risk"),
      (r'(?<![\w.])exec\s*\(', "exec() executes arbitrary code — a code-injection / RCE risk"),
  ]

  violations = []
  for pattern, message in PATTERNS:
      for match in re.finditer(pattern, code):
          line = code[:match.start()].count('\n') + 1
          violations.append({
              "rule_id": rule_id,
              "message": message,
              "severity": "error",
              "line": line,
              "suggestion": "Parse the input explicitly (e.g. ast.literal_eval / JSON.parse) instead of executing it."
          })

  json.dump({"pass": len(violations) == 0, "violations": violations}, sys.stdout)
---

## Kein eval/exec auf dynamischem Code

`eval()` und `exec()` führen beliebigen, zur Laufzeit zusammengebauten Code aus. Fließt dort je
Nutzereingabe ein, ist das eine direkte Remote-Code-Execution-Lücke.

### Warum

- **RCE:** Enthält der übergebene String Nutzerdaten, kann ein Angreifer beliebigen Code ausführen.
- **Unnötig:** Fast jeder `eval`-Einsatz lässt sich durch explizites Parsen ersetzen
  (`ast.literal_eval`, `json.loads`, `JSON.parse`, ein Dispatch-Dictionary).
- **Nicht analysierbar:** Dynamisch erzeugter Code ist für Linter, Typprüfer und Reviewer unsichtbar.

### Korrekt

```python
import ast, json
config = ast.literal_eval(literal)     # nur Literale, kein Code
payload = json.loads(raw)              # strukturierte Daten
handler = HANDLERS[name]               # Dispatch statt eval(name)
```

### Falsch

```python
result = eval(user_input)
exec(f"do_{action}()")
```

```javascript
const value = eval(req.query.expr);
```
