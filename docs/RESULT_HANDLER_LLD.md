# Result Handler - Low Level Design

> **Note:** This document is partly Copilot-generated content based on high-level architecture discussions.

## Table of Contents

- [Overview](#overview)
- [Class Structure](#class-structure)
  - [ResultHandler](#resulthandler)
- [Implementation Details](#implementation-details)
  - [HandleHeaderMessage Implementation](#handleheadermessage-implementation)
  - [HandlePayloadMessage Implementation](#handlepayloadmessage-implementation)
- [Storage and Persistence Services](#storage-and-persistence-services)
  - [AzureStorageService](#azurestorageservice)
  - [KVStoreService](#kvstoreservice)
- [Message Structures](#message-structures)
  - [HeaderMessage](#headermessage)
  - [PayloadMessage](#payloadmessage)
- [Processing Flow](#processing-flow)
  - [Transaction Initialization](#transaction-initialization)
  - [Header Message Processing](#header-message-processing)
  - [Payload Message Processing](#payload-message-processing)
  - [Completion Processing](#completion-processing)
  - [Crash Recovery](#crash-recovery)
- [Concurrency Considerations](#concurrency-considerations)
  - [Multi-Instance Processing](#multi-instance-processing)
  - [Thread Safety](#thread-safety)
- [Message Handling Patterns: Consumer Thread Release, Memory Efficiency, and Crash Safety](#message-handling-patterns-consumer-thread-release-memory-efficiency-and-crash-safety)

## Overview
This document provides the low-level design for the **ResultHandler** class, which handles message processing logic through callback methods.

**ResultHandler** processes messages arriving from RabbitMQ queues by:
- Registering callback methods (`HandleHeaderMessage` and `HandlePayloadMessage`) with queue consumers in its constructor
- Processing header messages to initialize transaction contexts
- Processing payload messages by streaming data to Azure Blob Storage and tracking completion
- Notifying the state machine when all expected messages for a transaction have been received

The entire message processing flow is managed through invocations of these two callback methods.

For the overall system architecture including ResultService, ResultManager, and StateMachine, see ARCHITECTURE.md.

## Class Structure

### ResultHandler
Handles message processing logic through callback methods registered with queue consumers.

**Dependencies (Injected via IOC):**
- `headerQueueConsumer: IQueueConsumer` - Consumes messages from Header Queue
- `payloadQueueConsumer: IQueueConsumer` - Consumes messages from Payload Queue
- `storageService: IAzureStorageService` - Azure Blob Storage operations
- `kvStoreService: IKVStoreService` - Azure Table Storage operations

**Note:** The queue consumers (headerQueueConsumer and payloadQueueConsumer) are instantiated by ResultService when it creates the StateMachine and ResultHandler. The ResultHandler has no awareness of the StateMachine - it is injected INTO the state machine.

**Constructor:**
```csharp
public ResultHandler(
    IQueueConsumer headerQueueConsumer,
    IQueueConsumer payloadQueueConsumer,
    IAzureStorageService storageService,
    IKVStoreService kvStoreService)
{
    this.headerQueueConsumer = headerQueueConsumer;
    this.payloadQueueConsumer = payloadQueueConsumer;
    this.storageService = storageService;
    this.kvStoreService = kvStoreService;
    
    // Register callback handlers with queue consumers
    headerQueueConsumer.OnMessageReceived += HandleHeaderMessage;
    payloadQueueConsumer.OnMessageReceived += HandlePayloadMessage;
}
```

**Methods:**
- `HandleHeaderMessage(message: HeaderMessage): void`
  - Callback invoked when header message arrives from Header Queue
  - Deserializes header message (Protocol Buffers)
  - Initializes transaction metadata in KV Store
  
- `HandlePayloadMessage(message: PayloadMessage): void`
  - Callback invoked when payload message arrives from Payload Queue
  - Deserializes payload message metadata (Protocol Buffers)
  - Streams payload data to Azure Blob Storage
  - Writes blob mapping to KV Store (PartitionKey: TransactionID, RowKey: SeriesType:Ordinal)

---

**Note:** TransactionContext and SeriesInfo are managed by the State Machine and State Handlers. See the State Machine documentation for details on these components.

---

## Implementation Details

### HandleHeaderMessage Implementation
```csharp
private void HandleHeaderMessage(HeaderMessage message) {
    Log.Info($"Received header message for transaction {message.TransactionId}");
    
    // Deserialize header message (Protocol Buffers)
    var header = ProtoBuf.Serializer.Deserialize<HeaderMessageProto>(message.Body);
    
    // Write transaction metadata to KV Store
    kvStoreService.WriteTransactionMetadata(
        header.TransactionId,
        header.SeriesTypes,
        header.SeriesCounts);
    
    Log.Info($"Initialized transaction {header.TransactionId} with {header.SeriesTypes.Count} series types");
}
```

### HandlePayloadMessage Implementation
```csharp
private void HandlePayloadMessage(PayloadMessage message) {
    Log.Info($"Received payload message for transaction {message.TransactionId}, " +
             $"series {message.SeriesType}, ordinal {message.Ordinal}");
    
    // Deserialize payload message metadata (Protocol Buffers)
    var payload = ProtoBuf.Serializer.Deserialize<PayloadMessageProto>(message.Body);
    
    // Stream payload data to Azure Blob Storage
    // Note: Streaming avoids loading full 250MB message into memory
    string blobUri;
    using (var dataStream = message.GetPayloadStream()) {
        blobUri = storageService.UploadBlobStream(
            payload.TransactionId, 
            payload.SeriesType, 
            payload.Ordinal, 
            dataStream);
    }
    
    Log.Info($"Uploaded blob for transaction {payload.TransactionId}, " +
             $"series {payload.SeriesType}, ordinal {payload.Ordinal} -> {blobUri}");
    
    // Write blob mapping to KV Store
    kvStoreService.WriteBlobMapping(
        payload.TransactionId, 
        payload.SeriesType, 
        payload.Ordinal, 
        blobUri);
    
    Log.Info($"Wrote blob mapping for transaction {payload.TransactionId}");
}
```

---

## Storage and Persistence Services

### AzureStorageService
Handles blob upload operations with streaming support.

**Methods:**
- `UploadBlobStream(transactionId: string, seriesType: string, ordinal: int, messageStream: Stream): string`
  - Streams data directly from RabbitMQ message to Azure Blob Storage
  - Uses fixed buffer size (e.g., 4-8MB) for streaming
  - Memory usage is constant regardless of message size
  - Implementation approaches:
    - **Stream-to-stream**: Read from message stream → write to blob upload stream
    - **Block blob staged upload**: Read chunk → upload as block → commit block list
  - Generates unique blob name: `{transactionId}/{seriesType}/{ordinal}.blob`
  - Returns blob URI after successful upload
  - Handles upload retries and errors
  
**Streaming Upload Pattern:**
```csharp
// Pseudo-code implementation
public string UploadBlobStream(string transactionId, string seriesType, 
                                int ordinal, Stream messageStream) {
    string blobName = $"{transactionId}/{seriesType}/{ordinal}.blob";
    
    using (var blobStream = azureBlob.OpenWriteStream(blobName)) {
        byte[] buffer = new byte[4 * 1024 * 1024]; // 4MB buffer
        int bytesRead;
        
        while ((bytesRead = messageStream.Read(buffer, 0, buffer.Length)) > 0) {
            blobStream.Write(buffer, 0, bytesRead);
        }
    }
    
    return azureBlob.GetUri(blobName);
}
```

**Memory Efficiency:**
- 250MB message: uses ~4-8MB memory during upload
- No full message materialization in memory
- Enables processing unlimited message sizes

---

### KVStoreService
Manages Azure Table Storage operations for ResultHandler.

**Methods:**
- `WriteTransactionMetadata(transactionId: string, seriesTypes: List<string>, seriesCounts: Dictionary<string, int>): void`
  - PartitionKey: transactionId
  - RowKey: `__metadata__` (special key for transaction metadata)
  - Stores expected series types and counts
  
- `WriteBlobMapping(transactionId: string, seriesType: string, ordinal: int, blobUri: string): void`
  - PartitionKey: transactionId
  - RowKey: `{seriesType}:{ordinal}`
  - Stores blob URI as property
  
- `GetAllBlobMappings(transactionId: string): List<BlobMapping>`
  - Queries all entries with PartitionKey = transactionId
  - Excludes metadata rows
  - Returns list of (SeriesType, Ordinal, BlobURI) tuples

**Note:** Additional KV Store operations for transaction context tracking and series completion are managed by the State Machine. See State Machine documentation for details.

---

## Message Structures

**Serialization Format:** All RabbitMQ messages use **Protocol Buffers (protobuf)** for serialization. The structures below represent the deserialized message content. The actual `.proto` schema definitions are shared across all participants (producers, BigResultHandler, and consumers).

### HeaderMessage
```csharp
class HeaderMessage {
    string TransactionId;
    List<string> ExpectedSeriesTypes;
    DateTime Timestamp;
}
```

### PayloadMessage
```csharp
class PayloadMessage {
    string TransactionId;
    string SeriesType;
    int Ordinal;
    int TotalCount;
    Stream DataStream;  // Stream for streaming upload (not byte[] to avoid full load in memory)
    DateTime Timestamp;
}
```

---

## Processing Flow

### Transaction Initialization
1. ResultService receives transaction trigger (e.g., header message arrives)
2. ResultService instantiates StateMachine for the specific TransactionID
   - Passes ResultHandler instance to StateMachine (ResultHandler injected into state handlers)
3. ResultService instantiates ResultManager with StateMachine as dependency
4. ResultService calls `resultManager.ProcessTransaction()`
5. ResultManager calls `stateMachine.Run()`
6. StateMachine begins in State 1 (AwaitingResults), awaiting messages

### Header Message Processing
1. Header Consumer (instantiated by ResultService) receives header from Header Queue
2. Header Consumer invokes registered callback: `HandleHeaderMessage(headerMessage)`
3. ResultHandler.HandleHeaderMessage:
   - Deserializes header message (Protocol Buffers)
   - Writes transaction metadata to KV Store (expected series types and counts)
   - Logs transaction initialization
4. Returns (callback completes)
5. Header Consumer continues listening for more messages

### Payload Message Processing
1. Payload Consumer (instantiated by ResultService) receives payload from Payload Queue
2. Payload Consumer invokes registered callback: `HandlePayloadMessage(payloadMessage)`
3. ResultHandler.HandlePayloadMessage:
   - Deserializes payload message metadata (Protocol Buffers)
   - Streams payload data to Azure Blob Storage (using fixed 4-8MB buffer)
   - Receives blob URI after upload completes
   - Writes blob mapping to KV Store (PartitionKey: TransactionID, RowKey: SeriesType:Ordinal)
   - Logs completion
4. Returns (callback completes)
5. Payload Consumer continues listening for more messages

**Note:** Transaction and series completion tracking is handled by the State Machine and State Handlers, not by ResultHandler.

### Completion Processing
1. StateMachine transitions to SendingCompletionStateHandler
2. SendingCompletionStateHandler.OnEnter():
   - Calls `resultAssembler.AssembleBlobs(transactionId)` → queries KV Store for all blob mappings
   - Result Assembler organizes URIs by series type and ordinal
   - Receives organized blob data
   - Calls `notificationSender.SendNotification(transactionId, organizedData)` → publishes to Notification Queue
   - Marks transaction as complete in KV Store

### Crash Recovery
1. Process restarts after crash
2. ResultService recovery manager scans KV Store for incomplete transactions
3. For each incomplete transaction:
   - Calls `TransactionContext.LoadFromKVStore(transactionId)`
   - ResultService instantiates StateMachine for the recovered transaction (with ResultHandler injected into state handlers)
   - ResultService instantiates ResultManager with the StateMachine
   - StateMachine loads recovered context
   - Sets appropriate state handler based on context.CurrentState
   - ResultManager calls `stateMachine.Run()` to resume processing from last known state

---

## Concurrency Considerations

### Multi-Instance Processing
- Multiple StateMachine instances run concurrently (one per transaction)
- Each StateMachine is isolated by its TransactionID
- ResultManager coordinates multiple StateMachine instances
- Shared dependencies (KV Store, Azure Storage) injected via IOC are thread-safe
- KV Store uses TransactionId as PartitionKey for isolation
- Azure Storage uses TransactionId in blob path for isolation

### Thread Safety
- Each StateMachine instance is single-threaded per transaction
- Multiple StateMachine instances can process different transactions in parallel
- KVStoreService uses optimistic concurrency (eTags) for updates
- SeriesInfo.ReceivedOrdinals (HashSet) provides O(1) duplicate detection

### Message Ordering
- Messages can arrive in any order within a transaction
- Ordinal tracking ensures logical ordering is maintained
- No dependency on physical arrival order

---

## Error Handling

### Duplicate Messages
- SeriesInfo.AddOrdinal() returns false if ordinal already exists
- Log duplicate detection
- Ignore duplicate payload (already uploaded and tracked)

### Missing Messages
- Timeout mechanism tracks LastUpdateTimestamp
- Periodic scanner checks for stale transactions
- SeriesInfo.GetMissingOrdinals() reports gaps
- Alerting for transactions stuck in AwaitingResults state

### Storage Failures
- AzureStorageService implements retry logic with exponential backoff
- Transient failures retry up to N times
- Persistent failures trigger error notification
- Transaction remains in current state until resolved

### State Persistence Failures
- Critical operation - must succeed before proceeding
- Retry with exponential backoff
- Alert on persistent failures
- Circuit breaker pattern to prevent cascading failures

---

## Monitoring & Observability

### Metrics
- `transactions_in_progress` - Gauge of active transactions
- `transactions_completed` - Counter of completed transactions
- `messages_processed` - Counter by type (header/payload)
- `storage_upload_duration` - Histogram of upload times
- `state_transition_duration` - Histogram of state processing times
- `series_completion_time` - Time to complete each series
- `transaction_completion_time` - Total time from first message to notification

### Logging
- Transaction state transitions (with TransactionId)
- Message processing (TransactionId, SeriesType, Ordinal)
- Blob upload success/failure (with URIs)
- Duplicate detection events
- Error conditions with context
- Recovery operations

### Alerts
- Transactions stuck in AwaitingResults for > X minutes
- High rate of duplicate messages
- Storage upload failures
- State persistence failures
- Memory/CPU threshold violations

---

## Configuration

### Timeouts
- `TransactionTimeout` - Max time in AwaitingResults state (e.g., 30 minutes)
- `StorageUploadTimeout` - Max time for blob upload (e.g., 5 minutes)
- `StateTransitionTimeout` - Max time for state handler operations (e.g., 1 minute)

### Retry Policies
- `StorageUploadRetries` - Number of retry attempts (e.g., 3)
- `StorageUploadRetryDelay` - Initial retry delay (e.g., 1 second)
- `StateUpdateRetries` - Number of retry attempts for KV Store updates (e.g., 5)

### Resource Limits
- `MaxConcurrentTransactions` - Limit on active transactions (e.g., 1000)
- `MaxSeriesPerTransaction` - Limit on series types (e.g., 100)
- `MaxMessagesPerSeries` - Limit on ordinals (e.g., 10000)

---

## Message Handling Patterns: Consumer Thread Release, Memory Efficiency, and Crash Safety

### Goals
- **Release the consumer thread as soon as possible:** Prevent blocking RabbitMQ consumer threads to maximize throughput and avoid backpressure.

### Scale & Memory Notes (reference)

This section collects practical math, observations and recommended mitigations for large transactions and memory safety. Keep this as a reference when tuning prefetch, parallelism and upload behavior.

- Basic assumptions used for sizing examples in this document:
  - Result payload per file (text): ~1,000 characters; UTF-8 ≈ 4 bytes/char → ~4,000 bytes (~4 KB) per file result
  - Average file size (example): 10 MB

- Example A — 100 GB root
  - Files = 100 GB / 10 MB ≈ 10,000 files
  - Results total ≈ 10,000 × 4 KB = ~40,000,000 B ≈ 40 MB
  - Protobuf overhead per result is small (field tag + varint length), so serialized transaction is still ≈ ~40 MB
  - RabbitMQ message limit (250 MB) → whole transaction fits comfortably in a single 250 MB message (or split into a few messages intentionally)

- Example B — 100 TB root (scale-up)
  - Files = 100 TB / 10 MB ≈ 10,000,000 files
  - Results total ≈ 10,000,000 × 4 KB = ~40,000,000,000 B ≈ 40 GB per transaction
  - With 250 MB RabbitMQ max message size, the transaction would be split into ~40 GB / 250 MB ≈ 160 messages

- **Legacy System (100 MB max message size, single message per transaction):**
  - Max message size: 100 MB
  - Maximum results per transaction: 100 MB / 4 KB ≈ 25,000 results
  - **Maximum supported disk space: 25,000 files × 10 MB/file ≈ 250 GB per transaction**
  - This was the hard limit before the introduction of the big payload queue and multi-message transactions

- Key takeaway about in-process memory:
  - RabbitMQ .NET client delivers message bodies to consumers and those bodies are resident in memory while delivered. In-process queues (Subject, ActionBlock, Channel) only hold references to messages already loaded into memory by the client.
  - Therefore true memory control requires limiting how many messages the broker will deliver concurrently (RabbitMQ QoS/prefetch) or changing the delivery shape (small messages or external offload).

- ActionBlock defaults and tuning:
  - `ExecutionDataflowBlockOptions.MaxDegreeOfParallelism` default = 1 (single-threaded). You must explicitly set it to allow concurrent uploads.
  - Use `BoundedCapacity` to limit in-process queueing; pair `BoundedCapacity` with RabbitMQ `prefetch` for predictable memory bounds.

- Practical mitigations (recommended order):
  1. Set RabbitMQ consumer `prefetch` to the desired number of in-flight messages per consumer (P). Configure ActionBlock with `MaxDegreeOfParallelism = P` and `BoundedCapacity = P` (or slightly larger). This bounds message bodies in memory to ≈ P per consumer.
  2. If feasible, change producer behavior: upload large binary payloads to object storage and send small metadata messages (URIs) via RabbitMQ. Consumers stream directly from storage.
  3. If producer changes are impossible, use consumer-side temporary staging: immediately write incoming message body to disk (file-backed stream) and release the in-memory buffer, then process from disk.
  4. Use chunking of payloads (smaller message sizes) so that reasonable prefetch counts remain safe.

- Example capacity planning rule of thumb:
  - If each message can be up to 250 MB and you want to cap memory per consumer at ~2 GB, then set `prefetch = floor(2 GB / 250 MB) = 8`.
  - Scale overall throughput by running multiple consumer instances with the same per-consumer bounds.

- Notes on streaming uploads
  - Streaming upload implementations (read from incoming stream → write to blob stream in fixed-size buffers) keep memory usage per upload small (e.g., 4–8 MB buffers), but do not change the fact that the broker may have delivered N message bodies into memory unless `prefetch` is controlled.

Keep these notes with the ResultHandler LLD as a practical reference when tuning production deployments.
- **Efficient memory usage:** Avoid loading entire (potentially large) messages into memory; process data in a streaming fashion.
- **Crash and recovery safety:** Ensure that message processing is idempotent and can safely resume after a crash, with no data loss or duplication.

### Approaches Considered

#### 1. .NET Reactive Extensions (Rx) / Observable Pattern
- **Description:** Use IObservable/IObserver to model message streams, with subscription-based handlers.
- **Pros:** Elegant for composing asynchronous event streams, supports LINQ-style operators, good for complex event processing.
- **Cons:** Not ideal for backpressure or explicit thread management; less control over consumer thread release; can be overkill for simple message dispatch.

#### 2. .NET Dataflow (TPL Dataflow, e.g., ActionBlock)
- **Description:** Use ActionBlock or TransformBlock to process messages asynchronously, decoupling message arrival from processing.
- **Pros:** Explicit control over degree of parallelism, built-in support for async processing, easy to post messages and immediately release consumer thread, robust error handling.
- **Cons:** Slightly more boilerplate, but better fit for high-throughput, memory-efficient pipelines.

### Chosen Approach: TPL Dataflow (ActionBlock)

- **Implementation:**  
  - Each queue consumer registers a callback (e.g., `OnMessageReceived`) that posts the message to an ActionBlock.
  - The ActionBlock processes messages asynchronously, allowing the consumer thread to return immediately.
  - Payloads are processed using streaming APIs, reading from the message stream and writing to blob storage in fixed-size buffers.
  - After successful processing and persistence, the message is acknowledged (ACK) to RabbitMQ.
  - If processing fails, the message is not ACK’d and will be redelivered after recovery.

- **Benefits:**  
  - Consumer threads are never blocked by slow uploads or storage operations.
  - Memory usage is bounded and predictable, regardless of message size.
  - Processing is naturally parallelizable and can be tuned for system resources.
  - Crash recovery is safe: unacknowledged messages are redelivered, and idempotency is enforced via KV Store checks.

### Implementation Details

- **Callback Registration:**
  - `headerQueueConsumer.OnMessageReceived += (msg) => headerActionBlock.Post(msg);`
  - `payloadQueueConsumer.OnMessageReceived += (msg) => payloadActionBlock.Post(msg);`
- **ActionBlock Configuration:**
  - `new ActionBlock<PayloadMessage>(async msg => await HandlePayloadMessage(msg), new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = N })`
- **Streaming Upload:**
  - Use a fixed-size buffer to read from the message stream and write to blob storage, never loading the full message into memory.
- **Crash Handling:**
  - If the process crashes before ACK, RabbitMQ redelivers the message.
  - KV Store is checked before upload to ensure idempotency.
- **Error Handling:**
  - Exceptions in the ActionBlock are logged; failed messages are not acknowledged and will be retried.

### Why Not Rx?
- Rx is powerful for event composition but less suited for explicit thread management and backpressure in high-throughput, I/O-bound scenarios.
- TPL Dataflow provides more direct control over concurrency, memory, and error handling.

#### Why Rx Is Not Suitable for Explicit Thread Management

Rx (.NET Reactive Extensions) is designed for composing and reacting to asynchronous event streams, but it abstracts away explicit thread management and backpressure control. Here’s why it’s not ideal for this scenario:

1. **Threading is Abstracted, Not Explicitly Controlled:**
   - Rx lets you specify where (on which scheduler) observers run, but it doesn’t give you direct control over when the producer (the queue consumer) is allowed to release its thread.
   - If you subscribe to an observable, the producer may keep its thread busy until the observer completes processing, unless you explicitly offload work (e.g., with `ObserveOn` or `SubscribeOn`), which can be error-prone and hard to reason about in high-throughput, I/O-bound scenarios.

2. **No Built-in Backpressure or Bounded Queuing:**
   - Rx does not natively support backpressure (controlling the rate at which producers emit items based on consumer readiness).
   - If the consumer is slow, the producer may overwhelm the system with events, leading to unbounded memory growth or dropped messages.

3. **Immediate Callback Execution by Default:**
   - By default, Rx invokes observer callbacks synchronously on the producer’s thread unless you explicitly schedule otherwise.
   - This means the queue consumer thread could be blocked until the entire message is processed, which is exactly what you want to avoid.

4. **No Native Support for Asynchronous/Awaitable Processing:**
   - Rx is not designed for async/await patterns out of the box. Handling asynchronous message processing (e.g., streaming to blob storage) requires awkward workarounds, such as wrapping async code in `Task.Run`, which can lead to thread pool starvation or subtle bugs.

5. **TPL Dataflow is Designed for This Use Case:**
   - TPL Dataflow’s `ActionBlock` is built for decoupling producer and consumer, with explicit control over concurrency, bounded capacity, and async processing.
   - You can post a message to an `ActionBlock` and immediately release the producer thread, knowing the block will process messages in the background.

**Summary:**
Rx is great for event composition and UI/reactive programming, but for high-throughput, I/O-bound, and memory-sensitive server-side message processing where you need explicit thread release and backpressure, TPL Dataflow is a much better fit.
