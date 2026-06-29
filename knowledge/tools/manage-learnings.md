---
id: tool-manage-learnings
title: manage_learnings Tool Guide
domain: tools.memory
type: Guideline
priority: Medium
tags: [tool, manage-learnings, learning, correction]
concepts: [learning, correction, mistake, improve, lernen, korrektur]
author: system
requires: [tool-manage-memory, tool-manage-userdata]
---

## manage_learnings

### Beschreibung
Verwaltet zeitgestempelte Korrekturen und Lerneintraege des Agenten. Jeder Eintrag wird mit einem Zeitstempel versehen, sodass der Agent aus vergangenen Fehlern und Korrekturen lernen kann. Maximale Dateigroesse: 100 KB.

### Parameter
| Parameter | Typ | Pflicht | Beschreibung |
|-----------|-----|---------|--------------|
| action | string | Ja | Aktion: `read`, `append`, `clear` |
| content | string | Nur bei `append` | Beschreibung der Korrektur oder des Lerneintrags |

### Verwendungsbeispiele
- Agent erkennt eigenen Fehler → action: `append`, content: "Benutzer bevorzugt kurze Antworten, nicht ausfuehrliche Erklaerungen"
- Benutzer korrigiert den Agenten → action: `append`, content: "API-Endpunkt ist /v2/data, nicht /v1/data"
- Agent prueft bisherige Learnings → action: `read`
- Benutzer sagt "Vergiss deine Korrekturen" → action: `clear`
- Benutzer sagt "Du machst den gleichen Fehler immer wieder" → action: `read` (zum Pruefen)

### Best Practices
- Learnings praezise und kontextreich formulieren, damit sie spaeter nuetzlich sind
- `append` statt `write` verwenden — Eintraege werden automatisch zeitgestempelt und angehaengt
- Vor jeder Aufgabe relevante Learnings lesen, um bekannte Fehler zu vermeiden
- Nur echte Korrekturen speichern, keine allgemeinen Informationen (dafuer `manage_memory` verwenden)
