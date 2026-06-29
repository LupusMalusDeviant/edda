# ADR-0004: Live-Apply der Laufzeit-Konfiguration über Resolving-Fassaden

- **Status:** Vorgeschlagen
- **Datum:** 2026-06-17
- **Autor:** Eric Lenk
- **Konsultiert:** —

## Kontext und Problemstellung

Edda bezieht heute jede Konfiguration **einmalig beim Start** aus Umgebungsvariablen
(`EMBEDDING_PROVIDER`, `INGESTION_LLM_*`, `INGEST_GIT_*` …). Die Provider sind als
DI-Singletons registriert; eine Änderung wirkt erst nach Prozess-Neustart. Mit der geplanten
Konfigurations-UI (Plan-0002) soll der Betreiber Provider, API-Keys und Wissensquellen direkt
im Web-UI setzen. Das verschlüsselte `ICredentialStore` existiert bereits, wird aber bislang
nirgends beschrieben oder gelesen.

Die Frage ist nicht *ob* Konfiguration persistiert wird, sondern *wann* sie wirksam wird —
und das entscheidet maßgeblich über die Architektur der Provider-Auflösung.

**Kernfrage:** Wie sollen im UI gesetzte Konfigurations- und Secret-Änderungen wirksam
werden — sofort im laufenden Prozess oder erst nach einem Neustart?

## Anforderungen

### Funktional

- Provider, Modell, API-Key und Quellen sind im UI änderbar; die Änderung greift ohne Neustart.
- Ein neu hinterlegter API-Key ist sofort testbar (Verbindungs-/Mini-Call).
- Secrets werden verschlüsselt persistiert und nie im Klartext an das UI zurückgegeben.

### Nicht-Funktional

- Thread-sicher bei gleichzeitigen Requests (Blazor-Server, mehrere Circuits).
- Interface-First (Core-Vertrag), kein direkter File-I/O (nur `IFileSystem`), keine Zeitabfrage außer `TimeProvider`.
- Kein Provider- oder `HttpClient`-Leak; vertretbarer Wartungsaufwand.
- Rückwärtskompatibel: bestehende Umgebungsvariablen bleiben gültiger Fallback.

## Betrachtete Optionen

### Option 0: Resolving-Fassade

`IEmbeddingService`, `ILlmChatClient` u. a. werden als dünne Fassaden registriert, die pro
Aufruf den konkreten Provider aus einem neuen `ISettingsService` plus `ICredentialStore`
auflösen und bis zur nächsten Änderung cachen (Cache-Schlüssel = Provider + Modell + URL +
Dimension + Key-Hash), invalidiert über ein `Changed`-Event.

**Positiv:**
- Änderungen greifen sofort; neuer Key direkt testbar.
- Zentrale, an einer Stelle getestete Auflösungslogik.
- Umgebungsvariablen bleiben als Fallback erhalten.

**Negativ:**
- Mehr Engineering als ein simpler Datei-Load.
- Cache-Invalidierung und Thread-Sicherheit sind eine echte Fehlerquelle.
- Zusätzliche Indirektionsschicht.

### Option 1: Neustart zum Anwenden

Settings werden in eine Datei persistiert und nur beim Start geladen — das heutige Muster,
lediglich Datei statt Umgebungsvariable.

**Positiv:**
- Minimaler Code; kein Cache- und kein Threading-Risiko.
- Deterministischer, eingefrorener Startzustand.

**Negativ:**
- Jede Änderung erfordert einen Neustart des Web-Hosts.
- Ein neuer Key ist nicht live testbar.
- Schlechte UX für eine Konfigurations-UI.

### Option 2: IOptionsMonitor / reloadOnChange

Auf die ASP.NET-Options-Infrastruktur mit Datei-Watcher (`reloadOnChange`) setzen.

**Positiv:**
- Eingebauter Change-Push; weniger Eigencode für die Benachrichtigung.

**Negativ:**
- Secrets gehören nicht in `IConfiguration`/Options (Klartext, Logging-Risiko).
- Der Datei-Watcher umgeht die `IFileSystem`-Abstraktion (verletzt die Projektregel).
- Options-Reload deckt den Neuaufbau konkreter Provider und den Credential-Store nicht ab.
- Mischt zwei Persistenzwege (Options-Datei + Credential-Store).

## Vorschlag des Autors

Eine Konfigurations-UI, bei der jede Änderung einen Neustart verlangt, entwertet den Zweck
der UI — gerade das Testen eines frisch eingetragenen Keys ist zentral. Option 2 scheitert
daran, dass Secrets bewusst aus `IConfiguration` herausgehalten werden sollen und der
Datei-Watcher die `IFileSystem`-Regel bricht. Bleibt Option 0: die Resolving-Fassade kapselt
die Provider-Auflösung an einer Stelle, trennt Secrets (Credential-Store) sauber von
Nicht-Secrets (`settings.json`) und liefert die geforderte Live-UX. Der Mehraufwand für Cache
und Thread-Sicherheit ist überschaubar und gut testbar.

## Entscheidung

**Gewählte Option:** "Resolving-Fassade"

Ausschlaggebend waren die Live-Wirksamkeit und die saubere Secret-Trennung; der zusätzliche
Engineering- und Test-Aufwand für Cache-Invalidierung und Thread-Sicherheit wird bewusst in
Kauf genommen.

## Konsequenzen

### Positiv

- Provider-/Key-/Quellen-Änderungen wirken ohne Neustart; Keys sind sofort testbar.
- Eine zentrale, testbare Provider-Auflösung statt verstreuter Start-Verdrahtung.
- Secrets bleiben verschlüsselt im Credential-Store, getrennt von `settings.json`.
- Bestehende Umgebungsvariablen funktionieren als Fallback weiter.

### Negativ

- Resolving-Fassaden und Cache erfordern sorgfältige Tests (Settings-Change-Pfad).
- Zusätzliche Indirektion gegenüber direkter Singleton-Injektion.
- Verantwortung für Thread-Sicherheit liegt im Eigencode.

### Folge-Entscheidungen

- Granularität: Welche Settings dürfen live wechseln, welche erfordern eine bewusste Aktion
  (z. B. Index-Embedding-Wechsel → Re-Embed statt stillem Live-Switch)?
- Konkrete Cache-Invalidierungsstrategie und Schlüsselbildung.
- Ob die Neo4j-Verbindung ebenfalls live umkonfigurierbar wird (vorerst nein, bleibt Start-fixiert).

### Review

**Reality-Check geplant für:** 2026-08-05 (ca. 7 Wochen nach Entscheidung)

## Weitere Informationen

### Scope

Gilt für die Auflösung von Embedding-, LLM-Enrichment- und Quellen-Providern im
Edda-Standalone. Ausgenommen ist die Graph-DB-Verbindung (Neo4j/Memgraph), die vorerst beim
Start fixiert bleibt.

### Referenzen

- Plan-0002 — UI-Konfiguration (lokales Arbeitsdokument)
- [ADR-0003 — Interface-First für alle injizierten Services](./0003-interface-first-fuer-injizierte-services.md)
- Bestehende Verträge: `ICredentialStore`, `IFileSystem`; neuer Vertrag: `ISettingsService`
