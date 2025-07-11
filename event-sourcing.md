# Event-Sourced Reader-Writer Implementation Specification

## Executive Summary

This specification presents an innovative approach to the classic reader-writer synchronization problem using event sourcing principles. By replacing direct state mutations with an append-only event log, we eliminate lock contention while gaining powerful capabilities like time travel, audit trails, and horizontal scalability. This approach is particularly suited for distributed systems, high-throughput scenarios, and domains requiring strong auditability.

## Table of Contents

1. [Introduction](#introduction)
2. [Core Concepts](#core-concepts)
3. [System Architecture](#system-architecture)
4. [Implementation Details](#implementation-details)
5. [Consistency Models](#consistency-models)
6. [Performance Optimization](#performance-optimization)
7. [Operational Considerations](#operational-considerations)
8. [Migration Strategy](#migration-strategy)
9. [Case Studies](#case-studies)
10. [Reference Implementation](#reference-implementation)

## Introduction

### Problem Statement

The traditional reader-writer problem involves coordinating access to shared data where:
- Multiple readers can access data simultaneously
- Writers require exclusive access
- Lock contention becomes a bottleneck at scale

### Event-Sourced Solution

Instead of modifying shared state directly, we:
- Capture all changes as immutable events
- Append events to a sequential log
- Reconstruct current state by replaying events
- Eliminate locks entirely through architectural design

### Key Benefits

- **Zero Lock Contention**: Writers append without blocking
- **Complete Audit Trail**: Every change is permanently recorded
- **Time Travel**: Reconstruct state at any point in history
- **Horizontal Scalability**: Distribute reads across multiple nodes
- **Natural Conflict Resolution**: Event ordering provides deterministic outcomes

## Core Concepts

### Event Sourcing Fundamentals

Event sourcing captures state changes as a sequence of domain events rather than storing current state directly. Each event represents a fact that occurred in the system.

```
Traditional Approach:
  State: { counter: 5 }
  Operation: Increment by 2
  New State: { counter: 7 }

Event-Sourced Approach:
  Events: [
    { type: "Initialized", value: 0 },
    { type: "Incremented", by: 5 },
    { type: "Incremented", by: 2 }
  ]
  Current State: Computed by replaying events = 7
```

### Reader-Writer Mapping

| Traditional Pattern | Event-Sourced Pattern |
|-------------------|---------------------|
| Acquire read lock | Fetch events |
| Read shared state | Apply events to compute state |
| Release read lock | No lock to release |
| | |
| Acquire write lock | Create event |
| Modify shared state | Append to event store |
| Release write lock | No lock to release |

## System Architecture

### High-Level Architecture

```
┌─────────────┐     ┌─────────────┐     ┌─────────────┐
│   Writer 1  │     │   Writer 2  │     │   Writer N  │
└──────┬──────┘     └──────┬──────┘     └──────┬──────┘
       │                   │                   │
       └───────────────────┴───────────────────┘
                           │
                    ┌──────▼──────┐
                    │ Event Store │
                    │  (Append)   │
                    └──────┬──────┘
                           │
       ┌───────────────────┴───────────────────┐
       │                                       │
┌──────▼──────┐     ┌──────────────┐   ┌──────▼──────┐
│ Read Model  │     │  Projection  │   │  Snapshot   │
│   Cache 1   │     │   Engine     │   │    Store    │
└─────────────┘     └──────┬───────┘   └─────────────┘
                           │
       ┌───────────────────┴───────────────────┐
       │                   │                   │
┌──────▼──────┐     ┌──────▼──────┐     ┌──────▼──────┐
│  Reader 1   │     │  Reader 2   │     │  Reader N   │
└─────────────┘     └─────────────┘     └─────────────┘
```

### Component Specifications

#### Event Store
- **Purpose**: Persistent, append-only log of all events
- **Guarantees**: 
  - Strict ordering via sequence numbers
  - Durability (events never lost)
  - High availability
- **Operations**:
  - `Append(event)`: Add event atomically
  - `ReadFrom(sequence)`: Read events from sequence
  - `Subscribe(from)`: Real-time event stream

#### Projection Engine
- **Purpose**: Transform events into read-optimized views
- **Features**:
  - Multiple projections from same event stream
  - Asynchronous processing
  - Resumable from last position

#### Snapshot Store
- **Purpose**: Periodic state captures for performance
- **Strategy**: 
  - Create after N events or T time
  - Store with sequence number reference
  - Enable fast state reconstruction

## Implementation Details

### Event Schema Design

```typescript
// Base event structure
interface Event {
  id: UUID
  sequenceNumber: bigint
  timestamp: DateTime
  aggregateId: string
  eventType: string
  eventVersion: number
  metadata: EventMetadata
}

interface EventMetadata {
  userId: string
  correlationId: UUID
  causationId: UUID
  ipAddress?: string
  userAgent?: string
}

// Domain-specific events
interface DataWrittenEvent extends Event {
  eventType: "DataWritten"
  payload: {
    key: string
    value: any
    previousValue?: any
  }
}

interface DataDeletedEvent extends Event {
  eventType: "DataDeleted"
  payload: {
    key: string
    deletedValue: any
  }
}

interface BulkUpdateEvent extends Event {
  eventType: "BulkUpdate"
  payload: {
    updates: Array<{
      key: string
      value: any
    }>
  }
}
```

### Event Store Implementation

```typescript
interface IEventStore {
  // Write operations
  append(event: Event): Promise<bigint>
  appendBatch(events: Event[]): Promise<bigint[]>
  
  // Read operations
  readForward(from: bigint, count: number): Promise<Event[]>
  readBackward(from: bigint, count: number): Promise<Event[]>
  readStream(aggregateId: string, from?: bigint): Promise<Event[]>
  
  // Subscription
  subscribe(from: bigint, handler: (event: Event) => void): Subscription
  
  // Metadata
  getLatestSequence(): Promise<bigint>
  getEventCount(): Promise<bigint>
}

// Example implementation with PostgreSQL
class PostgresEventStore implements IEventStore {
  async append(event: Event): Promise<bigint> {
    const result = await db.query(`
      INSERT INTO events (
        id, aggregate_id, event_type, event_version,
        payload, metadata, created_at
      ) VALUES ($1, $2, $3, $4, $5, $6, $7)
      RETURNING sequence_number
    `, [
      event.id,
      event.aggregateId,
      event.eventType,
      event.eventVersion,
      JSON.stringify(event.payload),
      JSON.stringify(event.metadata),
      event.timestamp
    ])
    
    return BigInt(result.rows[0].sequence_number)
  }
  
  // ... other methods
}
```

### Writer Implementation

```typescript
class EventSourcedWriter {
  constructor(
    private eventStore: IEventStore,
    private validator: IEventValidator
  ) {}
  
  async write(key: string, value: any): Promise<void> {
    // Create event
    const event: DataWrittenEvent = {
      id: generateUUID(),
      sequenceNumber: 0n, // Will be assigned by event store
      timestamp: new Date(),
      aggregateId: key,
      eventType: "DataWritten",
      eventVersion: 1,
      metadata: {
        userId: getCurrentUserId(),
        correlationId: getCorrelationId(),
        causationId: generateUUID()
      },
      payload: { key, value }
    }
    
    // Validate event
    await this.validator.validate(event)
    
    // Append to store
    const sequenceNumber = await this.eventStore.append(event)
    
    // Optionally publish to message bus
    await this.publishEvent(event, sequenceNumber)
  }
  
  async delete(key: string): Promise<void> {
    // Similar pattern for delete events
  }
}
```

### Reader Implementation

```typescript
class EventSourcedReader {
  private cache: Map<string, CachedState> = new Map()
  private lastSequence: bigint = 0n
  
  constructor(
    private eventStore: IEventStore,
    private snapshotStore: ISnapshotStore
  ) {}
  
  async read(key: string): Promise<any> {
    // Check cache freshness
    const cached = this.cache.get(key)
    if (cached && this.isFresh(cached)) {
      return cached.value
    }
    
    // Load from snapshot if available
    const snapshot = await this.snapshotStore.getLatest(key)
    let state = snapshot?.state || {}
    let fromSequence = snapshot?.sequenceNumber || 0n
    
    // Apply events since snapshot
    const events = await this.eventStore.readStream(key, fromSequence)
    for (const event of events) {
      state = this.applyEvent(state, event)
    }
    
    // Update cache
    this.cache.set(key, {
      value: state,
      sequence: events[events.length - 1]?.sequenceNumber || fromSequence,
      timestamp: new Date()
    })
    
    return state
  }
  
  private applyEvent(state: any, event: Event): any {
    switch (event.eventType) {
      case "DataWritten":
        return { ...state, [event.payload.key]: event.payload.value }
      case "DataDeleted":
        const { [event.payload.key]: deleted, ...rest } = state
        return rest
      default:
        return state
    }
  }
}
```

## Consistency Models

### Strong Consistency

Ensures readers always see the latest committed writes.

```typescript
class StronglyConsistentReader extends EventSourcedReader {
  async read(key: string): Promise<any> {
    // Always fetch latest events
    const latestSequence = await this.eventStore.getLatestSequence()
    const events = await this.eventStore.readStream(key, 0n)
    
    // Build state from all events
    let state = {}
    for (const event of events) {
      state = this.applyEvent(state, event)
    }
    
    return state
  }
}
```

### Eventual Consistency

Accepts slightly stale data for better performance.

```typescript
class EventuallyConsistentReader extends EventSourcedReader {
  private readonly maxStaleness = 5000 // 5 seconds
  
  async read(key: string): Promise<any> {
    const cached = this.cache.get(key)
    
    if (cached && (Date.now() - cached.timestamp.getTime()) < this.maxStaleness) {
      return cached.value // Return potentially stale data
    }
    
    // Refresh cache asynchronously
    this.refreshCache(key).catch(console.error)
    
    return cached?.value || await super.read(key)
  }
}
```

### Bounded Staleness

Configurable consistency with maximum staleness bounds.

```typescript
interface ConsistencyPolicy {
  maxStalenessMs: number
  maxSequenceLag: bigint
}

class BoundedConsistencyReader extends EventSourcedReader {
  constructor(
    eventStore: IEventStore,
    snapshotStore: ISnapshotStore,
    private policy: ConsistencyPolicy
  ) {
    super(eventStore, snapshotStore)
  }
  
  async read(key: string): Promise<any> {
    const cached = this.cache.get(key)
    
    if (cached) {
      const timeStaleness = Date.now() - cached.timestamp.getTime()
      const sequenceStaleness = await this.getSequenceLag(cached.sequence)
      
      if (timeStaleness < this.policy.maxStalenessMs && 
          sequenceStaleness < this.policy.maxSequenceLag) {
        return cached.value
      }
    }
    
    return super.read(key)
  }
}
```

## Performance Optimization

### Snapshot Strategy

```typescript
class SnapshotManager {
  constructor(
    private eventStore: IEventStore,
    private snapshotStore: ISnapshotStore,
    private config: SnapshotConfig
  ) {}
  
  async createSnapshot(aggregateId: string): Promise<void> {
    // Get all events for aggregate
    const events = await this.eventStore.readStream(aggregateId)
    
    if (events.length < this.config.minEventCount) {
      return // Not enough events to warrant snapshot
    }
    
    // Build current state
    let state = {}
    for (const event of events) {
      state = this.applyEvent(state, event)
    }
    
    // Store snapshot
    await this.snapshotStore.save({
      aggregateId,
      sequenceNumber: events[events.length - 1].sequenceNumber,
      state,
      timestamp: new Date()
    })
    
    // Optionally compact old events
    if (this.config.compactAfterSnapshot) {
      await this.compactEvents(aggregateId, events)
    }
  }
}
```

### Read Model Optimization

```typescript
class MaterializedView {
  private state: Map<string, any> = new Map()
  private lastSequence: bigint = 0n
  
  constructor(
    private eventStore: IEventStore,
    private projectionName: string
  ) {
    this.startProjection()
  }
  
  private async startProjection(): Promise<void> {
    // Subscribe to event stream
    this.eventStore.subscribe(this.lastSequence, async (event) => {
      await this.handleEvent(event)
      this.lastSequence = event.sequenceNumber
    })
  }
  
  private async handleEvent(event: Event): Promise<void> {
    // Update materialized view based on event
    switch (event.eventType) {
      case "DataWritten":
        this.state.set(event.payload.key, event.payload.value)
        break
      case "DataDeleted":
        this.state.delete(event.payload.key)
        break
    }
  }
  
  async read(key: string): Promise<any> {
    return this.state.get(key) // O(1) lookup
  }
}
```

### Partitioning Strategy

```typescript
class PartitionedEventStore {
  private partitions: Map<number, IEventStore> = new Map()
  private partitionCount: number
  
  constructor(partitionCount: number) {
    this.partitionCount = partitionCount
    // Initialize partitions
    for (let i = 0; i < partitionCount; i++) {
      this.partitions.set(i, new PostgresEventStore(i))
    }
  }
  
  private getPartition(aggregateId: string): IEventStore {
    const hash = this.hashString(aggregateId)
    const partition = hash % this.partitionCount
    return this.partitions.get(partition)!
  }
  
  async append(event: Event): Promise<bigint> {
    const partition = this.getPartition(event.aggregateId)
    return partition.append(event)
  }
}
```

## Operational Considerations

### Monitoring and Observability

```typescript
interface EventStoreMetrics {
  eventsPerSecond: number
  averageEventSize: number
  totalEvents: bigint
  storageUsed: bigint
  replicationLag: number
  projectionLag: Map<string, number>
}

class EventStoreMonitor {
  async collectMetrics(): Promise<EventStoreMetrics> {
    // Collect various metrics
    return {
      eventsPerSecond: await this.calculateEventRate(),
      averageEventSize: await this.calculateAverageSize(),
      totalEvents: await this.eventStore.getEventCount(),
      storageUsed: await this.calculateStorageUsage(),
      replicationLag: await this.measureReplicationLag(),
      projectionLag: await this.measureProjectionLags()
    }
  }
  
  setupAlerts(): void {
    // Alert on high event rate
    this.alertOn('eventRate', rate => rate > 10000, 'High event rate detected')
    
    // Alert on projection lag
    this.alertOn('projectionLag', lag => lag > 60000, 'Projection lagging behind')
    
    // Alert on storage usage
    this.alertOn('storageUsage', usage => usage > 0.8, 'Storage usage high')
  }
}
```

### Backup and Recovery

```typescript
class EventStoreBackup {
  async performBackup(): Promise<void> {
    const latestSequence = await this.eventStore.getLatestSequence()
    const batchSize = 10000n
    
    for (let seq = 0n; seq < latestSequence; seq += batchSize) {
      const events = await this.eventStore.readForward(seq, Number(batchSize))
      await this.backupStorage.store(events)
    }
  }
  
  async restore(fromBackup: string): Promise<void> {
    const events = await this.backupStorage.load(fromBackup)
    
    for (const batch of this.batchEvents(events, 1000)) {
      await this.eventStore.appendBatch(batch)
    }
  }
}
```

### Schema Evolution

```typescript
class EventUpgrader {
  private upgraders: Map<string, Map<number, EventUpgrader>> = new Map()
  
  registerUpgrader(
    eventType: string, 
    fromVersion: number, 
    toVersion: number,
    upgrader: (event: any) => any
  ): void {
    if (!this.upgraders.has(eventType)) {
      this.upgraders.set(eventType, new Map())
    }
    this.upgraders.get(eventType)!.set(fromVersion, upgrader)
  }
  
  upgrade(event: Event): Event {
    const upgrader = this.upgraders
      .get(event.eventType)
      ?.get(event.eventVersion)
    
    if (upgrader) {
      return upgrader(event)
    }
    
    return event
  }
}
```

## Migration Strategy

### Phase 1: Shadow Mode

Run event sourcing alongside existing system:

```typescript
class DualModeWriter {
  constructor(
    private legacyWriter: LegacyWriter,
    private eventWriter: EventSourcedWriter
  ) {}
  
  async write(key: string, value: any): Promise<void> {
    // Write to both systems
    await Promise.all([
      this.legacyWriter.write(key, value),
      this.eventWriter.write(key, value)
    ])
  }
}
```

### Phase 2: Read Migration

Gradually migrate reads to event-sourced system:

```typescript
class MigrationReader {
  constructor(
    private legacyReader: LegacyReader,
    private eventReader: EventSourcedReader,
    private migrationPercentage: number
  ) {}
  
  async read(key: string): Promise<any> {
    if (Math.random() * 100 < this.migrationPercentage) {
      // Read from event source
      const value = await this.eventReader.read(key)
      
      // Verify against legacy for safety
      if (this.verificationEnabled) {
        const legacyValue = await this.legacyReader.read(key)
        if (!this.deepEqual(value, legacyValue)) {
          this.logDiscrepancy(key, value, legacyValue)
        }
      }
      
      return value
    } else {
      // Read from legacy
      return this.legacyReader.read(key)
    }
  }
}
```

### Phase 3: Full Migration

Complete transition with rollback capability:

```typescript
class MigrationCoordinator {
  async completeMigration(): Promise<void> {
    // Stop writes to legacy system
    await this.legacyWriter.disable()
    
    // Ensure all events are processed
    await this.waitForProjections()
    
    // Switch all reads to event source
    await this.switchReaders()
    
    // Keep legacy system for rollback
    await this.archiveLegacySystem()
  }
  
  async rollback(): Promise<void> {
    // Re-enable legacy writer
    await this.legacyWriter.enable()
    
    // Replay events missed during migration
    await this.replayMissedEvents()
    
    // Switch readers back
    await this.switchToLegacyReaders()
  }
}
```

## Case Studies

### Case Study 1: Financial Trading System

**Challenge**: High-frequency trading system with strict audit requirements

**Solution**:
- Event store partitioned by instrument
- Sub-millisecond append latency
- Compliance projections for regulators
- Time-travel for trade investigations

**Results**:
- 100,000 events/second throughput
- Complete audit trail maintained
- 50% reduction in compliance reporting time

### Case Study 2: E-commerce Inventory

**Challenge**: Global inventory system with multiple warehouses

**Solution**:
- Events for all inventory movements
- Regional read models for low latency
- Eventual consistency with 5-second bounds
- Snapshot every 1000 events

**Results**:
- Eliminated inventory discrepancies
- 10x improvement in read scalability
- Historical inventory analysis enabled

### Case Study 3: Collaborative Document Editing

**Challenge**: Real-time collaborative editing with conflict resolution

**Solution**:
- Operational transformation as events
- Event ordering provides natural conflict resolution
- User-specific projections for personalized views
- Real-time event streaming to clients

**Results**:
- Zero conflicts in concurrent editing
- Complete edit history preserved
- Time-travel through document versions

## Reference Implementation

### Complete Working Example

```typescript
// Domain Events
type ShoppingCartEvent = 
  | { type: "CartCreated"; cartId: string; userId: string }
  | { type: "ItemAdded"; cartId: string; itemId: string; quantity: number }
  | { type: "ItemRemoved"; cartId: string; itemId: string }
  | { type: "CartCheckedOut"; cartId: string; orderId: string }

// Event-Sourced Shopping Cart
class EventSourcedShoppingCart {
  private eventStore: IEventStore
  private projections: Map<string, CartProjection> = new Map()
  
  async addItem(cartId: string, itemId: string, quantity: number): Promise<void> {
    const event: ItemAddedEvent = {
      id: generateUUID(),
      sequenceNumber: 0n,
      timestamp: new Date(),
      aggregateId: cartId,
      eventType: "ItemAdded",
      eventVersion: 1,
      metadata: { userId: getCurrentUserId() },
      payload: { cartId, itemId, quantity }
    }
    
    await this.eventStore.append(event)
  }
  
  async getCart(cartId: string): Promise<Cart> {
    // Check projection cache
    let projection = this.projections.get(cartId)
    
    if (!projection || this.isStale(projection)) {
      projection = await this.rebuildProjection(cartId)
      this.projections.set(cartId, projection)
    }
    
    return projection.currentState
  }
  
  private async rebuildProjection(cartId: string): Promise<CartProjection> {
    const events = await this.eventStore.readStream(cartId)
    
    let state: Cart = { id: cartId, items: [], total: 0 }
    let lastSequence = 0n
    
    for (const event of events) {
      state = this.applyEvent(state, event)
      lastSequence = event.sequenceNumber
    }
    
    return {
      currentState: state,
      lastSequence,
      lastUpdated: new Date()
    }
  }
  
  private applyEvent(cart: Cart, event: Event): Cart {
    switch (event.eventType) {
      case "ItemAdded":
        return {
          ...cart,
          items: [...cart.items, {
            itemId: event.payload.itemId,
            quantity: event.payload.quantity
          }]
        }
      case "ItemRemoved":
        return {
          ...cart,
          items: cart.items.filter(item => 
            item.itemId !== event.payload.itemId
          )
        }
      default:
        return cart
    }
  }
}
```

### Testing Strategy

```typescript
describe("EventSourcedShoppingCart", () => {
  it("should handle concurrent additions", async () => {
    const cart = new EventSourcedShoppingCart(eventStore)
    
    // Simulate concurrent additions
    await Promise.all([
      cart.addItem("cart1", "item1", 1),
      cart.addItem("cart1", "item2", 2),
      cart.addItem("cart1", "item3", 3)
    ])
    
    const result = await cart.getCart("cart1")
    expect(result.items).toHaveLength(3)
  })
  
  it("should maintain consistency across readers", async () => {
    const writer = new EventSourcedShoppingCart(eventStore)
    const reader1 = new EventSourcedShoppingCart(eventStore)
    const reader2 = new EventSourcedShoppingCart(eventStore)
    
    await writer.addItem("cart1", "item1", 1)
    
    const [result1, result2] = await Promise.all([
      reader1.getCart("cart1"),
      reader2.getCart("cart1")
    ])
    
    expect(result1).toEqual(result2)
  })
})
```

## Conclusion

Event sourcing provides a powerful alternative to traditional locking mechanisms for the reader-writer problem. While it introduces complexity in state reconstruction and requires careful consideration of consistency models, the benefits of zero lock contention, complete audit trails, and horizontal scalability make it an excellent choice for many modern distributed systems.

The key to successful implementation lies in:
1. Careful event schema design
2. Appropriate consistency model selection
3. Effective use of snapshots and projections
4. Comprehensive monitoring and operations tooling
5. Gradual migration with verification

As systems continue to scale and audit requirements become more stringent, event-sourced architectures will become increasingly valuable for solving complex coordination problems.