# Security

Das **Security**-Projekt implementiert alle sicherheitsrelevanten Querschnitts-Belange: Eingabe-Sanitierung, Secret-Redaktion, verschlüsselte Credential-Verwaltung, tamper-evidentes Audit-Logging und Datenfluss-Sicherheit via Taint-Tracking.

---

## Abhängigkeiten

```
Security → Core
```

Keine externen Kryptographie-Bibliotheken — ausschließlich `System.Security.Cryptography` aus dem .NET-Runtime.

---

## Verzeichnisstruktur

```
Security/
├── Audit/            ← HMAC-signiertes Audit-Log mit Merkle-Chain
├── Credentials/      ← AES-256-GCM Credential-Store
├── DependencyInjection/
├── Models/           ← SanitizationResult DTO
├── OutputFilter/     ← Secret-Redaktion aus Ausgaben
├── Sanitization/     ← Prompt-Injection-Schutz
└── Taint/            ← Datenfluss-Sicherheit (F24)
```

---

## Audit/

### `HmacAuditLog.cs`
Implementiert `IAuditLog`. **Append-only**, tamper-evident, Merkle-Chain-verknüpft.

**Funktionsweise:**
1. Jeder Eintrag bekommt eine monotone `SequenceNumber`
2. `PrevHash` = SHA-256-Hash des vorherigen Eintrags (Merkle-Chain)
3. Der Gesamteintrag wird mit HMAC-SHA256 signiert (Key aus `data/.credential-key`)
4. Alle Schreibzugriffe serialisiert via `SemaphoreSlim(1,1)`

**Speicherort:** `data/audit.jsonl` (JSON Lines)

**Pflicht-Properties jedes Eintrags:**
```json
{
  "SequenceNumber": 42,
  "Timestamp": "2026-03-05T10:00:00Z",
  "EventType": "ToolCall",
  "UserId": "web:admin",
  "CorrelationId": "abc-123",
  "Payload": { ... },
  "PrevHash": "sha256:...",
  "Hmac": "sha256:..."
}
```

### `MerkleAuditVerifier.cs`
Offline-Verifikation der gesamten `audit.jsonl`-Datei:
- Prüft HMAC-Signatur jedes Eintrags
- Prüft monotone Sequenznummern
- Prüft lückenlose `PrevHash`-Kette
- Ergebnis: `AuditVerificationResult` (Valid/Invalid + Fehler-Details)

---

## Credentials/

### `AesCredentialStore.cs`
Implementiert `ICredentialStore`. AES-256-GCM verschlüsselt.

**Schema:**
- Schlüsseldatei: `data/.credential-key` (32 zufällige Bytes, wird beim ersten Start generiert)
- Datendatei: `data/credentials.enc` (JSON-Dictionary, AES-256-GCM verschlüsselt, Base64-encoded)
- Pro Verschlüsselungsoperation: neues 12-Byte-Nonce (GCM-Standard)
- Alle Operationen serialisiert via `SemaphoreSlim(1,1)`

**API:**
```csharp
await store.StoreAsync("users:admin:password_hash", hash, ct);
string? val = await store.RetrieveAsync("users:admin:password_hash", ct);
await store.DeleteAsync("users:admin:password_hash", ct);
IReadOnlyList<string> keys = await store.ListKeysAsync(ct);
```

---

## OutputFilter/

### `SecretRedactor.cs`
Regex-basierte Redaktion von Secrets aus Strings. Patterns (in Prioritätsreihenfolge):

| Pattern | Beispiel | Ersatz |
|---|---|---|
| Anthropic API Keys | `sk-ant-api03-...` | `[REDACTED_ANTHROPIC_KEY]` |
| OpenAI API Keys | `sk-...` (51 Zeichen) | `[REDACTED_OPENAI_KEY]` |
| Private Keys | `-----BEGIN ... PRIVATE KEY-----` | `[REDACTED_PRIVATE_KEY]` |
| AWS Access Keys | `AKIA...` | `[REDACTED_AWS_KEY]` |
| Generic Tokens | `token=xxx`, `key=xxx` | `[REDACTED_TOKEN]` |
| Kreditkartennummern | 16-stellige Zahlenfolgen mit Luhn-Check | `[REDACTED_CC]` |
| Passwörter in URLs | `https://user:pass@host` | `[REDACTED_URL_CREDENTIAL]` |

---

## Sanitization/

### `InputSanitizer.cs`
Schutz vor **Prompt-Injection-Angriffen**. Filtert Muster wie:
- `Ignore previous instructions`
- `Jetzt bist du...` / `You are now...`
- `[SYSTEM]`, `<|system|>` Tags
- Base64-kodierte Instruktionen
- Verschachtelte Rollenspiel-Konstrukte

**Limit:** 32.000 Zeichen (darüber hinaus wird abgeschnitten).

**Rückgabe:** `SanitizationResult(Text, WasModified)` — nie `null`, wirft nie.

---

## Taint/

Datenfluss-Sicherheit (F24). Verhindert, dass Daten aus unsicheren Quellen in gefährliche Sinks fließen.

### Taint-Label-Lattice

```
TRUSTED < USER_INPUT < WEB_FETCH < TOOL_OUTPUT < UNTRUSTED
```

Höhere Labels propagieren sich: `TRUSTED + WEB_FETCH = WEB_FETCH`.

### `TaintTracker.cs` (internal)
Per-Turn-Instanz (eine pro Agent-Invocation). Thread-safe für parallele Tool-Ausführung.

**Lebenszyklus:**
1. `TrackSource(toolCallId, label)` — markiert Tool-Output mit Taint-Label
2. `CheckSink(toolCallId, sinkTool, label)` — prüft ob Label im Sink verboten ist
3. `Declassify(toolCallId, reason)` — explizite Herabstufung (mit Audit-Log-Eintrag)

**Sichtbarkeit:** `internal` — nur `AgentRuntime` und Tests dürfen direkt zugreifen (`InternalsVisibleTo`).

### `TaintSinkRegistry.cs`
Konfiguriert welche Labels in welchen Tools verboten sind.

**Default-Sinks:**
| Tool | Verbotene Labels |
|---|---|
| `shell_execute` | `WEB_FETCH`, `UNTRUSTED` |
| `python_code_interpreter` | `WEB_FETCH`, `UNTRUSTED` |
| `manage_credentials` | `TOOL_OUTPUT`, `WEB_FETCH`, `UNTRUSTED` |
| `manage_memory` | `UNTRUSTED` |
| `http_request` | `UNTRUSTED` |

Erweiterbar via `TAINT_EXTRA_SINKS` Umgebungsvariable.

---

## Models/

### `SanitizationResult.cs`
```csharp
public record SanitizationResult(string Text, bool WasModified);
```

---

## DependencyInjection/

### `SecurityServiceExtensions.AddSecurityServices(IServiceCollection)`
Registriert als Singletons: `InputSanitizer`, `SecretRedactor`, `IAuditLog` (→ `HmacAuditLog`), `ICredentialStore` (→ `AesCredentialStore`), `TaintSinkRegistry`, `MerkleAuditVerifier`.

---

## Sicherheitsgarantien

1. **Secrets verlassen das System nie im Klartext** — `SecretRedactor` wird auf alle LLM-Ausgaben und Logs angewendet
2. **Audit-Log ist fälschungssicher** — HMAC + Merkle-Chain; Verifikation via `GET /api/audit/verify`
3. **Credentials sind verschlüsselt at rest** — AES-256-GCM mit zufälligem Nonce pro Schreibvorgang
4. **Prompt-Injection wird abgefangen** — bevor der Input die Pipeline erreicht
5. **Tainted Data erreicht keine gefährlichen Sinks** — TaintTracker blockiert zur Laufzeit
