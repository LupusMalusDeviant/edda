---
id: security-no-plaintext-secrets
title: Keine Klartext-Secrets
domain: security
type: Constraint
priority: Critical
tags: [security, credentials, encryption, secrets]
concepts: [password, api-key, token, secret, credential]
author: system
validatorScript: |
  import json, sys, re

  data = json.load(sys.stdin)
  code = data.get("code", "")
  rule_id = data.get("rule_id", "security-no-plaintext-secrets")

  PATTERNS = [
      (r'(?i)(password|passwd|pwd)\s*=\s*["\'][^"\']{3,}["\']', "Plaintext password detected"),
      (r'(?i)(api_?key|apikey|secret_?key)\s*=\s*["\'][^"\']{8,}["\']', "Plaintext API key detected"),
      (r'(?i)(token|bearer)\s*=\s*["\'][^"\']{8,}["\']', "Plaintext token detected"),
      (r'(?i)sk-[a-zA-Z0-9]{20,}', "Hardcoded API key pattern (sk-...) detected"),
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
              "suggestion": "Use manage_credentials(action='store') or ICredentialStore.StoreAsync() instead."
          })

  json.dump({"pass": len(violations) == 0, "violations": violations}, sys.stdout)
requires: [tool-manage-credentials, world-security-principles]
validatorFixtures:
  pass:
    - |
      await credentialStore.StoreAsync("my-service-api-key", apiKey, ct);
  fail:
    - |
      password = "meinGeheimesPasswort123"
---

## Keine Klartext-Secrets

Passwörter, API-Keys und Tokens dürfen niemals im Klartext in Code, Konfigurationsdateien oder Ausgaben erscheinen.

### Warum

Klartext-Secrets in Code oder Dateien führen unweigerlich zu:
- Accidentellem Commit ins Repository (git history ist permanent)
- Exponierung in Logs, Error-Messages oder Backups
- Cross-user Leakage in Multi-Tenant-Systemen

### Korrekt

```csharp
// Credential speichern
await credentialStore.StoreAsync("my-service-api-key", apiKey, ct);

// Credential abrufen
var apiKey = await credentialStore.RetrieveAsync("my-service-api-key", ct);
```

```python
# Über Umgebungsvariable (nie hardkodiert)
import os
api_key = os.environ["MY_SERVICE_API_KEY"]
```

### Falsch

```python
# VERBOTEN
password = "meinGeheimesPasswort123"
api_key = "sk-ant-api03-..."
```

```csharp
// VERBOTEN
var token = "eyJhbGciOiJIUzI1NiJ9...";
```
