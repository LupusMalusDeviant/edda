# ADR-0002: Git-Client-Technologie für die Ingestion

- **Status:** Akzeptiert
- **Datum:** 2026-06-15
- **Autor:** Eric Lenk

Ersetzt: —

## Kontext und Problemstellung

Die AKG-Ingestion-Pipeline (siehe [Plan-0001](../plans/0001-akg-ingestion-pipeline.md), WP2) liest Wissen zuerst aus Git-Repositories. Der `IGitClient`-Vertrag in `Core` steht bereits; er klont ein Remote-Repo per URL und optionalem Branch/Tag in ein lokales Verzeichnis, das die `GitMarkdownSource` anschließend über `IFileSystem` scannt. Die produktive Implementierung dieses Vertrags ist offen — ADR-0001 hat diese Wahl als Folge-Entscheidung angekündigt.

Edda ist als self-hostende, containerisierte Anwendung ausgelegt (`CLAUDE.md` Regel 12: Self-Hosting, keine externen Abhängigkeiten). Die Wahl der Klon-Technologie entscheidet, ob zur Laufzeit ein externes Werkzeug vorausgesetzt wird, wie portabel das Deployment bleibt und wie die Komponente getestet werden kann.

**Kernfrage:** Womit klont die Ingestion-Pipeline Remote-Repositories — und wie bleibt das self-contained und testbar?

## Anforderungen

### Funktional

- Klonen eines Remote-Repos per URL und optionalem Branch/Tag in ein lokales Verzeichnis.
- Lesbarkeit des Arbeitsbaums danach über `IFileSystem`.
- Authentifizierung für private Repos (Token).

### Nicht-Funktional

- **Self-Contained:** möglichst keine zusätzliche Laufzeitabhängigkeit im Container.
- **Generisch:** nicht an einen einzelnen Git-Hoster gebunden.
- **Secret-Hygiene:** Token ausschließlich aus der Umgebung, nie aus Request-Daten.
- **Testbarkeit:** die Kernlogik (Pipeline, Source, Mapper) muss ohne Infrastruktur testbar bleiben.

## Betrachtete Optionen

### Option 0: LibGit2Sharp (managed NuGet)

Managed .NET-Wrapper um `libgit2`. Klonen läuft in-process ohne externes Werkzeug.

**Positiv:**
- Keine externe `git`-Laufzeitabhängigkeit; das Image bleibt self-contained.
- Generisch über das Git-Protokoll (jeder Hoster), in den .NET-Build integriert.
- In-Process-API ohne Prozess-/Ausgabe-Parsing.

**Negativ:**
- Bringt plattformspezifische native Binaries mit; auf Alpine/musl-Images gelegentlich problematisch (ggf. Debian-basiertes Image nötig).
- Zusätzliche Dependency-Größe.
- Wrappt eine externe Bibliothek → die Implementierung ist nicht sinnvoll unit-testbar.

### Option 1: git-CLI über Prozessaufruf

Ruft ein installiertes `git` als Kindprozess auf.

**Positiv:**
- Sehr robust und plattformkompatibel; wenig eigener Code.
- `git` ist im Container trivial installierbar.

**Negativ:**
- Setzt `git` als Laufzeitabhängigkeit voraus — widerspricht dem Self-Contained-Ziel.
- Erfordert eine Prozess-Abstraktion (kein direkter `Process.Start` im Sinne der Test-/Sandbox-Konventionen) und Parsen von Prozessausgaben/Exit-Codes.

### Option 2: GitLab-HTTP-API (Repository-Archive/Files)

Kein Klon, sondern Abruf der Dateien über die HTTP-API des Hosters.

**Positiv:**
- Kein lokaler Klon, keine native Abhängigkeit.

**Negativ:**
- Hoster-spezifisch (GitLab) — verletzt die Generik-Anforderung; ein Wechsel/zweiter Hoster bräuchte neue Anbindung.
- Mehr Eigenentwicklung (Paginierung, Auth-Spezifika, Datei-Traversierung).

## Vorschlag des Autors

LibGit2Sharp erfüllt Self-Containment und Generik am besten: kein zusätzliches Laufzeitwerkzeug, ein einziger NuGet-Verweis, jeder Git-Hoster. Der Hauptnachteil — nicht unit-testbar — trifft nur die dünne Wrapper-Klasse; die gesamte Entscheidungslogik (Source, Mapper, Pipeline) ist bereits gegen einen `FakeGitClient` vollständig abgedeckt. Die native-Binary-Problematik ist durch die Wahl des Container-Basisimages beherrschbar.

## Entscheidung

**Gewählte Option:** „LibGit2Sharp (managed NuGet)"

Ausschlaggebend war die Vermeidung einer externen `git`-Laufzeitabhängigkeit bei gleichzeitiger Hoster-Unabhängigkeit. Der akzeptierte Preis sind native Binaries (Image-Wahl) und eine Wrapper-Klasse, die statt per Unit-Test über einen optionalen Integrationstest abgesichert wird.

## Konsequenzen

### Positiv

- Das Deployment bleibt self-contained; kein `git` im Image erforderlich.
- Hoster-unabhängig — GitLab heute, anderes Remote morgen, ohne Code-Änderung.
- Saubere In-Process-API ohne Prozess- und Ausgaben-Handling.

### Negativ

- Native Binaries müssen zur Ziel-Plattform passen (Alpine/musl prüfen, ggf. Debian-Image).
- Die `IGitClient`-Implementierung ist nicht unit-testbar und braucht einen separaten, optionalen Integrationstest.
- Etwas größere Abhängigkeit.

### Folge-Entscheidungen

- Container-Basisimage so wählen/prüfen, dass die LibGit2Sharp-Binaries laufen.
- Konvention für das Klon-Cache-Verzeichnis und dessen Aufräumen (offener Punkt aus Plan-0001).

### Review

**Reality-Check geplant für:** 2026-08-10 (zusammen mit ADR-0001).

## Weitere Informationen

### Scope

Gilt für die `IGitClient`-Implementierung in `src/AKG.Ingestion`. Der `IGitClient`-Vertrag in `Core` und die übrige Pipeline bleiben unberührt und technologie-neutral.

### Referenzen

- ADR: [0001-optionaler-llm-enricher-ingestion.md](./0001-optionaler-llm-enricher-ingestion.md) (übergeordnete Ingestion-Entscheidung)
- Plan: [../plans/0001-akg-ingestion-pipeline.md](../plans/0001-akg-ingestion-pipeline.md) (WP2)
