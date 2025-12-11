# BigResultHandler - Interview Questions & Answers

This document addresses common questions that interviewers and reviewers may have about the BigResultHandler system design.

---

<details>
<summary><h3>1. How is it handling crashes?</h3></summary>

The system uses a state machine architecture designed for crash recovery and process continuity. When a crash occurs:
- The current state (either "Awaiting Results" or "Sending Completion") is persisted to durable storage
- Transaction progress including received message ordinals is stored
- Upon recovery, the process reads the persisted state
- Processing resumes from the last known state without data loss
- Partially received message series can continue from where they left off

> **Implementation detail:** The specific persistence mechanism (KV Store, database, or dedicated state store) needs to be defined based on requirements for consistency, performance, and recovery time objectives.

</details>

<details>
<summary><h3>2. How much data can it handle?</h3></summary>

The system is designed to handle unlimited result sizes:
- Payload messages are chunked into 250MB segments in the Payload Queue
- Azure Blob Storage handles the final assembled data with no practical size limit
- Each message series can contain an arbitrary number of messages
- The KV Store only holds metadata and references (blob URIs), not the actual data
- Memory footprint remains constant regardless of total data size

</details>

<details>
<summary><h3>3. How is its memory management?</h3></summary>

The system employs streaming and reference-based architecture:
- Messages are processed as they arrive, not held in memory
- Only metadata (Transaction ID, series type, ordinal IDs, counts) is kept in memory during processing
- Large payload data flows through to Azure Storage without full in-memory accumulation
- The KV Store reference pattern ensures only pointers (blob URIs) are stored, not full data
- Memory footprint per transaction is bounded by metadata size, not payload size

> **Note:** Cleanup strategy for completed transactions (memory, KV Store, Azure Storage) needs to be defined based on retention requirements (see Question 7).

</details>

<details>
<summary><h3>4. How are messages arriving together handled?</h3></summary>

The system handles concurrent message arrival through:
- Each message contains Transaction ID for proper routing
- Messages from different transactions are processed independently
- Within a transaction, messages can arrive in any order
- The Result Handler tracks ordinal IDs per series type
- Completion is determined by receiving all ordinals (1 to Total Count) regardless of arrival order

> **Implementation details needed:** Concurrency control mechanisms, state transition atomicity guarantees, and locking strategies need to be defined.

</details>

<details>
<summary><h3>5. What happens if the process is recovering twice from the same state?</h3></summary>

Idempotent recovery requires careful design:
- **State 1 (Awaiting Results):** Re-reading persisted tracking data allows continuing to wait for missing messages. Duplicate message handling needs to check received ordinals.
- **State 2 (Sending Completion):** Need mechanisms to detect if notification was already sent.

**Implementation details needed:**
- Completion flag/status tracking mechanism
- Azure Storage conditional write strategy
- Duplicate notification prevention approach
- Recovery timestamp tracking

</details>

<details>
<summary><h3>6. How is the data arranged in the KV store?</h3></summary>

The KV Store needs to track:
- Transaction ID (primary key)
- State machine state (if persisted to KV Store)
- Header information defining expected series types
- Series tracking: which ordinals have been received per series type
- Blob URI reference after Azure upload
- Timestamps for timeout management

**Example structure (to be defined):**
```
Transaction_ID/
  ├── header: { expected_series_types: [...] }
  ├── series_tracking/
  │     ├── series_type_1: { total_count: N, received_ordinals: [1,2,5,...] }
  │     └── series_type_2: { total_count: M, received_ordinals: [1,3,...] }
  ├── blob_uri: "https://storage.azure.com/..."
  └── timestamps: { created, last_updated }
```

> **Note:** Actual schema depends on chosen KV Store technology and access patterns.

</details>

<details>
<summary><h3>7. Who makes sure garbage is cleaned from KV and Azure? And in case a transaction is abandoned?</h3></summary>

Garbage collection strategy needs to be defined:

**Normal Cleanup (to be designed):**
- Trigger: After successful notification
- What to clean: KV Store entries, temporary data
- Azure blob retention policy needs definition
- Completed transaction metadata retention requirements

**Abandoned Transactions (to be designed):**
- Detection mechanism: timeout-based or monitoring service
- Cleanup trigger and scheduling
- KV Store entry removal strategy
- Azure blob cleanup for partial uploads
- Alerting and logging requirements

**Key questions to answer:**
- When and how are completed transactions cleaned?
- Who is responsible for cleanup (Result Notifier, background service, lifecycle policies)?
- What is the retention period for completed vs. abandoned transactions?
- How are orphaned resources detected and cleaned?

</details>

<details>
<summary><h3>8. Does a transaction have a timeout?</h3></summary>

Transactions should have timeout mechanisms to prevent indefinite waiting:

**Timeout types to consider:**
- **Awaiting Results Timeout:** Maximum time to wait for all expected messages
- **Sending Completion Timeout:** Maximum time to complete upload and notification

**Implementation needs to define:**
- Timeout values (configurable per transaction type?)
- Where timeout tracking occurs
- Who monitors for timeout (background service, Result Handler itself)
- Actions on timeout: state changes, cleanup, notifications
- Whether timeouts allow retry or mark transaction as failed/abandoned

</details>

<details>
<summary><h3>9. How does the system handle duplicate messages?</h3></summary>

Duplicate handling leverages the ordinal ID design:
- Each message has a unique ordinal ID within its series (as specified in architecture)
- The Result Handler can check if an ordinal has already been received

**Implementation needs to define:**
- Where duplicate detection occurs (in-memory cache, KV Store lookup)
- What happens to duplicate messages (ignore, acknowledge, log)
- How to achieve exactly-once processing semantics
- Whether RabbitMQ acknowledgment strategy affects this

</details>

<details>
<summary><h3>10. What are the failure modes and recovery strategies?</h3></summary>

Key failure modes to consider:

**Queue Consumer Failures:**
- Messages remain in queue (RabbitMQ behavior)
- Recovery: Consumer re-initialization

**Result Handler Failures:**
- State machine design enables recovery
- RabbitMQ message redelivery behavior applies

**Azure Storage Failures:**
- Upload failures during State 2
- Need: retry strategy, error handling, operator alerts

**KV Store Failures:**
- Cannot persist or read state/metadata
- Impacts crash recovery capability

**Implementation needs to define:**
- Retry policies for each failure type
- Fallback strategies
- Alerting and monitoring
- High availability requirements

</details>

<details>
<summary><h3>11. How does the system scale horizontally?</h3></summary>

Scaling considerations for the architecture:

**Potential scaling approaches:**
- Multiple Result Handler instances processing different transactions
- RabbitMQ consumer distribution across instances
- Consumers (Header/Payload) can potentially scale independently
- Azure Storage supports parallel operations

**Design challenges to address:**
- How to partition transactions across instances
- Ensuring single Result Handler processes each transaction (no split-brain)
- KV Store concurrent access patterns and locking strategy
- State machine state coordination across instances
- Message routing and Transaction ID affinity

</details>

<details>
<summary><h3>12. What monitoring and observability is needed?</h3></summary>

Key observability requirements:

**Per Transaction Metrics:**
- State transitions and current state
- Message counts and series completion progress
- Processing duration
- Transaction lifecycle timestamps

**System-wide Metrics:**
- Active transactions count
- Throughput rates
- Error and timeout rates
- Queue depths

**Infrastructure Metrics:**
- Memory usage per component
- KV Store latency and operation rates
- Azure Storage upload performance

**Operational Needs:**
- Alert conditions and thresholds
- Dashboard requirements
- Distributed tracing approach
- Log aggregation strategy

</details>

<details>
<summary><h3>13. How is ordering guaranteed within a message series?</h3></summary>

Ordering capabilities from the architecture:
- Messages include ordinal IDs (1 to Total Count) per series
- Messages can arrive out of order
- Result Handler tracks which ordinals have been received

**Implementation needs to define:**
- Whether ordering matters for processing
- If messages need to be buffered and reordered
- How to reconstruct order when needed (sort by ordinal)
- Whether Azure Storage upload requires ordering
- Memory management for out-of-order message buffering

</details>

<details>
<summary><h3>14. What happens if the header message arrives after payload messages?</h3></summary>

Late header arrival is a design consideration:

**Scenario:**
- Payload messages may arrive before the header
- Result Handler doesn't know which series to expect without the header

**Design needs to address:**
- Should early payload messages be buffered? Where (memory, KV Store)?
- What is the maximum buffer capacity?
- Is there a timeout for header arrival?
- What happens to orphaned payload messages without headers?
- Should there be a dead letter queue for unmatched payloads?

> **Note:** This depends on whether the system guarantees header-first delivery or must handle any arrival order.

</details>

<details>
<summary><h3>15. How are errors communicated to upstream systems?</h3></summary>

Error communication requirements:

**What needs to be communicated:**
- Transaction ID
- Error type and details
- Failure stage (which state, which operation)
- Timestamp

**Communication mechanisms to define:**
- Error notification queue/topic
- Status query API for upstream systems
- Error persistence in KV Store
- Retry vs. terminal failure distinction
- Operator notification and dashboards

> **Note:** Error handling strategy needs to be defined based on upstream system integration requirements.

</details>
