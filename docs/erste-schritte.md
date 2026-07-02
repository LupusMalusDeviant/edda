# Erste Schritte — in 15 Minuten von der Installation zur ersten validierten Regel

Dieses Tutorial führt dich einmal durch den kompletten Kreislauf: **erste Regel anlegen → Agent
anbinden → `search_memory` in Aktion sehen → Code mit TDK validieren**. Es nutzt die
**mitgelieferten Beispielregeln** unter `knowledge/`, die beim ersten Start automatisch in den Graphen
geladen werden — du brauchst also keinerlei Vorbereitung außer einer laufenden Edda-Instanz.

Unbekannte Begriffe (AKG, TDK, MMR, Head-Vektor, Decay, …) sind im **[Glossar](glossar.md)** erklärt.

> **Voraussetzung:** Edda läuft (siehe [README](../README.md) → *Installation*). Im Zweifel für dieses
> Tutorial ohne Keys starten: Embeddings sind optional, die Keyword-Suche funktioniert auch ohne.
> Für den Entwicklungsmodus genügt:
> ```bash
> docker compose up -d neo4j
> dotnet run --project src/Web      # → http://127.0.0.1:8080
> ```

---

## Minute 0–2 · Edda öffnen und den Graphen sehen

Öffne die angezeigte Adresse (Default **http://127.0.0.1:8080**). Die Startseite ist der
**Wissensgraph** (`/`, Seite *Knowledge*). Er ist bereits mit den mitgelieferten Beispielen gefüllt —
u. a.:

- `coding/async-await-patterns.md` — Async/Await-Best-Practices (.NET)
- `security/no-plaintext-secrets.md` — Verbot von Klartext-Secrets (mit **Validator-Skript**, siehe TDK unten)
- mehrere `world/`-Regeln (OOP, API-Design, Security-Prinzipien) als allgemeines Hintergrundwissen

Jeder Knoten ist eine **Regel** oder eine **Domäne**; Kanten sind **Relationen** (z. B. „benötigt",
„ersetzt"). Klicke einen Knoten an, um Titel, Domäne, Typ, Priorität und Rumpf zu sehen.

> Ist der Graph leer? Klicke oben die Schaltfläche **„Baseline neu laden"** (lädt `knowledge/` erneut)
> oder prüfe die Logs mit `docker compose logs -f edda`.

---

## Minute 2–5 · Das Gedächtnis durchsuchen (`search_memory`)

`search_memory` ist das wichtigste Lese-Tool: Es beantwortet eine Anfrage mit den passendsten Regeln
aus dem Graphen. Intern läuft die **4-Phasen-Kontext-Kompilierung** ab — Keyword-Treffer, dann
**semantische** (Vektor-)Suche, dann **MMR** (Dubletten raus, Vielfalt rein), dann
**Konfliktauflösung** (widersprüchliche Regeln werden nach Priorität/Konfidenz aufgelöst).

Probiere es im UI über die **Suchleiste** der Knowledge-Seite aus:

- Suche `async` → es sollte `coding-async-await-patterns` erscheinen.
- Suche `secret` oder `passwort` → `security-no-plaintext-secrets`.

Genau dieses Ergebnis bekommt später auch ein angebundener Agent zurück — nur eben als MCP-Antwort
statt im Browser.

> **Ohne Embedding-Provider** (`EMBEDDING_PROVIDER=null`) entfällt die semantische Phase; die
> Keyword-Phase allein liefert für dieses Tutorial trotzdem brauchbare Treffer. Für echte
> Ähnlichkeitssuche siehe [docs/embeddings.md](embeddings.md).

---

## Minute 5–9 · Deine erste eigene Regel anlegen

Eine Regel ist eine Markdown-Datei mit **Frontmatter** (dem YAML-Kopf) und einem Rumpf. Es gibt zwei
gleichwertige Wege:

### Weg A — im UI (schnell)

Auf der Knowledge-Seite **„Neue Regel"** wählen und ausfüllen:

- **Titel** — z. B. „Immer strukturiertes Logging"
- **Domäne** — z. B. `coding`
- **Typ** — `Guideline` (Empfehlung), `Constraint` (harte Vorgabe) oder `WorldKnowledge` (Hintergrundwissen)
- **Priorität** — `Critical` / `High` / `Medium` / `Low` (steuert die Konfliktauflösung)
- **Rumpf** — der eigentliche Inhalt in Markdown

Speichern — die Regel taucht sofort im Graphen auf und ist ab dann durchsuchbar.

### Weg B — als Datei (versionierbar)

Lege eine Datei `knowledge/coding/structured-logging.md` an:

```markdown
---
id: coding-structured-logging
title: Immer strukturiertes Logging
domain: coding
type: Guideline
priority: Medium
tags: [logging, observability]
concepts: [logging, structured, serilog]
author: du
---

## Immer strukturiertes Logging

Nachrichten mit benannten Platzhaltern statt String-Interpolation loggen, damit Felder
maschinell auswertbar bleiben:

    // Gut
    logger.LogInformation("Regel {RuleId} geladen (Domäne {Domain})", id, domain);

    // Schlecht — kein strukturiertes Feld
    logger.LogInformation($"Regel {id} geladen");
```

Danach auf der Knowledge-Seite **„Baseline neu laden"** klicken (oder `POST /api/akg/reload`). Das
`id`-Feld ist der stabile Schlüssel — gleiche `id` = dieselbe Regel (idempotentes Update).

> Suche danach `logging` → deine neue Regel erscheint. Damit hast du den Schreib-Kreislauf einmal
> geschlossen.

---

## Minute 9–12 · Einen Agenten anbinden (MCP, read-only)

Angebundene Agenten erhalten **ausschließlich Leserechte** (Default-Deny-Allowlist: `search_memory`
und `list_memory`). Schreib-Tools sind über MCP grundsätzlich blockiert — Edda lässt sich damit
gefahrlos auch fremden Agenten anbieten.

**HTTP/SSE** (Claude Code, Cursor, Remote-Clients) — `.mcp.json` beim Client hinterlegen:

```jsonc
{
  "mcpServers": {
    "edda": {
      "url": "http://127.0.0.1:8080/mcp",
      "headers": { "Authorization": "Bearer <EDDA_AUTH_TOKEN>" }
    }
  }
}
```

> Ohne gesetzten `EDDA_AUTH_TOKEN` ist `/mcp` nur über Loopback (127.0.0.1) erreichbar — dann entfällt
> der `Authorization`-Header.

**stdio** (lokale Clients wie Claude Desktop):

```jsonc
{
  "mcpServers": {
    "edda": { "command": "dotnet", "args": ["run", "--project", "src/Edda.Mcp.Stdio"] }
  }
}
```

Beim Verbinden sendet Edda einen **Connect-Handshake** (`instructions`-Text), der den Client anweist,
zuerst `search_memory` aufzurufen. Frag deinen Agenten testweise etwas wie „Welche Async-Konventionen
gelten hier?" — er ruft `search_memory` auf und bekommt `coding-async-await-patterns` zurück, genau wie
du eben im Browser.

> Exposition, exponierte Tools und (für vertrauenswürdige Setups) Schreibzugriff lassen sich unter
> **Einstellungen → MCP-Server** live umschalten. Details: [docs/mcp.md](mcp.md).

---

## Minute 12–15 · Code aktiv prüfen (TDK)

**TDK (Test-Driven Knowledge)** ist Eddas Alleinstellungsmerkmal: Eine Regel kann ein
**Validator-Skript** (Python) tragen, das generierten Code **aktiv ablehnt**, statt Konventionen nur zu
beschreiben. Die mitgelieferte Regel `security/no-plaintext-secrets.md` enthält ein solches Skript, das
hartkodierte Passwörter/API-Keys/Tokens erkennt.

Öffne die Seite **`/tdk`**, füge einen Code-Block mit einem eingebauten Secret ein, z. B.:

```python
password = "hunter2supersecret"
api_key  = "sk-1234567890abcdef"
```

… und starte die Prüfung. Der Ablauf dahinter: Kontext kompilieren → nur Regeln **mit**
Validator-Skript behalten → jedes Skript **isoliert in der Sandbox** ausführen → Verstöße nach
**Schweregrad** (`error` → `warning` → `info`) melden.

> **Sandbox nötig.** Validatoren laufen isoliert; `TDK_SANDBOX_TYPE` wählt die Isolation: `docker`
> (Default, braucht einen erreichbaren Docker-Daemon), `wasm` (Python 3.12 auf dem Host) oder `null`
> (führt `tdk_validate` aus, meldet aber **keine** Verstöße — für reine Retrieval-Nutzung). Ist keine
> echte Sandbox konfiguriert, zeigt `/tdk` das lesbar an. Vollständige Einrichtung und das
> Skript-Format: **[docs/tdk.md](tdk.md)**.

Über MCP ist `tdk_validate` **nicht** in der Default-Allowlist — es lässt sich per
`MCP_EXPOSED_TOOLS` (oder im UI) freischalten, wenn ein Agent aktiv gegen die Wissensbasis prüfen soll.

---

## Geschafft — wie geht es weiter?

Du hast eine Regel angelegt, einen Agenten read-only angebunden, `search_memory` in Aktion gesehen und
den TDK-Prüfpfad kennengelernt. Zum Vertiefen:

- **[Glossar](glossar.md)** — alle Fachbegriffe kompakt erklärt.
- **[docs/mcp.md](mcp.md)** — MCP über HTTP und stdio, Allowlist, Auth im Detail.
- **[docs/embeddings.md](embeddings.md)** — Embedding-Provider einrichten und Vektoren neu bauen.
- **[docs/tdk.md](tdk.md)** — Validatoren schreiben und die Sandbox konfigurieren.
- **[docs/architektur.md](architektur.md)** — Projektgraph und DI-Fluss.
- **[docs/betrieb.md](betrieb.md)** — Compose, Env-Variablen, Ports, Remote-Betrieb.
