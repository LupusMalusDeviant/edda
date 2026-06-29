---
id: tool-list-memory
title: list_memory Tool Guide
domain: tools.knowledge
type: Guideline
priority: Medium
tags: [tool, list-memory, memory, langzeitgedaechtnis, eintraege]
concepts: [memory, list, langzeitgedaechtnis, eintraege, filter, domain, durchstoebern]
author: system
requires: [tool-search-memory]
---

## list_memory

### Beschreibung

Durchstöbert und listet die gespeicherten Gedächtnis-Einträge auf, die für den aktuellen Benutzer sichtbar sind. Unterstützt Filterung nach Domain, Typ und Tag.

### Parameter

| Parameter | Typ | Pflicht | Beschreibung |
|-----------|-----|---------|-------------|
| domain | string | Nein | Filtert nach Domain (z.B. "coding", "security") |
| type | string | Nein | Filtert nach Typ (Rule, Pattern, Convention, etc.) |
| tag | string | Nein | Filtert nach Tag |

### Verwendungsbeispiele

- "Welche Einträge gibt es zu Security?" → `domain: "security"`
- "Zeig mir alle Constraints" → `type: "Constraint"`
- Alle Einträge mit Tag "async" → `tag: "async"`

### Best Practices

- Filter kombinieren für präzise Ergebnisse
- Nutze dieses Tool, um einen Überblick über das gespeicherte Langzeitgedächtnis zu bekommen
