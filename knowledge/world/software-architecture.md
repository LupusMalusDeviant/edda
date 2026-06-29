---
id: world-software-architecture
title: Software Architecture Patterns
domain: world
type: WorldKnowledge
priority: Low
tags: [microservices, ddd, cqrs, clean-architecture, event-sourcing, hexagonal]
concepts: [microservices, ddd, cqrs, clean-architecture, event-sourcing, hexagonal, bounded-context]
author: system
requires: [world-oop-principles, world-data-patterns]
---

## Software Architecture Patterns

### Clean / Hexagonal Architecture

Organize code in concentric layers: **Domain → Application → Infrastructure → Presentation**. Dependencies always point inward. The domain knows nothing about databases, HTTP, or frameworks. Hexagonal Architecture (Ports & Adapters) formalizes this: the application core exposes *ports* (interfaces) and infrastructure provides *adapters* (implementations).

### Domain-Driven Design (DDD)

- **Bounded Context**: A clear boundary within which a domain model applies. Different contexts can use the same term differently.
- **Aggregate**: A cluster of objects treated as a unit for data changes. Only the aggregate root is accessible from outside.
- **Value Object**: Immutable, identity-free object defined only by its attributes.
- **Domain Events**: Record something significant that happened; decouple contexts via events.

### CQRS (Command Query Responsibility Segregation)

Separate the *write model* (Commands that change state) from the *read model* (Queries that return data). Write side enforces business rules; read side is optimized for query performance. Often combined with Event Sourcing.

### Event Sourcing

Store a sequence of domain events as the source of truth instead of current state. Current state is derived by replaying events. Enables full audit history and temporal queries.

### Microservices

Decompose a system into small, independently deployable services, each owning its data. Key principles: autonomous deployment, API-first communication, resilience (circuit breakers, retries), and service discovery.

### Key Trade-offs

- Microservices add operational complexity (networking, distributed tracing, eventual consistency).
- Event Sourcing is powerful but increases query complexity.
- CQRS pays off only when read/write models diverge significantly.
- Always prefer the simplest architecture that meets current requirements.
