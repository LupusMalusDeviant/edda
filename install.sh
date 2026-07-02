#!/usr/bin/env bash
#
# Edda — interaktives Deployment-Skript.
#
# Flow:
#   1. Eingaben abfragen (Zielserver/Port, optionale erste Keys für Embedding & LLM — überspringbar).
#   2. .env schreiben (Secrets; gitignored).
#   3. Docker-Deploy ausführen (docker compose up -d --build).
#   4. Erreichbarkeit testen (Health-Check, optional Graph-Check).
#   5. Bei Erfolg klickbaren Link ausgeben.
#
# Aufruf (nach `git pull`):  ./install.sh
#
set -euo pipefail

# Immer aus dem Repo-Verzeichnis arbeiten (das Skript liegt im Repo-Root).
cd "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

bold() { printf '\033[1m%s\033[0m\n' "$1"; }
ok()   { printf '\033[32m%s\033[0m\n' "$1"; }
warn() { printf '\033[33m%s\033[0m\n' "$1"; }
err()  { printf '\033[31m%s\033[0m\n' "$1" >&2; }

bold "── Edda Deployment ─────────────────────────────────────────"

# ── 1) Voraussetzungen ──────────────────────────────────────────────────────
if ! command -v docker >/dev/null 2>&1; then
    err "Docker ist nicht installiert oder nicht im PATH."
    exit 1
fi
if ! docker compose version >/dev/null 2>&1; then
    err "'docker compose' (v2) ist nicht verfügbar."
    exit 1
fi

# ── 2) Eingaben ─────────────────────────────────────────────────────────────
read -rp "Zielserver-Host/IP für den Zugriff [127.0.0.1]: " HOST || true
HOST="${HOST:-127.0.0.1}"
read -rp "Port [8080]: " PORT || true
PORT="${PORT:-8080}"

# Lokal nur an Loopback binden; bei entferntem Host an allen Interfaces (remote erreichbar).
case "$HOST" in
    127.0.0.1|localhost) BIND="127.0.0.1" ;;
    *)                   BIND="0.0.0.0"   ;;
esac

echo
echo "── Embeddings ──"
read -rp "Lokales Embedding via Ollama im Stack (self-hosted, kein API-Key)? [j/N]: " USE_OLLAMA_IN || true
USE_OLLAMA=0
EMB_PROVIDER=""
EMB_KEY=""
EMB_MODEL=""
EMB_BASE_URL=""
EMB_MODELS=""
case "${USE_OLLAMA_IN:-}" in
    j|J|y|Y|ja|Ja|yes|Yes)
        USE_OLLAMA=1
        EMB_PROVIDER="ollama"
        EMB_BASE_URL="http://ollama:11434"
        read -rp "  Embedding-Modell [bge-m3]: " EMB_MODEL || true
        EMB_MODEL="${EMB_MODEL:-bge-m3}"
        read -rp "  Weitere Modelle vorab laden (Komma-getrennt, leer = keine): " EXTRA || true
        EMB_MODELS="$EMB_MODEL"
        [ -n "${EXTRA:-}" ] && EMB_MODELS="$EMB_MODEL ${EXTRA//,/ }"
        ;;
    *)
        echo "Erste Keys — Enter = überspringen (später in der UI unter „Einstellungen\" konfigurierbar):"
        read -rp "  Embedding-Provider (openai/google/voyage/custom) [skip]: " EMB_PROVIDER || true
        if [ -n "${EMB_PROVIDER:-}" ]; then
            read -rsp "  Embedding-API-Key: " EMB_KEY || true; echo
        fi
        ;;
esac
read -rp "  LLM-Provider für Enrichment (anthropic/openai/openrouter/gemini/bedrock/ollama/custom) [skip]: " LLM_PROVIDER || true
LLM_KEY=""
if [ -n "${LLM_PROVIDER:-}" ]; then
    read -rsp "  LLM-API-Key: " LLM_KEY || true; echo
fi
read -rsp "Optionaler Auth-Token (Bearer für /api & /mcp; Enter = nur lokal/Loopback): " AUTH_TOKEN || true
echo

# ── 3) .env schreiben ───────────────────────────────────────────────────────
if [ -f .env ]; then
    cp .env ".env.bak"
    warn "Bestehende .env nach .env.bak gesichert."
fi

ENRICHER=""
[ -n "${LLM_PROVIDER:-}" ] && ENRICHER="llm"

# Neo4j-Zufallspasswort erzeugen (24 alphanumerische Zeichen; kein '/', da NEO4J_AUTH=neo4j/<pw>
# den Schrägstrich als Trenner nutzt). So startet die DB NICHT im offenen No-Auth-Modus.
NEO4J_PW="$(head -c 32 /dev/urandom | base64 | tr -dc 'A-Za-z0-9' | cut -c1-24)"

cat > .env <<EOF
# Von install.sh erzeugt. Enthält Secrets — niemals committen (steht in .gitignore).
EDDA_BIND=$BIND
EDDA_PORT=$PORT
EMBEDDING_PROVIDER=${EMB_PROVIDER:-null}
EMBEDDING_API_KEY=$EMB_KEY
EMBEDDING_MODEL=$EMB_MODEL
EMBEDDING_BASE_URL=$EMB_BASE_URL
INGESTION_ENRICHER=$ENRICHER
INGESTION_LLM_PROVIDER=${LLM_PROVIDER:-}
INGESTION_LLM_API_KEY=$LLM_KEY
EDDA_AUTH_TOKEN=${AUTH_TOKEN:-}
# Neo4j-Authentifizierung (automatisch erzeugtes Zufallspasswort — DB nicht offen).
NEO4J_AUTH=neo4j/$NEO4J_PW
NEO4J_AUTH_MODE=basic
NEO4J_USERNAME=neo4j
NEO4J_PASSWORD=$NEO4J_PW
EOF
# Ollama-Profil aktivieren, damit jeder `docker compose`-Aufruf den Embedding-Server mitstartet.
[ "$USE_OLLAMA" = "1" ] && echo "COMPOSE_PROFILES=local-embeddings" >> .env
ok ".env geschrieben (Bind ${BIND}:${PORT})."
ok "Neo4j-Zufallspasswort erzeugt und in .env hinterlegt (keine offene DB)."

# ── 4) Deploy ───────────────────────────────────────────────────────────────
echo
bold "── Docker-Deploy (Build + Start) ───────────────────────────"
docker compose up -d --build

# ── 5) Erreichbarkeit testen ────────────────────────────────────────────────
echo
bold "── Erreichbarkeit prüfen ───────────────────────────────────"
HEALTH="http://127.0.0.1:${PORT}/health"
STATS="http://127.0.0.1:${PORT}/api/akg/stats"

echo "Warte auf Edda (Health-Check, bis zu 120s) ..."
up=0
for _ in $(seq 1 60); do
    code=$(curl -fsS -o /dev/null -w "%{http_code}" "$HEALTH" 2>/dev/null || echo 000)
    if [ "$code" = "200" ]; then up=1; break; fi
    sleep 2
done

if [ "$up" != "1" ]; then
    err "Edda antwortet nicht auf $HEALTH."
    err "Logs ansehen:  docker compose logs --tail 50 edda"
    exit 1
fi

# Embedding-Modelle in den Ollama-Dienst laden (nur wenn lokales Embedding gewählt wurde).
if [ "$USE_OLLAMA" = "1" ]; then
    echo
    bold "── Embedding-Modelle laden (Ollama) ────────────────────────"
    echo "Warte auf Ollama ..."
    for _ in $(seq 1 30); do
        if docker compose exec -T ollama ollama list >/dev/null 2>&1; then break; fi
        sleep 2
    done
    for m in $EMB_MODELS; do
        echo "  → ollama pull $m"
        docker compose exec -T ollama ollama pull "$m" \
            || warn "Pull von '$m' fehlgeschlagen — später: docker compose exec ollama ollama pull $m"
    done
    ok "Embedding-Modelle bereit (Provider=ollama, Modell=${EMB_MODEL})."
fi

# Tieferer Check: Wissensgraph (Neo4j) erreichbar? Auth-Token mitschicken, falls gesetzt.
auth_hdr=()
[ -n "${AUTH_TOKEN:-}" ] && auth_hdr=(-H "Authorization: Bearer ${AUTH_TOKEN}")
graph=$(curl -fsS "${auth_hdr[@]}" -o /dev/null -w "%{http_code}" "$STATS" 2>/dev/null || echo 000)

# ── 6) Ergebnis ─────────────────────────────────────────────────────────────
echo
ok "✓ Edda läuft und ist erreichbar."
if [ "$graph" = "200" ]; then
    ok "✓ Wissensgraph (Neo4j) erreichbar."
else
    warn "⚠ Graph-Stats lieferten HTTP $graph (Neo4j evtl. noch im Hochlauf, oder Auth aktiv)."
fi
echo
bold "Edda öffnen:"
printf '\033[1;36m   http://%s:%s\033[0m\n' "$HOST" "$PORT"
echo
echo "Stoppen:  docker compose down       Logs:  docker compose logs -f edda"
