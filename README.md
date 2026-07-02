# Edda

Eigenständiger **Agent Knowledge Graph (AKG)** + **Test-Driven Knowledge (TDK)** mit Embeddings,
bereitgestellt als **MCP-Server** (HTTP/SSE und stdio) mit eigenem **Blazor-UI**. Lokal-only —
jeder beliebige Agent/LLM kann den Wissensgraphen über MCP anbinden (**read-only**).

Stack: .NET 10 · C# 13 · Neo4j 5 (oder Memgraph) · Blazor Server · ModelContextProtocol 1.4

> **Neu hier?** Der **[Erste-Schritte-Guide](docs/erste-schritte.md)** führt in 15 Minuten durch
> Installation → erste Regel anlegen → Agent anbinden → TDK-Validierung. Unbekannte Begriffe klärt das
> **[Glossar](docs/glossar.md)**.

---

## Für wen & wofür

Edda ist ein **lokales Langzeitgedächtnis für Coding-Agenten** — kuratiertes, projektübergreifendes
Wissen, das jeder MCP-fähige Agent (Claude Code, Cursor, …) read-only anzapfen kann. Drei typische
Einsätze:

- **Privater Dev-Speicher** — persönliche Coding-Konventionen, Architektur-Entscheidungen und
  „Lessons Learned", lokal und ohne Cloud.
- **Team-Wissensbasis** — geteilte Standards und Domänenwissen, die Agenten konsistent anwenden.
- **Coding-Standards-Wächter** — mit **TDK** prüfen Regeln generierten Code aktiv, nicht nur beratend.

Bewusst anders als generische Memory-Frameworks: **Safety-First-MCP** (read-only, default-deny — gefahrlos
für fremde Agenten exponierbar), **kein großes LLM nötig** (kuratiertes Wissen statt LLM-Extraktion, läuft
auf schwacher Hardware) und **.NET-nativ**.

---

## Voraussetzungen

- **Docker** + **Docker Compose v2** (für den Betrieb)
- Optional für Entwicklung ohne Container: **.NET 10 SDK**

## Installation (empfohlen)

Repo holen und das plattformpassende Installskript ausführen. Es fragt **Zielserver/Port** und
**optionale erste Keys** (Embedding/LLM — mit Enter überspringbar) ab, schreibt `.env`, baut + startet
die Container, **testet die Erreichbarkeit** und gibt am Ende den klickbaren Link aus.

```bash
git clone https://github.com/LupusMalusDeviant/edda.git
cd edda
```

**Mac / Linux:**
```bash
./install.sh
```

**Windows (PowerShell):**
```powershell
.\install.ps1
# Falls die Ausführung blockiert ist:
#   powershell -ExecutionPolicy Bypass -File .\install.ps1
```

Danach läuft Edda unter dem angezeigten Link (Default `http://127.0.0.1:8080`).
Stoppen: `docker compose down` · Logs: `docker compose logs -f edda`.

> **Remote-Zugriff:** Wird ein anderer Host als `127.0.0.1`/`localhost` angegeben, bindet Edda auf
> allen Interfaces (`0.0.0.0`). Setze dann unbedingt einen **Auth-Token** (im Skript abgefragt).

### Manuell (ohne Skript)
```bash
cp .env.example .env       # Werte anpassen (Bind/Port, Keys, MCP, Token)
docker compose up -d --build
```

### Entwicklung (ohne Container)
```bash
docker compose up -d neo4j
dotnet run --project src/Web      # → http://127.0.0.1:8080
```

---

## MCP an LLMs anbinden (read-only)

Edda stellt den Wissensgraphen als MCP-Server bereit. **Angebundene LLMs erhalten ausschließlich
Leserechte:**

- Default-Allowlist (**default-deny**): `search_memory`, `list_memory` — beide lesend.
  `tdk_validate` ist standardmäßig **nicht** exponiert, lässt sich aber über `MCP_EXPOSED_TOOLS`
  freischalten.
- **Schreib-Tools** (`manage_memory`, `manage_userdata`, `manage_learnings`) werden über MCP
  **grundsätzlich blockiert** — selbst wenn sie versehentlich in `MCP_EXPOSED_TOOLS` stehen. Nur ein
  explizites `MCP_ALLOW_WRITE_TOOLS=true` hebt das auf (für vertrauenswürdige Setups).
- **Im Web-UI konfigurierbar:** unter *Einstellungen → MCP-Server* lassen sich Exposition (an/aus), die
  exponierten Tools und der Schreibzugriff **live** einstellen (ohne Neustart). UI-Werte haben Vorrang
  vor den `MCP_*`-Umgebungsvariablen.
- **Connect-Handshake:** Der Server sendet beim Verbinden einen `instructions`-Text, der Edda als das
  persistente Langzeitgedächtnis des Agents beschreibt und Clients anweist, zuerst `search_memory`
  aufzurufen.

**HTTP/SSE** (Claude Code, Cursor, Remote-Clients) — `.mcp.json`:
```jsonc
{
  "mcpServers": {
    "edda": {
      "url": "http://<host>:8080/mcp",
      "headers": { "Authorization": "Bearer <EDDA_AUTH_TOKEN>" }
    }
  }
}
```
> Ohne gesetzten `EDDA_AUTH_TOKEN` ist `/mcp` nur über Loopback erreichbar (kein Header nötig).

**stdio** (lokale Clients, z. B. Claude Desktop):
```jsonc
{
  "mcpServers": {
    "edda": { "command": "dotnet", "args": ["run", "--project", "src/Edda.Mcp.Stdio"] }
  }
}
```

---

## Konfiguration (`.env`)

| Variable | Zweck |
|---|---|
| `EDDA_BIND` / `EDDA_PORT` | Host-Bind (`127.0.0.1` = lokal, `0.0.0.0` = remote) + Port |
| `EMBEDDING_PROVIDER` / `EMBEDDING_API_KEY` | Embeddings (`openai`/`google`/`voyage`/`ollama`/`custom`/`null`) |
| `INGESTION_ENRICHER=llm` + `INGESTION_LLM_PROVIDER` / `_API_KEY` | LLM-Anreicherung beim Import |
| `EDDA_AUTH_TOKEN` | Bearer-Token für `/api/*` und `/mcp` (leer = nur Loopback) |
| `MCP_SERVER_ENABLED` | MCP-Server an/aus |
| `MCP_EXPOSED_TOOLS` | Allowlist der MCP-Tools (Default: die 2 Lese-Tools `search_memory`, `list_memory`) |
| `MCP_ALLOW_WRITE_TOOLS` | Schreib-Tools über MCP erlauben (Default: aus → read-only) |

Vollständige Vorlage mit allen Variablen: **`.env.example`**.

---

## Dokumentation

| Datei | Inhalt |
|-------|--------|
| `docs/erste-schritte.md` | **Einstieg:** geführtes 15-Minuten-Tutorial |
| `docs/glossar.md` | Alle Fachbegriffe erklärt |
| `CLAUDE.md` | Architektur, Projektliste, Regeln |
| `docs/architektur.md` | Projektgraph + DI-Fluss |
| `docs/mcp.md` | MCP HTTP + stdio, Allowlist, Auth |
| `docs/embeddings.md` | Embedding-Provider + Rebuild |
| `docs/tdk.md` | TDK-Validierung + Sandbox |
| `docs/betrieb.md` | Compose, Env-Variablen, Ports |
| `docs/adr/` | Architecture Decision Records |

## Tests

```bash
dotnet test Edda.slnx     # Unit-Tests, ohne Infrastruktur
```
