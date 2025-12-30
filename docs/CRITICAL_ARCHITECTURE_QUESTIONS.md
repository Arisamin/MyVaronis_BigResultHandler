# Critical Architecture Questions

> **Document Purpose:** This document contains unresolved architectural questions, potential issues, and areas requiring clarification identified during technical review of the BigResultHandler system documentation.

> **Status Tracking:** Questions marked as ✅ RESOLVED have been addressed in updated documentation.

---

## **Critical Architecture Questions:**

### 1. **Message Routing & Transaction Discovery** ✅ RESOLVED
**Issue:** How do messages get routed to the correct StateMachine instance?

**RESOLUTION:**
Each transaction gets its own dedicated RabbitMQ queues with deterministic names:
- `transaction-{TransactionId}-header`
- `transaction-{TransactionId}-payload`

**Flow:**
1. State 0 (Establishing Connection): StateMachine creates queues and sends request to Producer with queue names
2. Producer publishes messages to the transaction-specific queues
3. StateMachine consumes from its own queues only
4. No routing needed - queue isolation provides natural message segregation

**Documentation Updated:** STATE_MACHINE_LLD.md, CRASH_RECOVERY.md

---

### 2. **ResultHandler Lifecycle & Scope** ✅ RESOLVED
**Contradiction in Documentation:**
- STATE_MACHINE_LLD.md shows StateMachine constructor receives `resultHandler` as dependency (suggesting one ResultHandler per StateMachine/transaction)
- RESULT_HANDLER_LLD.md shows ResultHandler registers with queue consumers in constructor (suggesting one ResultHandler for all messages listening to shared queues)

**RESOLUTION:**
ResultHandler is **per-transaction**, not singleton. The entire transaction processing stack is instantiated within a dedicated Autofac lifetime scope:

**Architecture:**
- ResultService receives request with TransactionID
- Creates Autofac lifetime scope for this transaction
- Within scope, instantiates:
  - Transaction-specific RabbitMQ queues
  - StateMachine (one per transaction)
  - ResultHandler (one per transaction)
  - Queue consumers (specific to transaction queues)
- On completion, scope is disposed, destroying all components

**No shared infrastructure** - each transaction is fully isolated.

**Documentation Updated:** STATE_MACHINE_LLD.md, CRASH_RECOVERY.md

---

### 3. **Queue Consumer Instantiation Timing** ✅ RESOLVED
**Issue:** RESULT_HANDLER_LLD.md states: "queue consumers are instantiated by ResultService when it creates the StateMachine and ResultHandler"

**Question:**
- How can queue consumers be instantiated per-transaction if they're listening to shared RabbitMQ queues?
- Aren't queue consumers singleton infrastructure components that listen continuously?

**RESOLUTION:**
There are **no shared queues**. Queue consumers ARE instantiated per-transaction because:
1. Each transaction gets its own queues (deterministic naming)
2. Queue consumers are created within the transaction's Autofac lifetime scope
3. Consumers listen only to that transaction's queues
4. On transaction completion, consumers are disposed along with queues

**Crash Recovery:**
- Queue names are deterministic: Same TransactionID → Same queue names
- On recovery, Autofac scope recreates consumers for the same queues
- RabbitMQ queues survive crashes (durable), enabling reconnection

**Documentation Updated:** STATE_MACHINE_LLD.md, CRASH_RECOVERY.md

---
- Clear distinction between queue listener infrastructure (singleton) vs. transaction-specific message processing
- Queue consumer architecture and lifecycle

---

### 4. **Transaction Initialization Flow**
**Ambiguity in First Message Handling:**

STATE_MACHINE_LLD.md "Transaction Initialization" section states:
> "ResultService receives trigger (e.g., first message for new transaction)"

**Questions:**
- How does ResultService detect a "new" transaction vs. an existing one?
- Who detects the first message? ResultHandler callback? ResultManager? Queue consumer?
- What triggers StateMachine instantiation?
- Is there a transaction registry that tracks active transactions?

**Missing:**
- First-message detection and handling logic
- StateMachine instantiation trigger mechanism
- Transaction lifecycle from birth (first message) to death (completion + cleanup)

---

### 5. **AwaitingResultsStateHandler Polling Pattern**
**Concern:** STATE_MACHINE_LLD.md shows timer-based polling every 5 seconds to check KV Store for transaction completion.

**Questions:**
- Why use polling when ResultHandler writes to KV Store and could notify directly?
- What is the justification for polling vs. event-driven notification?

**Performance Impact:**
- Polling creates unnecessary KV Store queries (every 5 seconds for every active transaction)
- Introduces latency: up to 5 seconds before completion detection
- Scales poorly with many concurrent transactions (1000 transactions = 200 KV Store queries/second)

**Alternative Approach:**
- Event-driven notification when ResultHandler completes a payload write
- ResultHandler notifies AwaitingResultsStateHandler directly (or via callback/event)
- Eliminates polling overhead and reduces completion detection latency to near-zero

---

### 6. **Concurrency & Race Conditions**
**Scenario:**
- Multiple payload messages arrive simultaneously for the same transaction
- ResultHandler.HandlePayloadMessage writes to KV Store concurrently (from different threads/callbacks)
- AwaitingResultsStateHandler polls KV Store concurrently while messages are being processed

**Questions:**
- What prevents race conditions when checking completion while messages are still being processed?
- What if the state handler checks completion between the last message upload and KV Store write?
- Are KV Store operations atomic? Do they use optimistic concurrency (eTags)?

**Missing:**
- Synchronization/locking strategy for completion detection
- Thread safety guarantees for TransactionContext updates
- Concurrency control mechanisms

---

### 7. **Header Message Timing & Out-of-Order Delivery**
**Scenario:** What if payload messages arrive BEFORE the header message?

**Current Flow:**
1. ResultHandler.HandlePayloadMessage processes payload and writes blob mapping to KV Store
2. AwaitingResultsStateHandler.OnEnter() tries to load transaction metadata (written by HandleHeaderMessage)
3. If header hasn't arrived yet, metadata doesn't exist

**Questions:**
- How is message ordering guaranteed between Header Queue and Payload Queue?
- What happens if payloads arrive before header?
- Do we buffer payload messages until header arrives?
- Do we fail and rely on retry?

**Missing:**
- Out-of-order message handling strategy
- Message sequencing guarantees or lack thereof
- Buffering/retry logic for premature payload messages

---

### 8. **Error Recovery Scenarios**
**Incomplete Error Handling:**

**Scenario 1: Partial Payload Processing Failure**
- ResultHandler.HandlePayloadMessage uploads blob to Azure Storage successfully
- KV Store write fails (network issue, throttling, etc.)
- Result: Blob exists in Azure Storage but no KV Store entry
- Question: How is this detected and recovered? Is the operation retried?

**Scenario 2: Notification Sending Failure**
- SendingCompletionStateHandler assembles blob URIs successfully
- NotificationSender.PublishNotification fails (queue down, network issue)
- Question: Does transaction remain in SendingCompletion state for retry? Or is it lost?

**Scenario 3: Crash During State Transition**
- Process crashes after StateMachine.PersistContext() but before state handler OnEnter() completes
- Question: On recovery, does the system resume mid-transition? Replay the transition?

**Questions:**
- Are all operations idempotent? Can we safely retry?
- What are the failure modes for each component?
- What are the recovery procedures?

**Missing:**
- Failure mode analysis and documentation
- Idempotency guarantees or lack thereof
- Retry policies and exponential backoff strategies
- Dead letter queue handling for unrecoverable failures

---

### 9. **StateMachine Lifecycle Management**
**Issue:** STATE_MACHINE_LLD.md shows StateMachine instances created per transaction but doesn't address cleanup.

**Questions:**
- When are StateMachine instances disposed/cleaned up?
- After SendingCompletion completes? Immediately? After timeout?
- Who tracks active StateMachine instances?
- How do we prevent memory leaks with long-running or stuck transactions?
- Is there a StateMachine registry/manager?

**Missing:**
- StateMachine disposal and resource cleanup strategy
- Active transaction tracking mechanism
- Memory management and leak prevention
- Timeout-based cleanup for abandoned transactions

---

### 10. **KV Store Schema Conflicts & Documentation**
**Ambiguity in RowKey Usage:**

From different documents:
- RESULT_HANDLER_LLD.md: `WriteTransactionMetadata()` uses RowKey `__metadata__`
- STATE_MACHINE_LLD.md: Context persistence uses RowKey `__context__`
- RESULT_HANDLER_LLD.md: Blob mappings use RowKey `{seriesType}:{ordinal}`

**Questions:**
- Are these all in the same partition (TransactionID)? 
- What's the complete list of special RowKeys?
- Are there any other reserved RowKey patterns?
- How do we query to exclude special rows when retrieving blob mappings?

**Missing:**
- Complete KV Store schema specification documenting:
  - All RowKey patterns and their purposes
  - Special/reserved RowKeys (e.g., `__metadata__`, `__context__`)
  - Query patterns (e.g., get all blob mappings excluding special rows)
  - Data types stored in each row type
  - Retention policies

---

### 11. **ResultHandler Dependency in State Handler**
**Design Concern:** STATE_MACHINE_LLD.md shows AwaitingResultsStateHandler has dependency on ResultHandler.

**Questions:**
- Why does AwaitingResultsStateHandler need ResultHandler if ResultHandler operates independently via callbacks?
- What methods does the state handler call on ResultHandler?
- Isn't this a circular dependency concern (StateMachine → ResultHandler, StateHandler → ResultHandler)?

**Design Smell:**
- State handler shouldn't need to call back to message processor
- Suggests unclear separation of concerns
- May indicate missing abstraction layer

**Potential Issue:**
- If ResultHandler is singleton, state handlers from different transactions share same instance
- If ResultHandler is per-transaction, why does state handler need a reference to it?

---

### 12. **Transaction Completion Detection Logic Duplication**
**Concern:** Multiple components appear to have completion checking responsibility.

**Historical Context:**
- RESULT_HANDLER_LLD.md originally had completion checking logic (later removed)
- STATE_MACHINE_LLD.md AwaitingResultsStateHandler has completion checking logic

**Questions:**
- Who is authoritative for determining transaction completion?
- Should completion logic be in one place only?
- If both check completion, can they diverge and cause inconsistencies?

**Risk:**
- Logic drift between components over time
- Maintenance burden (updating logic in multiple places)
- Potential for inconsistent completion determination

---

### 13. **Message Acknowledgment & At-Least-Once Delivery**
**Question:** When are RabbitMQ messages acknowledged?

**Scenarios:**
- If acknowledged before processing completes → risk of message loss on crash
- If acknowledged after processing completes → risk of duplicate processing on crash

**Questions:**
- At what point in HandleHeaderMessage/HandlePayloadMessage are messages acknowledged?
- How does the system handle duplicate messages (at-least-once delivery guarantee)?
- Are operations idempotent to handle duplicates safely?

**Missing:**
- Message acknowledgment strategy
- Duplicate message handling
- At-least-once vs. exactly-once delivery semantics

---

### 14. **Crash Recovery Mechanics**
**Question:** How does crash recovery actually work?

STATE_MACHINE_LLD.md mentions:
> "ResultService recovery manager scans KV Store for incomplete transactions"

**Questions:**
- What identifies a transaction as "incomplete"? CurrentState != Completed?
- How often does recovery manager scan?
- What happens if a transaction is "in-progress" (currently being processed by another instance)?
- In a multi-instance deployment, how do we prevent two instances from recovering the same transaction?

**Missing:**
- Detailed recovery algorithm
- Transaction state identification (in-progress vs. abandoned vs. complete)
- Distributed lock mechanism for recovery in multi-instance scenarios
- Recovery interval and triggering conditions

---

### 15. **Multi-Instance Deployment Considerations**
**Question:** Can multiple ResultService instances run simultaneously?

**Scenarios:**
- Load balancing across multiple service instances
- High availability deployment

**Questions:**
- How are transactions distributed across instances?
- What prevents two instances from processing the same transaction?
- How does crash recovery work when other instances are still running?
- Are there distributed locks or leader election mechanisms?

**Missing:**
- Multi-instance deployment architecture
- Transaction ownership and locking
- Load distribution strategy
- High availability and failover design

---

## **Documentation Consistency Issues:**

### 16. **ARCHITECTURE.md vs LLD Mismatch: Message Flow**
**Inconsistency:**

ARCHITECTURE.md states:
> "Header Consumer forwards header messages to ResultManager"
> "Payload Consumer forwards payload messages to ResultManager"

RESULT_HANDLER_LLD.md shows:
> "Queue consumers register callbacks with ResultHandler"
> "headerQueueConsumer.OnMessageReceived += HandleHeaderMessage"

**Issue:** These are incompatible models.
- First model: Queue consumers → ResultManager → StateMachine → ResultHandler
- Second model: Queue consumers → ResultHandler (direct callbacks)

**Question:** Which is the actual architecture?

---

### 17. **ARCHITECTURE.md vs LLD Mismatch: Upload Responsibility**
**Inconsistency:**

ARCHITECTURE.md says StateMachine:
> "Uploads each payload message immediately to Azure Storage as a separate blob"

RESULT_HANDLER_LLD.md says ResultHandler:
> "Streams payload data to Azure Blob Storage"

**Question:** 
- Who actually performs the upload operation?
- Is the upload done BY StateMachine? Or by ResultHandler called by StateMachine?
- Or by ResultHandler directly without StateMachine involvement?

---

### 18. **State Handler vs ResultHandler Responsibilities**
**Unclear Boundary:**

Documents show overlapping responsibilities:
- ResultHandler writes blob mappings to KV Store
- AwaitingResultsStateHandler queries blob mappings from KV Store and updates TransactionContext

**Questions:**
- Why doesn't ResultHandler update TransactionContext directly when processing messages?
- Why does state handler need to query what ResultHandler just wrote?
- What is the clear responsibility boundary between these components?

---

## **Recommendations:**

### **Documentation Improvements:**
1. **Create sequence diagrams** showing:
   - First message arrival through StateMachine creation
   - Normal message processing flow (header then payloads)
   - Out-of-order message handling (payload before header)
   - Crash and recovery flow

2. **Clarify ResultHandler scope** with explicit statement:
   - Singleton shared across all transactions, OR
   - Per-transaction instance
   - Clear lifecycle documentation (creation, usage, disposal)

3. **Document message routing mechanism:**
   - How TransactionID maps to StateMachine instances
   - Transaction registry/lookup implementation
   - Message dispatching logic

4. **Create comprehensive error/failure mode documentation:**
   - Failure scenarios for each component
   - Recovery procedures
   - Retry policies
   - Idempotency guarantees
   - Dead letter queue handling

5. **Create KV Store schema reference document:**
   - All RowKey patterns and their purposes
   - Special/reserved RowKeys
   - Data types and structure for each row type
   - Query patterns with examples
   - Retention and cleanup policies

6. **Add concurrency/synchronization strategy documentation:**
   - Thread safety guarantees
   - Locking mechanisms
   - Race condition prevention
   - Atomic operations

7. **Document out-of-order message handling:**
   - Message ordering guarantees (or lack thereof)
   - Buffering strategy
   - Handling payloads that arrive before headers

8. **Document StateMachine lifecycle:**
   - Creation triggers
   - Active instance tracking
   - Disposal and cleanup timing
   - Resource management

### **Architecture Improvements:**
1. **Replace polling with event-driven completion notification:**
   - ResultHandler notifies AwaitingResultsStateHandler when payload processed
   - Eliminates polling overhead
   - Reduces completion detection latency
   - Better scalability

2. **Clarify component responsibilities:**
   - Single component authoritative for completion detection
   - Clear separation between message processing (ResultHandler) and state management (StateMachine)
   - Remove circular dependencies

3. **Add distributed locking for multi-instance deployment:**
   - Transaction ownership mechanism
   - Crash recovery coordination
   - Prevent duplicate processing

4. **Design comprehensive error handling:**
   - Idempotent operations where possible
   - Retry with exponential backoff
   - Dead letter queues for unrecoverable failures
   - Alerting and monitoring

5. **Add message ordering guarantees or handling:**
   - Ensure header arrives before payloads, OR
   - Handle out-of-order gracefully with buffering/retry

---

## **Priority Questions Requiring Immediate Clarification:**

1. **Is ResultHandler singleton or per-transaction?** (Affects entire architecture)
2. **How are messages routed to correct StateMachine instance?** (Core routing mechanism)
3. **What triggers StateMachine creation?** (Transaction initialization)
4. **How does AwaitingResultsStateHandler know when to check for completion?** (Polling vs. events)
5. **What happens if payload arrives before header?** (Message ordering)

---

## **Implementation Details & Design Patterns (Lower Priority)**

These questions explore code-level techniques, patterns, and design considerations. While important for interview preparation and deep technical understanding, they are less critical than the architectural questions above.

### ID-1. **Autofac Configuration & Lifetime Scopes**
**Questions:**
- How are components registered in Autofac modules?
- What lifetime scopes are used for different components?
  - InstancePerLifetimeScope for transaction-specific components?
  - SingleInstance for shared infrastructure?
- How is the per-transaction lifetime scope created and managed?
- What is the module build sequence? (queues → consumers → handlers → state machine)
- Why Autofac over built-in .NET DI container?

**Topics to Document:**
- Autofac module registration patterns
- Lifetime scope hierarchy and nesting
- Component disposal and cleanup via scope disposal
- Dependency resolution order

---

### ID-2. **Multi-threading & Concurrency Patterns**
**Questions:**
- How are multiple messages processed concurrently within a transaction?
- Are message handlers (HandleHeaderMessage, HandlePayloadMessage) thread-safe?
- What synchronization primitives are used? (locks, semaphores, concurrent collections)
- How is TransactionContext protected from concurrent updates?
- Are there thread pools for message processing?

**Topics to Document:**
- Thread safety guarantees for shared state
- Synchronization mechanisms and locking strategy
- Concurrent message processing design
- Race condition prevention techniques

---

### ID-3. **Message Handling Implementation Patterns**
**Questions:**
- How are consumer callbacks registered and invoked?
- How is message deserialization handled? (Protocol Buffers)
- How is message correlation tracked across header/payload queues?
- What happens to malformed or unparseable messages?

**Topics to Document:**
- Callback registration patterns
- Message deserialization and validation
- Error handling for corrupt messages
- Message correlation techniques

---

### ID-4. **Idempotency Implementation Techniques**
**Questions:**
- How is KV Store ordinal checking implemented to prevent duplicates?
- What happens if two threads check the same ordinal simultaneously?
- Are KV Store operations atomic? Using eTags for optimistic concurrency?
- How are partial failures handled? (blob uploaded but KV write fails)

**Topics to Document:**
- Idempotent operation patterns
- Race condition handling in duplicate checks
- Atomic operations and transactional boundaries
- Retry logic and compensation

---

### ID-5. **Error Handling & Logging Strategies**
**Questions:**
- What exceptions are caught and handled at each layer?
- What is logged at each stage of processing?
- How are errors propagated up the stack?
- What monitoring/observability hooks exist?
- How are errors surfaced to operators?

**Topics to Document:**
- Exception handling patterns per component
- Logging strategy (what, when, at what level)
- Monitoring and metrics collection
- Alerting on critical failures

---

### ID-6. **State Machine Pattern Implementation Details**
**Questions:**
- How is IStateHandler interface designed?
- How are state transitions triggered? (OnEnter completion → automatic transition)
- How does StateMachine know which handler to invoke next?
- Can state handlers fail and trigger rollback?

**Topics to Document:**
- State handler interface contract
- State transition mechanics
- State handler invocation lifecycle
- Error handling within state handlers

---


**Note:** Direct Azure SDK usage was not part of this project. All Azure access (Blob Storage, Table Storage, etc.) was performed via Varonis internal wrapper libraries. No code-level experience with Azure SDK configuration, connection management, or retry policies.

### ID-8. **Performance Optimization Techniques**
**Questions:**
- How is blob upload performance optimized? (streaming, chunking, parallel upload)
- Are KV Store operations batched?
- How is memory managed for large payloads?
- Are there any caching strategies?

**Topics to Document:**
- Blob upload optimization strategies
- Batch operations for KV Store
- Memory efficiency techniques
- Performance monitoring and profiling

---

## **Next Steps:**

1. Review and answer each question in this document
2. Update architecture and LLD documents to resolve inconsistencies
3. Create sequence diagrams for key flows
4. Document error handling and recovery procedures
5. Create KV Store schema reference
6. Add concurrency and synchronization documentation
7. Conduct architecture review session with team
8. Document implementation details and design patterns (lower priority)
