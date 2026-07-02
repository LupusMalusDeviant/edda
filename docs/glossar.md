# Glossar

Alle Fachbegriffe aus README und `CLAUDE.md` — alphabetisch, kompakt erklärt. Neu bei Edda? Der
**[Erste-Schritte-Guide](erste-schritte.md)** führt einmal durch den kompletten Ablauf.

---

### .env / .env.example
Die Konfigurationsdatei, aus der Edda alle Einstellungen liest (Bind/Port, Keys, MCP, Token).
`.env.example` ist die vollständige, kommentierte Vorlage — kopieren nach `.env` und anpassen.

### ADR (Architecture Decision Record)
Kurzes Dokument, das **eine** Architekturentscheidung samt Kontext und Konsequenzen festhält. Liegen
unter `docs/adr/`. Beispiel: ADR-0009 (Head-Vektoren), ADR-0010 (Ingest-Zeit-Extraktion).

### AKG (Agent Knowledge Graph)
Der **Wissensgraph** im Kern von Edda: kuratierte Wissens-**Regeln** in **Domänen**, verbunden über
**Relationen**. Beantwortet Anfragen über die 4-Phasen-**Kontext-Kompilierung**. Eines der beiden
Hauptmodule (das andere ist **TDK**).

### Allowlist (MCP) / Default-Deny
Die Liste der über MCP freigegebenen **Tools** (`MCP_EXPOSED_TOOLS`). „Default-Deny" heißt: Was nicht
ausdrücklich freigegeben ist, ist gesperrt. Voreinstellung sind nur die zwei Lese-Tools
`search_memory` und `list_memory`.

### Auth-Token / Bearer-Token (`EDDA_AUTH_TOKEN`)
Geheimnis für den Zugriff auf `/api/*` und `/mcp`. Wird als `Authorization: Bearer <Token>`-Header
gesendet. Ist es leer, sind diese Endpunkte nur über **Loopback** (127.0.0.1) erreichbar.

### Blazor Server
Das .NET-UI-Framework, mit dem Eddas Web-Oberfläche gebaut ist. Die Seiten (Knowledge, TDK,
Embeddings, Einstellungen, …) laufen serverseitig; der Browser hält nur eine Verbindung (Circuit).

### Chunk / Chunking
Regelrümpfe werden in kleinere Abschnitte („Chunks", intern `:RuleChunk`) zerlegt, bevor sie
**embedded** werden. Das verbessert die semantische Trefferqualität. Details:
[docs/chunking.md](chunking.md).

### Concepts (Frontmatter)
Frontmatter-Feld einer Regel: eine Liste von Kernbegriffen, die die Regel abdeckt. Hilft der
Keyword-Phase der Suche, die richtige Regel zu finden. Verwandt: **Tags**.

### Connect-Handshake / `instructions`
Beim Verbinden sendet der MCP-Server einen `instructions`-Text, der Edda als das persistente
Langzeitgedächtnis des Agenten beschreibt und den Client anweist, **zuerst `search_memory`**
aufzurufen.

### Constraint / Guideline / WorldKnowledge
Die drei **Regeltypen** (`type`): `Constraint` (harte, verbindliche Vorgabe), `Guideline`
(Empfehlung) und `WorldKnowledge` (allgemeines Hintergrundwissen, nicht projektspezifisch).

### Cytoscape
Die Graph-Visualisierungsbibliothek, mit der die Knowledge-Seite den Wissensgraphen zeichnet
(Knoten = Regeln/Domänen, Kanten = Relationen). Lokal eingebunden (keine CDN-Abhängigkeit).

### Decay (Konfidenz-Decay)
Zeitlicher Abbau der **Feedback-Konfidenz**: veraltetes Feedback zieht den Vertrauensmultiplikator
einer Regel über eine Halbwertszeit langsam Richtung neutral zurück, damit alte Bewertungen nicht
ewig nachwirken (`FEEDBACK_DECAY_HALFLIFE_DAYS`).

### DI (Dependency Injection)
Das .NET-Muster, mit dem Edda seine Dienste verdrahtet (zentral in `AddEddaCore` / `AddAkgServices`).
Der Projekt- und DI-Fluss ist in [docs/architektur.md](architektur.md) beschrieben.

### Docker / Docker Compose
Container-Laufzeit bzw. Orchestrierungswerkzeug. Der empfohlene Betrieb startet Edda + Neo4j per
`docker compose up`. Docker ist außerdem die Default-**Sandbox** für TDK-Validatoren.

### Domäne (Domain)
Thematische Gruppe, der eine Regel zugeordnet ist (z. B. `coding`, `security`, `tools`, `world`).
Domänen können Unterdomänen haben (`tools.web`, `tools.browser`, …).

### Embedding / Embedding-Provider / Embedding-Cache
Ein **Embedding** ist ein Zahlenvektor, der die Bedeutung eines Textabschnitts repräsentiert und
**semantische** Ähnlichkeitssuche ermöglicht. Der **Provider** (`EMBEDDING_PROVIDER`: `openai`,
`google`, `voyage`, `ollama`, `custom` oder `null`) erzeugt die Vektoren; der **Cache** speichert sie
auf den **Chunks** und baut sie bei Bedarf neu auf. Details: [docs/embeddings.md](embeddings.md).

### Enricher (`INGESTION_ENRICHER=llm`)
Optionale, standardmäßig **abgeschaltete** LLM-Anreicherung **zur Ingest-Zeit** (ADR-0010): ein
einmaliger Extraktions-Client, der importiertes Material aufbereitet. Kein Chat-/Agent-Loop — ohne
Aktivierung bleibt der Betrieb lokal-only.

### Entity-Extraktion (`INGESTION_ENTITY_EXTRACTION=true`)
Ebenfalls opt-in (ADR-0010): extrahiert typisierte Entitäten/Relationen aus Text in eine
LightRAG-artige Entity-Schicht. Getrennt vom **Enricher** schaltbar, Default aus.

### Feedback-Konfidenz / Konfidenz
Ein Vertrauenswert pro Regel, gespeist aus Nutzungs-Feedback (hilfreich / nicht hilfreich). Fließt in
die **Konfliktauflösung** und in die TDK-Auswertung ein; unterliegt dem **Decay**. Verwaltet vom
`RuleConfidenceStore` / Feedback-Loop.

### Frontmatter
Der YAML-Kopf einer Regel-Markdown-Datei (zwischen `---`-Zeilen) mit Feldern wie `id`, `title`,
`domain`, `type`, `priority`, `tags`, `concepts`, `requires`, `validatorScript`.

### Head-Vektor (Head-Centroid)
Ein pro Repository/Quelle vorab berechneter Durchschnittsvektor (Centroid) über dessen Chunks
(ADR-0009). Dient als hierarchische **Stage-1-Vorfilterung**: irrelevante Quellen werden früh
aussortiert, bevor die teure Chunk-Suche läuft (`RETRIEVAL_HEAD_THRESHOLD`).

### HTTP/SSE
Eine der beiden MCP-Transportarten: MCP über HTTP mit Server-Sent Events, für Netzwerk-Clients
(Claude Code, Cursor, Remote). Endpoint: `/mcp`. Gegenstück: **stdio**.

### IFileSystem
Die Abstraktion für Datei-Zugriffe (Regel 2: kein direkter `File.*`/`Directory.*`/`Path.*`-Zugriff).
Erlaubt Tests ohne echtes Dateisystem; die reale Implementierung ist `PhysicalFileSystem`.

### Konfliktauflösung
Die vierte Phase der **Kontext-Kompilierung**: widersprüchliche Regeln werden nach **Priorität** und
**Konfidenz** gewichtet, sodass der Agent einen konsistenten Kontext bekommt.

### Kontext-Kompilierung (4 Phasen)
Der Kern des Retrievals hinter `search_memory`: **Keyword-Treffer → semantische (Vektor-)Suche →
MMR → Konfliktauflösung**. Ergebnis ist der zusammengestellte Wissenskontext zu einer Anfrage.

### Langzeitgedächtnis
Die Rolle, die Edda für einen Coding-Agenten spielt: kuratiertes, projektübergreifendes Wissen, das
über Sitzungen hinweg bestehen bleibt und per MCP read-only abgefragt wird.

### list_memory
Lese-**Tool**: listet/durchstöbert gespeicherte Gedächtnis-Einträge, optional nach Domäne, Typ oder
Tag gefiltert. Teil der Default-Allowlist.

### Lokal-only (local-only)
Grundprinzip: Edda läuft vollständig lokal, ohne Cloud-Zwang und ohne großes LLM. Optionale
LLM-Features (Enricher, Entity-Extraktion) sind ausdrücklich opt-in.

### manage_memory / manage_userdata / manage_learnings
Die **Schreib-Tools** für nutzer-bezogene Stores (user-scoped). Über MCP **grundsätzlich blockiert** —
nur ein ausdrückliches `MCP_ALLOW_WRITE_TOOLS=true` hebt das für vertrauenswürdige Setups auf.

### MCP (Model Context Protocol)
Der offene Standard, über den Agenten/LLMs externe Werkzeuge und Kontext anbinden. Edda exponiert
seinen Wissensgraphen spec-konform als **MCP-Server** (über **HTTP/SSE** und **stdio**).

### Memgraph
Eine zu Neo4j (Bolt-Protokoll) kompatible Graphdatenbank, die alternativ zu **Neo4j** als
Speicher-Backend genutzt werden kann (`GRAPH_PROVIDER`).

### MMR (Maximal Marginal Relevance)
Dritte Phase der **Kontext-Kompilierung**: wählt aus den Treffern eine Menge, die zugleich relevant
**und** untereinander vielfältig ist — verhindert, dass fünf fast identische Regeln den Kontext füllen
(`RETRIEVAL_MMR_TOP_N`, `RETRIEVAL_MMR_LAMBDA`).

### ModelContextProtocol
Das .NET-SDK (Version 1.4), mit dem Edda den MCP-Server und -Client implementiert. Nicht zu verwechseln
mit **MCP** (dem Protokoll selbst).

### Neo4j
Die Standard-Graphdatenbank hinter dem AKG (Version 5, Bolt-Protokoll). Speichert Regeln, Domänen,
Relationen, Chunks und Embeddings. Alternative: **Memgraph**.

### null (Provider/Sandbox)
Der „Aus"-Wert für optionale Bausteine: `EMBEDDING_PROVIDER=null` (keine Vektoren, nur Keyword-Suche)
bzw. `TDK_SANDBOX_TYPE=null` (Validatoren melden „nicht konfiguriert", `tdk_validate` läuft ohne
Verstöße). Hält den Betrieb lokal-only und leichtgewichtig.

### Priorität (priority)
Frontmatter-Feld einer Regel: `Critical`, `High`, `Medium` oder `Low`. Steuert das Gewicht bei der
**Konfliktauflösung**.

### read-only (Lesezugriff)
Angebundene Agenten erhalten über MCP ausschließlich Leserechte. Zusammen mit **Default-Deny** macht
das Edda gefahrlos auch für fremde Agenten exponierbar.

### Regel (KnowledgeRule)
Die Wissenseinheit im AKG: eine Markdown-Datei mit **Frontmatter** und Rumpf. Trägt `id`, `title`,
`domain`, `type`, `priority` u. a. und optional ein **Validator-Skript** für **TDK**.

### Relation (Relationen)
Eine Kante zwischen Regeln/Domänen im Graphen — z. B. `requires` (benötigt), `supersedes` (ersetzt)
oder die generische `RELATES_TO`-Beziehung der Entity-Schicht.

### Retrieval / Retrieval-Benchmark
**Retrieval** = das Abrufen der passenden Regeln zu einer Anfrage (die 4-Phasen-Kompilierung). Der
**Benchmark** (`AkgBenchmarkRunner`) misst dessen Qualität (Precision@k, Recall@k, MRR, nDCG@k) plus
Latenz für einen Testdatensatz.

### Sandbox (ISandboxFactory)
Die isolierte Ausführungsumgebung für **TDK**-Validator-Skripte. `TDK_SANDBOX_TYPE` wählt sie:
`docker` (Default, Netz `--network=none`), `wasm` (lokaler Python-Subprozess) oder `null` (keine
Ausführung). Details: [docs/tdk.md](tdk.md).

### search_memory
Das zentrale Lese-**Tool**: durchsucht das Langzeitgedächtnis zu einer Anfrage (über die 4-Phasen-
**Kontext-Kompilierung**) und liefert die passendsten Regeln. Soll vor dem Scan des Dateisystems
aufgerufen werden. Teil der Default-Allowlist.

### Secret / SecretRedactor
Ein **Secret** ist ein Geheimnis (Passwort, API-Key, Token). Regel 4: keine Secrets im Code. Der
`SecretRedactor` (in `Edda.Security`) schwärzt erkannte Geheimnisse in Log-Ausgaben; die
Beispielregel `no-plaintext-secrets` prüft Code aktiv darauf.

### semantische Suche (Vektorsuche)
Zweite Phase der **Kontext-Kompilierung**: findet Regeln über die Ähnlichkeit ihrer **Embeddings**
zur Anfrage — auch ohne wörtliche Übereinstimmung. Braucht einen Embedding-Provider ≠ `null`.

### stdio
Die zweite MCP-Transportart: MCP über Standard-Ein-/Ausgabe für lokale Clients (z. B. Claude
Desktop), gehostet von `src/Edda.Mcp.Stdio`. Gegenstück: **HTTP/SSE**.

### Tags (Frontmatter)
Frontmatter-Feld: freie Schlagworte zur Regel (z. B. `[csharp, async, deadlock]`). Für Filterung
(`list_memory`) und Keyword-Suche genutzt. Verwandt: **Concepts**.

### TDK (Test-Driven Knowledge)
Das zweite Hauptmodul: Regeln mit **Validator-Skripten** (Python), die generierten Code **aktiv
ablehnen** statt Konventionen nur zu beschreiben. Ausführung isoliert in der **Sandbox**; Ausgabe nach
**Schweregrad** (`error`/`warning`/`info`). Tool: `tdk_validate`, Seite `/tdk`.

### tdk_validate
Das **Tool**, das Code gegen die **TDK**-Validatoren prüft. **Nicht** in der Default-Allowlist — muss
über `MCP_EXPOSED_TOOLS` (oder im UI) freigeschaltet werden.

### tenantId / userId
Der Nutzer- bzw. Mandanten-Bezug einer Anfrage. Regel 6: `userId`/`tenantId` kommen **immer** aus dem
`ToolExecutionContext`, nie aus Tool-Argumenten — so kann kein Aufrufer fremde Daten adressieren.

### TimeProvider
Die .NET-Abstraktion für die aktuelle Zeit (Regel 3: nie `DateTime.UtcNow` direkt). Ermöglicht
deterministische Zeit in Tests (z. B. `FakeTimeProvider`).

### Tool
Eine über MCP exponierbare Funktion. Lese-Tools: `search_memory`, `list_memory`, `analyze_coverage`.
Schreib-/Prüf-Tools: `tdk_validate`, `manage_memory`, `manage_userdata`, `manage_learnings`. Ein Tool
wirft nie eine Exception, sondern liefert ein `ToolResult` (Regel 5).

### ToolExecutionContext
Der Ausführungskontext, den jedes **Tool** erhält — enthält u. a. `userId`/`tenantId` für das
User-Scoping (siehe **tenantId / userId**).

### Validator-Skript (validatorScript)
Ein Python-Skript im **Frontmatter** einer Regel, das für **TDK** einen Code-Block prüft: liest
`TdkValidatorInput` (Code, Sprache, RuleId) als JSON von stdin, schreibt `TdkValidatorOutput`
(`pass`, `violations[]`) nach stdout. Beispiel: `knowledge/security/no-plaintext-secrets.md`.

### WorldKnowledge
Ein **Regeltyp** (und die zugehörigen `:WorldKnowledge`-Knoten): allgemeines, nicht projektspezifisches
Hintergrundwissen (OOP-Prinzipien, API-Design, …), das beim Start aus `knowledge/world/` geseedet wird.

### Wissensgraph (Knowledge Graph)
Der Graph aus Regeln (Knoten), Domänen und Relationen (Kanten), den der **AKG** verwaltet und die
Knowledge-Seite mit **Cytoscape** darstellt.
