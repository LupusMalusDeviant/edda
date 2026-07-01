# CLAUDE.md — Edda

> Eigenständige, lokal-only Auskopplung von **AKG** (Agent Knowledge Graph) + **TDK**
> (Test-Driven Knowledge) aus dem Edda-Monorepo. Stellt AKG+TDK als **MCP-Server**
> (HTTP/SSE **und** stdio) mit **eigenem Blazor-UI** bereit, inklusive aller Embedding-Provider.

---

## Was das ist

Ein schlanker Wissensgraph-Dienst, den **jeder beliebige Agent** über MCP anbinden kann:

- **AKG** — Wissensregeln (Domains, Relationen), 4-Phasen-Kontext-Kompilierung
  (Keyword → semantisch → MMR → Konfliktauflösung), Feedback-Konfidenz, Retrieval-Benchmark.
- **TDK** — Regeln mit Python-Validator-Skripten, die generierten Code aktiv gegen die
  Wissensbasis prüfen (sandboxed).
- **Embeddings** — sechs austauschbare Provider (OpenAI, Google, Voyage, Ollama, Custom, Null).
- **MCP** — exponiert die erlaubten Tools spec-konform über HTTP/SSE und stdio.
- **UI** — Blazor-Server: Knowledge-Graph (Cytoscape), TDK-Validierung, Embedding-Status.

**Bewusst NICHT enthalten** (im Gegensatz zum Edda-Monorepo): Chat-LLM-Runtime,
Multi-Agent, Scheduling, Web-/Code-/Docker-/Browser-Tools, Kanäle (Telegram/Matrix).
Daher gibt es **kein** `compile_knowledge` und **kein** `analyze_codebase` (diese bräuchten
eine Chat-Runtime / einen Agent-Loop).

**Ausnahme seit M2 (ADR-0010):** eine reine *Ingest-Zeit*-LLM-Extraktion ist **opt-in**
zuschaltbar — der Enricher (`INGESTION_ENRICHER=llm`) und die Entity-Extraktion
(`INGESTION_ENTITY_EXTRACTION=true`), beide getrennt schaltbar, Default AUS. Das ist kein
Chat/Agent-Loop, sondern ein einmaliger Extraktions-Client; ohne Aktivierung bleibt der
Betrieb local-only.

Stack: **.NET 10 | C# 13 | Neo4j 5 (oder Memgraph) | Blazor Server | xUnit | ModelContextProtocol 1.4**

---

## Projektstruktur

| Projekt | Assembly | Inhalt |
|---------|----------|--------|
| `src/Core` | `Edda.Core` | Interfaces + Models (Verträge) |
| `src/AKG` | `Edda.AKG` | Wissensgraph: Graph, Context, Parser, Providers, Embeddings-Cache, Feedback, Benchmark |
| `src/Embeddings` | `Edda.Embeddings` | 6 `IEmbeddingService`-Provider + DI (`AddEmbeddingService`) |
| `src/Security` | `Edda.Security` | SecretRedactor, HmacAuditLog, Sanitizer, Taint, AesCredentialStore |
| `src/Sandboxing` | `Edda.Sandboxing` | `ISandboxFactory` (Docker/Wasm/Null) für TDK |
| `src/Agent` | `Edda.Agent` | Schlanker Tool-Layer: TDK-Engine, ToolRegistry (= `IToolExecutor`+`IToolRegistry`), 6 Tools, PhysicalFileSystem, ToolKnowledgeService |
| `src/AKG.Mcp` | `Edda.AKG.Mcp` | MCP-Server (intern→extern) + Client (extern→intern) + Adapter |
| `src/Edda.Hosting` | `Edda.Hosting` | Geteilte DI (`AddEddaCore`), MCP-Handler-Wiring, `/api/akg/*`-Endpoints, lokale Identity + Auth |
| `src/Web` | `Edda.Web` | Blazor-Server-Host (UI + REST + MCP/HTTP), `Program.cs` |
| `src/Edda.Mcp.Stdio` | `Edda.Mcp.Stdio` | stdio-MCP-Host (Konsole) |

**Abhängigkeiten:** `Core` ← alles; `AKG`/`Embeddings`/`Security`/`Sandboxing`/`AKG.Mcp` → `Core`;
`Agent` → `Core`+`Security`; `Hosting` → alle Libs; `Web`/`Stdio` → `Hosting`.

Die meisten Projekte behalten den `Edda.*`-Namespace aus dem Monorepo (Ganzkopie ohne
Edits). Nur die neuen Host-/Hosting-Teile nutzen `Edda.*`.

## Tools (MCP-exponierbar)

| Tool | Zweck | Default-Allowlist |
|------|-------|-------------------|
| `search_memory` | Langzeitgedächtnis (Wissensgraph) zu einer Anfrage durchsuchen — vor dem Scan des Dateisystems aufrufen | ✓ |
| `list_memory` | Gespeicherte Gedächtnis-Einträge durchstöbern/auflisten (optional nach Domain/Typ/Tag gefiltert) | ✓ |
| `analyze_coverage` | Wissensgraph auf Abdeckungslücken prüfen (dünne Domains, kaputte Referenzen, Konflikte, Low-Confidence, veraltete Regeln) — read-only | — (via `MCP_EXPOSED_TOOLS`) |
| `tdk_validate` | Code gegen TDK-Validatoren prüfen | — (via `MCP_EXPOSED_TOOLS`) |
| `manage_memory` / `manage_userdata` / `manage_learnings` | Nutzer-Stores (user-scoped) | — (via `MCP_EXPOSED_TOOLS`) |

Allowlist konfigurierbar über `MCP_EXPOSED_TOOLS` (Komma-getrennt, Default-Deny).

---

## Absolute Regeln (aus dem Monorepo übernommen)

```
1.  INTERFACE FIRST — neue von außen genutzte Klassen brauchen ein Interface in Core/.
2.  KEIN DIREKTER FILE-I/O — immer IFileSystem. Nie File.*, Directory.*, Path.*.
3.  KEINE DIREKTE ZEITABFRAGE — immer TimeProvider. Nie DateTime.UtcNow.
4.  KEINE SECRETS IM CODE.
5.  TOOLS WERFEN NIE EXCEPTIONS — immer ToolResult.Fail(...).
6.  USER-SCOPING — userId immer aus ToolExecutionContext, nie aus Tool-Argumenten.
7.  100% UNIT-TEST-COVERAGE für neue Klassen. Tests laufen OHNE Infrastruktur (Mocks).
8.  Testbenennung: MethodName_Scenario_ExpectedResult.
9.  100% IN-CODE-DOKUMENTATION (Englisch) für public class/interface/method/property.
10. Externe Doku (docs/) + Commit-Messages auf Deutsch, ohne KI-Mention.
11. TreatWarningsAsErrors=true, Nullable enable, keine Magic Strings, kein toter Code.
12. SELF-HOSTING — keine CDN-Abhängigkeiten; Assets lokal in wwwroot/ oder als NuGet.
```

---

## Build & Run

```bash
# Build + Test (ohne Infrastruktur)
dotnet build Edda.slnx
dotnet test  Edda.slnx

# Lokal starten: Neo4j hochfahren, dann den Web-Host
docker compose up -d neo4j
dotnet run --project src/Web              # UI + MCP unter http://127.0.0.1:8080

# Komplett containerisiert
docker compose up -d --build

# stdio-MCP-Host (für lokale stdio-Clients wie Claude Desktop)
dotnet run --project src/Edda.Mcp.Stdio
```

Details: `docs/architektur.md`, `docs/mcp.md`, `docs/embeddings.md`, `docs/chunking.md`, `docs/tdk.md`, `docs/betrieb.md`.
