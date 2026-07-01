# Betrieb

## Voraussetzungen

- .NET 10 SDK (siehe `global.json`)
- Docker (für die Graph-DB und — optional — die TDK-Docker-Sandbox)

## Start-Varianten

```bash
# Entwicklung: DB im Container, App per dotnet run
docker compose up -d neo4j
dotnet run --project src/Web

# Vollständig containerisiert (App + DB)
docker compose up -d --build
```

Die App bindet lokal auf `http://127.0.0.1:8080` (UI, REST, MCP). Der Container exponiert
`8080` nur auf `127.0.0.1`.

## Ports

| Port | Dienst |
|------|--------|
| 8080 | Edda (UI + REST + MCP) — nur loopback |
| 7474 | Neo4j Browser |
| 7687 | Neo4j Bolt |

## Wichtige Env-Variablen (siehe `.env.example`)

| Variable | Default | Zweck |
|----------|---------|-------|
| `GRAPH_PROVIDER` | `neo4j` | `neo4j` oder `memgraph` |
| `NEO4J_URI` | `bolt://neo4j:7687` | Bolt-URL (lokal: `bolt://localhost:7687`) |
| `NEO4J_AUTH` | `none` | `none` oder `basic` |
| `EMBEDDING_PROVIDER` | `null` | openai/google/voyage/ollama/custom/null |
| `MCP_SERVER_ENABLED` | `true` | HTTP-MCP-Endpoint aktivieren |
| `MCP_EXPOSED_TOOLS` | `search_memory,list_memory` | Allow-Liste (die 2 Lese-Tools) |
| `EDDA_AUTH_TOKEN` | (leer) | optionaler Bearer-Token für `/api/akg/*` + `/mcp` |
| `TDK_SANDBOX_TYPE` | `docker` | docker/wasm/null |
| `INGESTION_ENRICHER` | (leer) | `llm` aktiviert den opt-in LLM-Enricher (Verdichtung + Relationen) |
| `INGESTION_ENTITY_EXTRACTION` | (leer) | `true` aktiviert die opt-in Entity-Extraktion beim Ingest |
| `INGESTION_LLM_PROVIDER` | (leer → `openrouter`) | Provider bei Aktivierung; lokal empfohlen: `ollama` |

## Volumes & Verzeichnisse

| Pfad | Inhalt |
|------|--------|
| `./knowledge` | Wissensregeln (World, Coding, Security, Tool-Docs) — beim Start in den Graph geladen |
| `./data` | Laufzeitdaten (Audit-Log, Feedback-DB, Schlüssel) — git-ignoriert |

## REST-Endpoints (Auszug)

| Methode | Route | Zweck |
|---------|-------|-------|
| GET | `/health` | Health-Check (anonym) |
| GET | `/api/akg/rules` · `/api/akg/rules/{id}` · `/rules/{id}/neighbors` | Regeln lesen |
| POST | `/api/akg/propose` · DELETE `/api/akg/rules/{id}` | Regeln schreiben/löschen |
| GET | `/api/akg/stats` · `/api/akg/context?task=…` | Statistik / Kontext-Kompilierung |
| POST | `/api/akg/reload` · `/embed/rebuild` · `/world-knowledge/reload` · `/benchmark` | Admin-Operationen |
| POST | `/api/akg/ingest` · `/api/akg/entities/ingest` | Wissens-/Entity-Ingestion (Admin, opt-in via `ENABLE_INGESTION`) |

## Graph-DB-Wechsel auf Memgraph (optional)

`GRAPH_PROVIDER=memgraph` setzen, `NEO4J_URI` auf die Memgraph-Bolt-URL zeigen lassen und in
`docker-compose.yml` den auskommentierten `memgraph`-Block aktivieren. Der semantische Boost nutzt
dann den App-seitigen Cosine-Fallback (kein nativer Vektorindex).

## Optionale LLM-Extraktion (M2, opt-in)

Standardmäßig läuft Edda **local-only ohne LLM**. Für den automatischen Wissensgraph-Aufbau aus
Rohdaten (ADR-0010) lassen sich zwei voneinander unabhängige, per Default **abgeschaltete**
Ingest-Zeit-Schritte aktivieren:

| Schritt | Aktivierung | Wirkung |
|---------|-------------|---------|
| LLM-Enricher | `INGESTION_ENRICHER=llm` | verdichtet Rohtext + schlägt Relationen zu bestehenden Knoten vor |
| Entity-Extraktion | `INGESTION_ENTITY_EXTRACTION=true` | extrahiert typisierte Entitäten/Relationen (LightRAG-Stil) in den Entity-Layer |

Beide nutzen denselben, austauschbaren Provider (`INGESTION_LLM_PROVIDER` + `INGESTION_LLM_*`, Key im
Credential-Store). **Beide Wege sind first-class**; für den local-first-Betrieb wird **Ollama** empfohlen,
für gleichmäßigere Qualität ein Cloud-Provider.

### Lokales Ollama (empfohlen, zero-cloud)

```bash
# Ollama installieren (https://ollama.com); der Dienst läuft auf 127.0.0.1:11434
ollama pull llama3.1            # brauchbares Default-Modell für die Extraktion
```

```bash
INGESTION_ENRICHER=llm
INGESTION_ENTITY_EXTRACTION=true
INGESTION_LLM_PROVIDER=ollama
INGESTION_LLM_MODEL=llama3.1
INGESTION_LLM_BASE_URL=http://localhost:11434
```

Manueller Entity-Ingest (Admin, `ENABLE_INGESTION=true`): `POST /api/akg/entities/ingest` mit
`{ "text": "…", "domainHint": "…" }`.

> **Datenschutz:** Mit einem **Cloud**-Provider verlässt der ingestierte Inhalt die Maschine (an den
> Anbieter). Lokal (Ollama) bleibt alles auf dem Host. Kleine lokale Modelle liefern rauschendere Graphen
> — ein bewusst akzeptierter Kompromiss (ADR-0010). Ohne Aktivierung bleibt der Betrieb local-only.
