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
| `EDDA_BIND` | `127.0.0.1` | Host-Bind-Adresse (`0.0.0.0` = alle Interfaces, remote erreichbar) |
| `EDDA_ALLOW_INSECURE_REMOTE` | (leer) | `true` hebt den Fail-Fast bei Remote-Bind ohne Token auf |
| `EDDA_TRUSTED_PROXIES` | (leer) | kommagetrennte Proxy-IPs, deren `X-Forwarded-*` vertraut wird (leer = Header ignoriert) |
| `TDK_SANDBOX_TYPE` | `docker` | docker/wasm/null |
| `INGESTION_ENRICHER` | (leer) | `llm` aktiviert den opt-in LLM-Enricher (Verdichtung + Relationen) |
| `INGESTION_ENTITY_EXTRACTION` | (leer) | `true` aktiviert die opt-in Entity-Extraktion beim Ingest |
| `INGESTION_LLM_PROVIDER` | (leer → `openrouter`) | Provider bei Aktivierung; lokal empfohlen: `ollama` |

## Sicherheit: Remote-Bind-Guard

Standardmäßig bindet Edda nur auf Loopback (`127.0.0.1`) und ist damit ausschließlich lokal
erreichbar. Wer den Dienst im Netz freigeben will (`EDDA_BIND=0.0.0.0`), **muss** einen
Zugriffs-Token setzen (`EDDA_AUTH_TOKEN`). Andernfalls verweigert die App beim Start den Dienst
(Fail-Fast) mit einer klaren Fehlermeldung, statt API und UI unauthentifiziert ins Netz zu stellen.

Ist ein bewusst unauthentifizierter Remote-Betrieb gewünscht — etwa hinter einem Reverse-Proxy, der
die Authentifizierung selbst übernimmt —, lässt sich der Guard mit `EDDA_ALLOW_INSECURE_REMOTE=true`
deaktivieren. Der Guard wertet primär `EDDA_BIND` aus; beim direkten `dotnet run` ohne `EDDA_BIND`
zieht er ersatzweise `ASPNETCORE_URLS` heran. Der interne All-Interfaces-Bind des Containers zählt
nicht als Remote-Freigabe — dort entscheidet allein `EDDA_BIND` über die Host-seitige Erreichbarkeit.

## Remote-Betrieb hinter Reverse-Proxy (Caddy/nginx, TLS)

Edda terminiert **selbst kein TLS** und macht **keine HTTPS-Weiterleitung** (kein `UseHttpsRedirection`):
der Kestrel-Server liefert reines HTTP auf der gebundenen Adresse aus. Lokal (Loopback) ist das
unkritisch; für **öffentlichen/Remote-Betrieb terminiert ein vorgelagerter Reverse-Proxy das TLS**
(Let's Encrypt o. ä.) und reicht die Requests intern per HTTP an Edda weiter.

Empfohlenes Setup für den Remote-Betrieb:

1. **Edda nur intern erreichbar machen** — an Loopback binden (`EDDA_BIND=127.0.0.1`), sodass ausschließlich
   der Proxy auf demselben Host zugreift. Der öffentliche Port gehört dem Proxy, nicht Kestrel.
2. **Authentifizierung** — `EDDA_AUTH_TOKEN` setzen (Bearer-Token für `/api/akg/*` + `/mcp`). Übernimmt der
   Proxy die Authentifizierung selbst und wird Edda bewusst an ein Nicht-Loopback-Interface gebunden, lässt
   sich der Remote-Bind-Guard mit `EDDA_ALLOW_INSECURE_REMOTE=true` deaktivieren (siehe oben).
3. **Forwarded-Header** — die Proxy-IP in `EDDA_TRUSTED_PROXIES` eintragen, damit Edda die echte Client-IP
   und das Schema (http/https) sieht. Details samt nginx-/Caddy-Snippets im folgenden Abschnitt
   „Betrieb hinter einem Reverse-Proxy (Forwarded Headers)".
4. **TLS am Proxy terminieren:**
   - **Caddy** holt und erneuert Let's-Encrypt-Zertifikate automatisch — das `Caddyfile` aus dem
     Forwarded-Header-Abschnitt genügt bereits für HTTPS.
   - **nginx** benötigt Zertifikate (z. B. via `certbot`) und die `ssl_certificate`/`ssl_certificate_key`-
     Direktiven im `listen 443 ssl`-Block.

Da Edda selbst nicht auf HTTPS umleitet, sollte der Proxy HTTP→HTTPS erzwingen: Caddy tut das
automatisch; bei nginx per zusätzlichem `listen 80`-Server, der mit `return 301 https://$host$request_uri;`
umleitet.

## Betrieb hinter einem Reverse-Proxy (Forwarded Headers)

Hinter einem Reverse-Proxy (nginx, Caddy, Traefik …) ist der direkte Peer aller Requests der Proxy —
`RemoteIpAddress` wäre also für jeden Request die Proxy-IP (oft Loopback), und IP-basierte Logik wie das
Rate-Limiting würde alle Clients in einen Topf werfen. Damit Edda die echte Client-IP und das Schema
(http/https) sieht, muss es die `X-Forwarded-For`/`X-Forwarded-Proto`-Header auswerten.

Aus Sicherheitsgründen ist das **opt-in**: Edda vertraut diesen Headern nur, wenn die Proxy-IP in
`EDDA_TRUSTED_PROXIES` steht (kommagetrennt). Ohne die Variable werden Forwarded-Header **ignoriert**
(sicherer Default) — sonst könnte ein direkter Client seine Quell-IP fälschen. Es werden ausschließlich
die gelisteten Proxys vertraut (die Framework-Defaults für Loopback werden bewusst entfernt); ein lokal
laufender Proxy muss also mit seiner Loopback-Adresse (`127.0.0.1` bzw. `::1`) eingetragen werden.

```bash
# Reverse-Proxy läuft lokal auf demselben Host:
EDDA_TRUSTED_PROXIES=127.0.0.1,::1
```

**nginx** (Ausschnitt) — terminiert TLS und setzt die Standard-Forwarded-Header:

```nginx
server {
    listen 443 ssl;
    server_name edda.example.com;
    # ssl_certificate / ssl_certificate_key …

    location / {
        proxy_pass         http://127.0.0.1:8080;
        proxy_set_header   Host              $host;
        proxy_set_header   X-Forwarded-For   $proxy_add_x_forwarded_for;
        proxy_set_header   X-Forwarded-Proto $scheme;
        # WebSocket/SSE (Blazor-Circuit, MCP-SSE):
        proxy_http_version 1.1;
        proxy_set_header   Upgrade           $http_upgrade;
        proxy_set_header   Connection        "upgrade";
    }
}
```

**Caddy** (`Caddyfile`) — setzt die Forwarded-Header automatisch:

```caddy
edda.example.com {
    reverse_proxy 127.0.0.1:8080
}
```

Läuft der Proxy auf einem anderen Host, trägt man dessen IP(s) statt der Loopback-Adressen ein; bei einer
Proxy-Kette müssen alle Hop-IPs aufgeführt werden.

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
