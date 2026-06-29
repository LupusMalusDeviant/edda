---
id: tool-manage-memory
title: manage_memory Tool Guide
domain: tools.memory
type: Guideline
priority: Medium
tags: [tool, manage-memory, memory, long-term]
concepts: [memory, remember, recall, forget, store, merken, erinnern]
author: system
requires: [tool-manage-learnings, tool-manage-userdata]
---

## manage_memory

### Beschreibung
Verwaltet das Langzeitgedaechtnis des Agenten pro Benutzer. Speichert, liest und loescht Inhalte in einer `memory.md`-Datei im benutzerspezifischen Verzeichnis. Maximale Dateigroesse: 100 KB.

### Parameter
| Parameter | Typ | Pflicht | Beschreibung |
|-----------|-----|---------|--------------|
| action | string | Ja | Aktion: `read`, `write`, `clear` |
| content | string | Nur bei `write` | Inhalt der in die Memory-Datei geschrieben wird |

### Verwendungsbeispiele
- Benutzer sagt "Merk dir, dass ich Python bevorzuge" → action: `write`, content: "Benutzer bevorzugt Python"
- Benutzer sagt "Was weisst du ueber mich?" → action: `read`
- Benutzer sagt "Vergiss alles" → action: `clear`
- Benutzer sagt "Erinnere dich an mein Projekt" → action: `read`
- Benutzer sagt "Speichere das fuer spaeter" → action: `write`

### Best Practices
- Vor dem Schreiben immer zuerst lesen, um bestehende Eintraege nicht zu ueberschreiben
- Inhalte strukturiert und kompakt halten, da das Limit bei 100 KB liegt
- Sensible Daten gehoeren nicht ins Memory — dafuer `manage_credentials` verwenden
- Bei `clear` den Benutzer vorher bestaetigen lassen, da die Aktion nicht rueckgaengig gemacht werden kann
