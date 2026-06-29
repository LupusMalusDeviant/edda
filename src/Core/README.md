# Core

Das **Core**-Projekt ist das Fundament der gesamten Architektur. Es enthält ausschließlich Interfaces, DTOs, Records und Exceptions — **keine Logik, keine Infrastruktur-Abhängigkeiten**.

Alle anderen Projekte dürfen Core referenzieren, aber Core referenziert nichts.

---

## Abhängigkeiten

```
Core → (keine externen Abhängigkeiten)
```

Nur `Microsoft.Extensions.DependencyInjection.Abstractions` und `Microsoft.Extensions.Logging.Abstractions` als reine Abstraktion-Pakete.

---

## Verzeichnisstruktur

```
Core/
├── Abstractions/   ← alle Interfaces (normativ)
├── Models/         ← alle DTOs, Records, Enums
└── Exceptions/     ← Exception-Hierarchie
```

---

## Abstractions/

Alle Interfaces des Systems. Diese Datei ist **normativ** — Implementierungen DÜRFEN davon nicht abweichen.

| Interface | Implementiert von | Zweck |
|---|---|---|
| `IAgentRuntime` | `Agent.Runtime.AgentRuntime` | 10-Phasen Agent-Pipeline |
| `IAgentTool` | alle Tools in `Agent.Tools.*` | Tool-Vertrag: `ExecuteAsync` gibt immer `ToolResult` zurück, wirft nie |
| `IToolExecutor` | `Agent.Runtime.ToolLoop` | Orchestriert Tool-Aufruf-Schleife |
| `IToolRegistry` | `Agent.Registry.ToolRegistry` | Werkzeugregistrierung und -suche |
| `IModelClient` | alle Provider in `Agent.Providers` | LLM-Aufruf (ChatAsync, StreamAsync) |
| `IKnowledgeGraph` | `AKG.Graph.Neo4jKnowledgeGraph` | CRUD auf Neo4j-Wissensgraph |
| `IKnowledgeCompiler` | `Agent.Knowledge.KnowledgeCompiler` | Markdown → KnowledgeRule-Parsing |
| `IDomainManager` | `AKG.Graph.DomainManager` | Domain-Hierarchie im AKG verwalten |
| `IIdentityContext` | `AnonymousIdentityContext`, `JwtIdentityContext`, `CookieIdentityContext`, `TelegramIdentityContext`, `MatrixIdentityContext` | Wer führt eine Anfrage aus (UserId, IsAdmin) |
| `IConversationStore` | `Memory.ConversationStore` | Gesprächsverlauf lesen/schreiben |
| `IShortTermMemoryStore` | `Memory.SqliteShortTermMemoryStore` | Kurzzeit-Gedächtnis (STM) in SQLite |
| `IMemorySearch` | `Memory.Hybrid.HybridMemorySearch` | BM25+Vector+MMR Suche |
| `ICredentialStore` | `Security.Credentials.AesCredentialStore` | AES-256-GCM Secrets |
| `IAuditLog` | `Security.Audit.HmacAuditLog` | Tamper-evident Audit-Log mit Merkle-Chain |
| `ITaintTracker` | `Security.Taint.TaintTracker` | Datenfluss-Sicherheit (Taint-Tracking) |
| `IEmbeddingService` | alle in `Agent.Providers.Embeddings` | Text → Float-Vektor |
| `ISandboxFactory` | `Sandboxing.Docker.DockerSandboxFactory`, `Sandboxing.Wasm.WasmSandboxFactory` | Sandbox erstellen |
| `ITdkEngine` | `Agent.Tdk.TdkEngine` | Test-Driven Knowledge Validierung |
| `ILoopGuard` | `Agent.Runtime.LoopGuard` | Endlos-Tool-Loop-Erkennung |
| `IAgentChannel` | `Channels.Telegram`, `Channels.Matrix` | Eingehende Nachrichten-Kanäle |
| `IDeliveryChannel` | `HttpWebhookDeliveryChannel`, `TelegramDeliveryChannel` etc. | Ausgehende Nachrichten-Zustellung |
| `IDeliveryChannelResolver` | `Agent.Scheduling.Delivery.DeliveryChannelResolver` | UserId-Präfix → Kanal-Name auflösen |
| `ICloneOrchestrator` | `Agent.Multiagent.ContainerOrchestrator` | Clone-Lifecycle verwalten |
| `IHandsOrchestrator` | `Agent.Multiagent.Hands.HandsOrchestrator` | Autonome Hands-Worker |
| `IHandProgressStore` | `Agent.Multiagent.Hands.HandProgressStore` | Fortschritt der Hands speichern |
| `IWorkflowEngine` | `Agent.Workflow.WorkflowEngine` | DAG-Workflow-Ausführung |
| `IRuleFeedbackService` | `AKG.Feedback.RuleFeedbackService` | AKG-Feedback-Loop |
| `IRuleConfidenceStore` | `AKG.Confidence.SlidingWindowRuleConfidenceStore` | Konfidenz-Scores pro Regel |
| `IFileSystem` | `Agent.Infrastructure.PhysicalFileSystem` | Datei-I/O-Abstraktion (nie `File.*` direkt!) |
| `IConfigService` | `Agent.Config.FileConfigService` | Agent-Konfiguration lesen/schreiben |
| `IEventBus` | `Gateway.Infrastructure.InProcessEventBus` | Interne Event-Distribution |
| `IStartupValidator` | `Gateway.Infrastructure.StartupValidator` | Startup-Checks |
| `IPluginLoader` | `Agent.Plugins.PluginLoader` | Hot-loadable Plugin-Assemblies |
| `ISkillProfileLoader` | `Agent.Skills.SkillProfileLoader` | SKILL.md-Kompetenzprofile |
| `IWebSearchProvider` | `Agent.Tools.Web.Providers.DuckDuckGoSearchProvider` | Web-Suche-Provider |
| `IBrowserProxy` | `Agent.Tools.Browser.HttpBrowserProxy` | Browser-Automatisierung via Playwright |
| `ICodingCliRegistry` | `Agent.Config.CodingCliRegistry` | Coding-CLI-Konfigurationen |
| `ISystemMetricsProvider` | `Agent.Scheduling.Providers.*` | CPU/RAM-Metriken |
| `ITriggerStore` | `Agent.Scheduling.Stores.TriggerFileStore` | Scheduler-Trigger persistieren |
| `ITaskQueueStore` | `Agent.Scheduling.TaskQueue.TaskQueueStore` | Task-Queue persistieren |

---

## Models/

Alle DTOs und Value Objects des Systems. Keine Logik — nur Daten.

| Datei | Wichtigste Typen | Zweck |
|---|---|---|
| `AgentConfig.cs` | `AgentConfig` | Laufzeitkonfiguration des Agents (Modell, MaxTools...) |
| `AgentMessage.cs` | `AgentMessage`, `MessageRole` | Einzelne Chat-Nachricht (User/Assistant/System/Tool) |
| `AgentRequest.cs` | `AgentRequest` | Eingehende Anfrage an den Agent (Text, ConversationId, UserId) |
| `AgentResponseEvent.cs` | `AgentResponseEvent`, `AgentEventType` | Streaming-Event aus der Pipeline (Token, ToolCall, Done, Error) |
| `AuditEvent.cs` | `AuditEvent` | Einzelner Audit-Log-Eintrag |
| `AuditModels.cs` | `AuditVerificationResult`, `AuditChainEntry` | Merkle-Chain Verifikationsergebnisse |
| `ChannelModels.cs` | `IncomingMessage`, `OutgoingMessage` | Kanal-Ein-/Ausgabe |
| `CloneModels.cs` | `CloneRequest`, `CloneStatus`, `CloneResult` | Clone-Lifecycle-DTOs |
| `ContentPart.cs` | `ContentPart`, `ContentPartType` | Multi-modal LLM-Inhalt (Text, Image, ToolUse, ToolResult) |
| `ContextResult.cs` | `ContextResult` | Ergebnis des AKG-Context-Compilers (aktive Regeln, Konflikte) |
| `ConversationModels.cs` | `ConversationSummary`, `ConversationMessage` | Gesprächs-Metadaten |
| `DomainModels.cs` | `DomainInfo`, `DomainHierarchy` | AKG-Domain-Struktur |
| `FeedbackModels.cs` | `RuleFeedback`, `FeedbackType` | AKG-Feedback (TDK-Verletzung, User, Compliance) |
| `GraphStats.cs` | `GraphStats` | Neo4j-Graph-Statistiken |
| `HandModels.cs` | `HandSpec`, `HandStatus`, `HandResult` | Autonome Hand-Worker DTOs |
| `KnowledgeCompilationResult.cs` | `KnowledgeCompilationResult` | Ergebnis des Knowledge-Compiler (Regeln, Fehler) |
| `KnowledgeRule.cs` | `KnowledgeRule`, `RuleType`, `RulePriority` | Eine Wissensregel im AKG |
| `LoopGuardModels.cs` | `LoopGuardVerdict`, `LoopViolationType` | LoopGuard-Ergebnis (Allow/Warn/Abort) |
| `MemorySearchModels.cs` | `MemorySearchResult`, `MemoryCandidate` | Hybrid-Memory-Suchergebnisse |
| `ModelResponse.cs` | `ModelResponse`, `FinishReason`, `TokenUsage` | LLM-Antwort mit Token-Statistiken |
| `PluginModels.cs` | `PluginInfo`, `PluginState` | Plugin-Metadaten |
| `SandboxResult.cs` | `SandboxResult` | Ergebnis einer Sandbox-Ausführung (Stdout, Stderr, ExitCode) |
| `SearchModels.cs` | `SearchResult` | Web-Suchergebnis |
| `ShortTermMemoryEntry.cs` | `ShortTermMemoryEntry` | STM-Eintrag mit Keywords, Source und Embedding |
| `SkillModels.cs` | `SkillProfile`, `SkillTool` | SKILL.md-Kompetenzprofil |
| `StreamEvent.cs` | `StreamEvent`, `StreamEventType` | SSE-Streaming-Events |
| `SystemMetrics.cs` | `SystemMetrics` | CPU/RAM-Momentaufnahme |
| `TaintModels.cs` | `TaintLabel`, `TaintedValue`, `SinkViolation` | Taint-Tracking-Typen |
| `TaskContext.cs` | `TaskContext` | Kontext für eine Agent-Aufgabe (Nachricht, extrahierte Konzepte) |
| `TdkResult.cs` | `TdkResult`, `TdkViolation` | TDK-Validierungsergebnis |
| `ToolCall.cs` | `ToolCall` | Ein Tool-Aufruf vom LLM (Name, Argumente, CallId) |
| `ToolDefinition.cs` | `ToolDefinition`, `ToolParameter` | Tool-Schema (für LLM-Kontext) |
| `ToolExecutionContext.cs` | `ToolExecutionContext` | Ausführungskontext für Tools (UserId, ConversationId, Services) |
| `ToolResult.cs` | `ToolResult` | Ergebnis eines Tool-Aufrufs; `ToolResult.Fail(...)` für Fehler |
| `TriggerModels.cs` | `TriggerDefinition`, `TriggerType` | Scheduler-Trigger-Konfiguration |
| `UserIdentity.cs` | `UserIdentity` | Benutzeridentität (UserId, Username, Roles) |
| `ValidationModels.cs` | `ValidationResult`, `ValidationIssue` | Startup-Validator-Ergebnisse |
| `WorkflowModels.cs` | `WorkflowDefinition`, `WorkflowNode`, `WorkflowEdge` | DAG-Workflow-Datenstruktur |
| `CodingCliConfig.cs` | `CodingCliConfig` | Konfiguration für Coding-CLI-Integration |
| `SandboxResult.cs` | `SandboxResult` | Sandbox-Ausführungsergebnis |

---

## Exceptions/

Strukturierte Exception-Hierarchie. Alle leiten von `EddaException` ab.

```
EddaException
├── AgentException        ← Pipeline-Fehler (Runtime, Tools)
├── AkgException          ← Knowledge-Graph-Fehler
├── CloneException        ← Clone-Lifecycle-Fehler
├── ConfigurationException← Konfigurationsfehler beim Start
├── CredentialException   ← Credential-Store-Fehler
├── IdentityException     ← Auth/Identity-Fehler
├── ProviderException     ← LLM-Provider-Fehler (Netz, API)
└── SandboxException      ← Sandbox-Ausführungsfehler
```

---

## Regeln

- **Kein Code in Core** außer Interface-Definitionen und Daten-Records.
- **Keine NuGet-Pakete** außer reine Abstraktion-Bibliotheken.
- Änderungen an Interfaces hier erfordern explizite Bestätigung — sie sind normativ.
