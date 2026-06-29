# ADR-0005: Config-getriebenes Connector-Modell für Wissensquellen

- **Status:** Vorgeschlagen
- **Datum:** 2026-06-17
- **Autor:** Eric Lenk
- **Konsultiert:** —

## Kontext und Problemstellung

Die bestehende Ingestion (Plan-0001) modelliert Quellen über `IIngestionSource` mit den
Implementierungen für `git` und `gitlab-group`. Diese Abstraktion ist auf Datei-/Git-Quellen
zugeschnitten, im Code fest verdrahtet, und Zugangsdaten kommen global aus Umgebungsvariablen
(`INGEST_GIT_TOKEN`). Künftig sollen mehrere Git-Repos mit **je eigenem Token** sowie später
**Jira, Awork und beliebige Custom-HTTP-Quellen** im UI konfiguriert werden. Der erklärte
Wunsch ist „ein Modul, das alle Retrievals abdeckt und per Config befüllt wird".

Heute würde jeder neue Quelltyp eine neue Implementierung **und** neuen UI-Code erfordern.
Das skaliert nicht für eine wachsende Zahl von Konnektoren.

**Kernfrage:** Wie modellieren wir Wissensquellen so, dass neue Quelltypen ohne UI- und
Pipeline-Umbau hinzukommen und jede Quell-Instanz eigene Zugangsdaten führt?

## Anforderungen

### Funktional

- Mehrere Quell-Instanzen pro Typ, jede mit eigenen Zugangsdaten.
- Ein neuer Quelltyp kommt ohne UI-Code aus — das UI rendert die Eingabefelder automatisch.
- Git zuerst lauffähig; Jira/Awork/Custom-HTTP später andockbar.
- Tokens je Instanz verschlüsselt und getrennt von der Quell-Definition.

### Nicht-Funktional

- Interface-First; nutzt die bestehende Pipeline (`IngestionItem` → `KnowledgeRule`) weiter.
- Erweiterbarkeit ohne Vervielfachung von Boilerplate und UI.
- Testbar ohne Infrastruktur (Mocks).

## Betrachtete Optionen

### Option 0: Config-getriebene Connectoren

Ein generisches `IKnowledgeConnector` (`TypeId`, `Describe() → ConnectorDescriptor`,
`FetchAsync(instanceConfig)`). Der `ConnectorDescriptor` beschreibt die nötigen Felder
**deklarativ** (Schlüssel, Label, Typ, Pflicht, Default, Hilfetext); ein `IConnectorRegistry`
sammelt alle Konnektoren per DI-Multi-Binding. Das UI rendert das Eingabeformular automatisch
aus dem Deskriptor; Tokens landen pro Instanz im `ICredentialStore`.

**Positiv:**
- Neue Quelle = Deskriptor + dünner Adapter, **kein** UI-Code.
- Ein einziges generisches Modul für alle Retrievals.
- Zugangsdaten je Instanz, getrennt von der Definition.
- Baut auf der vorhandenen Ingestion-Pipeline auf.

**Negativ:**
- Generisches Auto-UI bildet sehr komplexe Quellen evtl. schlecht ab.
- Das Feldmodell des Deskriptors muss genug Typen und Validierung tragen.
- Mehr Abstraktion als die heutige feste Verdrahtung.

### Option 1: Ein Interface/Implementierung pro Quelle

Je Quelltyp eine eigene Klasse plus eine eigene, handgebaute Razor-Seite.

**Positiv:**
- Explizit und leicht zu debuggen; maßgeschneidertes UI je Quelle.
- Keine generische Feld-Magie.

**Negativ:**
- Boilerplate vervielfacht sich; jeder neue Typ braucht UI-Code und ein Deployment.
- Widerspricht direkt dem Ziel „1 Modul, per Config befüllt".

### Option 2: Nur Git minimal erweitern

`IIngestionSource` beibehalten, Token je Repo aus dem `ICredentialStore` ziehen, UI nur für Git.

**Positiv:**
- Kleinster Schritt; schnell lieferbar; kein neues Framework.

**Negativ:**
- Jira/Awork erfordern später erneuten Umbau.
- Keine Generalisierung — verschiebt das eigentliche Designproblem nur.

## Vorschlag des Autors

Das Ziel „ein Modul für alle Retrievals, per Config befüllt" schließt Option 1 praktisch aus,
da dort jeder Konnektor UI-Code und Deployment nach sich zieht. Option 2 liefert kurzfristig
schnell, verschiebt aber die Generalisierung und führt bei der nächsten Quelle (Jira) zum
erneuten Umbau. Option 0 investiert einmalig in ein Deskriptor-getriebenes Modell und macht
danach jede weitere Quelle zu einem dünnen Adapter. Das zentrale Risiko — zu generische,
unbrauchbare Formulare — wird durch ein ausreichend reiches Feldtyp-/Validierungsmodell und
einen Custom-HTTP-Connector als frühen Realitätstest beherrscht.

## Entscheidung

**Gewählte Option:** "Config-getriebene Connectoren"

Ausschlaggebend war die geforderte Erweiterbarkeit ohne UI-Vervielfachung; das Risiko zu
generischer Formulare wird über Feldtypen, Validierung und einen Custom-HTTP-Connector als
Realitätstest bewusst gemanagt.

## Konsequenzen

### Positiv

- Erfüllt „1 Modul, per Config befüllt"; minimaler Aufwand je neuer Quelle.
- Zugangsdaten je Instanz, verschlüsselt im Credential-Store.
- Konsistentes, automatisch aus dem Deskriptor erzeugtes UI.
- Wiederverwendung der bestehenden Pipeline und Modelle.

### Negativ

- Höhere Abstraktion als heute; Einarbeitung in das Deskriptor-Modell nötig.
- Auto-UI stößt bei sehr eigenwilligen Quellen an Grenzen (dann Spezial-UI als Ausnahme).

### Folge-Entscheidungen

- Konkretes Feldtyp- und Validierungsschema im `ConnectorDescriptor`.
- Ob der Import (Plan-0002, #4) als „Upload-Connector" in dasselbe Modell passt.
- Scheduling/Inkrement-Läufe (vorerst manueller Lauf je Instanz).

### Review

**Reality-Check geplant für:** 2026-08-05 (ca. 7 Wochen nach Entscheidung)

## Weitere Informationen

### Scope

Gilt für die Wissensquellen-Ingestion im Edda-Standalone. Knüpft an die in Plan-0001 /
ADR-0001 angelegte Quellen-Abstraktion an (Generalisierung, **kein** Supersede — `IIngestionSource`
wird zu `IKnowledgeConnector` verallgemeinert, der Pipeline-Kern bleibt).

### Referenzen

- Plan-0002 — UI-Konfiguration (lokales Arbeitsdokument)
- [ADR-0001 — Optionaler LLM-Enricher für die AKG-Ingestion](./0001-optionaler-llm-enricher-ingestion.md)
- Bestehende Verträge: `IIngestionSource`, `IIngestionPipeline`, `ICredentialStore`
