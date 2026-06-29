# Security & Konfiguration

## Zweck

Querschnittliche Sicherheits-Bausteine und die persistente Laufzeit-Konfiguration: ein AES-verschlüsselter
Credential-Store für API-Keys/Tokens, ein HMAC-/Merkle-verkettetes Audit-Log, Input-Sanitizing,
Taint-Tracking und Secret-Redaction der Tool-Ausgaben — plus der `FileSettingsService`, der die
UI-editierbaren Einstellungen als JSON hält und Änderungen live signalisiert (siehe ADR-0004).

## Dateien

| Pfad | Rolle |
|------|-------|
| `src/Security/Credentials/AesCredentialStore.cs` | AES-verschlüsselte Secrets, abgelegt unter `data/`; nie im Klartext zurückgegeben. |
| `src/Security/Credentials/CredentialKeyScheme.cs` | User-Scoping der Schlüssel: `Scope(userId, name)` → `{userId}:{name}`; `IsValidName`. |
| `src/Security/Configuration/FileSettingsService.cs` | `ISettingsService`: liest/schreibt `data/settings.json` über `IFileSystem`, `Changed`-Event, `SemaphoreSlim`. |
| `src/Security/Audit/HmacAuditLog.cs` | Append-only Audit-Log mit HMAC-Verkettung. |
| `src/Security/Audit/MerkleAuditVerifier.cs` + `IMerkleAuditVerifier.cs` | Integritätsprüfung der Audit-Kette. |
| `src/Security/OutputFilter/SecretRedactor.cs` + `ISecretRedactor.cs` | Maskiert Secrets in Tool-Ausgaben. |
| `src/Security/Sanitization/InputSanitizer.cs` + `IInputSanitizer.cs` | Bereinigt eingehende Inhalte. |
| `src/Security/Taint/TaintTracker.cs` + `TaintSinkRegistry.cs` | Taint-Verfolgung für gefährliche Senken. |
| `src/Security/DependencyInjection/SecurityServiceExtensions.cs` | Registriert Store, Audit, Settings, Sanitizer, Taint. |

## Abhängigkeiten

### Intern
- **Core** — `ICredentialStore`, `IAuditLog`, `ISettingsService`, `IFileSystem`, `ITaintTracker`,
  `ISecretRedactor`, `IInputSanitizer`, `AuditEvent`.

### Extern (Packages)
Keine — BCL-Kryptographie (AES, HMAC).

## Öffentliche API / Interface

- `ICredentialStore` — `StoreAsync` / `RetrieveAsync` / `DeleteAsync` / `ListAsync` (Werte AES-at-rest).
- `ISettingsService` — `Current`, `ReloadAsync()`, `SaveAsync(EddaSettings)`, Event `Changed`.
- `CredentialKeyScheme.Scope(userId, name)` / `IsValidName(name)` (erlaubt `a–z 0–9 - _ . :`).
- `IAuditLog.LogAsync(AuditEvent, userId, message, …)`.
- `ISecretRedactor`, `IInputSanitizer`, `ITaintTracker`.

## Datenfluss / Call-Flow

1. **Secrets:** UI/API schreiben über `ICredentialStore.StoreAsync(Scope(userId, name), value)` →
   AES-verschlüsselt nach `data/`. Resolving-Fassaden (Embeddings, LLM, Connectoren) lesen sie per Schlüssel.
2. **Settings:** UI/`PUT /api/settings` rufen `SaveAsync` → JSON nach `data/settings.json` → `Changed`-Event
   → Resolving-Fassaden invalidieren ihren Cache und greifen sofort (ohne Neustart).
3. **Audit:** sicherheitsrelevante Aktionen werden HMAC-verkettet protokolliert und sind via
   `IMerkleAuditVerifier` prüfbar.

## Offene Fragen / TODOs

Keine offenen Punkte bekannt.
