# BigResultHandler - Architecture

## Overview
The BigResultHandler system manages unlimited-size results by coordinating header and payload data received through separate RabbitMQ queues, ensuring complete result assembly before notification.

### Component Structure

**ResultService**: Top-level service that manages the overall system
- Uses IOC Container (e.g., Autofac) for dependency injection
- Registers shared dependencies: Queues, Azure Storage, KV Store (Azure Table Storage)
- Creates ResultManager instances for processing

**ResultManager**: Orchestrates transaction processing
- Created by ResultService via IOC Container with injected dependencies
- Instantiates a StateMachine for each specific TransactionID
- Calls `stateMachine.Run()` to begin processing
- Each transaction gets its own StateMachine instance

**StateMachine**: Per-transaction state coordinator
- Instantiated specifically for a single TransactionID
- Contains state handlers for the two states
- Maintains current state (initially State 1: Awaiting Results)
- Runs continuously, awaiting all messages for its transaction
- Enables recovery from crashes by persisting and restoring state

**Note on Terminology:** Throughout this document, "KV Store" refers to **Azure Table Storage**, a NoSQL key-value store that organizes data using PartitionKey (TransactionID) and RowKey (composite key of SeriesType and Ordinal). This allows efficient querying of all entries within a transaction namespace.

**Message Serialization:** All RabbitMQ messages (Header Queue, Payload Queue, and Notification Queue) use **Protocol Buffers (protobuf)** for serialization. The `.proto` schema definitions are shared across all participants in the flow, ensuring consistent message format and structure across producers and consumers.

## High-Level Architecture

See the architecture diagram: [architecture.mermaid](architecture.mermaid)

To view the diagram, open the `.mermaid` file in VS Code and use `Ctrl+Shift+P` → "Mermaid: Preview"

## Key Components

### 1. **Header Consumer**
- Listens to Header Queue
- Receives result headers with Transaction ID and payload series types
- Forwards header messages to ResultManager

### 2. **Payload Consumer**
- Listens to Payload Queue (handles 250MB messages)
- Receives payload messages with Transaction ID, series type, message ordinal, and total message count in the series
- Forwards payload messages to ResultManager

### 3. **ResultManager**
- Orchestrates processing for transactions
- Created by ResultService with injected dependencies (Queues, Storage, KV Store)
- Instantiates StateMachine for each TransactionID
- Calls `stateMachine.Run()` to start transaction processing
- Manages lifecycle of StateMachine instances

### 4. **StateMachine**
- Per-transaction state coordinator instantiated with specific TransactionID


- Begins in State 1 (Awaiting Results) and awaits all expected messages
- Contains state handler instances that process messages based on current state
- Interprets header to determine expected message types and series
- Coordinates different payload message type series (which are unaware of each other)
- Uses header as the binder to know which message series to expect
- Tracks completion of message series using metadata in each payload message:
  - **Series Type**: Identifies which series the message belongs to
  - **Total Count**: Total number of messages in the series
  - **Ordinal ID**: Sequential position of the message within its series
- Determines series completion when all ordinal IDs (1 to Total Count) are received
- **Uploads each payload message immediately to Azure Storage as a separate blob** (as messages arrive, one-by-one)
- Receives blob URI from Azure Storage for each uploaded message
- Writes to KV Store for each message:
  - **Namespace**: `TransactionID`
  - **Key**: `<SeriesType, Ordinal>`
  - **Value**: `BlobURI`
- Azure Storage is agnostic to series types and ordinals - it only stores raw data blobs

### 5. **Result Assembler**
- Retrieves all blob URIs for a transaction from KV Store by querying the Transaction ID namespace
- Accesses KV Store namespace `TransactionID` and retrieves all key-value pairs:
  - Each entry has key `<SeriesType, Ordinal>` and value `BlobURI`
- Collects all tuples: `[(SeriesType, Ordinal, BlobURI), ...]`
- Organizes the blob URI references by series type
- Prepares structured metadata for the Result Notifier
- Does not assemble actual data - only organizes the references to where data is stored

### 6. **Result Notifier**
- Receives organized blob URIs from Result Assembler
- Publishes notification to Notification Queue containing:
  - Transaction ID
  - All blob URIs organized by series type and ordinal
- Client receives notification and can:
  - Access all pieces of every series type
  - Sort them by ordinal within each series
  - Retrieve actual data from Azure Storage using the blob URIs
  - Restore the complete result by assembling data from all blobs

### 7. **KV Store (Key-Value Store)**
- External storage system with namespace support
- Organized by Transaction ID namespaces
- Data structure:
  - **Namespace**: `TransactionID`
  - **Key**: `<SeriesType, Ordinal>`
  - **Value**: `BlobURI`
- Each payload message creates one entry within the Transaction ID namespace
- Result Assembler queries a Transaction ID namespace to retrieve all key-value pairs
- Provides metadata for organizing blob URI references

### 8. **Azure Storage**
- External blob storage
- Stores raw payload data from each message as separate blobs
- **Completely agnostic to series types, ordinals, and transaction structure**
- Simply stores data and returns blob URIs
- All semantic meaning (which blob belongs to which series/ordinal) is maintained in KV Store
- Enables handling of unlimited result sizes through separate blob storage per message

**Streaming Upload Implementation:**
- Payload data is streamed directly from RabbitMQ to Azure Blob Storage without loading the entire message into memory
- Uses stream-based upload with fixed buffer size (e.g., 4-8MB)
- Memory footprint remains constant regardless of message size (250MB messages use same memory as smaller messages)
- Azure Block Blob API supports staged uploads: read chunk → upload as block → commit block list
- This enables processing 250MB payload messages with minimal memory overhead

## State Machine

Each transaction is processed by its own StateMachine instance with two states:

### **State Machine Implementation**

The StateMachine is implemented using the **State Pattern** with the following design:

**Instantiation and Lifecycle:**
- StateMachine is instantiated by ResultManager for a specific TransactionID
- Each StateMachine instance is dedicated to one transaction
- Contains its own set of state handler instances
- Begins in State 1 (Awaiting Results) when `Run()` is called
- Processes messages for its transaction until completion

**State Handler Objects**: Each state is implemented as a separate handler object within the StateMachine
- `AwaitingResultsStateHandler`: Handles State 1 operations
- `SendingCompletionStateHandler`: Handles State 2 operations
- State handlers are created and owned by the StateMachine instance

**Context Object**: A context object is maintained throughout state transitions and is accessible from every state handler
- Contains the **Transaction ID** for identifying the transaction being processed
- Passed to each state handler when transitioning between states
- Provides continuity and state information across the state machine lifecycle

**Shared Resources**: Multiple StateMachine instances can exist in the process simultaneously to handle concurrent transactions
- **KV Store**: Shared among all StateMachine instances (injected via IOC)
- **Azure Storage**: Shared among all StateMachine instances (injected via IOC)
- Each StateMachine instance operates on its own transaction data using the unique Transaction ID from the context object

**Data Isolation**: State handlers interact with their relevant data in KV Store and Azure Storage by:
- Using the **Transaction ID** from the context object as the key
- Each transaction's data is isolated by its unique Transaction ID
- Multiple StateMachines can safely coexist without interference

### **State 1: Awaiting Results**
- Component is waiting for all expected messages for a transaction
- Receives header message defining expected message types
- Receives payload messages from different type series
- Each payload message contains:
  - Series Type identifier
  - Total Count of messages in that series
  - Ordinal ID (position within the series)
- Tracks received ordinal IDs per series type
- Determines series completion when all ordinal IDs (1 to Total Count) are received
- Tracks completion of all message series specified in the header
- State persisted to KV Store for crash recovery

### **State 2: Sending Completion Message**
- All expected messages have been received
- Result is uploaded to Azure Storage
- Blob URI is written to KV Store
- Completion notification is sent to Notification Queue
- State persisted to KV Store for crash recovery

### **State Persistence Rationale**
The state machine design enables:
- **Crash Recovery**: If the process crashes, it can resume from the last persisted state
- **Continuity**: Processing continues where it left off without data loss
- **Reliability**: State is persisted to KV Store at key transition points
- **Concurrency**: Multiple transactions can be processed simultaneously by different state machine instances

## Data Flow

1. **Header Queue** receives header message with Transaction ID and expected message type series
2. **Payload Queue** receives payload messages from different type series, each with Transaction ID
3. **Header Consumer** consumes header message and forwards to Result Handler
4. **Payload Consumer** consumes payload messages and forwards to Result Handler (unaware of headers or coordination)
5. **Result Handler** receives header and interprets which message type series to expect
6. **Result Handler** receives payload messages from different series and enters **State 1: Awaiting Results**
7. For **each payload message received**, **Result Handler**:
   - Immediately uploads the message data to **Azure Storage** as a separate blob
   - Receives blob URI from **Azure Storage**
   - Writes to **KV Store** in namespace `TransactionID` with key `<SeriesType, Ordinal>` and value `BlobURI`
8. **Result Handler** coordinates the different message series using header as the binder
9. **Result Handler** tracks completion of all message series specified in the header
10. When all expected messages are received, **Result Handler** transitions to **State 2: Sending Completion Message**
11. **Result Assembler** queries **KV Store** namespace `TransactionID` to retrieve all key-value pairs, forming tuples: `[(SeriesType, Ordinal, BlobURI), ...]`
12. **Result Assembler** organizes blob URIs by series type and forwards to **Result Notifier**
13. **Result Notifier** publishes notification to **Notification Queue** containing:
    - Transaction ID
    - All blob URIs organized by series type and ordinal
14. **Client** receives notification and retrieves actual data from Azure Storage using the blob URIs

## Transaction ID Binding

- Every message (header and payload chunks) contains the same Transaction ID
- Transaction ID is the key for coordinating and assembling results
- Ensures correct matching of headers with their corresponding payloads


## Scalability Considerations

- Payload chunking at 250MB allows handling unlimited result sizes
- Component can handle multiple concurrent transactions
- Storage must support concurrent access by Transaction ID

### Service-Level Scaling and Queue Pair Isolation

In this architecture, scaling is achieved by launching additional service instances. Each service instance manages its own set of state machines, KV store client, and RabbitMQ consumers. For each transaction, the remote service is provided with a unique header/payload queue pair, ensuring that only the owning service instance receives messages for the transactions it initiated.

**Strengths:**
- Service-level scaling allows horizontal expansion by spinning up more service instances.
- Queue pair isolation ensures that messages for a transaction are routed only to the correct service instance.
- KV store namespace isolation (by Transaction ID) prevents data collision between transactions.

**Potential Holes / Considerations:**
1. **Orphaned Transactions / Instance Failure**: If a service instance fails, its header/payload queues may become orphaned, and in-flight or future messages for those transactions may be lost or stuck.
  - *Mitigation*: Implement queue monitoring and orphaned queue cleanup. Consider a mechanism for reassigning or draining orphaned queues if an instance dies unexpectedly.
2. **Resource Utilization**: Creating a queue pair per transaction or per instance can lead to a large number of queues, which may be a management and resource challenge.
  - *Mitigation*: Use shared queues with message affinity (partitioning by Transaction ID) if possible, or implement queue lifecycle management.
3. **Remote Service Coupling**: The remote service must be correctly configured with the header/payload queue pair for each transaction. Misconfiguration or race conditions could result in misrouted messages.
  - *Mitigation*: Ensure robust handshaking and validation when providing queue information to the remote service.
4. **Scaling Granularity**: Scaling at the service instance level is coarse-grained. Uneven load distribution may occur if some instances handle more or larger transactions than others.
  - *Mitigation*: Consider finer-grained scaling or dynamic transaction assignment if this becomes a bottleneck.
5. **KV Store Consistency**: If multiple service instances ever need to access or update the same transaction’s state (e.g., for failover or recovery), the KV store must support strong consistency and optimistic concurrency.
  - *Mitigation*: Use atomic operations and handle concurrency conflicts in the KV store.

**Summary:**
The architecture is robust if each transaction is strictly bound to a single service instance for its lifetime, and if queue and instance lifecycles are tightly managed. The main risks are around orphaned resources and ensuring that no messages are lost if an instance fails. If more dynamic scaling or failover is desired, additional mechanisms for transaction ownership reassignment and safe queue draining are required.

## Why Two Queues: Header and Payload?

The system uses two separate RabbitMQ queues—one for headers and one for payloads—instead of a single queue for all messages. This design choice is motivated by several key factors:

1. **Separation of Concerns**: Headers (metadata, control info) and payloads (large result data or chunk references) have different lifecycles, sizes, and processing needs. Splitting them allows each to be handled optimally.
2. **Performance and Throughput**: Payload messages are often much larger and slower to process. By separating them, header processing (which may be lightweight and latency-sensitive) is not blocked by large payloads.
3. **Scalability**: You can scale consumers for headers and payloads independently, allocating more resources to whichever is the bottleneck.
4. **Reliability and Recovery**: If a payload message fails or is delayed, it doesn’t block header processing or vice versa. This improves fault isolation and recovery.
5. **Ordering and Idempotency**: Headers may need strict ordering or deduplication, while payloads may not. Separate queues make it easier to enforce the right semantics for each.

This separation enables the system to efficiently process high-throughput, large-scale results with minimal coupling between control flow and data transfer.

### Pushbacks and Challenges: Two-Queue Design

Below are common challenges or pushbacks to the two-queue (header/payload) design, along with responses:

**1. Both message types can be handled in one queue by examining a type header—this isn’t much overhead.**
- True, a single queue can work by inspecting a type field and routing to the correct handler. The overhead is minimal. The main advantage of two queues is independent scaling and prioritization: header processing (often latency-sensitive) won’t be delayed by large payloads. For simple flows or low throughput, a single queue is valid.

**2. A transaction is comprised of both header and payloads—there’s no meaning in handling new headers while their corresponding payloads are blocked or delayed.**
- Correct, if payloads are delayed, processing new headers may not help. Separate queues are most beneficial when header and payload arrival rates differ, or when you want to avoid large payloads blocking lightweight header processing. If both are tightly coupled, a single queue can be simpler.

**3. How do separate queues make it easier to enforce ordering or deduplication?**
- Separate queues don’t inherently enforce ordering or deduplication. They make it easier only if you want different policies for headers vs. payloads (e.g., strict ordering for headers, relaxed for payloads). If you need global ordering or deduplication, you must implement it at the consumer or application level, regardless of queue separation.

---

**Practical/Historical Reason in Result Handler:**

In the actual Result Handler implementation, the two-queue design is rooted in system evolution:
- The header queue existed before the addition of big message functionality and still serves other flows where only header messages are passed for transactions.
- The big payload queue was introduced to support new functionality for handling large disk spaces, broken down into many big payload messages.
- For legacy transactions, every transaction expects only one header message—there is no series of messages.

This separation allows legacy flows to continue using the header queue, while the payload queue enables scalable processing of large results.
