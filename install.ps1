<#
  Edda - interaktives Deployment-Skript (Windows / PowerShell).
  Pendant zu install.sh (Mac/Linux). Flow:
    1. Eingaben abfragen (Zielserver/Port, optionale erste Keys - ueberspringbar)
    2. .env schreiben (Secrets; gitignored)
    3. docker compose up -d --build
    4. Erreichbarkeit testen (Health + Graph)
    5. Bei Erfolg klickbaren Link ausgeben

  Aufruf (nach `git pull`):  .\install.ps1
  Falls die Ausfuehrung blockiert ist:  powershell -ExecutionPolicy Bypass -File .\install.ps1
#>
#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

function Write-Bold($m) { Write-Host $m -ForegroundColor White }
function Write-Ok($m)   { Write-Host $m -ForegroundColor Green }
function Write-Warn($m) { Write-Host $m -ForegroundColor Yellow }
function Write-Err($m)  { Write-Host $m -ForegroundColor Red }

# Verdeckte Eingabe -> Klartext (fuer die .env). Leere Eingabe ergibt "".
function Read-Secret($prompt) {
    $sec = Read-Host -Prompt $prompt -AsSecureString
    return [System.Net.NetworkCredential]::new('', $sec).Password
}

Write-Bold "==== Edda Deployment (Windows) ============================="

# -- 1) Voraussetzungen ------------------------------------------------------
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    Write-Err "Docker ist nicht installiert oder nicht im PATH."
    exit 1
}
docker compose version *> $null
if ($LASTEXITCODE -ne 0) {
    Write-Err "'docker compose' (v2) ist nicht verfuegbar."
    exit 1
}

# -- 2) Eingaben -------------------------------------------------------------
$host_ = Read-Host "Zielserver-Host/IP fuer den Zugriff [127.0.0.1]"
if ([string]::IsNullOrWhiteSpace($host_)) { $host_ = "127.0.0.1" }
$port = Read-Host "Port [8080]"
if ([string]::IsNullOrWhiteSpace($port)) { $port = "8080" }

# Lokal nur an Loopback binden; bei entferntem Host an allen Interfaces (remote erreichbar).
$bind = if ($host_ -in @("127.0.0.1", "localhost")) { "127.0.0.1" } else { "0.0.0.0" }

Write-Host ""
Write-Host "---- Embeddings ----"
$useOllamaIn = Read-Host "Lokales Embedding via Ollama im Stack (self-hosted, kein API-Key)? [j/N]"
$useOllama  = $false
$embProvider = ""
$embKey      = ""
$embModel    = ""
$embBaseUrl  = ""
$embModels   = @()
if ($useOllamaIn -match '^(j|J|y|Y|ja|Ja|yes|Yes)$') {
    $useOllama   = $true
    $embProvider = "ollama"
    $embBaseUrl  = "http://ollama:11434"
    $embModel = Read-Host "  Embedding-Modell [bge-m3]"
    if ([string]::IsNullOrWhiteSpace($embModel)) { $embModel = "bge-m3" }
    $extra = Read-Host "  Weitere Modelle vorab laden (Komma-getrennt, leer = keine)"
    $embModels = @($embModel)
    if (-not [string]::IsNullOrWhiteSpace($extra)) {
        $embModels += ($extra -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    }
} else {
    Write-Host "Erste Keys - Enter = ueberspringen (spaeter in der UI unter 'Einstellungen' konfigurierbar):"
    $embProvider = Read-Host "  Embedding-Provider (openai/google/voyage/custom) [skip]"
    if (-not [string]::IsNullOrWhiteSpace($embProvider)) { $embKey = Read-Secret "  Embedding-API-Key" }
}
$llmProvider = Read-Host "  LLM-Provider fuer Enrichment (anthropic/openai/openrouter/gemini/bedrock/ollama/custom) [skip]"
$llmKey = ""
if (-not [string]::IsNullOrWhiteSpace($llmProvider)) { $llmKey = Read-Secret "  LLM-API-Key" }
$authToken = Read-Secret "Optionaler Auth-Token (Bearer fuer /api und /mcp; Enter = nur lokal/Loopback)"

# -- 3) .env schreiben -------------------------------------------------------
if (Test-Path .env) {
    Copy-Item .env .env.bak -Force
    Write-Warn "Bestehende .env nach .env.bak gesichert."
}

$enricher       = if (-not [string]::IsNullOrWhiteSpace($llmProvider)) { "llm" }  else { "" }
$embProviderOut = if ([string]::IsNullOrWhiteSpace($embProvider))      { "null" } else { $embProvider }

# Neo4j-Zufallspasswort erzeugen (24 alphanumerische Zeichen; kein '/', da NEO4J_AUTH=neo4j/<pw>
# den Schraegstrich als Trenner nutzt). So startet die DB NICHT im offenen No-Auth-Modus.
$neo4jRng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$neo4jBytes = New-Object 'System.Byte[]' 48
$neo4jRng.GetBytes($neo4jBytes)
$neo4jRng.Dispose()
$neo4jPw = (([System.Convert]::ToBase64String($neo4jBytes)) -replace '[^A-Za-z0-9]', '').Substring(0, 24)

$lines = @(
    "# Von install.ps1 erzeugt. Enthaelt Secrets - niemals committen (steht in .gitignore)."
    "EDDA_BIND=$bind"
    "EDDA_PORT=$port"
    "EMBEDDING_PROVIDER=$embProviderOut"
    "EMBEDDING_API_KEY=$embKey"
    "EMBEDDING_MODEL=$embModel"
    "EMBEDDING_BASE_URL=$embBaseUrl"
    "INGESTION_ENRICHER=$enricher"
    "INGESTION_LLM_PROVIDER=$llmProvider"
    "INGESTION_LLM_API_KEY=$llmKey"
    "EDDA_AUTH_TOKEN=$authToken"
    "# Neo4j-Authentifizierung (automatisch erzeugtes Zufallspasswort - DB nicht offen)."
    "NEO4J_AUTH=neo4j/$neo4jPw"
    "NEO4J_AUTH_MODE=basic"
    "NEO4J_USERNAME=neo4j"
    "NEO4J_PASSWORD=$neo4jPw"
)
# Ollama-Profil aktivieren, damit jeder docker-compose-Aufruf den Embedding-Server mitstartet.
if ($useOllama) { $lines += "COMPOSE_PROFILES=local-embeddings" }
# A8: Docker-GID fuer das TDK-Override (docker-compose.tdk.yml). Unter Docker Desktop
# (Windows/macOS) ist der Socket in der VM offen - der Default 0 genuegt hier.
$lines += "DOCKER_GID=0"
# UTF-8 OHNE BOM schreiben - ein BOM wuerde die erste .env-Variable fuer docker compose unbrauchbar machen.
$envPath = Join-Path $PSScriptRoot ".env"
[System.IO.File]::WriteAllText($envPath, (($lines -join "`n") + "`n"), (New-Object System.Text.UTF8Encoding $false))
Write-Ok ".env geschrieben (Bind ${bind}:${port})."
Write-Ok "Neo4j-Zufallspasswort erzeugt und in .env hinterlegt (keine offene DB)."

# -- 4) Deploy ---------------------------------------------------------------
Write-Host ""
Write-Bold "---- Docker-Deploy (Build + Start) -------------------------"
docker compose up -d --build
if ($LASTEXITCODE -ne 0) {
    Write-Err "docker compose ist fehlgeschlagen."
    exit 1
}

# -- 5) Erreichbarkeit testen ------------------------------------------------
Write-Host ""
Write-Bold "---- Erreichbarkeit pruefen --------------------------------"
$healthUrl = "http://127.0.0.1:$port/health"
$statsUrl  = "http://127.0.0.1:$port/api/akg/stats"

Write-Host "Warte auf Edda (Health-Check, bis zu 120s) ..."
$up = $false
for ($i = 0; $i -lt 60; $i++) {
    try {
        $r = Invoke-WebRequest -Uri $healthUrl -UseBasicParsing -TimeoutSec 5
        if ($r.StatusCode -eq 200) { $up = $true; break }
    } catch { }
    Start-Sleep -Seconds 2
}

if (-not $up) {
    Write-Err "Edda antwortet nicht auf $healthUrl."
    Write-Err "Logs ansehen:  docker compose logs --tail 50 edda"
    exit 1
}

# Embedding-Modelle in den Ollama-Dienst laden (nur wenn lokales Embedding gewaehlt wurde).
if ($useOllama) {
    Write-Host ""
    Write-Bold "---- Embedding-Modelle laden (Ollama) ----------------------"
    Write-Host "Warte auf Ollama ..."
    for ($i = 0; $i -lt 30; $i++) {
        docker compose exec -T ollama ollama list *> $null
        if ($LASTEXITCODE -eq 0) { break }
        Start-Sleep -Seconds 2
    }
    foreach ($m in $embModels) {
        Write-Host "  -> ollama pull $m"
        docker compose exec -T ollama ollama pull $m
        if ($LASTEXITCODE -ne 0) { Write-Warn "Pull von '$m' fehlgeschlagen - spaeter: docker compose exec ollama ollama pull $m" }
    }
    Write-Ok "[OK] Embedding-Modelle bereit (Provider=ollama, Modell=$embModel)."
}

# Tieferer Check: Wissensgraph (Neo4j). Auth-Token mitschicken, falls gesetzt.
$graphOk = $false
try {
    $headers = @{}
    if (-not [string]::IsNullOrWhiteSpace($authToken)) { $headers["Authorization"] = "Bearer $authToken" }
    $r = Invoke-WebRequest -Uri $statsUrl -Headers $headers -UseBasicParsing -TimeoutSec 5
    if ($r.StatusCode -eq 200) { $graphOk = $true }
} catch { }

# -- 6) Ergebnis -------------------------------------------------------------
Write-Host ""
Write-Ok "[OK] Edda laeuft und ist erreichbar."
if ($graphOk) {
    Write-Ok "[OK] Wissensgraph (Neo4j) erreichbar."
} else {
    Write-Warn "[!] Graph-Stats nicht erreichbar (Neo4j evtl. noch im Hochlauf, oder Auth aktiv)."
}
Write-Host ""
Write-Bold "Edda oeffnen:"
Write-Host "   http://${host_}:${port}" -ForegroundColor Cyan
Write-Host ""
Write-Host "Stoppen:  docker compose down       Logs:  docker compose logs -f edda"
