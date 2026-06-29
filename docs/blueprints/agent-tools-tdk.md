# Agent-Tools & TDK

## Zweck

Der schlanke Tool-Layer von Edda: eine Tool-Registry mit Ausführungs-Pipeline (Taint-Check → Ausführung →
Secret-Redaction → Audit) und die wenigen exponierbaren Tools — read-only Memory-Abfragen
(`search_memory`, `list_memory`), das `tdk_validate` (nicht im Default-Allowlist) und die user-scoped
Stores (`manage_memory`, `manage_userdata`, `manage_learnings`). Enthält die **TDK-Engine**, die
generierten Code über Python-Validatoren (sandboxed) gegen die Wissensbasis prüft.

## Dateien

| Pfad | Rolle |
|------|-------|
| `src/Agent/Registry/ToolRegistry.cs` | `IToolRegistry` + `IToolExecutor` — Registrierung, Ausführung, Taint, Redaction, Audit, Timeout. |
| `src/Agent/DependencyInjection/AgentToolsServiceExtensions.cs` | `AddLeanAgentTools` + Hosted Service, der die Tools registriert. |
| `src/Agent/Tools/Knowledge/KnowledgeGetContextTool.cs` | `search_memory` — durchsucht das Langzeitgedächtnis und kompiliert den Kontext für eine Anfrage. |
| `src/Agent/Tools/Knowledge/KnowledgeListRulesTool.cs` | `list_memory` — listet gespeicherte Gedächtnis-Einträge (gefiltert). |
| `src/Agent/Tools/Knowledge/TdkValidateTool.cs` | Validiert Code gegen TDK-Validatoren. |
| `src/Agent/Tools/Memory/Manage{Memory,Userdata,Learnings}Tool.cs` | User-scoped Stores (schreibend). |
| `src/Agent/Tools/ToolArgumentHelper.cs` | Argument-Parsing-Helfer. |
| `src/Agent/Tdk/TdkEngine.cs` · `NullTdkEngine.cs` | `ITdkEngine` — führt Validatoren in der Sandbox aus. |
| `src/Agent/Tdk/CodeBlockExtractor.cs` · `TdkFeedbackFormatter.cs` · `Models/*` | Code-Extraktion, Ergebnis-Formatierung, DTOs. |
| `src/Agent/Knowledge/ToolKnowledgeService.cs` | `IToolKnowledgeService` — Tool-Wissen aus dem Graphen. |
| `src/Agent/Infrastructure/PhysicalFileSystem.cs` | `IFileSystem`-Implementierung (einzige Stelle mit echtem File-I/O). |

## Abhängigkeiten

### Intern
- **Core** — `IToolRegistry`, `IToolExecutor`, `IAgentTool`, `ITdkEngine`, `IToolKnowledgeService`, `IFileSystem`, `ToolDefinition`/`ToolCall`/`ToolResult`, `TaskContext`.
- **Security** — `ISecretRedactor`, `IAuditLog`, `ITaintTracker` (in der Ausführungs-Pipeline).
- **Sandboxing** (Laufzeit) — `ISandboxFactory` für die TDK-Validatoren.
- **Wissensgraph (AKG)** (Laufzeit) — `IKnowledgeGraph` für Wissens-Tools und TDK-Kontext.

### Extern (Packages)
DI-/Hosting-/Logging-Abstractions (keine fachlichen Packages).

## Öffentliche API / Interface

- `IToolRegistry` — `Register`, `GetAvailableTools`, `GetFilteredTools(names)`, `GetTool`, `Unregister`.
- `IToolExecutor.ExecuteAsync(ToolCall, ToolExecutionContext, ct)` → `ToolResult` (wirft nie; `ToolResult.Fail`).
- `IAgentTool` — `Definition` (Name, Beschreibung, Input-Schema) + `ExecuteAsync`.
- `ITdkEngine.ValidateAsync(...)` → Verstöße bzw. „keine Verstöße".
- Tools sind user-scoped über `ToolExecutionContext.UserId` (nie aus Tool-Argumenten).

## Datenfluss / Call-Flow

1. `IToolExecutor.ExecuteAsync(call, ctx)` schlägt das Tool nach, prüft Taint, setzt ein 90s-Timeout.
2. Das Tool führt aus (z. B. `TdkValidateTool` → AKG-Kontext kompilieren → `TdkEngine.ValidateAsync`).
3. `TdkEngine` extrahiert Code-Blöcke und führt die Python-Validatoren über `ISandboxFactory` aus;
   `TdkFeedbackFormatter` formatiert die Verstöße.
4. Die Ausgabe wird redigiert (`ISecretRedactor`), auditiert (`IAuditLog`) und als `ToolResult` zurückgegeben.

## Offene Fragen / TODOs

Keine offenen Punkte bekannt.
