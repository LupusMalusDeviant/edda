<!-- Danke für deinen Beitrag! Bitte fülle die Vorlage aus. Sprache: Deutsch. -->

## Was & warum

<!-- Was ändert dieser PR und warum? Verlinke ein zugehöriges Issue: "Closes #123". -->

## Art der Änderung

- [ ] Bugfix
- [ ] Neues Feature
- [ ] Refactoring (kein Verhaltensänderung nach außen)
- [ ] Doku
- [ ] Sonstiges:

## Tests

<!-- Wie wurde die Änderung abgesichert? Neue/angepasste Tests? -->

## Checkliste

- [ ] `dotnet build Edda.slnx` ist grün (0 Warnings — Warnings sind Fehler)
- [ ] `dotnet test Edda.slnx` ist grün; neue Klassen haben 100 % Unit-Test-Coverage (ohne Infrastruktur/Mocks)
- [ ] Von außen genutzte Klassen haben ein Interface in `src/Core/` (Interface-First)
- [ ] Kein direkter `File.*`/`Directory.*`/`Path.*`-I/O (nur `IFileSystem`), kein `DateTime.UtcNow` (nur `TimeProvider`)
- [ ] Keine Secrets im Code; Tools werfen keine Exceptions (`ToolResult.Fail`); `userId` aus dem `ToolExecutionContext`
- [ ] Public API mit englischer In-Code-Doku; externe Doku (`docs/`) und Commit-Messages auf Deutsch
- [ ] Der read-only-/default-deny-Charakter des MCP-Servers bleibt gewahrt

<!-- Details zu den Regeln: CONTRIBUTING.md. Sicherheitslücken bitte NICHT als PR, sondern über SECURITY.md melden. -->
