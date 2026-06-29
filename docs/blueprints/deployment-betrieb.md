# Deployment & Betrieb

## Zweck

Vereinfacht den Weg von `git pull` zu „läuft und ist erreichbar". Ein containerisierter Stack (Edda +
Neo4j) plus interaktive Installskripte, die Zielserver/Port und optionale erste Keys abfragen, `.env`
schreiben, bauen, starten, die Erreichbarkeit testen und den Link ausgeben. Kein Code-Feature im engeren
Sinn, aber zentral für Inbetriebnahme und Betrieb.

## Dateien

| Pfad | Rolle |
|------|-------|
| `install.sh` | Interaktives Deployment (Mac/Linux, bash): Eingaben → `.env` → `docker compose up --build` → Health-Check → Link. |
| `install.ps1` | Pendant für Windows (PowerShell, ASCII-only; `.env` UTF-8 ohne BOM). |
| `docker-compose.yml` | Services `edda` (Build aus `Dockerfile`) + `neo4j`; Bind/Port via `${EDDA_BIND}`/`${EDDA_PORT}`. |
| `Dockerfile` | Multi-Stage-Build (`dotnet publish` → Runtime-Image). |
| `.env.example` | Vorlage aller Umgebungsvariablen (Netzwerk, Embeddings, LLM, Auth, MCP, Neo4j, TDK). |

## Abhängigkeiten

### Intern
- Baut das **Web-UI** (`Edda.Web`) als Container und startet es gegen **Neo4j**.

### Extern
- **Docker** + **Docker Compose v2**. Neo4j-Image `neo4j:5.19` (mit APOC).

## Öffentliche API / Interface

- `./install.sh` bzw. `.\install.ps1` — interaktiver Ablauf (Keys überspringbar).
- Ports: `8080` (UI + REST + `/mcp`), `7474`/`7687` (Neo4j-Browser/Bolt).
- Kern-Env (vollständig in `.env.example`): `EDDA_BIND`/`EDDA_PORT`, `EMBEDDING_*`, `INGESTION_ENRICHER` +
  `INGESTION_LLM_*`, `EDDA_AUTH_TOKEN`, `MCP_SERVER_ENABLED` / `MCP_EXPOSED_TOOLS` / `MCP_ALLOW_WRITE_TOOLS`,
  `NEO4J_AUTH`, `TDK_SANDBOX_TYPE`.

## Datenfluss / Call-Flow

1. Installskript prüft Docker, fragt Zielserver/Port + erste Keys (überspringbar) ab.
2. Schreibt `.env` (Bind leitet sich ab: `localhost` → nur lokal, sonst `0.0.0.0`); Secrets bleiben lokal,
   `.env` ist gitignored.
3. `docker compose up -d --build` baut das Image und startet Edda gegen das healthy Neo4j.
4. Health-Check (`/health` + `/api/akg/stats`); bei Erfolg wird der klickbare Link ausgegeben.

## Offene Fragen / TODOs

- Für Remote-Betrieb ist `EDDA_AUTH_TOKEN` zu setzen (sonst ist `/api/*` und `/mcp` nur über Loopback
  erreichbar); ein Reverse-Proxy/TLS ist nicht Teil des Stacks.
