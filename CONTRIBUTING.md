# Mitwirken an Edda

Schön, dass du beitragen möchtest! Edda ist ein lokal-only **Agent Knowledge Graph (AKG)** +
**Test-Driven Knowledge (TDK)**, bereitgestellt als MCP-Server mit Blazor-UI. Dieses Dokument
fasst zusammen, wie du Änderungen einbringst und welche Konventionen im Projekt gelten.

Mit deinem Beitrag stimmst du zu, dass er unter der [Apache-2.0-Lizenz](LICENSE) des Projekts
steht. Es gilt unser [Verhaltenskodex](CODE_OF_CONDUCT.md).

## Was in Edda passt (und was nicht)

Edda ist bewusst schlank: ein Wissensgraph-Dienst, den beliebige Agenten über MCP **read-only**
anbinden. **Nicht** enthalten (und in der Regel nicht erwünscht) sind Chat-LLM-Runtime,
Multi-Agent-Loops, Scheduling oder Web-/Code-/Browser-Tools — dafür gibt es das größere
Edda-Monorepo. Größere Features besprichst du am besten vorab in einem
[Issue](https://github.com/LupusMalusDeviant/edda/issues), bevor du Zeit in einen PR steckst.

## Voraussetzungen

- **.NET 10 SDK** (für Build & Tests ohne Container)
- **Docker** + **Docker Compose v2** (für Neo4j/Memgraph und den containerisierten Betrieb)

## Entwicklungs-Setup

```bash
git clone https://github.com/LupusMalusDeviant/edda.git
cd edda

# Build + alle Tests (laufen OHNE Infrastruktur, per Mocks)
dotnet build Edda.slnx
dotnet test  Edda.slnx

# Lokal starten: Graph-DB hochfahren, dann den Web-Host
docker compose up -d neo4j
dotnet run --project src/Web        # UI + MCP unter http://127.0.0.1:8080
```

Architektur- und Betriebsdetails: `docs/architektur.md`, `docs/mcp.md`, `docs/tdk.md`,
`docs/embeddings.md`, `docs/betrieb.md`. Einstieg: `docs/erste-schritte.md`, Begriffe: `docs/glossar.md`.

## Absolute Regeln

Diese Regeln sind für einen Merge verbindlich — der Review prüft aktiv darauf:

1. **Interface First** — neue von außen genutzte Klassen brauchen ein Interface in `src/Core/`.
2. **Kein direkter File-I/O** — immer `IFileSystem`. Nie `File.*`, `Directory.*`, `Path.*`.
3. **Keine direkte Zeitabfrage** — immer `TimeProvider`. Nie `DateTime.UtcNow`.
4. **Keine Secrets im Code.**
5. **Tools werfen nie Exceptions** — immer `ToolResult.Fail(...)`.
6. **User-Scoping** — `userId` immer aus dem `ToolExecutionContext`, nie aus Tool-Argumenten.
7. **100 % Unit-Test-Coverage** für neue Klassen. Tests laufen **ohne** Infrastruktur (Mocks).
8. **Testbenennung:** `MethodName_Scenario_ExpectedResult`.
9. **100 % In-Code-Dokumentation (Englisch)** für public `class`/`interface`/`method`/`property`.
10. **Externe Doku (`docs/`) und Commit-Messages auf Deutsch.**
11. `TreatWarningsAsErrors=true`, `Nullable enable`, keine Magic Strings, kein toter Code.
12. **Self-Hosting** — keine CDN-Abhängigkeiten; Assets lokal in `wwwroot/` oder als NuGet.

### Security-Baseline

Zusätzlich gilt: kein Auto-Confirm von Write-/Admin-Aktionen · keine selbstgebaute Krypto
(AES-GCM / Data-Protection statt AES-CBC) · Uploads per MIME **und** Magic-Bytes prüfen ·
`Html.Raw` nur nach server-seitiger Sanitization. Der read-only-/default-deny-Charakter des
MCP-Servers darf nicht aufgeweicht werden.

## Branch-, Commit- & PR-Prozess

1. **Branch** von `main` abzweigen, sprechender Name (`feat/…`, `fix/…`, `docs/…`).
2. **Kleine, fokussierte Commits.** Commit-Messages auf **Deutsch**, im Imperativ
   (z. B. „Füge Pagination für list_memory hinzu").
3. **Vor dem Push:** `dotnet build Edda.slnx` und `dotnet test Edda.slnx` müssen grün sein —
   0 Warnings (Warnings sind Fehler).
4. **Pull Request** gegen `main` öffnen und die PR-Vorlage ausfüllen (was & warum, Tests,
   betroffene Regeln). Verlinke ein zugehöriges Issue.
5. **CI** (`.github/workflows/ci.yml`, Build + Test + Docker-Smoke) muss durchlaufen.

Der Review schaut aus mehreren Blickwinkeln (Security, Correctness, Over-Engineering) und
verlangt die Einhaltung der Absoluten Regeln oben.

## Sicherheitslücken

Melde Schwachstellen **nicht** über öffentliche Issues, sondern über den privaten Kanal —
siehe [SECURITY.md](SECURITY.md).

## Fragen

Für Fragen und Feature-Ideen: [GitHub-Issues](https://github.com/LupusMalusDeviant/edda/issues).
Danke für deinen Beitrag!
