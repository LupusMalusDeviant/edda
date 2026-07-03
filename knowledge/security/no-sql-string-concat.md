---
id: security-no-sql-string-concat
title: Kein SQL per String-Verkettung bauen
domain: security
type: Constraint
priority: Critical
tags: [security, sql, injection, database]
concepts: [sql, injection, query, parameterized, concatenation]
appliesTo: [python, csharp, javascript, typescript]
author: system
requires: [world-security-principles]
validatorScript: |
  import json, sys, re

  data = json.load(sys.stdin)
  code = data.get("code", "")
  rule_id = data.get("rule_id", "security-no-sql-string-concat")

  KW = r'(?:SELECT|INSERT|UPDATE|DELETE)'
  # [^\n]*? is line-bounded (re is not DOTALL) and non-greedy, so an inner quote in the SQL
  # (e.g. name = '{name}') does not cut the match short.
  PATTERNS = [
      # "…SELECT…" +  → literal SQL followed by string concatenation
      (r'(?i)["\'][^\n]*?\b' + KW + r'\b[^\n]*?["\']\s*\+', "SQL query built by string concatenation"),
      # f"…SELECT…{…}" or $"…SELECT…{…}"  → interpolated SQL (Python f-string / C#)
      (r'(?i)(?:f|\$)["\'][^\n]*?\b' + KW + r'\b[^\n]*?\{', "SQL query built by string interpolation"),
      # `…SELECT…${…}`  → interpolated SQL (JS/TS template literal)
      (r'(?i)`[^\n]*?\b' + KW + r'\b[^\n]*?\$\{', "SQL query built by template-literal interpolation"),
      # "…SELECT…".format(  → Python .format() injection
      (r'(?i)["\'][^\n]*?\b' + KW + r'\b[^\n]*?["\']\s*\.\s*format\s*\(', "SQL query built by .format()"),
  ]

  violations = []
  seen_lines = set()
  for pattern, message in PATTERNS:
      for match in re.finditer(pattern, code):
          line = code[:match.start()].count('\n') + 1
          if line in seen_lines:
              continue
          seen_lines.add(line)
          violations.append({
              "rule_id": rule_id,
              "message": message + " — a SQL-injection risk",
              "severity": "warning",
              "line": line,
              "suggestion": "Use parameterized queries / bound parameters instead of building SQL from strings."
          })

  json.dump({"pass": len(violations) == 0, "violations": violations}, sys.stdout)
validatorFixtures:
  pass:
    - |
      cursor.execute("SELECT * FROM users WHERE id = %s", (user_id,))
  fail:
    - |
      query = "SELECT * FROM users WHERE id = " + user_id
---

## Kein SQL per String-Verkettung bauen

SQL-Abfragen, die per String-Verkettung, `f`-String, `$"…"`, Template-Literal oder `.format()` aus
Variablen zusammengesetzt werden, sind die klassische SQL-Injection-Lücke.

### Warum

- **Injection:** Fließt Nutzereingabe unquotiert in die Query, kann ein Angreifer die Abfrage
  umschreiben (`' OR 1=1 --`).
- **Parametrisierung schützt:** Gebundene Parameter trennen Code und Daten — der Treiber quotiert
  korrekt, egal was in der Variable steht.

### Korrekt

```python
cursor.execute("SELECT * FROM users WHERE id = %s", (user_id,))
```

```csharp
cmd.CommandText = "SELECT * FROM users WHERE id = @id";
cmd.Parameters.AddWithValue("@id", id);
```

### Falsch

```python
query = "SELECT * FROM users WHERE id = " + user_id
query = f"SELECT * FROM users WHERE name = '{name}'"
```

```csharp
var sql = $"DELETE FROM sessions WHERE token = '{token}'";
```
