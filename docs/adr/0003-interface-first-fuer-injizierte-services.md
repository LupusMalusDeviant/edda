# ADR-0003: Interface-First für alle injizierten Services

- **Status:** Akzeptiert
- **Datum:** 2026-06-17
- **Autor:** Eric Lenk

Ersetzt: —

## Kontext und Problemstellung

`CLAUDE.md` Regel 1 fordert „Interface First" für von außen genutzte Klassen. In der Praxis
waren jedoch mehrere injizierte, verhaltenstragende Services nur als konkrete Klassen
registriert und injiziert — teils projektübergreifend, teils projekt-intern. Das erschwert
das Mocken in Tests, koppelt Konsumenten an Implementierungen und ist über die Projekte
hinweg inkonsistent.

Betroffen waren drei Gruppen:

- **Security:** `InputSanitizer`, `SecretRedactor`, der Merkle-/HMAC-Audit-Verifier.
- **AKG.Mcp:** `McpToolRegistry`, `McpServer`, `McpProtocolHandlers`, `McpToolImporter`.
- **AKG:** `RuleLoader`, `WorldKnowledgeSeeder`, `GraphValidator`, `ContextCompiler`,
  `Neo4jEmbeddingCache`, `RuleFeedbackStore`.

**Kernfrage:** Werden konsequent **alle** injizierten Services hinter Interfaces gelegt — auch
projekt-interne Einzel-Implementierungen — oder nur die projektübergreifend genutzten?

## Anforderungen

### Funktional

- Jeder per DI injizierte, verhaltenstragende Service wird über ein Interface aufgelöst.
- Keine Verhaltensänderung; reine Struktur-/Verdrahtungsänderung.

### Nicht-Funktional

- **Core bleibt frei von projektspezifischen Abhängigkeiten** (NetArchTest: Core hängt von
  keinem anderen `src`-Projekt ab; Core-Interfaces liegen in `Core.Abstractions`).
- **Testbarkeit:** bestehende Unit-Tests bleiben ohne Infrastruktur grün.
- **Konsistenz:** dieselbe Konvention in allen Projekten.

## Betrachtete Optionen

### Option 0: Alle injizierten Services hinter Interfaces (volle Konsequenz)

Jeder injizierte Service erhält ein Interface. Das Interface liegt dort, wo seine
Signaturtypen es erlauben — **projekt-lokal** (`internal interface`), wenn die Vertragstypen
projekt-intern sind (z. B. `SanitizationResult` in `Security.Models`, MCP-Modelle,
AKG-interne Typen); in `Core.Abstractions` nur, wenn der Vertrag projektübergreifend/von
außen genutzt wird.

**Positiv:** maximale Konsistenz; durchgängig mockbare Verträge; DI komplett interface-basiert.
**Negativ:** interne Einzel-Implementierungs-Interfaces sind teils Zeremonie; interne Member
müssen `public` werden (auf `internal`-Klassen — kein Sichtbarkeitsleck nach außen).

### Option 1: Nur projektübergreifend genutzte Services (enge Lesart von Regel 1)

Nur Security + AKG.Mcp hinter Interfaces; die rein AKG-internen Helfer konkret lassen, da
Regel 1 wörtlich „von außen genutzte" Klassen meint.

**Positiv:** kein Zeremonie-Overhead für interne Helfer.
**Negativ:** uneinheitlich; die DI-Verdrahtung bliebe teils konkret, teils abstrakt.

### Option 2: Status quo

Keine Änderung.

**Negativ:** bestehende Inkonsistenz und erschwertes Mocken bleiben.

## Vorschlag des Autors

Option 0 — auf ausdrücklichen Wunsch maximale Konsistenz über alle Projekte. Der
Zeremonie-Overhead interner Interfaces wird bewusst in Kauf genommen; im Gegenzug ist die
gesamte DI-Schicht einheitlich interface-basiert und vollständig mockbar.

## Entscheidung

**Gewählte Option:** „Alle injizierten Services hinter Interfaces (volle Konsequenz)".

Umgesetzt wurden 13 Interfaces:

- **Security (3):** `IInputSanitizer`, `ISecretRedactor`, `IMerkleAuditVerifier` (lokal, da
  `SanitizationResult` ein `Security.Models`-Typ ist — ein Core-Interface würde Core an
  Security koppeln).
- **AKG.Mcp (4):** `IMcpToolRegistry`, `IMcpServer`, `IMcpProtocolHandlers`,
  `IMcpToolImporter` (lokal, da die Verträge MCP-Modelle nutzen).
- **AKG (6):** `IRuleLoader`, `IWorldKnowledgeSeeder`, `IGraphValidator`, `IContextCompiler`,
  `INeo4jEmbeddingCache`, `IRuleFeedbackStore` (projekt-intern, `internal interface`).

`ToolRegistry` war bereits konform: eine Instanz, registriert hinter `IToolExecutor` **und**
`IToolRegistry` — kein Konsument injiziert den konkreten Typ. Keine Änderung nötig.

**Designregel:** Ein Interface lebt dort, wo seine Signaturtypen es erlauben. Projekt-lokal,
solange die Vertragstypen projekt-intern sind; in `Core.Abstractions` nur bei
projektübergreifenden Verträgen. So bleibt Core frei von projektspezifischen Abhängigkeiten.

## Konsequenzen

### Positiv

- Einheitliche, durchgängig interface-basierte DI-Schicht über alle Projekte.
- Jeder Service ist mockbar; Konsumenten hängen nur noch an Verträgen.
- `Core.Abstractions` bleibt schlank und projektübergreifend — NetArchTest bleibt grün.

### Negativ

- Interne Einzel-Implementierungs-Interfaces (v. a. die AKG-6) sind überwiegend Zeremonie.
- Minimaler Indirektions-Overhead.
- Interne Member wurden `public` (auf `internal`-Klassen, ohne externe Sichtbarkeit).

### Verifikation

- Build: 0 Warnungen, 0 Fehler (bei `TreatWarningsAsErrors`).
- Tests: 622 grün, 0 übersprungen. `AKG.Tests` unverändert bei 213 — Tests übergeben
  konkrete Instanzen an die neuen Interface-Parameter, daher war **keine** Teständerung nötig.

## Weitere Informationen

### Scope

Betrifft DI-Registrierungen und Konstruktor-Signaturen in `Security`, `AKG.Mcp` und `AKG`.
Reine Struktur-/Verdrahtungsänderung ohne Verhaltensänderung.

### Referenzen

- `CLAUDE.md` Regel 1 (Interface First).
- ADR: [0001-optionaler-llm-enricher-ingestion.md](./0001-optionaler-llm-enricher-ingestion.md)
