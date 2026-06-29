---
id: tool-manage-userdata
title: manage_userdata Tool Guide
domain: tools.memory
type: Guideline
priority: Medium
tags: [tool, manage-userdata, user, preferences]
concepts: [user, preferences, settings, name, language, benutzer]
author: system
requires: [tool-manage-memory, tool-manage-learnings]
---

## manage_userdata

### Beschreibung
Verwaltet benutzerspezifische Praeferenzen und Einstellungen in einer `userdata.md`-Datei. Speichert strukturierte Daten wie Name, Sprache, Zeitzone und weitere persoenliche Einstellungen. Maximale Dateigroesse: 100 KB.

### Parameter
| Parameter | Typ | Pflicht | Beschreibung |
|-----------|-----|---------|--------------|
| action | string | Ja | Aktion: `read`, `get`, `write`, `set`, `delete` |
| content | string | Nur bei `write`/`set` | Inhalt oder Schluessel-Wert-Paar zum Speichern |

### Verwendungsbeispiele
- Benutzer sagt "Mein Name ist Max" → action: `set`, content: "name: Max"
- Benutzer sagt "Ich spreche Deutsch" → action: `set`, content: "language: de"
- Benutzer sagt "Was weisst du ueber meine Einstellungen?" → action: `read`
- Benutzer sagt "Loesche meine Spracheinstellung" → action: `delete`, content: "language"
- Benutzer sagt "Wie heisse ich?" → action: `get`, content: "name"

### Best Practices
- `get` fuer einzelne Werte verwenden, `read` fuer die gesamte Uebersicht
- `set` aktualisiert oder erstellt einzelne Eintraege, `write` ueberschreibt die gesamte Datei
- Schluessel-Wert-Paare konsistent im Format `key: value` halten
- Sensible Daten wie Passwoerter gehoeren nicht in Userdata — dafuer `manage_credentials` verwenden
