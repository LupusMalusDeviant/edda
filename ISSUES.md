# Edda — Audit: Issues & Umsetzungsplan

> Repo-weites Audit vom 2026-07-02. Fünf Analysebereiche (Retrieval/Embeddings, Ingestion/Memory,
> Security, Architektur/Tests, UI/DX) wurden gegen den Code geprüft und mit marktüblichen
> Memory-/Wissensmanagement-Systemen (Mem0, Zep, Letta, Cognee, Graphiti/LightRAG) verglichen.
> Zeilenangaben sind Näherungswerte zum Auffinden, nicht exakt.
>
> **Basis-Befund:** Build grün, 1000+ Tests grün, 0 Warnings, CLAUDE.md-Regeln fast vollständig
> eingehalten. Die Issues unten sind Lücken und Optimierungspotential, keine Brandherde.

---

## Inhalt

1. [Stärken (nicht anfassen)](#stärken)
2. [Marktvergleich](#marktvergleich)
3. [Issues — A: Sicherheit](#a--sicherheit)
4. [Issues — B: Retrieval-Qualität](#b--retrieval-qualität)
5. [Issues — C: Memory & Ingestion (Feature-Lücken vs. Markt)](#c--memory--ingestion)
6. [Issues — D: Architektur, API & Tests](#d--architektur-api--tests)
7. [Issues — E: UI, DX & Doku](#e--ui-dx--doku)
8. [Issues — F: TDK (Test-Driven Knowledge)](#f--tdk-test-driven-knowledge) — Detail-Evaluierung: `docs/tdk-vertiefung.md`
9. [Umsetzungsplan für ein schwächeres Modell](#umsetzungsplan)

---

## Stärken

Diese Punkte sind Alleinstellungsmerkmale bzw. sauber gelöst — **bei keinem Issue-Fix regressieren**:

- **Safety-First-MCP**: Default-Deny-Allowlist, Write-Tools doppelt blockiert (`McpExposurePolicy`).
- **TDK**: Wissen validiert Code aktiv (sandboxed) — hat kein Marktbegleiter.
- **Security-Layer**: AES-GCM-CredentialStore, HMAC/Merkle-Audit, SecretRedactor + InputSanitizer vor jedem LLM-Call.
- **Hybrid-Retrieval**: RRF (k=60) + MMR-Reranking + hierarchisches Coarse-to-Fine (ADR-0008/0009).
- **Adaptives Chunking**: stil-erkennend (Prose/Markdown/Code/Table), deterministisch, ohne Tokenizer-Download.
- **Codequalität**: Interface-First, TimeProvider, IFileSystem, ConfigureAwait, keine `.Result`/`.Wait()`-Deadlocks, 100%-XML-Doku.
- **Idempotente Ingestion**: deterministische Rule-IDs + `MERGE`-Upsert, Content-Hash-IDs bei `remember`.
- **Export/Import-Roundtrip**: `GET /api/knowledge/export` + `KnowledgeBundle` (mit `SchemaVersion`) existieren — entgegen erster Annahme vollständig.

---

## Marktvergleich

| Fähigkeit | Edda | Mem0 | Zep | Letta | Cognee |
|---|---|---|---|---|---|
| Auto-Extraktion aus Rohtext (opt-in LLM) | ✅ | ✅ | ✅ | ✅ | ✅ |
| Auto-Extraktion aus **Konversationen** | ❌ (C3-Umfeld) | ✅ | ✅ | ✅ | ✅ |
| Update/**Merge** statt Append | ❌ (C4) | ✅ | ✅ | ✅ | ✅ |
| Kontradiktions-Erkennung | ❌ (C3) | ✅ | ✅ | ✅ | ✅ |
| Temporale Kanten (validFrom/Until auf Relationen) | ❌ (C9) | ❌ | ✅ | teils | ✅ |
| Incremental Sync der Quellen | ❌ (C5) | ✅ | ✅ | ✅ | ✅ |
| BM25-/Hybrid-Volltext | teils (B1) | ✅ | ✅ | ✅ | ✅ |
| Multi-Tenant + Rollen | teils (C1/C2) | ✅ | ✅ | ✅ | ❌ |
| Read-only-MCP mit Default-Deny | ✅ **einzigartig** | ❌ | ❌ | ❌ | ❌ |
| Code-Validierung gegen Wissen (TDK) | ✅ **einzigartig** | ❌ | ❌ | ❌ | ❌ |
| Läuft ohne LLM / auf schwacher Hardware | ✅ | ❌ | ❌ | ❌ | teils |
| .NET-nativ | ✅ **einzigartig** | ❌ | ❌ | ❌ | ❌ |

**Fazit:** Die Moat-Features stehen. Die größten Markt-Lücken sind die „lebende Wissensbasis"
(Merge/Kontradiktion/Temporalität — Issues C3, C4, C9), Incremental Sync (C5) und der
geschlossene Feedback-Loop für Agenten (E2).

---

## Issue-Format

Jedes Issue: **ID · Titel** | Schweregrad (Kritisch/Hoch/Mittel/Niedrig) | Aufwand (S ≤ ½ Session, M = 1–2 Sessions, L = 3+) | Eignung fürs schwächere Modell (✅ direkt / ⚠️ mit Spec / ❌ stärkeres Modell).

---

## A — Sicherheit

### A1 · Auth-Token-Vergleich ist nicht timing-safe
**Kritisch (bei Remote-Exposure) · S · ✅**
`src/Edda.Hosting/Authentication/LocalAuthenticationHandler.cs:~48` vergleicht den Bearer-Token mit
`string.Equals(..., StringComparison.Ordinal)`. Das erlaubt theoretisch Timing-Seitenkanal-Angriffe.
- **Fix:** Beide Strings zu UTF-8-Bytes, Vergleich via `CryptographicOperations.FixedTimeEquals`. Längen-Ungleichheit darf nicht früh returnen (gegen Dummy-Puffer vergleichen).
- **Akzeptanz:** Unit-Test: korrekter Token ✓, falscher Token ✗, leerer Token ✗, Token anderer Länge ✗; kein `string.Equals` mehr auf dem Token-Pfad.

### A2 · Kein Rate-Limiting auf `/api/*` und `/mcp`
**Hoch · S · ✅**
`src/Web/Program.cs` registriert keine Rate-Limiting-Middleware — unbegrenzte Token-Brute-Force möglich.
- **Fix:** `AddRateLimiter` (Fixed-Window, z. B. 100 Req/min/IP, konfigurierbar via `EDDA_RATE_LIMIT_PER_MINUTE`, 0 = aus) + `UseRateLimiter()`; Policy auf die `/api`- und `/mcp`-Gruppen anwenden. `.env.example` ergänzen.
- **Akzeptanz:** 429 nach Limit-Überschreitung; Loopback-UI (Blazor-Circuit) bleibt unbeeinträchtigt; Tests für die Options-Bindung.

### A3 · Neo4j startet per Default ohne Auth
**Hoch · S · ✅**
`docker-compose.yml` (`NEO4J_AUTH=${NEO4J_AUTH:-none}`) und `.env.example` defaulten auf No-Auth. Bei Fehlkonfiguration (Port 7687 exponiert) steht die DB offen.
- **Fix:** Install-Skripte (`install.sh`, `install.ps1`) generieren ein Zufallspasswort und schreiben es in `.env`; `.env.example`-Default auf Platzhalter mit Warnkommentar; Startup-Log-Warnung im `Neo4jGraphDatabaseProvider`, wenn `AuthTokens.None` aktiv ist.
- **Akzeptanz:** Frische Installation via Skript hat Neo4j-Passwort; bestehende `.env` mit `none` funktioniert weiter, loggt aber eine Warnung.

### A4 · Remote-Bind ohne Auth-Token wird nicht verhindert
**Hoch · S · ✅**
`EDDA_BIND=0.0.0.0` ohne `EDDA_AUTH_TOKEN` exponiert API+UI unauthentifiziert; nur das Install-Skript warnt, der Code nicht.
- **Fix:** Startup-Guard in `src/Web/Program.cs`: Wenn Bind ≠ Loopback **und** Token leer → Fail-Fast mit klarer Fehlermeldung (Override-Env `EDDA_ALLOW_INSECURE_REMOTE=true` für bewusste Ausnahmen).
- **Akzeptanz:** Unit-Test der Guard-Logik (reine Funktion `IsInsecureRemoteBind(bind, token, allowInsecure)`); Doku-Absatz in `docs/betrieb.md`.

### A5 · Loopback-Erkennung hinter Reverse-Proxy umgehbar/kaputt
**Mittel · S · ✅**
Es gibt keine `ForwardedHeadersMiddleware`-Konfiguration. Hinter einem Proxy ist entweder jeder Request „Loopback" (Proxy lokal) oder die Loopback-Erkennung falsch.
- **Fix:** `ForwardedHeaders` opt-in via `EDDA_TRUSTED_PROXIES` (kommagetrennte IPs → `KnownProxies`); ohne Konfiguration bleiben Forwarded-Header **ignoriert** (sicherer Default).
- **Akzeptanz:** Doku in `docs/betrieb.md` (nginx/Caddy-Beispiel); Tests für das Options-Parsing.

### A6 · MCP-Token wird auch als Query-Parameter akzeptiert
**Mittel · S · ✅**
`src/Web/Program.cs:~58` akzeptiert `?token=` — Tokens landen in Logs, Browser-History, Referrern.
- **Fix:** Query-Parameter-Pfad entfernen; nur `Authorization: Bearer`. Falls stdio-/Legacy-Clients ihn brauchen: Deprecation-Warnung loggen statt sofort entfernen (Entscheidung im PR dokumentieren).
- **Akzeptanz:** Header-Auth ✓, Query-Auth 401 (oder Warnung); README/`docs/mcp.md` angepasst.

### A7 · Wasm-/Subprocess-Sandbox ohne Ressourcen-Limits
**Mittel · M · ⚠️**
Docker-Sandbox ist gehärtet (256 MB, 50 % CPU, network=none), aber der Wasm-/Subprocess-Pfad (`src/Sandboxing/Wasm/`) startet Python ohne cgroup/ulimit — DoS auf dem Host möglich.
- **Fix:** Hartes Wall-Clock-Kill (Prozess-Kill nach Timeout, existiert teils), `ProcessPriorityClass.BelowNormal`, Output-Größenlimit; unter Linux zusätzlich ulimit-Wrapper. Vollständige cgroup-Isolation ist out-of-scope — Restrisiko in `docs/tdk.md` dokumentieren.
- **Akzeptanz:** Endlosschleifen-Skript wird zuverlässig gekillt; Test mit Fake-Runner; Doku-Absatz.

### A8 · Edda-Container läuft als root (docker.sock)
**Mittel · M · ⚠️**
`Dockerfile:~37` nutzt `USER root` für den gemounteten Docker-Socket (TDK-Sandbox). Container-Escape-Risiko.
- **Fix (pragmatisch):** Non-root-User + `group_add: [docker-gid]` in Compose; docker.sock-Mount nur im `tdk`-Compose-Profil; Betrieb ohne TDK-Docker-Sandbox läuft non-root. Doku der Trade-offs.
- **Akzeptanz:** Standard-Compose läuft non-root; TDK-Profil dokumentiert.

### A9 · MCP-Token-Hashes ungesalzen (SHA-256)
**Niedrig · S · ✅**
`src/Security/Mcp/FileMcpTokenStore.cs:~138`. Für generierte High-Entropy-Tokens ausreichend, aber ohne Salt sind identische Tokens erkennbar und schwache manuelle Tokens offline-brute-forcebar.
- **Fix:** Pro Token zufälliges Salt speichern, Hash = SHA-256(salt ‖ token). Migration: Alt-Einträge ohne Salt weiter akzeptieren, beim nächsten Schreiben migrieren.
- **Akzeptanz:** Roundtrip-Tests neu+alt; Datei-Format-Version im JSON.

### A10 · SecretRedactor nicht im globalen Log-/Exception-Pfad
**Mittel · S · ✅**
Redaction läuft vor LLM-Calls, aber Exceptions (z. B. HTTP-Fehler mit API-Key in Message/URL) können ungefiltert in Logs landen.
- **Fix:** Globalen Exception-Handler (bzw. die zentrale Fehler-Middleware in `Edda.Hosting`) durch `ISecretRedactor.Redact(...)` auf Message + ToString() schleusen, bevor geloggt wird.
- **Akzeptanz:** Test: Exception mit `sk-...`-Muster in Message → Log-Ausgabe redacted.

### A11 · HTTPS-/Proxy-Betrieb nicht dokumentiert
**Niedrig · S · ✅**
Kein `UseHttpsRedirection`, kein TLS — für lokal ok, für Remote fehlt die Anleitung.
- **Fix:** Abschnitt „Remote-Betrieb hinter Reverse-Proxy (Caddy/nginx, TLS)" in `docs/betrieb.md`, inkl. Verweis auf A5-Konfiguration. Kein Code nötig.
- **Akzeptanz:** Doku-Review.

---

## B — Retrieval-Qualität

### B1 · Keyword-Phase: Substring-Matching ohne Tokenisierung/IDF
**Hoch · M · ⚠️**
`src/AKG/Context/KeywordScorer.cs:~65-95`: `string.Contains` statt Token-Match; häufige Tags wiegen wie seltene; additive Scores ohne Sättigung. Markt-Standard ist BM25 bzw. mindestens TF-IDF.
- **Fix (Stufe 1, fürs schwächere Modell):** Tokenisierung (Wortgrenzen, lowercase) statt Substring; Match nur auf ganze Tokens; Score-Sättigung (z. B. `log(1+n)`). **Stufe 2 (separat, Spec nötig):** leichte IDF-Gewichtung über Korpus-Statistik im Graph.
- **Akzeptanz:** Bestehende KeywordScorer-Tests grün bzw. bewusst angepasst; neuer Test: „test" matcht nicht „latest"; Benchmark (`AkgBenchmarkRunner`) vorher/nachher dokumentiert.

### B2 · Embedding-Cache erkennt Provider-/Modellwechsel nicht
**Hoch · M · ⚠️**
`src/AKG/Embeddings/Neo4jEmbeddingCache.cs`: Invalidierung nur über Body-Hash. Wechsel von Provider/Modell (andere Dimension) lässt alte Vektoren stehen → stiller Dimension-Mismatch, Retrieval bricht.
- **Fix:** Embedding-Fingerprint (`provider:model:dimension`) auf jedem Chunk speichern; beim Backfill-Zyklus Fingerprint gegen aktiven Provider prüfen; bei Abweichung betroffene Chunks als stale markieren und neu embedden; Vektorindex bei Dimensionswechsel neu anlegen.
- **Akzeptanz:** Test mit Fake-Provider A (dim 4) → Wechsel auf B (dim 8) → Chunks werden neu gebaut, Index neu erstellt, keine Mixed-Dimension-Queries.

### B3 · Retrieval-Schwellwerte hart codiert
**Hoch · S · ✅**
`SemanticBooster.cs` (SimilarityThreshold 0.5, VectorTopK 100, MmrTopN 15), `RuleMmrReranker.cs` (λ 0.7), `ContextCompiler.cs` (HeadSimilarityThreshold 0.4). Kein Tuning ohne Rebuild.
- **Fix:** In eine `RetrievalOptions`-Klasse heben, aus Env binden (`RETRIEVAL_SIMILARITY_THRESHOLD`, `RETRIEVAL_VECTOR_TOP_K`, `RETRIEVAL_MMR_LAMBDA`, `RETRIEVAL_MMR_TOP_N`, `RETRIEVAL_HEAD_THRESHOLD`), Defaults = heutige Werte; `.env.example` + `docs/embeddings.md` ergänzen. Vorbild: bestehende `CHUNKING_*`-Optionen (ADR-0004).
- **Akzeptanz:** Ohne Env-Variablen identisches Verhalten (Tests unverändert grün); Options-Bindungs-Tests.

### B4 · `EmbedBatchAsync` nutzt keine echte Batch-API
**Mittel · S · ✅**
`src/Embeddings/OpenAiEmbeddingService.cs:~57`: n Einzel-Requests statt einem Batch-Request (`input: [..]`). Verlangsamt Rebuilds, erhöht Rate-Limit-Druck.
- **Fix:** OpenAI-kompatible Provider (openai, custom): ein Request mit Text-Array, Antwort nach `index` sortiert zuordnen; Batchgröße kappen (z. B. 128). Andere Provider unverändert lassen.
- **Akzeptanz:** Test mit Fake-HttpMessageHandler: 10 Texte → 1 Request; Reihenfolge der Vektoren stimmt.

### B5 · Kein Query-Preprocessing/-Expansion
**Mittel · M · ❌ (Design-Entscheidung nötig)**
Query wird 1:1 embedded. Synonym-/Konzept-Expansion (z. B. über vorhandene `concepts` im Graph) könnte Recall verbessern — braucht aber Benchmark-Absicherung, sonst Noise. Erst nach B1/B3 + Benchmark-Baseline angehen.

### B6 · O(N)-Brute-Force-Fallback ohne Vektorindex
**Mittel · M · ⚠️**
`SemanticBooster.cs:~188-256`: Ohne Neo4j-Vektorindex (Memgraph, Index-Fehler) app-seitige Cosine über **alle** Chunks. Skalierungsgrenze ≈ 100k Chunks.
- **Fix (pragmatisch):** Fallback-Pfad deckeln (Kandidaten zuerst per Keyword-Score vorfiltern, dann nur Top-M Chunks cosinen); Latenz des Fallbacks loggen (Warnung ab Schwellwert).
- **Akzeptanz:** Test: Fallback berechnet Cosine nur über vorgefilterte Menge; Log-Warnung bei Fallback-Aktivierung.

### B7 · Embedding-Retry ohne Backoff
**Niedrig · S · ✅**
`Neo4jEmbeddingCache.cs:~182`: Attempt-Counter (max 5) ohne Exponential-Backoff/Jitter.
- **Fix:** Verzögerung `baseDelay * 2^attempt` + Jitter zwischen Versuchen im Backfill (TimeProvider-basiert, testbar).
- **Akzeptanz:** Test mit FakeTimeProvider: Delays wachsen exponentiell.

### B8 · Kein Overlap über Block-Grenzen im Chunker
**Niedrig · S · ✅**
`src/AKG/Chunking/AdaptiveDocumentChunker.cs:~52-78`: Overlap nur innerhalb gesplitteter Blöcke, nicht an Code↔Prosa-Kanten.
- **Fix:** Beim Packen den Schluss des Vorgänger-Blocks (bis `OverlapChars`) dem Folge-Chunk voranstellen, wenn beide Textstil haben.
- **Akzeptanz:** Chunker-Tests für die Kanten-Fälle; Determinismus bleibt.

### B9 · Head-Centroids werden unnötig neu berechnet
**Niedrig · S · ✅**
`EmbeddingBackfillHostedService.cs:~103` + `Neo4jHeadVectorStore.cs:~69`: K-Means läuft auch ohne Chunk-Änderungen; Dirty-Flag wird nicht sauber zurückgesetzt.
- **Fix:** Rebuild nur, wenn der Cache-Durchlauf tatsächlich Chunks geschrieben hat; Dirty-Flag nach Erfolg löschen.
- **Akzeptanz:** Test: 2. Zyklus ohne Änderungen → kein K-Means-Aufruf (Fake-Store zählt Aufrufe).

---

## C — Memory & Ingestion

### C1 · Tenant-Isolation ist modelliert, aber nicht durchgesetzt
**Kritisch (sobald Multi-Tenant genutzt wird) · L · ⚠️ (nur mit Spec + Isolationstests)**
`KnowledgeRule.TenantId` wird persistiert (ADR-0012, Scheibe A), aber `src/AKG/Graph/Neo4jKnowledgeGraph.cs:~82-104` filtert nur `ownerId`, nie `TenantId`. Entspricht Roadmap Track 3 „Scheibe B offen" — laut Roadmap ist vor Umsetzung die **Threading-Rückfrage beim Nutzer** vorgesehen.
- **Fix:** `tenantId`-Parameter in alle lesenden/schreibenden Cypher-Queries (aus `IIdentityContext.TenantId`, Default `"default"`); dito im InMemory-Executor; Entity-/Feedback-Store folgen als separate Scheibe.
- **Akzeptanz:** Isolationstests: Regeln von Tenant A sind für Tenant B unsichtbar (Read, Search, List, Delete); Single-Tenant-Verhalten byte-identisch (Bestandstests grün).

### C2 · Rollen-Modell (Owner/Editor/Viewer) fehlt komplett
**Hoch · L · ❌**
ADR-0012 beschreibt Rollen, es existiert kein Code. Bewusst vertagt (Roadmap) — als Issue tracken, erst nach C1 und nur mit Design-Session angehen.

### C3 · Keine Kontradiktions-Erkennung im episodischen Gedächtnis
**Hoch · M · ⚠️**
`remember("X ist 5")` und später `remember("X ist 6")` erzeugen zwei koexistierende Fakten — kein Konflikt-Hinweis. Mem0/Letta/Cognee erkennen Widersprüche.
- **Fix (LLM-frei, Edda-Stil):** Beim `remember` vorhandene Memories des Users mit hoher Token-Überlappung (z. B. Jaccard > 0.6 auf normalisierten Tokens) finden; Treffer als `SUPERSEDES`-Kante vom neuen zum alten Fakt anlegen + altes Memory im `recall`-Ranking abwerten; Antwort des Tools meldet „ersetzt möglicherweise: …".
- **Akzeptanz:** Tests für die Ähnlichkeits-Heuristik; `recall` bevorzugt den neueren von zwei ähnlichen Fakten; keine False-Positives bei disjunkten Fakten.

### C4 · Konsolidierung dedupliziert nur exakte String-Normalisierung
**Hoch · M · ⚠️**
`src/Agent/Tools/Memory/ConsolidateTool.cs:~82`: Nur Case/Whitespace-Normalisierung — „nutze pnpm statt npm" und „npm durch pnpm ersetzen" bleiben Duplikate. Markt macht semantisches Merge.
- **Fix (Stufe 1):** Token-basierte Ähnlichkeit (Jaccard/Overlap, gleiche Heuristik wie C3, Schwellwert konfigurierbar) zusätzlich zur exakten Dedup; Merge-Regel: neuestes Memory bleibt, Verlierer werden gelöscht und in der Tool-Antwort aufgeführt. **Stufe 2 (optional, Spec):** Embedding-basierte Dedup, wenn Embedding-Provider aktiv.
- **Akzeptanz:** Tests mit paraphrasierten Duplikaten; `consolidate_memory` bleibt deterministisch und MCP-default-deny.

### C5 · Kein Incremental Sync / Change-Tracking in Connectoren
**Hoch · M · ⚠️**
Alle Connector-Läufe sind Full-Ingests (`src/AKG.Ingestion/`). Kein „seit letztem Lauf", kein ETag/Commit-Vergleich. Bei großen Quellen unnötig teuer (Embeddings!).
- **Fix (Stufe 1):** Per-Instanz-Sync-State (letzter Commit-Hash für Git, Timestamp/ETag für HTTP) in `data/` persistieren; Git: `git diff --name-only <lastCommit>..HEAD` statt Full-Scan; unveränderte Items überspringen (Content-Hash-Skip vor Re-Embedding — Roadmap WP2 nennt das bereits als optionale Optimierung).
- **Akzeptanz:** Zweiter Lauf ohne Quell-Änderung ingestiert 0 Items (Test mit Fake-Git-Client); Force-Full-Sync-Flag vorhanden.

### C6 · Jira/GitLab/Awork-Connectoren sind Deskriptor-Stubs
**Mittel · M · ⚠️**
`src/AKG.Ingestion/Connectors/{Jira,GitLabGroup,Awork}KnowledgeConnector.cs` existieren als Shells ohne Vendor-Logik (Auth-Kodierung, Pagination, Feld-Mapping). ADR-0006 verspricht sie.
- **Fix:** Pro Connector eine Session: Vendor-Auth (Jira: `base64(email:token)`), List-Endpoint + Pagination, Mapping auf `KnowledgeItem`; Tests mit aufgezeichneten Fake-Responses. Alternativ: Stubs aus der UI-Auswahl entfernen, bis implementiert (Erwartungsmanagement).
- **Akzeptanz:** Je Connector: Mock-HTTP-Tests für Auth, Pagination, Mapping, Fehlerpfade.

### C7 · MCP-Connector (Wissensquelle „fremder MCP-Server") fehlt
**Mittel · L · ❌**
ADR-0006 nennt „beliebige MCP-Server" als Quelle — kein Code vorhanden. Braucht MCP-**Client**-Infrastruktur in der Ingestion; Design-Session nötig (welche Tools, welches Mapping).

### C8 · Entity-Layer ohne Dedup
**Hoch · M · ⚠️**
`src/AKG.Ingestion/Entities/` appended Extraktionen blind: Zwei Läufe über denselben Text duplizieren Entitäten/Relationen.
- **Fix:** Deterministische Entity-IDs aus normalisiertem Namen+Typ (analog Content-Hash bei Rules); `MERGE` statt `CREATE` im EntityStore; Relationen ebenso über `MERGE`.
- **Akzeptanz:** Test: Doppel-Ingest desselben Texts → identische Knoten-/Kantenzahl wie Einzel-Ingest.

### C9 · Keine temporalen Kanten (validFrom/validUntil auf Relationen)
**Mittel · M · ❌**
Zep/Graphiti modellieren Fakten-Gültigkeit auf der Kante (bi-temporal). Edda hat nur `validUntil` auf Regeln. Sinnvoll erst nach C3 (Supersedes-Kanten liefern die Basis); Design-Session nötig.

### C10 · Konsolidierung nur manuell auslösbar
**Mittel · S · ✅**
`consolidate_memory` existiert, aber es gibt keinen periodischen Trigger — verblasste/duplizierte Memories bleiben liegen, bis jemand das Tool aufruft.
- **Fix:** Opt-in HostedService (`MEMORY_CONSOLIDATION_INTERVAL_HOURS`, Default 0 = aus), der die vorhandene Konsolidierungslogik pro User ausführt; Ergebnis ins Audit-Log.
- **Akzeptanz:** Test mit FakeTimeProvider: Service feuert im Intervall, ruft die bestehende Logik, Default bleibt aus.

### C11 · LLM-Enricher ohne Retry und ohne Output-Schema-Validierung
**Mittel · S · ✅**
`src/AKG.Ingestion/Enrichment/LlmIngestionEnricher.cs:~62`: Provider-Fehler → sofort aufgeben (best-effort ok, aber ein 429 sollte nicht gleich verloren sein); ungültiges JSON → still verworfen.
- **Fix:** 2 Retries mit Backoff für transiente Fehler (429/5xx/Timeout); JSON-Antwort gegen erwartete Struktur prüfen und bei Abweichung 1 Reparatur-Versuch (strenger Hinweis im Prompt), sonst sauber loggen.
- **Akzeptanz:** Tests mit Fake-ChatClient: 429→Retry→Erfolg; Garbage-JSON → Item bleibt unangereichert, Warnung geloggt, Pipeline läuft weiter.

---

## D — Architektur, API & Tests

### D1 · N+1-Query bei Edge-Upserts
**Hoch · S · ✅**
`src/AKG/Graph/Neo4jKnowledgeGraph.cs:~303-309`: eine Cypher-Query **pro Kante**. 100 Relationen = 100 Roundtrips.
- **Fix:** `UNWIND $targetIds AS targetId` + `MERGE` in einer Query pro Relationstyp (Vorbild: EntityStore nutzt UNWIND bereits). InMemory-Executor um die neue Query-Shape ergänzen.
- **Akzeptanz:** Fake-Executor-Test: n Kanten → 1 ExecuteAsync pro Relationstyp; Bestandstests grün.

### D2 · Fire-and-Forget `Task.Run` ohne Aufsicht
**Mittel · M · ⚠️**
`Neo4jKnowledgeGraph.cs:~268`, `AkgEndpoints.cs:~75`, `WorldKnowledgeSeedHostedService.cs:~119`: `_ = Task.Run(...)` mit `CancellationToken.None` — kein Shutdown-Respekt, Fehler nur als Log-Warning.
- **Fix:** Kleine `IBackgroundWorkQueue` (Channel-basiert, Interface in Core) + HostedService-Consumer; die drei Stellen enqueuen statt `Task.Run`; ApplicationStopping-Token durchreichen.
- **Akzeptanz:** Tests für Queue (enqueue/consume/cancellation); kein `_ = Task.Run` mehr in den drei Dateien.

### D3 · Null Integrationstest-Coverage für Hosting/Web
**Hoch · L · ⚠️**
Kein Testprojekt deckt `/api/akg/*`, Auth-Boundary oder MCP-HTTP ab — Regressionen dort sind unsichtbar. (Unit-Coverage der Libs ist exzellent.)
- **Fix:** Neues `tests/Hosting.Tests` mit `WebApplicationFactory`, InMemory-Graph-Provider (`GRAPH_PROVIDER=memory`), Null-Embeddings. Startpaket ~20 Szenarien: Auth (mit/ohne Token, Loopback), Rules-CRUD, MCP-Token-Validierung, Fehlerformat, A4-Guard.
- **Akzeptanz:** Tests laufen ohne Docker/Neo4j in `dotnet test Edda.slnx`.

### D4 · Inkonsistente API-Fehlerformate
**Mittel · S · ✅**
`AkgEndpoints.cs` nutzt `Results.Problem()` (RFC 7807), `SettingsEndpoints.cs`/`ConnectorEndpoints.cs` teils `BadRequest(new { error = ... })`.
- **Fix:** Alle Fehler-Antworten auf ProblemDetails vereinheitlichen (`Results.Problem` / `Results.ValidationProblem`); UI-Aufrufer (Blazor-Services) auf das Format anpassen.
- **Akzeptanz:** Grep findet kein anonymes `{ error = ... }`-Fehlerobjekt mehr in `src/Edda.Hosting/Api/`; D3-Tests prüfen das Format.

### D5 · Keine Pagination auf `/api/akg/rules`
**Mittel · S · ✅**
`AkgEndpointHandlers.cs:~22`: liefert immer den kompletten Result-Set — OOM-/Latenz-Risiko bei großen Graphen; auch `list_memory` profitiert von Limits.
- **Fix:** Optionale `skip`/`take`-Query-Parameter (Default take=200, Max 1000, validiert) + `X-Total-Count`-Header; abwärtskompatibel (ohne Parameter: heutiges Verhalten bis Max-Deckel).
- **Akzeptanz:** Handler-Tests für Grenzen (negativ, >Max); UI funktioniert unverändert.

### D6 · Keine Input-Validierung am Entity-Ingest-Endpoint
**Mittel · S · ✅**
`AkgEndpoints.cs:~100`: `request.Text` ungeprüft → leere oder riesige Texte erzeugen sinnlose LLM-Kosten.
- **Fix:** Validierung im Handler (nicht leer, Länge ≤ `INGESTION_MAX_TEXT_CHARS`, Default 20000) → `ValidationProblem` bei Verstoß.
- **Akzeptanz:** Tests: leer → 400, zu lang → 400, ok → 200.

### D7 · Keine Metriken/Tracing, Health-Check ohne Tiefe
**Niedrig · M · ⚠️**
`/health` existiert, prüft aber nichts; kein OpenTelemetry, keine Meter (Query-Latenz, Cache-Hits, Retrieval-Counts).
- **Fix (Stufe 1):** ASP.NET-HealthChecks für Neo4j-Konnektivität + Embedding-Provider (degraded statt unhealthy); Stufe 2 (separat): `ActivitySource`/`Meter` + optionaler OTLP-Export.
- **Akzeptanz:** `/health` meldet degraded bei abgeschaltetem Neo4j; Tests mit Fakes.

### D8 · Keine CI-Pipeline im Repo
**Mittel · S · ✅**
Kein `.github/workflows/` — 1000+ Tests laufen nur, wenn jemand daran denkt.
- **Fix:** GitHub-Actions-Workflow: `dotnet build` + `dotnet test Edda.slnx` auf push/PR (ubuntu-latest, .NET 10 SDK); optional Docker-Build-Smoke.
- **Akzeptanz:** Workflow-Datei vorhanden und lokal via `act` oder erstem Push verifizierbar.

### D9 · Cursor nicht in `await using` im CypherExecutor
**Niedrig · S · ✅**
`src/AKG/Graph/Neo4jCypherExecutor.cs:~28,43`: Session ist `await using`, der Result-Cursor nicht — kosmetisch, aber sauberer.
- **Fix:** Cursor ebenfalls deterministisch konsumieren/disposen (je nach Driver-API `ToListAsync` in try/finally bzw. Consume).
- **Akzeptanz:** Bestandstests grün.

---

## E — UI, DX & Doku

### E1 · Keine Volltextsuche + Filter im Knowledge-UI
**Hoch · M · ⚠️**
`/knowledge` bietet nur Graph-Navigation. Nutzer können nicht suchen/filtern (Domain/Typ/Tag), was `search_memory`/`list_memory` längst können.
- **Fix:** Suchleiste + Filter-Dropdowns über der Graph-Ansicht; Backend existiert (Context-Compiler bzw. gefilterte Rules-API); Trefferliste mit Klick → Knoten-Detail.
- **Akzeptanz:** Suche nach Tag/Wort liefert Liste; Auswahl fokussiert den Knoten im Graph; i18n-Keys (de+en) gepflegt.

### E2 · Kein Feedback-Rückkanal für Agenten über MCP
**Hoch · M · ⚠️**
Agenten können Regeln lesen, aber keine Nützlichkeits-Signale zurückgeben — der vorhandene Feedback-/Konfidenz-Layer (`IRuleFeedbackService`) bleibt für MCP-Clients unerreichbar. Das System ist nicht selbstverbessernd (Markt: Mem0/Letta schließen den Loop).
- **Fix:** Neues Tool `rate_memory(ruleId, outcome: helpful|not_helpful|outdated)` → schreibt ins bestehende Feedback-System; Schreiben light, daher: in die Write-default-deny-Liste, opt-in via `MCP_EXPOSED_TOOLS` (Safety-Story bleibt intakt).
- **Akzeptanz:** Tool-Tests (Regel 5/6: nie Exceptions, userId aus Context); Feedback-Multiplikator ändert sich nach Ratings; MCP-Default bleibt read-only.

### E3 · Kein „Erste Schritte"-Guide und kein Glossar
**Hoch · S · ✅**
README erklärt Installation, aber niemand führt Neue durch „erste Regel anlegen → Agent anbinden → search_memory sehen → TDK validieren". Begriffe (AKG, TDK, MMR, Head-Vektor, Decay) sind unerklärt.
- **Fix:** `docs/erste-schritte.md` (geführtes 15-Minuten-Tutorial mit den mitgelieferten knowledge/-Beispielen) + `docs/glossar.md`; im README verlinken.
- **Akzeptanz:** Doku-Review; alle Fachbegriffe aus README/CLAUDE.md im Glossar.

### E4 · Export nur per API, kein UI-Button
**Mittel · S · ✅**
`GET /api/knowledge/export` existiert (inkl. `SchemaVersion`) — im UI fehlt der Download-Button als Gegenstück zur Import-Seite.
- **Fix:** Button „Exportieren (JSON-Bundle)" auf der Import-Seite (Umbenennung in „Import/Export"), Download via bestehendem Endpoint.
- **Akzeptanz:** Klick lädt gültiges Bundle; Re-Import des Bundles ist verlustfrei (Roundtrip manuell verifiziert).

### E5 · Kein Benchmark-/Gesundheits-Dashboard
**Mittel · M · ⚠️**
`AkgBenchmarkRunner` (Precision/Recall@k) und Konfidenz-Daten existieren, sind aber unsichtbar. Nutzer sehen nie, ob ihr Graph „gesund" ist.
- **Fix:** Neue UI-Seite `/quality`: analyze_coverage-Ergebnis (dünne Domains, stale Regeln, Konflikte) + Konfidenz-Verteilung + Button „Benchmark ausführen" mit Ergebnistabelle.
- **Akzeptanz:** Seite rendert mit InMemory-Provider; keine neuen Endpoints ohne Auth.

### E6 · i18n unvollständig
**Niedrig · S · ✅**
Nur Knowledge/NotFound/RuleEditor sind übersetzt; Embeddings, TDK, Settings, Sources, Import sind hart deutsch, obwohl `ILocalizationService` + LanguageToggle existieren.
- **Fix:** Verbleibende Seiten auf `Loc["key"]` umstellen, de+en-Ressourcen ergänzen.
- **Akzeptanz:** Sprachumschalter wirkt auf allen Seiten; kein hart codierter UI-String mehr in den fünf Seiten.

### E7 · Kein Markdown-Rendering/Vorschau im Rule-Editor
**Niedrig · S · ✅**
`RuleEditor.razor`/`RuleDetail.razor` zeigen den Body als Rohtext. Regel 12 (Self-Hosting) beachten: Markdown-Renderer als NuGet (z. B. Markdig), kein CDN.
- **Fix:** Detail-Ansicht rendert Markdown (sanitized!); Editor bekommt Vorschau-Tab.
- **Akzeptanz:** XSS-Test: `<script>` im Body wird nicht ausgeführt; Formatierung sichtbar.

### E8 · Keine Bulk-Operationen im UI
**Mittel · M · ⚠️**
Multi-Select-Löschen existiert; Batch-Tagging/-Priorität/-Domain-Änderung fehlen — Pflege großer Graphen ist mühsam.
- **Fix:** Auf bestehender Mehrfach-Auswahl aufsetzen: Aktion „Tag hinzufügen/entfernen", „Priorität setzen" für Auswahl; ein Batch-Endpoint (`POST /api/akg/rules/batch`) mit ProblemDetails-Fehlern.
- **Akzeptanz:** Handler-Tests; Audit-Log-Eintrag pro Batch.

### E9 · Entity-Layer hat keine UI
**Mittel · M · ⚠️**
Entity-Extraktion (LightRAG-Stil) läuft, aber Nutzer können Entitäten/Relationen nirgends browsen oder korrigieren — Qualitätskontrolle unmöglich. Erst nach C8 (Dedup) sinnvoll.
- **Fix:** Read-only-Ansicht zuerst (Liste + Nachbarschaft), Editieren später.

### E10 · Keine sichtbare Änderungshistorie/Undo
**Niedrig · M · ❌**
Audit-Log existiert intern (HMAC/Merkle), ist aber im UI unsichtbar; gelöschte Knoten sind weg. Design-Frage (Soft-Delete vs. Event-Historie) — nicht fürs schwächere Modell.

---

## F — TDK (Test-Driven Knowledge)

> Vollständige Evaluierung mit Marktvergleich, Beweisketten und Achsen-Begründung:
> **`docs/tdk-vertiefung.md`**. Hier nur die Issue-Kurzfassung.

### F1 · `validatorScript` wird weder geparst noch persistiert — TDK ist end-to-end funktionslos
**Kritisch · M · ⚠️**
`KnowledgeRule.ValidatorScript` (Core) wird **nirgendwo in src/ zugewiesen**: der Frontmatter-Parser (`src/AKG/Parser/`) extrahiert das Feld nicht, der `RuleLoader`-Upsert (`RuleLoader.cs:89-121`) schreibt es nicht nach Neo4j, der `NodeMapper` liest es nicht zurück. In Produktion trägt keine Regel je einen Validator → `tdk_validate` meldet immer „keine Verstöße"; die Stats-Query (`Neo4jKnowledgeGraph.cs:401`) zählt immer 0. Nur Unit-Tests setzen das Feld direkt am Objekt.
- **Fix:** Feld durch die gesamte Kette (Parser → Upsert → NodeMapper → InMemory-Executor → Frontmatter-Serializer); Lade-Pfad-Test: `knowledge/security/no-plaintext-secrets.md` parsen → Regel hat Validator.
- **Akzeptanz:** Nach Neustart mit Neo4j liefert der mitgelieferte Secrets-Validator auf `password = "geheim123"` eine Violation; Roundtrip Export→Import erhält den Validator.

### F17 · Bundle-Import darf Fremd-Validatoren nicht ungefragt übernehmen (Folge von F1)
**Hoch · S · ✅**
Sobald F1 gefixt ist, transportieren importierte Knowledge-Bundles **ausführbaren Code**. `KnowledgeImporter` muss `validatorScript` aus Fremd-Bundles per Default strippen (Opt-in `IMPORT_ALLOW_VALIDATORS=true`), analog zur bestehenden Vektor-Hygiene.

### F2 · `line`/`suggestion` werden beim Violation-Mapping verworfen
**Hoch · S · ✅**
`TdkValidatorViolation` (Agent) hat `line` + `suggestion`, das Core-Modell `TdkViolation` nicht — beim Mapping in `TdkEngine.cs:~189` gehen beide verloren; UI/Tool zeigen keine Zeilennummern und keine Fix-Vorschläge.
- **Fix:** `TdkViolation` um `int? Line`, `string? Suggestion` erweitern; Mapping, `TdkFeedbackFormatter` und `/tdk`-Tabelle nachziehen.

### F3 · Validator-Fehler sind unsichtbar und verfälschen die Konfidenz
**Hoch · S–M · ✅**
`TdkEngine.cs:~146-161`: ExitCode ≠ 0, Timeout, kaputtes JSON → nur Log-Warning **und** `RecordTdkOutcome(passed:false)` — ein Infrastruktur-Fehler wird als fachliches Scheitern in den Konfidenz-Store gebucht.
- **Fix:** `TdkResult.EngineErrors[]` (RuleId, ExitCode, stderr-Auszug, TimedOut); Sandbox-Fehler NICHT als Outcome buchen; UI + Tool-Antwort zeigen Engine-Fehler an.

### F9 · Kein deklaratives Sprach-Targeting **· Mittel · S · ✅** — Frontmatter `appliesTo: [python, csharp]`; Engine überspringt fremdsprachige Blöcke vor dem Sandbox-Start.
### F4 · Kein Validator-Helper-Modul **· Mittel · M · ⚠️** — mitgeliefertes `tdk.py` neben dem Skript (JSON-I/O, `violation()`-Builder, `python_ast()`); Roh-stdin/stdout bleibt gültig.
### F5 · Keine Validator-Selbsttests **· Hoch · M · ⚠️** — `validatorFixtures.pass[]/.fail[]` im Frontmatter; Prüf-Lauf verifiziert Validator gegen eigene Fixtures („test-driven" konsequent gemacht).
### F6 · Kein Dry-Run-Editor **· Mittel · M · ⚠️** — Tab auf `/tdk`: Skript + Beispielcode → Live-Ausgabe (stdout/stderr/Violations).
### F7 · Keine Versionierung/Kill-Switch **· Mittel · S · ✅** — SHA-256-Hash des Skripts persistieren, `validatorEnabled`-Flag, Konfidenz-Fenster bei Skript-Änderung zurücksetzen.
### F10 · Severity ohne Semantik **· Niedrig · S · ✅** — error/warning/info-Bedeutung in `docs/tdk.md` festschreiben; Formatter sortiert/zählt danach.
### F11 · Ein Container pro (Regel × Block) **· Hoch · M–L · ⚠️ (Spec)** — Batch-Runner: ein Container pro Validierung führt alle Paare aus (~15 Container → 1; Latenz 7–15 s → <2 s).
### F12 · Keine Parallelisierung im Alt-Modus **· Niedrig · S · ✅** — begrenztes `Task.WhenAll` (Semaphore, 4).
### F13 · Kein Ergebnis-Cache **· Niedrig · S · ✅** — `Hash(ruleId+validatorHash+blockHash)` → Ergebnis wiederverwenden.
### F14 · Validator-Bibliothek fehlt (1 von 11 Regeln) **· Hoch · L inkrementell · ✅ nach F4/F5** — 10–15 Standard-Validatoren mit Fixtures (`.Result`/`.Wait()`, `except: pass`, SQL-Konkatenation, `eval`, `console.log`, …).
### F15 · Keine Workflow-Andockpunkte **· Mittel · S · ✅** — Doku + Beispiele: Claude-Code-Hook → `tdk_validate` via MCP, pre-commit, CI-Schritt.
### F8 · AST-Potenzial ungenutzt **· Niedrig · S · ✅** — `import ast` in Beispiel-Validatoren + Doku.
### F16 · LLM-Judge-Validatoren **· Niedrig · M · ❌** — opt-in `validatorType: llm`, Default AUS (Design-Session; konsistent zu ADR-0010).

---

## Umsetzungsplan

Ziel: Ein schwächeres Modell (z. B. Haiku-Klasse) arbeitet die Issues eigenständig ab.
Der Plan minimiert dafür Interpretationsspielraum: kleine Häppchen, jedes Issue mit
Akzeptanzkriterien, harte Arbeitsregeln, klare Eskalationspfade.

### Arbeitsregeln (gelten für JEDES Issue)

1. **Ein Issue = eine Session = ein Commit.** Niemals zwei Issues mischen. Commit-Message deutsch, Format: `A1: Timing-sicherer Token-Vergleich` (Regel 10: keine KI-Mention).
2. **Vor dem Start:** betroffene Dateien vollständig lesen; prüfen, ob der beschriebene Zustand noch stimmt (Zeilennummern sind Näherungen). Weicht der Code stark von der Issue-Beschreibung ab → **abbrechen und melden**, nicht raten.
3. **CLAUDE.md-Regeln sind absolut:** Interface-First (neue Services → Interface in `src/Core`), `IFileSystem` statt File-I/O, `TimeProvider` statt `DateTime.UtcNow`, Tools werfen nie Exceptions (`ToolResult.Fail`), userId/tenantId aus Context — nie aus Tool-Argumenten, 100 % Unit-Tests für neue Klassen (Benennung `MethodName_Scenario_ExpectedResult`), XML-Doku englisch, externe Doku deutsch, keine CDN-Assets.
4. **Nach jedem Issue:** `dotnet build Edda.slnx` und `dotnet test Edda.slnx` — beides muss grün sein (0 Warnings, TreatWarningsAsErrors). Rot = selbst fixen oder Commit verwerfen; niemals Tests löschen/skippen, um grün zu werden.
5. **Verhalten per Default unverändert:** Neue Konfiguration bekommt Defaults, die das heutige Verhalten exakt beibehalten. `.env.example` bei jeder neuen Env-Variable ergänzen (mit deutschem Kommentar).
6. **Nicht anfassen:** MCP-Default-Deny-Logik lockern, Security-Layer entfernen/umgehen, Namespaces umbenennen, „nebenbei" refactoren.
7. **Eskalieren statt improvisieren:** Wenn ein Akzeptanzkriterium nicht erreichbar scheint, das Issue mit Begründung zurückgeben. Teilfortschritt committen ist ok, wenn Build+Tests grün sind und der Rest als TODO im Issue (nicht im Code) dokumentiert wird.

### Phase 1 — Mechanische Quick-Wins (✅, geringes Risiko, je ≤ ½ Session)

Reihenfolge so gewählt, dass frühe Erfolge das Sicherheitsniveau sofort heben:

| Schritt | Issue | Kurzanweisung |
|---|---|---|
| 1 | **A1** | `FixedTimeEquals` im Auth-Handler + 4 Unit-Tests |
| 2 | **A4** | Startup-Guard als pure Funktion + Fail-Fast + Tests |
| 3 | **A3** | Install-Skripte generieren Neo4j-Passwort; Warn-Log bei No-Auth |
| 4 | **A2** | RateLimiter-Middleware + Env-Option + `.env.example` |
| 5 | **D1** | UNWIND-Batch für Edge-Upserts + InMemory-Shape + Fake-Executor-Test |
| 6 | **D4** | Alle API-Fehler auf ProblemDetails; UI-Aufrufer anpassen |
| 7 | **D5** | skip/take-Pagination + Validierung + Tests |
| 8 | **D6** | Text-Validierung am Entity-Ingest + Tests |
| 9 | **B3** | `RetrievalOptions` aus Env, Defaults = Status quo |
| 10 | **B4** | Echte Batch-Embedding-Requests (openai/custom) + Fake-Handler-Test |
| 11 | **A6** | Query-Parameter-Token entfernen/deprecaten + Doku |
| 12 | **A10** | SecretRedactor in globalem Exception-/Log-Pfad + Test |
| 13 | **D8** | GitHub-Actions-Workflow (build+test) |
| 14 | **E4** | Export-Button im UI |
| 15 | **F2** | TdkViolation um line/suggestion erweitern; Mapping+Formatter+UI nachziehen |
| 16 | **F3** | EngineErrors sichtbar machen; Sandbox-Fehler nicht als Konfidenz-Outcome buchen |
| 17 | **B7, B9, D9, A9, A5, A11, E6, F9, F10, F13** | Kleinteilige Rest-Quick-Wins in beliebiger Reihenfolge |

**Checkpoint 1:** Nach Phase 1 einmal manuell verifizieren: Installation frisch durchspielen,
MCP-Anbindung testen, `docs/betrieb.md` auf Konsistenz lesen.

### Phase 2 — Mittlere Aufgaben mit klarer Vorlage (✅/⚠️, je 1–2 Sessions)

| Schritt | Issue | Hinweis für das Modell |
|---|---|---|
| 16 | **C10** | HostedService-Vorbild: `EmbeddingBackfillHostedService` kopieren/anpassen |
| 17 | **C11** | Retry-Muster aus Ollama-Circuit-Breaker als Referenz; Fake-ChatClient-Tests |
| 18 | **B2** | Fingerprint-Feld auf Chunks; Migrationspfad: fehlender Fingerprint = stale |
| 19 | **C8** | Deterministische Entity-IDs + MERGE; Vorbild: Rule-ID-Hashing der Pipeline |
| 20 | **D2** | Channel-basierte WorkQueue (Interface in Core) + 3 Callsites umstellen |
| 21 | **E3** | Erste-Schritte-Guide + Glossar (nur Doku, deutsch) |
| 22 | **B8** | Chunker-Overlap an Block-Kanten + Determinismus-Tests |
| 23 | **E7** | Markdig (NuGet, kein CDN) + sanitized Rendering + XSS-Test |
| 24 | **A7** | Prozess-Limits im Subprocess-Runner + Kill-Test + Doku |
| 25 | **B6** | Fallback-Pfad deckeln + Latenz-Warnung |
| 26 | **F1 + F17** | validatorScript-Kette (Parser→Upsert→Mapper→InMemory→Serializer) + Import-Stripping; Lade-Pfad-Test Pflicht |
| 27 | **F7** | Validator-Hash + Enabled-Flag + Konfidenz-Reset bei Skript-Änderung |
| 28 | **F14 (Start)** | Erste 5 Bibliotheks-Validatoren, je einer pro Session (nach F1; F4/F5 verbessern DX, sind aber keine Blocker) |

**Checkpoint 2:** Benchmark-Baseline erzeugen (`AkgBenchmarkRunner` auf Beispiel-Wissen) und
Zahlen in `docs/benchmarks.md` festhalten — Pflichtgrundlage für Phase 3.

### Phase 3 — Anspruchsvolle Issues (⚠️, nur mit Detail-Spec, Review durch stärkeres Modell empfohlen)

Vorgehen pro Issue: Erst eine **Spec-Session** (stärkeres Modell oder Mensch schreibt Detail-Spec
mit exakten Signaturen/Queries als `docs/plans/`-Eintrag), dann Umsetzung durch das schwächere
Modell strikt nach Spec, dann Review-Session.

| Schritt | Issue | Warum Spec nötig |
|---|---|---|
| 26 | **B1** | Retrieval-Ranking-Änderung — Benchmark-Vergleich Pflicht (Checkpoint 2) |
| 27 | **C3** | Ähnlichkeits-Heuristik + Schwellwerte müssen designt werden |
| 28 | **C4** | Merge-Semantik (was überlebt?) ist Produktentscheidung |
| 29 | **C5** | Sync-State-Format + Fehlerfälle (Rebase, Force-Push) |
| 30 | **D3** | Testinfrastruktur-Setup (WebApplicationFactory + InMemory-Provider-Wiring) |
| 31 | **E2** | MCP-Exposure-Semantik — Safety-Story darf nicht verwässern |
| 32 | **E1, E5, E8** | UI-Features mit UX-Entscheidungen |
| 33 | **C1** | Tenant-Filter — **vorher Threading-Rückfrage an den Nutzer** (lt. Roadmap vereinbart); Isolationstests sind Teil der Definition of Done |
| 34 | **C6** | Vendor-APIs (Jira/GitLab/Awork) — echte API-Doku in die Spec |
| 35 | **F4 + F5** | Helper-Modul-API + Fixtures-Format designen (Spec), dann mechanisch umsetzen |
| 36 | **F6, F11** | Dry-Run-Editor (UX) + Batch-Sandbox-Runner (Isolations-Trade-off in Spec festhalten) |

> **Hinweis zu TDK:** F1 ist der einzige **Kritisch**-Befund im ganzen Backlog, der ein
> beworbenes Kern-Feature betrifft — TDK funktioniert derzeit nur in Unit-Tests. Wer nach
> Phase 1 priorisieren will: F1+F17 vorziehen (Schritt 26) und F14 direkt anschließen.

### Nicht für das schwächere Modell (Design-Sessions / bewusst vertagt)

- **C2** Rollen-Modell (nach C1, Architektur-Session)
- **C7** MCP-Connector (MCP-Client-Infrastruktur, Design offen)
- **C9** Temporale Kanten (nach C3, Datenmodell-Entscheidung)
- **E10** Versionshistorie/Undo (Soft-Delete vs. Events)
- **B5** Query-Expansion (erst Benchmark-Evidenz aus B1)
- **A8** Rootless-Container vollständig (Compose-Profil-Umbau, betrifft Installer)
- **F16** LLM-Judge-Validatoren (opt-in-Design, nach F1–F14)

### Erfolgskontrolle

- Nach jeder Phase: kompletter `dotnet test Edda.slnx`-Lauf + manueller Smoke (UI öffnen, MCP `search_memory` aus einem Client).
- Nach Phase 3: Benchmark erneut fahren und mit Checkpoint-2-Baseline vergleichen — Retrieval-Änderungen (B1) müssen messbar neutral oder besser sein, sonst Revert.
- Marktvergleichstabelle oben aktualisieren, wenn C3/C4/C5/E2 landen — das sind die vier Zeilen, die Edda gegenüber Mem0/Zep/Cognee aufschließen lassen.

---

## Status-Log

Eine Zeile pro bearbeitetem Issue. Format: `- <ID> · <erledigt | blockiert | spec-erstellt | übersprungen> · <Datum> · <1 Satz Ergebnis/Grund>`

- A1 · erledigt · 2026-07-02 · Timing-sicherer Token-Vergleich: neuer `ConstantTimeComparer` (SHA-256-Digest + `FixedTimeEquals`, keine Längen-Frührückkehr) in Edda.Security, im `LocalAuthenticationHandler` statt `string.Equals` genutzt; 10 Unit-Tests.
- A4 · erledigt · 2026-07-02 · Startup-Guard gegen unauth. Remote-Bind: reine Funktion `RemoteBindGuard.IsInsecureRemoteBind` (Loopback-/Wildcard-Erkennung), Fail-Fast in `Program.cs`, Override `EDDA_ALLOW_INSECURE_REMOTE`; `EDDA_BIND` in Container-Env sichtbar gemacht; `.env.example` + `docs/betrieb.md` dokumentiert; 26 Unit-Tests.
- A3 · erledigt · 2026-07-02 · Neo4j-Auth abgesichert: `install.sh`/`install.ps1` erzeugen ein Zufallspasswort (→ `.env`), `docker-compose.yml` reicht `NEO4J_USERNAME`/`NEO4J_PASSWORD` an die App durch (Defaults = Status quo), `.env.example` mit Sicherheitswarnung, Warn-Log im `Neo4jGraphDatabaseProvider` bei `AuthTokens.None`. Skript-/Config-Issue → keine neuen Unit-Tests; Skripte syntax-geprüft, 1040 Tests grün.
- A2 · erledigt · 2026-07-02 · Rate-Limiting für `/api` & `/mcp`: `RateLimitOptions` (Core) + IP-partitionierter Fixed-Window-Limiter in `Program.cs`, opt-in via `EDDA_RATE_LIMIT_PER_MINUTE` (0/unset = aus = Status quo), UI/Blazor-Circuit per pfad-scopetem `UseWhen` ausgenommen; `.env.example` + `docker-compose.yml` ergänzt. 14 Options-Bindungs-Tests + Runtime-Smoke bestätigt (429 nach Limit, `/health` frei).
- D1 · erledigt · 2026-07-02 · N+1 bei Edge-Upserts behoben: `Neo4jKnowledgeGraph.UpsertEdgesAsync` macht Delete + MERGE nun in EINER `UNWIND $targetIds`-Query pro Relationstyp (statt 1 + N Einzel-Queries); `InMemoryCypherExecutor` um die Batch-Shape (`ReplaceEdges`) ergänzt; Fake-Executor-Test (25 Ziele → 1 Edge-Query) + InMemory-Test. 1056 Tests grün.
- Notiz (bei D1 gefunden) · übersprungen · `RuleLoader.cs:138-145` enthält dieselbe N+1-Edge-Upsert-Schleife (Delete + `foreach` MERGE) wie das gefixte `Neo4jKnowledgeGraph` — Kandidat für einen separaten Fix nach D1-Vorbild; bewusst NICHT in D1 angefasst (Scope = nur die im Issue genannte Datei).
- D4 · erledigt · 2026-07-02 · API-Fehlerformate vereinheitlicht: 7 anonyme `BadRequest(new { error })` in `ConnectorEndpoints` (3) + `SettingsEndpoints` (4) → `Results.Problem(detail:, statusCode: 400)` (RFC 7807, wie `AkgEndpoints`). Der 200er-Verbindungstest (`{ ok, error }`) bleibt bewusst Ergebnis-DTO. Keine UI-Aufrufer anzupassen — Blazor nutzt die Services in-process (kein HTTP-Call auf diese Endpoints). Grep-Akzeptanz (kein `new { error }` in `Api/`) erfüllt; D3-Format-Tests folgen mit D3. 1056 Tests grün.
- D5 · erledigt · 2026-07-02 · Pagination auf `/api/akg/rules`: optionale `skip`/`take`-Query-Parameter über neue reine `PageBounds.Resolve` (Core) validiert (skip≥0, take∈[1,1000], sonst 400 ProblemDetails), `X-Total-Count`-Header. Abwärtskompatibel: ohne Parameter voller Satz bis Max 1000; `skip` aktiviert Pagination mit Default-Seitengröße 200. Handler-Grenzlogik in `PageBounds` unit-getestet (12 Fälle), Handler-Integration folgt mit D3; Runtime-Smoke bestätigt (no-params 12/12, `take=2`→2, `skip=-1`/`take=0`/`take=5000`→400). 1068 Tests grün.
- D6 · erledigt · 2026-07-02 · Input-Validierung am Entity-Ingest (`POST /api/akg/entities/ingest`): neue reine `IngestionTextValidator` (Core) prüft nicht-leer + Länge ≤ `INGESTION_MAX_TEXT_CHARS` (Default 20000) → `Results.ValidationProblem` (400) VOR jedem LLM-Call; `.env.example` ergänzt. 15 Validator-Tests + Runtime-Smoke bestätigt (leer→400, zu lang→400, ok→200). 1083 Tests grün.
- B3 · erledigt · 2026-07-02 · Retrieval-Schwellwerte aus Env: neue `RetrievalOptions` (Core) + `RetrievalOptionsResolver` (AKG) binden `RETRIEVAL_SIMILARITY_THRESHOLD`/`_VECTOR_TOP_K`/`_MMR_TOP_N`/`_MMR_LAMBDA`/`_HEAD_THRESHOLD`, Defaults = bisherige Hardcodes (0.5/100/15/0.7/0.4). `SemanticBooster` + `ContextCompiler` nutzen die Options (optionaler ctor-Param, Default = Status quo); `.env.example` + `docs/embeddings.md` ergänzt. 8 neue Tests (Bindung + Defaults), Bestands-Retrieval-Tests unverändert grün → identisches Verhalten ohne Env. 1091 Tests grün.
- B4 · erledigt · 2026-07-02 · Echte Batch-Embeddings: `OpenAiEmbeddingService` + `CustomEmbeddingService` senden pro Batch (≤128 Texte) EINEN Request mit `input`-Array statt N Einzel-Requests; Antwort wird per `index` zurück in Eingabe-Reihenfolge sortiert. Andere Provider (google/voyage/ollama/bedrock/null) unverändert. Je 3 Tests mit Fake-HttpMessageHandler (10 Texte → 1 Request; reversed Response → Reihenfolge stimmt; >128 → 2 Requests). 1097 Tests grün.
- A6 · erledigt · 2026-07-02 · MCP-Token nur noch per Header: Query-Auth (`?token=`) im `/mcp`-Gate (`Program.cs`) entfernt — neue reine `BearerTokenParser` (Edda.Security) liest ausschließlich `Authorization: Bearer`; ein weiterhin gesendetes `?token=` wird ignoriert und mit Warnung geloggt. Entscheidung: entfernen statt behalten (der Query-String-Leak in Logs/History/Referrern ist der Kern des Issues). `docs/mcp.md` angepasst (README nutzte bereits nur den Header). 11 Parser-Tests. 1108 Tests grün.
- A10 · erledigt · 2026-07-02 · SecretRedactor im globalen Exception-Log-Pfad: neue reine `ExceptionRedactor.RedactForLog` (Edda.Security) redigiert `ex.ToString()` (inkl. Message + Inner) via `ISecretRedactor`; neue `SecretRedactingExceptionMiddleware` (Edda.Hosting) ersetzt im Non-Dev den Framework-`UseExceptionHandler` (der die Exception ungefiltert loggt) — loggt redigiert (Exception-Objekt wird NICHT an den Logger übergeben) + antwortet ProblemDetails 500; Dev behält die DeveloperExceptionPage. 3 Redaktions-Tests (sk-…-Muster → `[API_KEY_SK]`, auch Inner-Exception). 1111 Tests grün.
- D8 · erledigt · 2026-07-02 · CI-Pipeline: neuer GitHub-Actions-Workflow `.github/workflows/ci.yml` — Build & Test der `Edda.slnx` (Release) auf push (main) + PR, `ubuntu-latest`, .NET-SDK aus `global.json` (10.0.100); optionaler Docker-Build-Smoke (`continue-on-error`). Build+Test-Kommandos lokal in Release verifiziert (1111 grün), YAML geparst/validiert (keine Tabs, korrekte Jobs/Steps). Config-only → keine Code-/Test-Änderung.
- E4 · erledigt · 2026-07-02 · Export-Button im UI: `Import.razor` um eine Export-Karte mit Download-Link „Exportieren (JSON-Bundle)" (`<a href="/api/knowledge/export" download>`) ergänzt, `<PageTitle>` → „Import/Export", Nav-Label `nav.import` (de+en) → „Import/Export". Nutzt den bestehenden Export-Endpoint (kein neuer Endpoint). Runtime-Smoke: Endpoint liefert 200 + `Content-Disposition: attachment` + gültiges Bundle (schemaVersion=1, rules=12); `/import` rendert Link+Titel+Button. 1111 Tests grün.
- F2 · erledigt · 2026-07-02 · `TdkViolation` (Core) um `int? Line` + `string? Suggestion` erweitert (optionale Positionsparameter → abwärtskompatibel); Mapping in `TdkEngine.cs:189` reicht `v.Line`/`v.Suggestion` durch (waren im Validator-Output vorhanden, wurden verworfen); `TdkFeedbackFormatter` zeigt „(line N)" + „💡 Suggestion: …"; `/tdk`-Tabelle um Spalten „Zeile" + „Vorschlag" ergänzt. 2 neue Formatter-Tests (mit/ohne line/suggestion), Bestands-Tests unverändert grün. 1113 Tests grün.
- F3 · erledigt · 2026-07-02 · TDK-Engine-Fehler sichtbar + nicht konfidenz-verfälschend: `TdkResult.EngineErrors[]` (neuer Record `TdkEngineError`: RuleId, Reason, ExitCode, Stderr-Auszug, TimedOut). Die 4 Infra-Fehlerpfade in `TdkEngine` (Sandbox-Throw, ExitCode≠0/Timeout, kaputtes JSON, null-Output) buchen kein `RecordTdkOutcome(false)` mehr, sondern melden einen `EngineError` — nur der echte Validator-Verdikt (Pass/Fail) geht weiter in den Konfidenz-Store; `tdk_validate`-Tool-Antwort + `/tdk`-UI zeigen die Engine-Fehler. 2 Bestands-Tests korrigiert (bildeten das Bug-Verhalten „Infra-Fehler → RecordOutcome(false)" ab) + 2 neue Tests + Formatter-Test. 1115 Tests grün.
- D9 · erledigt · 2026-07-02 · [Rest-Quick-Win] `Neo4jCypherExecutor.ExecuteAsync` konsumiert den Result-Cursor jetzt deterministisch via `cursor.ConsumeAsync()`, statt sich zum Flushen auf das Session-Dispose zu verlassen; `QueryAsync` drainte den Cursor bereits via `ToListAsync`. Infrastruktur-Klasse (`[ExcludeFromCodeCoverage]`, nur integrationsgetestet) → kein Unit-Test; Bestandstests grün. 1115 Tests grün.
- Notiz (bei A6 gefunden) · übersprungen · `LocalAuthenticationHandler.cs:70` (`/api`-Auth mit `EDDA_AUTH_TOKEN`) akzeptiert `?token=` weiterhin — dieselbe Query-String-Leak-Schwachstelle wie A6, aber nicht im A6-Scope (Issue nennt nur den `/mcp`-Gate `Program.cs:~58`). Kandidat für einen Folge-Issue nach A6-Vorbild.
- B7 · erledigt · 2026-07-02 · [Rest-Quick-Win] Embedding-Retry mit Exponential-Backoff + Jitter: neue reine `ExponentialBackoff` (Core, `baseDelay·2^attempt` gedeckelt + `WithJitter`); `Neo4jEmbeddingCache.EmbedRuleAsync` wiederholt eine transiente `ProviderException` (429/5xx/Netz, außer `ProviderAuthException`) jetzt bis zu 3× mit TimeProvider-gesteuerter Backoff-Verzögerung, bevor der grobe Cross-Cycle-Zähler `embedAttempts` (max 5) greift; nicht-transiente Fehler propagieren sofort (Bestandsverhalten unverändert). `TimeProvider` als optionaler ctor-Param injiziert (Default `TimeProvider.System`; DI reicht den registrierten durch). 14 Backoff-Tests (Delays wachsen exponentiell/Jitter) + 3 FakeTimeProvider-Cache-Tests (Retry→Erfolg, Aufgeben nach Max→Failure gebucht, Auth→kein Retry). 1132 Tests grün.
- B9 · erledigt · 2026-07-02 · [Rest-Quick-Win] Kein unnötiger Head-Centroid-Rebuild mehr: `EmbeddingBackfillHostedService` ruft `IHeadVectorStore.RebuildAsync` nur noch auf, wenn der Cache-Durchlauf tatsächlich Chunks geschrieben hat; dafür gibt `INeo4jEmbeddingCache.RebuildAsync` jetzt die Anzahl (re)embeddeter Regeln zurück (0 = nichts geändert → Head-Pass wird übersprungen, spart die Leerlauf-Query/K-Means je Tick). Der Store-Teil des Issues (nur dirty/leere Heads clustern + `headVectorDirty` nach Erfolg via `ClearHeadDirtyAsync` löschen) war bereits umgesetzt — offen war nur der Aufrufer-Gate. 1 neuer Test (Leerlauf-Zyklus → Fake-Head-Store 0×), Bestandstest (Zyklus mit Chunks → ≥1×) unverändert grün. 1133 Tests grün.
- A9 · erledigt · 2026-07-02 · [Rest-Quick-Win] MCP-Token-Hashes gesalzen: `FileMcpTokenStore` erzeugt pro Token ein 16-Byte-Zufallssalt und speichert `Hash = SHA-256(salt ‖ token)` (hex) + `Salt` (base64url); `ResolveAsync` berechnet den erwarteten Hash pro Eintrag mit dessen Salt. Die Datei ist jetzt ein versioniertes Envelope `{ Version: 2, Tokens }`; Alt-Dateien (nacktes Array, ungesalzen) werden weiter geladen und akzeptiert (Eintrag ohne Salt → alter unsalted-Hash) und beim nächsten Schreiben ins v2-Format migriert — ohne Plaintext ist ein Re-Salten bestehender Einträge nicht möglich. 3 neue Tests (neu+gesalzen+Version, Alt-Eintrag resolved, Migration→v2), 6 Bestandstests unverändert grün. 1136 Tests grün.
- A5 · erledigt · 2026-07-02 · [Rest-Quick-Win] ForwardedHeaders opt-in hinter Reverse-Proxy: neue reine `TrustedProxyParser.Parse` (Edda.Security; kommagetrennte/`;`-getrennte Proxy-IPs → `IPAddress`-Liste, ungültige/leere übersprungen, dedupe). `Program.cs` konfiguriert `ForwardedHeadersOptions` (XForwardedFor|XForwardedProto, `KnownProxies` = geparste IPs, Framework-Loopback-Defaults via `KnownIPNetworks`/`KnownProxies` geleert) und ruft `UseForwardedHeaders()` als erste Middleware NUR, wenn `EDDA_TRUSTED_PROXIES` gesetzt ist — sonst werden Forwarded-Header ignoriert (RemoteIpAddress = direkter Peer, sicherer Default). `.env.example` + `docs/betrieb.md` (nginx/Caddy-Beispiel) dokumentiert. 10 Parser-Tests + Runtime-Smoke (Boot mit Trusted-Proxy, /health und XFF-Request → 200). 1146 Tests grün.
- A11 · erledigt · 2026-07-02 · [Rest-Quick-Win] Remote-/TLS-Betrieb dokumentiert: neuer Abschnitt „Remote-Betrieb hinter Reverse-Proxy (Caddy/nginx, TLS)" in `docs/betrieb.md` — stellt klar, dass Edda selbst kein TLS terminiert und nicht auf HTTPS umleitet (kein `UseHttpsRedirection`), sondern ein vorgelagerter Proxy TLS übernimmt; Checkliste (Loopback-Bind, `EDDA_AUTH_TOKEN`/`EDDA_ALLOW_INSECURE_REMOTE`, `EDDA_TRUSTED_PROXIES` mit Verweis auf den A5-Forwarded-Header-Abschnitt, TLS am Proxy: Caddy automatisch / nginx+certbot, HTTP→HTTPS-Redirect). Reine Doku, kein Code. Build 0 Warnungen, 1146 Tests unverändert grün.
- E6 · erledigt · 2026-07-02 · i18n vervollständigt: die fünf hart-deutschen Seiten (Embeddings, TDK, Settings, Sources, Import) auf `Loc["key"]` umgestellt; ~130 neue Schlüsselpaare in `LocalizationService` (de+en, jetzt 417/417 paritätisch, keine Duplikate/Waisen). Markup-Absätze via `MarkupString`, interpolierte Meldungen via `string.Format`; jede Seite abonniert `OnLanguageChanged`→`StateHasChanged` + `IDisposable` (Muster wie die bereits übersetzten Seiten). Marken (OpenAI/Google/…), technische Werte (`auto`, `us-east-1`, `true/false`) und `—`/`…`-Zeichen bleiben bewusst literal; Audit-Log-Texte sind kein UI. Kein hart codierter UI-String mehr in den fünf Seiten. Build 0 Warnungen, 1146 Tests grün; Runtime-Smoke: alle 5 Seiten rendern 200 mit de-Text.
- F9 · erledigt · 2026-07-02 · Deklaratives Sprach-Targeting für TDK-Validatoren: neues `KnowledgeRule.AppliesTo` (Frontmatter `appliesTo: [python, csharp]`, vom `KnowledgeRuleParser` gelesen; leer = alle Sprachen = bisheriges Verhalten unverändert). Neue reine `TdkLanguageMatcher.Applies` (alias-/case-insensitiv: py↔python, cs/c#↔csharp, js↔javascript, ts, sh↔bash; unbeschrifteter Block → läuft, um nichts zu verpassen). `TdkEngine` überspringt ein (Regel × Block)-Paar, dessen Blocksprache die Regel nicht targetet, VOR dem Sandbox-Start (spart Container-Läufe, beseitigt sprachfremde False-Positives). 17 Matcher-Tests + 2 Engine-Tests (nicht-targetet → `CreateAsync` nie / targetet → einmal). 1165 Tests grün.
- Notiz (bei F9 gefunden) · übersprungen · `KnowledgeRule.ValidatorScript` wird nirgends befüllt: `KnowledgeRuleParser` parst kein `validatorScript`-Frontmatter, `NodeMapper` mappt `r.validatorScript` nicht aus dem Graph, `RuleLoader` schreibt es nicht. TDK-Validator-Regeln (und damit auch `appliesTo`) lassen sich daher aktuell nur direkt konstruieren, nicht via Datei/Graph autoren — Kandidat für einen eigenen Folge-Issue (Validator-Authoring-Pipeline), bewusst NICHT in F9 angefasst.
- F10 · erledigt · 2026-07-02 · Severity-Semantik festgeschrieben: neue reine `TdkSeverity` (Rank: error=0/warning=1/info=2/unbekannt=3, `Normalize`). `TdkFeedbackFormatter.Format` sortiert Verstöße jetzt nach Schweregrad (error→warning→info, stabil innerhalb einer Stufe) und stellt eine Zählung pro Stufe voran („**N violation(s)** — E error, W warning, I info."); die `tdk_validate`-Antwort erhält das automatisch, da sie den Formatter nutzt. `docs/tdk.md` um Abschnitt „Schweregrade (`severity`)" (error=blockierend/muss behoben werden, warning=sollte, info=Hinweis) ergänzt. 12 `TdkSeverity`-Tests + 2 Formatter-Tests (Sortierung, Zählung); Bestands-Formatter-Tests unverändert grün. 1179 Tests grün.
- F13 · erledigt · 2026-07-02 · TDK-Ergebnis-Cache: neue `ITdkResultCache` (Core) + prozesslokale `InMemoryTdkResultCache` (ConcurrentDictionary, weich gedeckelt auf 1000 Einträge) + reine `TdkResultCacheKey.Compute` (SHA-256 über ruleId ∥ ValidatorScript ∥ Blocksprache ∥ Code, NUL-getrennt gegen Feldgrenzen-Kollisionen). `TdkEngine` prüft vor dem Sandbox-Lauf den Cache und übernimmt bei Treffer das gespeicherte Ergebnis (kein Sandbox-Lauf); Engine-/Infra-Fehler werden bewusst NICHT gecacht (transient), und der Konfidenz-Store wird bei Cache-Treffern nicht erneut gebucht (kein Flooding bei Agent-Loops, die denselben Code iterativ validieren). Cache optional injiziert (null = kein Cache = bisheriges Verhalten unverändert), als Singleton registriert. 11 neue Tests (Key-Stabilität/-Kollision/Hex, Get/Set/Overwrite, Engine: 2× identisch → Sandbox 1×, Verstoß 2× gemeldet, `RecordOutcome` 1×). 1190 Tests grün.
- Checkpoint 1 (Ende Phase 1) · erledigt · 2026-07-02 · Alle 26 Phase-1-Issues abgearbeitet. Kompletter Testlauf grün (1190 Tests, 0 übersprungen, 0 Warnungen bei TreatWarningsAsErrors). Runtime-Smoke (`GRAPH_PROVIDER=memory`, `EMBEDDING_PROVIDER=null`, MCP an): `/health`=200; UI `/settings`,`/knowledge`,`/import`=200 (E6-i18n rendert); `/api/akg/rules`=200 (D5); `/api/knowledge/export`=200 + `Content-Disposition` (E4); `/mcp` ohne Token=401 (Default-Deny-Gate/A6). Konsistenzprüfung: `docker compose config` valide; alle Phase-1-Env-Vars in `.env.example` ↔ `docker-compose.yml` ↔ `docs/betrieb.md` abgeglichen. GEFUNDEN & GESCHLOSSEN: `EDDA_TRUSTED_PROXIES` (A5) war in `.env.example`/`betrieb.md` dokumentiert, fehlte aber in `docker-compose.yml` (der App-Container hätte die Forwarded-Header-Konfiguration nie gesehen) → Zeile `EDDA_TRUSTED_PROXIES=${EDDA_TRUSTED_PROXIES:-}` ergänzt (Default leer = Status quo). Keine offenen Blockierungen; Phase 2 kann beginnen.
- C10 · erledigt · 2026-07-02 · [Phase 2] Periodische Gedächtnis-Konsolidierung: die bestehende Logik aus `ConsolidateTool` in einen neuen Service `IMemoryConsolidator`/`MemoryConsolidator` extrahiert (entfernt normalisierte Duplikate, prunt verblasste Memories); `ConsolidateTool` ist jetzt ein dünner Delegations-Wrapper (keine Logik-Duplikation). Neuer opt-in `MemoryConsolidationHostedService` (Vorbild `EmbeddingBackfillHostedService`, aber TimeProvider-getaktet für Testbarkeit): läuft alle `MEMORY_CONSOLIDATION_INTERVAL_HOURS` Stunden (Default 0 = Loop startet nie), konsolidiert pro User (neu: `IKnowledgeGraph.ListOwnersAsync(type)` → distinct Memory-Owner, dann je User) und schreibt eine Zusammenfassung ins Audit-Log (neues `AuditEvent.MemoryConsolidated`, userId=`system`). `.env.example` dokumentiert. 2 FakeTimeProvider-Tests (feuert im Intervall + Audit / Default 0 → nie) + 5 Consolidator-Logik-Tests (aus den Tool-Tests übernommen + All-Users-Aggregation) + 4 Tool-Delegations-Tests. 1195 Tests grün.
- C11 · erledigt · 2026-07-02 · [Phase 2] LLM-Enricher robuster: `LlmIngestionEnricher` wiederholt transiente Provider-Fehler (429 via `ProviderRateLimitException` + StatusCode 408/5xx) bis zu 2× mit Exponential-Backoff (wiederverwendet `ExponentialBackoff` aus B7, TimeProvider-getaktet für Testbarkeit); nicht-transiente Fehler bzw. erschöpfte Retries → best-effort, Item bleibt unverändert (loggt sauber). Bei unparsbarer JSON-Antwort jetzt EIN Reparatur-Versuch mit strengerem System-Prompt (`RepairSystemPrompt`, „ONLY JSON"), erst danach Warnung + Item unverändert. `TimeProvider` in den ctor injiziert (DI löst auf). `FakeLlmChatClient` um Sequenz-/`ThrowsThenReturns`-Fabriken + `CallCount` erweitert. 5 neue/verstärkte Tests (429→Retry→Erfolg, Garbage→Repair→Erfolg, Garbage→Repair→unverändert, Retries erschöpft→unverändert, non-transient→sofort auf), Bestandstests unverändert grün. 1199 Tests grün.
- B2 · erledigt · 2026-07-02 · [Phase 2] Embedding-Cache erkennt Provider-/Modellwechsel: `Neo4jEmbeddingCache` speichert auf jedem `(:RuleChunk)` einen Fingerprint `provider:model:dimension` (injizierter `Func<string>`; DI baut ihn aus Settings-Provider/-Model + `IEmbeddingService.Dimensions`, Fallback dimension-only). Die Backfill-Auswahl-Query selektiert jetzt zusätzlich Regeln mit stale-Fingerprint-Chunks (`coalesce(c.embeddingFingerprint,'') <> $fingerprint` — fehlender Fingerprint = stale = Migrationspfad) → betroffene Chunks werden neu embedded. `EnsureVectorIndexAsync` merkt sich die zuletzt gebaute Dimension auf einem Meta-Knoten `(:EddaEmbeddingMeta {id:'chunk_index'})`; bei Dimensionswechsel `DROP INDEX chunk_embeddings` + Neuanlage in neuer Dimension (gleiche Dim/anderer Provider → nur Re-Embed, Index bleibt). 1 neuer Test (Provider A dim4 → B dim8: Index dropped+recreated, Chunk mit neuem Fingerprint neu gebaut), 1 Bestandstest an das zusätzliche Meta-Write angepasst. 1200 Tests grün.
- Notiz (bei B2 gefunden) · übersprungen · Der Head-Vektor-Index (`head_embeddings`, `Neo4jHeadVectorStore.EnsureIndexAsync`) hat dieselbe Dimensionswechsel-Blindheit (`CREATE ... IF NOT EXISTS`) wie der Chunk-Index vor B2: bei Dimensionswechsel werden Head-Vektoren zwar neu berechnet (dirty-Flag), der Index bleibt aber in alter Dimension → Head-Retrieval fällt auf App-seitigen Cosine zurück. Kandidat für einen Folge-Fix nach B2-Vorbild (Meta-Dimension + DROP/CREATE), NICHT im B2-Datei-Scope (`Neo4jEmbeddingCache.cs`) angefasst.
- C8 · blockiert · 2026-07-02 · Beschreibung passt nicht mehr: `Neo4jEntityStore.IngestAsync` nutzt bereits durchgängig `MERGE` — Entities `MERGE (e:Entity {ownerId, normalizedName})` mit `ON CREATE`/`ON MATCH` (mentions-Counter), Relationen `MERGE (s)-[:RELATES_TO]->(t)` mit weight-Counter; die Entity-`id` ist deterministisch (`entity-{normalizedName}`) und es gibt In-Batch-Dedup (`GroupBy(normalizedName)`). Ein Doppel-Ingest desselben Texts dedupliziert damit bereits (kein `CREATE`-Node-Pfad — die einzigen `CREATE`-Vorkommen sind `ON CREATE SET`; einzige `IEntityStore`-Impl; MERGE seit dem Initial-Commit). Der beschriebene „appended blind / dupliziert"-Zustand existiert nicht. Bestandstests decken `MERGE`-Nutzung + In-Batch-Dedup ab; die Graph-Ebenen-Dedup ist durch die MERGE-Semantik garantiert (mit dem Fake-Executor nicht sinnvoll unit-testbar). Kein Code geändert (Schritt 2). [Wiederkehrendes M2/M3-Muster: Stack existiert bereits.]
- D2 · erledigt · 2026-07-02 · [Phase 2] Fire-and-Forget `Task.Run` beaufsichtigt: neue `IBackgroundWorkQueue` (Core) + `BackgroundWorkItem`-Record, Channel-basierte `ChannelBackgroundWorkQueue` (unbounded, Single-Reader/Multi-Writer → nicht-blockierendes `Enqueue` aus synchronen Call-Sites, kein Silent-Drop) + hosted `BackgroundWorkQueueConsumer` (`BackgroundService`, reicht den Stopping-Token an jedes Item durch, isoliert Item-Fehler per Log statt den Loop abzureißen). Die drei `_ = Task.Run(...)`-Stellen enqueuen jetzt statt zu detachen und bekommen den Shutdown-Token statt `CancellationToken.None`: `Neo4jKnowledgeGraph.EndBulkIngestion` (Post-Import-Rebuild), `AkgEndpoints` `/api/akg/embed/rebuild`, `WorldKnowledgeSeedHostedService` (Superseded-Invalidation). DI: Queue-Singleton + Consumer-HostedService in `AddAkgServices`; `Neo4jKnowledgeGraph`- und Seed-Service-Ctor um den Queue-Parameter erweitert (Testkonstruktionen angepasst). 10 neue Tests (6 Queue: enqueue→dequeue-identisch, FIFO, wartet-bis-enqueued, Token-Cancel→OCE, Null-Work→ArgumentNull, Null-Desc→leer; 3 Consumer: läuft Work, isoliert Fehler + nächstes Item läuft, Stopping-Token wird bei Stop gecancelt; 1 Seed-Service: StartAsync enqueued die Invalidation). Akzeptanz erfüllt: kein `_ = Task.Run` mehr in den drei Dateien (grep-verifiziert). Build 0 Warnungen, 1210 Tests grün. Runtime-Smoke (`GRAPH_PROVIDER=memory`): App bootet mit Queue+Consumer, `/health`=200, `POST /api/akg/embed/rebuild`=202; Log belegt, dass der Consumer BEIDE enqueuten Pfade drainte + ausführte (Startup-Invalidation „Superseded rules invalidated" + Endpoint-Rebuild „All chunk embeddings cleared; rebuilding from scratch").
- Notiz (bei D2 gefunden) · übersprungen · `src/Web/Components/Pages/Embeddings.razor:408` enthält ein viertes `_ = Task.Run(() => KnowledgeGraph.ResetAndRebuildEmbeddingsAsync(CancellationToken.None))` (UI-Button „Re-Embed") — dieselbe unbeaufsichtigte Fire-and-Forget-Form wie die drei D2-Stellen, aber NICHT im D2-Scope (Issue nennt nur drei Dateien; Akzeptanz: „kein `_ = Task.Run` mehr in den drei Dateien"). Kandidat für einen Folge-Fix (Button enqueued auf `IBackgroundWorkQueue`). Bewusst NICHT angefasst.
- E3 · erledigt · 2026-07-02 · [Phase 2] Erste-Schritte-Guide + Glossar (nur Doku, deutsch): neue `docs/erste-schritte.md` — geführtes 15-Minuten-Tutorial entlang der mitgelieferten `knowledge/`-Beispiele (Graph öffnen → `search_memory` → erste eigene Regel via UI **und** Datei mit Frontmatter → Agent über MCP anbinden HTTP/SSE + stdio → TDK auf `/tdk` mit dem mitgelieferten `no-plaintext-secrets`-Validator, inkl. ehrlicher Sandbox-Voraussetzung `TDK_SANDBOX_TYPE`). Neue `docs/glossar.md` — alle Fachbegriffe aus README + `CLAUDE.md` alphabetisch (AKG, TDK, MMR, Head-Vektor, Decay, MCP/HTTP-SSE/stdio, 4-Phasen-Kontext-Kompilierung inkl. Konfliktauflösung, Feedback-Konfidenz, Embedding/Chunk, Sandbox, alle Tools, Frontmatter-Felder/Regeltypen, Env-Vars, `IFileSystem`/`TimeProvider`/`ToolExecutionContext`, …). Im README verlinkt: „Neu hier?"-Callout oben + zwei Zeilen in der Doku-Tabelle. Reine Doku, kein Code — Build 0 Warnungen, Tests unverändert 1210 grün (aus D2). Term-Coverage-Check (50 Marquee-Begriffe) bestätigt: alle im Glossar. Hinweis zur Ehrlichkeit: die TDK-Sektion verspricht keinen automatisch feuernden Seed-Validator (die `validatorScript`-Datei-Pipeline ist noch dormant, siehe F9-Notiz), sondern beschreibt den `/tdk`-Pfad + Sandbox-Setup und verweist auf `docs/tdk.md`.
- B8 · erledigt · 2026-07-02 · [Phase 2] Chunker-Overlap über Block-Grenzen: `AdaptiveDocumentChunker.Pack` stellt am Pack-Seam den Schluss (bis `OverlapChars`) des Vorgänger-Blocks dem Folge-Chunk voran — aber NUR wenn Vorgänger UND Folgeblock `BlockKind.Text` sind (nie Code-/Table-Inhalt in Nachbar-Chunks bluten lassen), gedeckelt aufs Größenbudget (Skip, wenn Tail+Block > maxChars), Muster wie die Intra-Block-Overlap in `RecursiveTextSplitter` (`Tail`-Helper). `Pack` auf `internal` gehoben, damit der Text↔Text-Seam direkt testbar ist. WICHTIG/ehrlich: der `BlockSegmenter` erzeugt nie zwei benachbarte Text-Blöcke (zusammenhängende Text-Regionen werden zu EINEM Block gemerged, stets durch Code/Table getrennt) → über die öffentliche `Chunk`-API ist der Text↔Text-Seam derzeit nicht erreichbar; der Fix ist damit ein korrekter Boundary-Guard, der die Chunk-Ausgabe realer Dokumente (noch) nicht ändert — Code-/Table-Kanten bleiben bewusst overlap-frei, genau wie vom Issue gefordert (»wenn beide Textstil«). 7 neue Tests (6× `Pack` direkt: Text↔Text-Overlap, Text→Code + Code→Text kein Overlap, Zero-Overlap lossless, Overlap>Budget→Skip, Determinismus; 1× öffentliche Determinismus-Prüfung prose→code→prose). Determinismus bleibt (reine Funktion, kein Zustand/Zeit). Bestehende Chunker-Tests unverändert grün. Build 0 Warnungen, 1217 Tests grün.
- E7 · erledigt · 2026-07-02 · [Phase 2] Markdown-Rendering/Vorschau im Rule-Editor: neuer UI-Service `IMarkdownRenderer` + `MarkdigMarkdownRenderer` (`src/Web/Services`) auf Basis Markdig 1.3.2 (NuGet, self-hosted, kein CDN — Regel 12; Paket-Echtheit vor Nutzung geprüft: Autor Alexandre Mutel/xoofx, Repo github.com/xoofx/markdig, da 1.3.2 vom früheren 0.x-Schema abweicht). Sanitisierung: `.DisableHtml()` (Roh-HTML wie `<script>` wird escaped statt ausgeführt) + bewusst NUR sichere Extensions (`UsePipeTables` + `UseAutoLinks`, NICHT das Advanced-Bundle, das GenericAttributes/Attribut-Injection enthält) + Neutralisierung gefährlicher URL-Schemata (`javascript:`/`vbscript:`/`data:`) auf Links/Bildern. `RuleDetail.razor` rendert den Body jetzt als sanitized Markdown (`MarkupString`) statt Rohtext; `RuleEditor.razor` bekommt Edit/Vorschau-Tabs (neue Loc-Keys `knowledge.rule.tab.edit`/`.preview`, de+en). DI: Singleton (stateless). Neues Testprojekt `tests/Web.Tests` (Edda.Web.Tests, `FrameworkReference` AspNetCore.App + ProjectReference Web) mit 8 Tests: `<script>`/`<img onerror>` escaped, `javascript:`/`data:`-Links neutralisiert, `**bold**`/`# Heading`/Code-Fence gerendert, null/blank → leer. Build 0 Warnungen, 1225 Tests grün (8 neu). Runtime-Smoke (`GRAPH_PROVIDER=memory`): App bootet (IMarkdownRenderer aufgelöst), `/health`=200, `/` (Knowledge, instanziiert `RuleDetail`)=200, keine Fehler im Log.
- A7 · erledigt · 2026-07-02 · [Phase 2] Ressourcen-Limits im Wasm-/Subprocess-Runner: neue interne `WasmProcessLimits` (Sandboxing) + überarbeiteter `DefaultWasmScriptRunner`. Hartes Wall-Clock-Kill (Prozessbaum) bleibt; NEU: Prozess-Priorität `BelowNormal` (best-effort), Ausgabe-Cap (stdout/stderr je ≤ 1 Mio. Zeichen via `ReadCappedAsync` → bei Überlauf Prozess-Kill = DoS-Schutz gegen Ausgabe-Flut) und unter Linux ein `ulimit`-Wrapper via `/bin/sh -c "ulimit -t <timeout>; ulimit -f 10MB; ulimit -v 1GiB; exec python3 …"` (CPU-Zeit/Dateigröße/Adressraum). `BuildStartInfo(scriptPath, timeout, isLinux)` gekapselt + testbar (nicht-Linux → `python3` direkt). Vollständige cgroup-Isolation bewusst out-of-scope; Restrisiko in `docs/tdk.md` dokumentiert (neuer Abschnitt „Ressourcen-Grenzen und Restrisiko der `wasm`-Sandbox" — für nicht vertrauenswürdige Validatoren `docker` empfehlen). 7 neue Unit-Tests (`BuildStartInfo` Linux-ulimit/CPU-Clamp/non-Linux/Redirect; `ReadCappedAsync` unter/über/exakt Cap); der Bestands-Fake-Runner-Timeout-Test (`WasmSandbox`) deckt das Kill-Surfacing ab. Kein echter `python3`-Spawn im Test (Regel 7: ohne Infrastruktur). Build 0 Warnungen, 1232 Tests grün (7 neu).
- B6 · erledigt · 2026-07-02 · [Phase 2] O(N)-Brute-Force-Fallback gedeckelt: der app-seitige Cosine-Fallback in `SemanticBooster` (aktiv nur ohne nativen Vektorindex, z. B. Memgraph/Index-Fehler) bewertet jetzt nur noch die Top-M keyword-stärksten Kandidaten statt aller. Neue reine `SelectFallbackCandidates(orderedIds, max)` (Top-M der bereits keyword-sortierten Kandidaten); `BoostAsync` reicht die geordnete Kandidatenliste durch (statt eines Sets) — der schnelle Index-Pfad nutzt weiter das volle Membership-Set, NUR der Fallback wird gedeckelt. Neue Option `RetrievalOptions.FallbackMaxCandidates` (Default 500 = unverändertes Verhalten für Graphen mit ≤500 Keyword-Treffern) via `RETRIEVAL_FALLBACK_MAX_CANDIDATES` (Resolver + `.env.example`). Der Fallback misst seine Latenz (`Stopwatch` — reine Diagnose, keine zeitabhängige Verzweigung → Regel 3 unberührt) und loggt bei jeder Aktivierung eine WARNUNG („scored X of Y candidate rules in Zms" + Hinweis, dass der Pfad O(N) ist und ein Vektorindex nötig ist). 4 neue Tests (`SelectFallbackCandidates` über/innerhalb/exakt Cap; Fallback-Aktivierung → WARNUNG via Capturing-Logger). Build 0 Warnungen, 1236 Tests grün (4 neu).
- F1 + F17 · erledigt · 2026-07-02 · [Phase 2] validatorScript-Kette repariert + Import-Hygiene. **F1:** `validatorScript` fließt jetzt durch die gesamte Kette — (1) `KnowledgeRuleParser` versteht YAML-Block-Scalars (`validatorScript: |` + eingerückte Zeilen → mehrzeiliger String; Indent-Strip, Trailing-Clip) und setzt `ValidatorScript`; (2) `RuleLoader`-Upsert schreibt `r.validatorScript = $validatorScript`; (3) `NodeMapper` liest es in allen drei Pfaden zurück (`MapNode`/`MapKnowledgeRow`/`MapDictionary`); (4) `InMemoryCypherExecutor.UpsertRule` übernimmt den Key; (5) `FrontmatterSerializer` schreibt es als Block-Scalar (Markdown-Roundtrip). Vorher wurde das Feld NIRGENDS in src/ zugewiesen → TDK war end-to-end funktionslos. **F17:** `KnowledgeImporter` strippt `validatorScript` aus JSON-Fremd-Bundles per Default (`rule with { ValidatorScript = null }`), Opt-in via `IMPORT_ALLOW_VALIDATORS=true` (DI liest den Key; `.env.example` dokumentiert) → Fremd-Bundles schleusen keinen ausführbaren Validator-Code ein (analog zur Vektor-Hygiene). 7 neue Tests (Parser: Block-Scalar-Extraktion + kein-Validator→null; RuleLoader: Upsert enthält validatorScript; NodeMapper: mappt validatorScript; Serializer: Serialize→Parse-Roundtrip; Importer: Default strippt + Flag behält). Verifikation der Akzeptanz: (a) Runtime-Smoke `GRAPH_PROVIDER=memory` — nach Baseline-Seed trägt `security-no-plaintext-secrets` via `GET /api/akg/rules/...` seinen `validatorScript` (Parse→Upsert→InMemory→Map→API); (b) der geladene Validator, gegen `password = "geheim123"` ausgeführt, liefert `pass:false` + Violation „Plaintext password detected". Der Neo4j+Docker-Vollpfad ist Integration (Regel 7, nicht unit-getestet), der reparierte Lade-Pfad — der eigentliche Bug — ist an beiden Enden verifiziert. Markdown-Roundtrip erhält den Validator; JSON-Bundle-Import strippt ihn bewusst per Default (F17), mit Opt-in erhalten. Build 0 Warnungen, 1243 Tests grün (7 neu).
- F7 · erledigt · 2026-07-02 · [Phase 2] Validator-Versionierung + Kill-Switch: (1) neue reine `ValidatorScriptHash.Compute` (Core, SHA-256-Hex); `RuleLoader` berechnet + persistiert `r.validatorHash` (Nachvollziehbarkeit der Konfidenz-Historie), `NodeMapper` (alle 3 Pfade) + InMemory + Smoke bestätigen den Roundtrip. (2) neues `KnowledgeRule.ValidatorEnabled` (Default true): Parser liest `validatorEnabled: false`, durch die Kette persistiert/gemappt, `FrontmatterSerializer` schreibt es nur wenn false; `TdkEngine` überspringt Regeln mit `ValidatorEnabled=false` (Kill-Switch — Regel bleibt im Graph, Validator läuft aber nicht). (3) Konfidenz-Reset: `IRuleConfidenceStore.RecordOutcome` bekommt einen optionalen `validatorHash` (abwärtskompatibel); `SlidingWindowRuleConfidenceStore` merkt sich den Hash pro Regel und LEERT das Outcome-Fenster, wenn er sich ändert (alte Outcomes maßen alten Code); `TdkEngine` reicht `ValidatorScriptHash.Compute(rule.ValidatorScript)` durch. 15 neue Tests (Hash 4, Parser 2, Loader 1, Mapper 2, Confidence-Reset 3, Serializer 2, Engine-Kill-Switch 1). Runtime-Smoke (`GRAPH_PROVIDER=memory`): `security-no-plaintext-secrets` trägt via API `validatorHash=481b3c03…` + `validatorEnabled=true`. Build 0 Warnungen, 1258 Tests grün (15 neu).
- Notiz (bei F7 gefunden) · übersprungen · `Neo4jKnowledgeGraph.UpsertRuleAsync` (`:195-209`) persistiert `validatorScript`/`validatorEnabled`/`validatorHash` NICHT — F1 reparierte nur `RuleLoader.UpsertRuleAsync` (den Datei-Lade-Pfad, exakt wie im F1-Issue benannt). Regeln, die über den UI-Editor oder den Bundle-Import (`_graph.UpsertRuleAsync`) geschrieben werden, verlieren daher ihren Validator (und die F7-Felder); das F17-Stripping ist auf diesem Pfad folglich derzeit faktisch ein No-op. Bewusst NICHT in F7 angefasst (F1-Scope). Kandidat für einen Folge-Fix: `Neo4jKnowledgeGraph.UpsertRuleAsync` (+ InMemory-Param-Übergabe) auf denselben Validator-Feldsatz bringen.
- F14 (Bibliotheks-Validator 1/5) · erledigt · 2026-07-02 · [Phase 2] Erster mitgelieferter TDK-Validator: neue Regel `knowledge/coding/no-blocking-async.md` mit `validatorScript` (Python, roh-stdin/stdout — der F4-Helper ist laut Plan kein Blocker) erkennt blockierendes Warten auf async-Code in C# (`.Result` / `.Wait()` / `.GetAwaiter().GetResult()`; je Treffer eine Violation mit Zeile + `severity=warning` + await-Suggestion; `appliesTo: [csharp]`; `requires` → `coding-async-await-patterns`). Der Body enthält Korrekt/Falsch-Beispiele (die späteren F5-Fixtures). Verifiziert: python3-Smoke (Bad-C# → `pass:false`, 3 Violations mit korrekten Zeilen; Good-C# → `pass:true`) + App-Boot (`GRAPH_PROVIDER=memory`): `GET /api/akg/rules/coding-no-blocking-async` lädt die Regel über die volle Pipeline mit `validatorScript` + `validatorHash` (F7). Kein C#-Code geändert → Build 0 Warnungen, 1258 Tests unverändert grün. **F14 ist inkrementell (je einer pro Session) und NOCH NICHT abgeschlossen** — nächster Durchlauf: Validator 2/5 (Kandidaten: `except: pass` (Py), SQL-String-Konkatenation, `eval/exec`, `console.log` (JS)). Nach 5/5 folgt Checkpoint 2 (Benchmark-Baseline).
- Notiz (bei F14 gefunden) · übersprungen · `KnowledgeRule.AppliesTo` (F9) wird NICHT persistiert: der `RuleLoader`-Upsert schreibt kein `r.appliesTo`, der `NodeMapper` mappt es nicht zurück (analog zum `validatorScript`-Gap vor F1). Der Parser liest `appliesTo` zwar, aber über den Graph-Read-Back ist es leer → die Engine wendet jeden Validator auf ALLE Blocksprachen an (F9-Sprach-Targeting greift für datei-geladene Regeln nicht). Praktisch geringes Risiko für `coding-no-blocking-async` (die `.Result`/`.Wait()`-Muster sind C#-spezifisch), aber ein echter F9-Folge-Gap: `appliesTo` müsste dieselbe Kette (Upsert→Mapper→InMemory→Serializer) bekommen wie `validatorScript`. Bewusst NICHT in F14 angefasst (Scope = Validator schreiben).
- F14 (Bibliotheks-Validator 2/5) · erledigt · 2026-07-02 · [Phase 2] Zweiter mitgelieferter TDK-Validator: neue Regel `knowledge/coding/no-bare-except.md` erkennt stilles Verschlucken von Exceptions in Python — `except ...: pass` (`severity=error`) und nacktes `except:` (`severity=warning`, fängt auch `SystemExit`/`KeyboardInterrupt`); zeilen-dedupliziert (ein `except: pass` zählt einmal als error, nicht zusätzlich als bare-except-warning), `appliesTo: [python]`, `requires` → `world-security-principles`. Body mit Korrekt/Falsch-Beispielen (spätere F5-Fixtures). Verifiziert: python3-Smoke (`except:`/`pass` + `except Exception:`/`pass` → 2 error-Violations Zeilen 3+8; korrektes `except ValueError` mit Handling → pass; nacktes `except:` mit Handling → 1 warning) + App-Boot (`GRAPH_PROVIDER=memory`): `GET /api/akg/rules/coding-no-bare-except` lädt mit `validatorScript` + `validatorHash`. Kein C#-Code → Build 0 Warnungen, 1258 Tests unverändert grün. **F14 weiter inkrementell (2/5)** — nächster Durchlauf: Validator 3/5 (Kandidaten: `eval/exec`, SQL-String-Konkatenation, `console.log` (JS)). Nach 5/5 → Checkpoint 2.
- F14 (Bibliotheks-Validator 3/5) · erledigt · 2026-07-02 · [Phase 2] Dritter mitgelieferter TDK-Validator: neue Regel `knowledge/security/no-eval-exec.md` erkennt `eval()`/`exec()` auf dynamischem Code (Python/JS/TS) — RCE-Risiko, `severity=error`; ein Negativ-Lookbehind `(?<![\w.])` schließt Methodenaufrufe (`obj.eval`, `regex.exec`), `ast.literal_eval` und Funktionen mit „eval"-Suffix (`myeval`) aus. `appliesTo: [python, javascript, typescript]`, `requires` → `world-security-principles`. Body mit Korrekt/Falsch-Beispielen (spätere F5-Fixtures). Verifiziert: python3-Smoke (`eval(user_input)` + `exec(...)` + JS-`eval` → 3 error-Violations; `ast.literal_eval`/`json.loads`/`regex.exec`/`obj.eval`/`myeval` → pass) + App-Boot (`GRAPH_PROVIDER=memory`): `GET /api/akg/rules/security-no-eval-exec` lädt mit `validatorScript` + `validatorHash`. Kein C#-Code → Build 0 Warnungen, 1258 Tests unverändert grün. **F14 weiter inkrementell (3/5)** — nächster Durchlauf: Validator 4/5 (Kandidaten: SQL-String-Konkatenation, `console.log` (JS), HTTP-ohne-TLS). Nach 5/5 → Checkpoint 2.
- F14 (Bibliotheks-Validator 4/5) · erledigt · 2026-07-02 · [Phase 2] Vierter mitgelieferter TDK-Validator: neue Regel `knowledge/security/no-sql-string-concat.md` erkennt SQL, das per String-Verkettung/Interpolation gebaut wird (SQL-Injection-Risiko, `severity=warning`) — 4 Muster: `"…SELECT…" +` (Konkatenation), `f"…"`/`$"…{…}"` (Python-f-String/C#-Interpolation), `` `…${…}` `` (JS/TS-Template-Literal), `"…".format(` (Python). `[^\n]*?` (zeilen-gebunden, non-greedy) verkraftet innere Quotes (`name = '{name}'`), zeilen-dedupliziert. `appliesTo: [python, csharp, javascript, typescript]`, `requires` → `world-security-principles`. Body mit Korrekt (parametrisierte Queries)/Falsch-Beispielen (spätere F5-Fixtures). Verifiziert: python3-Smoke (5 Bad-Muster über Python/C#/JS → 5 Violations Zeilen 1-5; parametrisierte Query + statische SQL-Strings → pass) + App-Boot (`GRAPH_PROVIDER=memory`): `GET /api/akg/rules/security-no-sql-string-concat` lädt mit `validatorScript` + `validatorHash`. Kein C#-Code → Build 0 Warnungen, 1258 Tests unverändert grün. **F14 weiter inkrementell (4/5)** — nächster Durchlauf: Validator 5/5 (Kandidaten: `console.log` (JS), HTTP-ohne-TLS, hartkodierte IPs). Nach 5/5 → Checkpoint 2.
- F14 (Bibliotheks-Validator 5/5 — Erste 5 abgeschlossen) · erledigt · 2026-07-02 · [Phase 2] Fünfter mitgelieferter TDK-Validator + Abschluss von Plan-Schritt 28: neue Regel `knowledge/coding/no-leftover-debug.md` erkennt vergessene Debug-Reste in JS/TS — `console.log/debug/info(` und `debugger`-Statements (`severity=warning`); `console.error`/`console.warn` bleiben zulässig; `mydebugger` (Wortgrenze) wird nicht getroffen. `appliesTo: [javascript, typescript]`. Body mit Korrekt/Falsch-Beispielen (spätere F5-Fixtures). Verifiziert: python3-Smoke (`console.log` + `console.debug` + `debugger` → 3 Violations; `logger.info`/`console.error`/`console.warn`/`const mydebugger` → pass) + App-Boot (`GRAPH_PROVIDER=memory`): `GET /api/akg/rules/coding-no-leftover-debug` lädt mit `validatorScript` + `validatorHash`. Kein C#-Code → Build 0 Warnungen, 1258 Tests unverändert grün. **F14-Start (Plan-Schritt 28) abgeschlossen** — die ersten 5 Bibliotheks-Validatoren stehen: `no-blocking-async` (C#-Async), `no-bare-except` (Py-Exceptions), `no-eval-exec` (RCE), `no-sql-string-concat` (SQLi), `no-leftover-debug` (Debug-Hygiene). Die restlichen Bibliotheks-Validatoren (6–15) sind Roadmap/Phase-3, NICHT Teil von Schritt 28. **Nächster Plan-Schritt: Checkpoint 2** (Benchmark-Baseline mit `AkgBenchmarkRunner` → `docs/benchmarks.md`; Pflichtgrundlage für Phase 3).
- Checkpoint 2 (Ende Phase 2) · erledigt · 2026-07-02 · Kompletter Testlauf grün (**1258 Tests**, 0 übersprungen, 0 Warnungen bei TreatWarningsAsErrors). Runtime-Smoke (`GRAPH_PROVIDER=memory`, `EMBEDDING_PROVIDER=null`): `/health`=200, `/knowledge`=200, `/`=200. **Benchmark-Baseline erzeugt** (`AkgBenchmarkRunner` über `POST /api/akg/benchmark`, k=5, Datensatz `bundled-knowledge-baseline` mit 11 Fällen über die 12 mitgelieferten Regeln): **Recall@5=0.727, Precision@5=0.164, MRR=0.636, nDCG@5=0.660**, Latenz p50=20.9 ms / p95=53.4 ms, Ø 2858 Tokens/Anfrage. Keyword-only (kein Embedding-Provider) → ohne Secrets exakt reproduzierbar. 8/11 Fälle voll abgedeckt; 3 Fälle (`secrets`, `secprin`, `arch`) verfehlen die erwartete Regel — die erwartete Keyword-Schwäche, an der B1/semantische Suche messbar zulegen soll. Zahlen + Datensatz + Reproduktion in `docs/benchmarks.md` + `docs/benchmark-baseline-dataset.json` festgehalten (Pflichtgrundlage für Phase 3, insb. B1). Keine offenen Blockierungen aus Phase 2 (nur die dokumentierten Folge-Notizen: Neo4jKnowledgeGraph-Upsert persistiert Validator-Felder nicht; AppliesTo nicht persistiert; Head-Index-Dimensionswechsel; RuleLoader-N+1; LocalAuth `?token=`). **Phase 2 abgeschlossen — nächster Plan-Schritt: Phase 3** (⚠️-Issues, pro Issue erst Spec unter `docs/plans/`; erster laut Phase-3-Tabelle: B1).
- B1 · spec-erstellt · 2026-07-02 · [Phase 3] Detail-Spec `docs/plans/0003-b1-keyword-tokenisierung.md` geschrieben (nur Spec, kein Code — Phase-3-Vorgehen). **Stufe 1**: `KeywordScorer` von `string.Contains` (Substring) auf **Ganz-Token-Match** umstellen (Tokenizer über `char.IsLetterOrDigit`; ein Tag/Concept matcht den Task nur, wenn ALLE seine Token als ganze Wörter vorkommen → `"test"` matcht nicht `"latest"`, hyphenierte/mehrwortige Tags nur bei allen Token) **+ Score-Sättigung** `priorityMultiplier * log(1 + matchCount)` (No-Match bleibt 0, Prioritätsverhältnis 2.0 bleibt, Ranking monoton). Spec enthält exakte Änderungsstellen (`KeywordScorer.cs` Zeilen ~71/85/95), Testfälle (Bestandstests bleiben/legitim angepasst + 3 neue: Substring-Nicht-Match, hyphenierter Tag, Sättigung) und die **Benchmark-Pflicht** (Vergleich gegen die Checkpoint-2-Baseline; neutral-oder-besser, sonst Revert). Stufe 2 (IDF) bewusst ausgeklammert (eigener Spec). Abgrenzung dokumentiert (conceptSet-Exaktmatch/Prioritäten/Domain-Bonus/andere Retrieval-Phasen/DI unangetastet). Kein Code geändert → Build 0 Warnungen, 1258 Tests unverändert grün. **Nächster Schritt: B1 UMSETZEN** — die Spec `docs/plans/0003-b1-keyword-tokenisierung.md` existiert lokal (gitignored), also laut Phase-3-Regel strikt danach IMPLEMENTIEREN (spec-erstellt ≠ erledigt → B1 NICHT überspringen). Erst danach C3 (Spec).
- B1 · erledigt · 2026-07-02 · [Phase 3] Keyword-Tokenisierung + Score-Sättigung (Stufe 1) strikt nach Spec `docs/plans/0003` umgesetzt: `KeywordScorer` matcht Tags/Concepts jetzt gegen ganze Task-Token (neuer `Tokenize` über `char.IsLetterOrDigit`; `MatchesWholeTokens`: alle Phrasen-Token ⊆ Task-Token) statt `string.Contains` → `"test"` matcht nicht mehr `"latest"`, hyphenierte/mehrwortige Tags nur bei allen Token. Score gedeckelt: `priorityMultiplier * log(1 + matchCount)` (No-Match bleibt 0, Prioritätsverhältnis + Ranking-Monotonie erhalten → alle 9 Bestandstests unverändert grün, keine Anpassung nötig). 3 neue Tests (Substring-Nicht-Match, hyphenierter Tag, Sättigung sub-linear). **Benchmark vorher/nachher** (Checkpoint-2-Datensatz, memory/keyword-only, k=5) in `docs/benchmarks.md`: Recall@5 0.727→**0.818**, Precision@5 0.164→0.182, MRR 0.636→**0.705**, nDCG@5 0.660→**0.733** — messbar BESSER auf allen 4 Metriken (secprin Recall 0→1.0, except MRR 0.5→1.0; verbleibend `secrets`/`arch` = Keyword-Grenzen für die semantische Phase). Stufe 2 (IDF) bewusst ausgeklammert (eigener Spec nötig). Build 0 Warnungen, 1261 Tests grün (3 neu). Nächster Phase-3-Schritt: C3 (⚠️ → Spec).
- C3 · spec-erstellt · 2026-07-03 · [Phase 3] Detail-Spec `docs/plans/0004-c3-kontradiktions-erkennung.md` (lokal, da `docs/plans/` gitignored) geschrieben — nur Spec, kein Code. LLM-freie Kontradiktions-Erkennung im episodischen Gedächtnis: neue reine `MemorySimilarity` (Token-Jaccard, auch von C4 wiederverwendbar); `RememberTool` prüft beim `remember` bestehende Nutzer-Memories (`GetRulesAsync type=Memory`) auf Jaccard > `MEMORY_SUPERSEDE_JACCARD` (Default 0.6, Schwellwert per Ctor injiziert, best-effort/try-catch → `remember` nie schlechter als heute), setzt `RelatesTo.Supersedes` am neuen Memory (→ `SUPERSEDES`-Kante + `r.supersedes`-Property beim Upsert) und meldet „Possibly supersedes: …"; `RecallTool` wertet Supersede-Ziele der geladenen Menge im Ranking ab (Faktor 0.1). Spec enthält exakte Signaturen/Schrittfolge/Testfälle (MemorySimilarity-Jaccard; Remember: Overlap→Supersede / disjunkt→kein-FP / Selbst-Supersede / GetRules-wirft→best-effort; Recall: neuerer bevorzugt) und Abgrenzung (kein LLM, temporale Gültigkeit/Löschen/Hard-Merge unangetastet — Hard-Merge ist C4). Kein Code geändert → Build 0 Warnungen, 1261 Tests unverändert grün. **Nächster Schritt: C3 UMSETZEN** (Spec existiert lokal → strikt danach implementieren; spec-erstellt ≠ erledigt → C3 NICHT überspringen). Danach C4 (Spec, nutzt `MemorySimilarity`).
- C3 · erledigt · 2026-07-03 · [Phase 3] Kontradiktions-Erkennung im episodischen Gedächtnis strikt nach Spec `docs/plans/0004` umgesetzt (LLM-frei). Neue reine `MemorySimilarity` (Token-Jaccard über `char.IsLetterOrDigit`-Tokenizer, lowercase/distinct; für C4 wiederverwendbar). `RememberTool` findet beim `remember` bestehende Nutzer-Memories mit Jaccard > `MEMORY_SUPERSEDE_JACCARD` (Default 0.6, per Ctor injiziert, >1.0 schaltet ab), setzt `RelatesTo.Supersedes` am neuen Fakt (→ `SUPERSEDES`-Kante beim Upsert) und meldet „Possibly supersedes: …" (max 3 + „(+N more)"); der Lookup ist best-effort (try/catch → ein Graph-Lesefehler verhindert nie das Speichern), der eigene idempotente Knoten wird per Id ausgeschlossen (kein Selbst-Supersede). `RecallTool` sammelt die Supersede-Ziele der geladenen Menge und multipliziert deren Ranking-Score mit 0.1 (abgelöste Fakten sinken unter ihren Ersatz, ohne zu verschwinden). DI: `RememberTool` per Factory registriert, Schwellwert aus `MEMORY_SUPERSEDE_JACCARD` (neue `ParseSupersedeThreshold`, Default 0.6 bei unset/negativ/unparsebar); `.env.example` mit deutschem Kommentar dokumentiert. **Akzeptanz erfüllt:** (a) Tests für die Ähnlichkeits-Heuristik → 9 `MemorySimilarityTests` (Jaccard identisch/disjunkt/partial/leer/case+punct, Tokenize split/dedupe/leer); (b) `recall` bevorzugt den neueren von zwei ähnlichen Fakten → `RecallToolTests.PrefersNewerOfTwoSimilarFacts…` + Remember setzt die Kante; (c) keine False-Positives bei disjunkten Fakten → `RememberToolTests.DisjointFact_DoesNotSupersede` + `RecallToolTests.DisjointMemoryNotSuperseded…`. Neue Konfiguration = Verhalten wie heute per Default (Schwellwert 0.6 gilt, aber ohne bestehende Überlappung keine Kante; Recall unverändert ohne Supersede-Kanten). Build 0 Warnungen, 1276 Tests grün (+15). Nächster Phase-3-Schritt: C4 (⚠️ → Spec; nutzt `MemorySimilarity`).
- C4 · spec-erstellt · 2026-07-03 · [Phase 3] Detail-Spec `docs/plans/0005-c4-token-dedup-konsolidierung.md` (lokal, da `docs/plans/` gitignored) — nur Spec, kein Code. **Audit-Befund:** die Issue-Zeilenreferenz `ConsolidateTool.cs:~82` ist durch C10 veraltet — die Dedup-Logik liegt seit C10 in `MemoryConsolidator.FindDuplicates` (`src/Agent/Tools/Memory/MemoryConsolidator.cs:89-101`, exakte Normalisierungs-Dedup); `ConsolidateTool` ist nur noch ein Delegations-Wrapper. Spec (Stufe 1, LLM-frei): `MemoryConsolidator` bekommt einen Token-Jaccard-Schwellwert (ctor-injiziert, aus `MEMORY_CONSOLIDATE_JACCARD`, **Default AUS** = `double.PositiveInfinity` → heutiges Verhalten exakt erhalten, da Löschung irreversibel; empfohlener Opt-in 0.7); `FindRedundant` wird zweistufig (Stage A exakt wie heute + deterministischer Id-Tie-Break, Stage B greedy Near-Dup-Merge über die Survivors, neuestes überlebt); `MemoryConsolidationResult` um `NearDuplicatesRemoved` + `MergedAwayBodies` erweitert; `ConsolidateTool` listet die Verlierer („Merged away: …"), HostedService-Audit zählt Near-Dups mit; nutzt `MemorySimilarity` (C3) unverändert. Enthält exakte Signaturen/Schrittfolge/Testfälle (Consolidator: Merge-neuestes / disjunkt-kein-FP / Threshold-aus / exakt+near getrennt gezählt / Tie-Break; Tool: Verlierer-Liste) und Abgrenzung (kein LLM/Embeddings = Stufe 2; keine Supersede-Kanten; Default-off; kein Cross-User-Body-Leak). **Ehrlichkeits-Hinweis in der Spec:** Token-Jaccard erkennt nur lexikalische Near-Dups — das Issue-Beispiel „nutze pnpm statt npm"/„npm durch pnpm ersetzen" (Jaccard 0.33) bleibt in Stufe 1 unerkannt (Stufe 2/Embeddings). Kein Code geändert → Build 0 Warnungen, 1276 Tests unverändert grün. **Nächster Schritt: C4 UMSETZEN** (Spec existiert lokal → strikt danach implementieren; spec-erstellt ≠ erledigt → C4 NICHT überspringen). Danach C5 (Spec).
- C4 · erledigt · 2026-07-03 · [Phase 3] Token-basierte Near-Duplicate-Konsolidierung (Stufe 1) strikt nach Spec `docs/plans/0005` umgesetzt (LLM-frei, nutzt `MemorySimilarity` aus C3). `MemoryConsolidator.FindDuplicates` → `FindRedundant` wird zweistufig: Stage A exakte Normalisierungs-Dedup wie bisher (plus deterministischer `Id`-Ordinal-Tie-Break), Stage B greedy Near-Dup-Merge über die Stage-A-Survivors (neuestes je Cluster überlebt, ältere ähnliche werden absorbiert — löst Jaccard-Nicht-Transitivität auf). Schwellwert per Ctor injiziert aus `MEMORY_CONSOLIDATE_JACCARD`; **Default AUS** (`double.PositiveInfinity` → Stage B no-op → heutiges Verhalten exakt erhalten, da Löschung irreversibel; empfohlener Opt-in 0.7). `MemoryConsolidationResult` um `NearDuplicatesRemoved` + `MergedAwayBodies` erweitert (abwärtskompatibel, 4. Positional-Param Default 0); `ConsolidateUserAsync` füllt beide, `ConsolidateAllAsync` aggregiert den Count aber sammelt KEINE Bodies (kein Cross-User-Leak ins Audit). `ConsolidateTool` meldet „removed N duplicate(s), M near-duplicate(s), forgot K faded" + listet die Verlierer („Merged away: …", max 3 + „(+N more)"); HostedService-Audit zählt Near-Dups mit. DI per Factory (`ParseConsolidateThreshold`, Default-off); `.env.example` dokumentiert die Variable. **Akzeptanz erfüllt:** (a) Tests mit (lexikalisch) paraphrasierten Duplikaten → `ConsolidateUserAsync_MergesNearDuplicates_KeepingNewest` + `ExactAndNear_CountedSeparately` + `DisjointMemories_NotMerged` (keine False-Positives); (b) deterministisch → `FindRedundant` rein (Created + Id-Ordinal), `NearDuplicateTie_KeepsDeterministically`; (c) MCP-default-deny → `ConsolidateTool` Exposure/Mutating-Status unangetastet. Bestehende 5 Consolidator-Tests + HostedService-Tests unverändert grün (Default-off = Status quo). **Ehrlichkeits-Hinweis (aus Spec):** Token-Jaccard fängt nur lexikalische Near-Dups; echte Paraphrasen mit geringer Überlappung (Issue-Beispiel pnpm, Jaccard 0.33) bleiben Stufe 2 (Embeddings, ausgeklammert). Build 0 Warnungen, 1282 Tests grün (+6). Nächster Phase-3-Schritt: C5 (⚠️ → Spec).
- C5 · spec-erstellt · 2026-07-03 · [Phase 3] Detail-Spec `docs/plans/0006-c5-inkrementeller-sync.md` (lokal, da `docs/plans/` gitignored) — nur Spec, kein Code. **Audit-Befund:** jeder `IngestionPipeline.IngestAsync`-Lauf ist ein Full-Ingest (sammelt alle Items via `source.FetchAsync`, upsertet jedes, re-embeddet im Hintergrund) — kein Sync-State. Spec (Stufe 1, quellen-agnostisch, LLM-frei): pro Quell-Instanz ein Content-Hash-Manifest (`itemId → SHA-256(Title+Body+Domain+Tags+Links+ChunkStyle)`) unter `data/sync-state/` via neuer `ISyncStateStore` (Core-Interface) + `FileSyncStateStore` (`IFileSystem`, JSON); die Pipeline lädt das Manifest, überspringt Items mit unverändertem Hash (zählt `Skipped`, kein Upsert/Re-Embed), schreibt es best-effort neu. Neuer `IngestionRequest.ForceFullSync`-Flag (Default false) erzwingt Full-Re-Ingest. Der `ISyncStateStore`-ctor-Param ist **optional (`null` = heutiges Full-Ingest-Verhalten)** → alle 12 Bestands-Pipeline-Tests unverändert grün; DI injiziert den Store in Produktion (Sync aktiv). Neue reine Helfer `IngestionContentHash` + `IngestionInstanceKey`. Enthält exakte Signaturen/Schrittfolge/Testfälle (Akzeptanz: zweiter unveränderter Lauf → `Imported==0`/`Skipped==N` mit Fake-`git`-Quelle; ChangedItem/ForceFullSync/ohne-Store/FailedItem-Retry; Store-Roundtrip; Hash-/Key-Determinismus) und Abgrenzung. **Ehrlichkeits-Hinweis in der Spec:** die im Issue zusätzlich genannte Git-Commit-Diff- (`git diff --name-only`) und HTTP-ETag-Scan-Optimierung ist **Stufe 1b** (eigene Spec; braucht `IGitClient`-Erweiterung + reale Infrastruktur, nicht unit-testbar) — der Content-Hash-Skip erfüllt die Akzeptanz (0 Items bei unveränderter Wiederholung) bereits quellen-agnostisch. Kein Code geändert → Build 0 Warnungen, 1282 Tests unverändert grün. **Nächster Schritt: C5 UMSETZEN** (Spec existiert lokal → strikt danach implementieren; spec-erstellt ≠ erledigt → C5 NICHT überspringen). Danach D3 (Spec).
- C5 · erledigt · 2026-07-03 · [Phase 3] Inkrementeller Sync (Stufe 1, quellen-agnostischer Content-Hash) strikt nach Spec `docs/plans/0006` umgesetzt. Neue `ISyncStateStore` (Core, Interface-First) + `IngestionSyncState` (Core-Model, `itemId → Hash`) + `FileSyncStateStore` (`IFileSystem`, JSON unter `data/sync-state/`, eine Datei je Instanz, Key SHA-256-gehasht; korrupt/fehlend → leerer State → Full-Ingest). Reine Helfer `IngestionContentHash` (SHA-256 über Title+Body+Domain+Tags+Links+ChunkStyle) + `IngestionInstanceKey` (SourceKind|CanonicalUrl/RepositoryUrl/http-Settings). `IngestionPipeline`: neuer optionaler ctor-Param `ISyncStateStore? syncState = null` (**null = heutiges Full-Ingest-Verhalten** → alle 12 Bestands-Pipeline-Tests unverändert grün); bei aktivem Store + `!ForceFullSync` lädt sie das Manifest, überspringt Items mit unverändertem Hash (`Skipped++`, kein Upsert/Re-Embed), zeichnet Hashes nur bei Erfolg auf (fehlgeschlagene Items werden nächsten Lauf erneut versucht) und schreibt das Manifest best-effort neu (Save-Fehler scheitert den Ingest nie). Neuer `IngestionRequest.ForceFullSync` (Default false) erzwingt Full-Re-Ingest. DI registriert `FileSyncStateStore` → MS-DI füllt den optionalen Pipeline-Param (Sync in Produktion aktiv). **Akzeptanz erfüllt:** (a) zweiter Lauf ohne Quell-Änderung ingestiert 0 Items → `IngestAsync_SecondRunWithoutChanges_ImportsZeroAndSkipsAll` (Fake-`git`-Quelle, `Imported==0`/`Skipped==2`, kein Re-Upsert); (b) Force-Full-Sync-Flag vorhanden → `ForceFullSync` + `IngestAsync_ForceFullSync_ReimportsEverything`. 20 neue Tests (6 ContentHash, 5 InstanceKey, 4 Store-Roundtrip/korrupt/getrennt, 5 Pipeline inkl. ChangedItem/ohne-Store/FailedItem-Retry). **Ehrlichkeits-Hinweis (aus Spec):** die Git-Commit-Diff-/HTTP-ETag-Scan-Optimierung bleibt Stufe 1b (eigene Spec, braucht reale Infrastruktur); der Content-Hash-Skip liefert die Akzeptanz quellen-agnostisch. Kein neues Env nötig (fixer Pfad `data/sync-state`), kein `TimeProvider` (rein content-hash-basiert). Build 0 Warnungen, 1302 Tests grün (+20). Nächster Phase-3-Schritt: D3 (⚠️ → Spec).
- D3 · spec-erstellt · 2026-07-03 · [Phase 3] Detail-Spec `docs/plans/0007-d3-hosting-integrationstests.md` (lokal, da `docs/plans/` gitignored) — nur Spec, kein Code. **Audit-Befunde:** `tests/Web.Tests` existiert (seit E7), testet aber nur den Markdown-Renderer, nicht die Endpoints → das im Issue genannte neue `tests/Hosting.Tests` bleibt korrekt. `Program.cs` liest Config **eager** (vor `Build()`: A4-Guard + `AddEddaCore`→`GRAPH_PROVIDER`/`EMBEDDING_PROVIDER`), deshalb greifen `WebApplicationFactory`-Config-Overrides (Build-Zeit) NICHT — die Test-Factory muss echte **Env-Variablen** setzen (Tests seriell via `DisableTestParallelization`). `LocalAuthenticationHandler` liest `EDDA_AUTH_TOKEN` per `Environment` (kein Bestandstest referenziert ihn) → der Env-Ansatz deckt die Token-Szenarien ab, **ohne** Auth-Code zu ändern. `MemoryGraphDatabaseProvider` (`GRAPH_PROVIDER=memory`) + Null-Embeddings = zero-infra. Spec: neues `tests/Hosting.Tests` (csproj mit `Microsoft.AspNetCore.Mvc.Testing` + Web-Ref; xUnit/FluentAssertions aus geteilten Test-Props) mit `HostingTestFactory : WebApplicationFactory<Program>` (setzt Env, stellt sie im Dispose wieder her); **einzige Produktionsänderung: `public partial class Program {}`** (macht `Program` referenzierbar). ~20 Szenarien in 6 Klassen (Health-anonym, Auth-Boundary mit/ohne/falschem Token + Loopback-ohne-Token, Rules-CRUD Propose→Get→Delete + AdminOnly-Reload, Fehlerformat RFC 7807 via D5-`PageBounds`/404, MCP-Gate 401/404/`?token=`-ignoriert, A4-Guard: insecure-remote-bind wirft beim Start). Enthält exakte csproj/Factory/AssemblyInfo, Schrittfolge, Szenarienliste (Route+Status+Auth) und Abgrenzung. **Ehrlichkeits-Hinweis:** „gültiger MCP-Token passiert Gate" bleibt außen vor (bräuchte `IMcpTokenStore`-Seeding auf echte Platte) — 401/404/`?token=` decken das Gate deterministisch ab. Akzeptanz-Konformität: Tests laufen ohne Docker/Neo4j in `dotnet test Edda.slnx`. Kein Code geändert → Build 0 Warnungen, 1302 Tests unverändert grün. **Nächster Schritt: D3 UMSETZEN** (Spec existiert lokal → strikt danach implementieren; spec-erstellt ≠ erledigt → D3 NICHT überspringen). Danach E2 (Spec).
