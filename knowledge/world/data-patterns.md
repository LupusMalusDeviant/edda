---
id: world-data-patterns
title: Data Access and Storage Patterns
domain: world
type: WorldKnowledge
priority: Low
tags: [repository, unit-of-work, acid, cap-theorem, sharding, caching, indexes]
concepts: [repository, unit-of-work, acid, cap-theorem, sharding, caching, indexes, database]
author: system
requires: [world-oop-principles]
---

## Data Access and Storage Patterns

### Repository Pattern

Abstracts data access behind an interface. Domain logic talks to a repository, not to the database directly. Enables switching data stores, in-memory fakes in tests, and clean separation of concerns.

### Unit of Work

Groups multiple repository operations into a single transaction boundary. Tracks changes and commits or rolls back as a unit. EF Core's `DbContext` is a built-in Unit of Work implementation.

### ACID Properties

- **Atomicity**: All operations in a transaction succeed or all are rolled back.
- **Consistency**: A transaction brings the database from one valid state to another.
- **Isolation**: Concurrent transactions do not see each other's intermediate states.
- **Durability**: Committed data survives failures (written to durable storage).

### CAP Theorem

A distributed data store can guarantee at most two of: **Consistency** (all nodes see the same data), **Availability** (every request gets a response), **Partition tolerance** (the system continues despite network splits). Since network partitions are unavoidable, systems choose between CP (consistent) and AP (available) during a partition.

### Indexing Strategies

- Create indexes on columns used in WHERE, JOIN ON, and ORDER BY clauses.
- Composite indexes: column order matters — match the query's left-to-right column usage.
- Avoid over-indexing: indexes slow writes and consume storage.
- Use covering indexes for high-frequency read queries.

### Sharding

Horizontal partitioning: split data across multiple nodes by a shard key (e.g., userId range or hash). Increases write throughput and storage capacity. Cross-shard queries and transactions become complex.

### Caching Patterns

- **Cache-aside**: Application checks cache first; on miss, loads from DB and populates cache.
- **Write-through**: Write to cache and DB together; cache always consistent, higher write latency.
- **Write-behind**: Write to cache, sync to DB asynchronously; faster writes, risk of data loss.
- Invalidation strategy is critical: use TTL, event-driven invalidation, or versioned cache keys.

### Read Replicas

Direct heavy read traffic to read replicas, reducing load on the primary. Replicas may lag slightly (eventual consistency). Acceptable for analytics or non-critical reads; not for immediately consistent post-write reads.
