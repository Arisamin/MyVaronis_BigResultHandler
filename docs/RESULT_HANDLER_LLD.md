# Result Handler - Low Level Design

> **Note:** This document is Copilot-generated content based on high-level architecture discussions. A user-verified version will be created later.

## Overview
The Result Handler is responsible for receiving header and payload messages, tracking message series completion, uploading payload data to Azure Storage, and maintaining transaction state in KV Store (Azure Table Storage).

## Class Structure

### ResultHandler
Main orchestrator class that coordinates message processing and state transitions.

**Properties:**
- `stateHandlers: Dictionary<StateType, IStateHandler>` - Map of state handlers
- `currentState: StateType` - Current state of the handler
- `context: TransactionContext` - Current transaction context

**Methods:**
- `ProcessHeaderMessage(headerMessage: HeaderMessage): void` - Entry point for header messages
- `ProcessPayloadMessage(payloadMessage: PayloadMessage): void` - Entry point for payload messages
- `TransitionToState(newState: StateType): void` - State transition coordinator
- `RecoverFromCrash(transactionId: string): void` - Restore state from KV Store after crash

---

### TransactionContext
Context object maintained throughout state transitions, accessible by all state handlers.

**Properties:**
- `TransactionId: string` - Unique transaction identifier
- `ExpectedSeriesTypes: List<string>` - Series types from header message
- `SeriesTracker: Dictionary<string, SeriesInfo>` - Tracks completion per series type
- `CurrentState: StateType` - Current state for persistence
- `CreatedTimestamp: DateTime` - Transaction creation time for timeout tracking
- `LastUpdateTimestamp: DateTime` - Last state update time

**Methods:**
- `IsComplete(): bool` - Check if all series are complete
- `UpdateSeriesProgress(seriesType: string, ordinal: int, totalCount: int): void` - Update series tracking
- `Persist(): void` - Save context to KV Store
- `LoadFromKVStore(transactionId: string): TransactionContext` - Static method to restore context

---

### SeriesInfo
Tracks completion status for a single message series.

**Properties:**
- `SeriesType: string` - Type identifier for the series
- `TotalCount: int` - Total messages expected in series
- `ReceivedOrdinals: HashSet<int>` - Set of received message ordinals
- `BlobMappings: Dictionary<int, string>` - Map ordinal to blob URI

**Methods:**
- `IsComplete(): bool` - Returns true when ReceivedOrdinals.Count == TotalCount
- `AddOrdinal(ordinal: int, blobUri: string): bool` - Add ordinal and blob URI, returns false if duplicate
- `GetMissingOrdinals(): List<int>` - Returns list of missing ordinals (1 to TotalCount)

---

## State Handlers

### IStateHandler Interface
Common interface for all state handlers.

**Methods:**
- `HandleHeader(context: TransactionContext, headerMessage: HeaderMessage): StateType` - Process header message, return next state
- `HandlePayload(context: TransactionContext, payloadMessage: PayloadMessage): StateType` - Process payload message, return next state
- `OnEnter(context: TransactionContext): void` - Called when entering this state
- `OnExit(context: TransactionContext): void` - Called when exiting this state

---

### AwaitingResultsStateHandler
Implements State 1: Awaiting Results

**Methods:**
- `HandleHeader(context, headerMessage): StateType`
  - Initialize SeriesTracker with expected series types from header
  - Set TotalCount to -1 (unknown until first payload of each series)
  - Persist context to KV Store
  - Return StateType.AwaitingResults
  
- `HandlePayload(context, payloadMessage): StateType`
  - Upload payload data to Azure Storage
  - Receive blob URI from storage service
  - Update SeriesInfo for the message's series type:
    - Set TotalCount if this is first message of series
    - Add ordinal and blob URI to SeriesInfo
    - Check for duplicates (ordinal already received)
  - Write to KV Store:
    - Namespace: TransactionId
    - Key: `{SeriesType}:{Ordinal}`
    - Value: BlobURI
  - Check if all series are complete using context.IsComplete()
  - If complete, persist context and return StateType.SendingCompletion
  - Otherwise, persist context and return StateType.AwaitingResults

- `OnEnter(context): void`
  - Log state entry
  - Record state entry timestamp for monitoring

- `OnExit(context): void`
  - Log state exit
  - Record completion metrics (duration, message count)

---

### SendingCompletionStateHandler
Implements State 2: Sending Completion Message

**Methods:**
- `HandleHeader(context, headerMessage): StateType`
  - Ignore header (transaction already complete)
  - Log warning about late header arrival
  - Return StateType.SendingCompletion

- `HandlePayload(context, payloadMessage): StateType`
  - Ignore payload (transaction already complete)
  - Log warning about late payload arrival
  - Return StateType.SendingCompletion

- `OnEnter(context): void`
  - Call Result Assembler to organize blob URIs
  - Send completion notification to Notification Queue
  - Mark transaction as complete in KV Store
  - Log completion
  - Trigger cleanup timer (if implemented)

- `OnExit(context): void`
  - Log state exit (should rarely exit this state)

---

## Supporting Services

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
Manages Azure Table Storage operations.

**Methods:**
- `WriteBlobMapping(transactionId: string, seriesType: string, ordinal: int, blobUri: string): void`
  - PartitionKey: transactionId
  - RowKey: `{seriesType}:{ordinal}`
  - Stores blob URI as property
  
- `WriteTransactionContext(context: TransactionContext): void`
  - PartitionKey: transactionId
  - RowKey: `__context__` (special key for context data)
  - Stores serialized context (state, series tracker, timestamps)
  
- `LoadTransactionContext(transactionId: string): TransactionContext`
  - Retrieves context from RowKey `__context__`
  - Deserializes and returns TransactionContext
  
- `GetAllBlobMappings(transactionId: string): List<BlobMapping>`
  - Queries all entries with PartitionKey = transactionId
  - Excludes `__context__` row
  - Returns list of (SeriesType, Ordinal, BlobURI) tuples

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
}
```

---

## Processing Flow

### Header Message Processing
1. Header Consumer receives header from Header Queue
2. Calls `ResultHandler.ProcessHeaderMessage(headerMessage)`
3. ResultHandler delegates to current state handler
4. AwaitingResultsStateHandler:
   - Creates/updates TransactionContext
   - Initializes SeriesTracker with expected series types
   - Persists context to KV Store
5. Returns to waiting for payload messages

### Payload Message Processing
1. Payload Consumer receives payload from Payload Queue
2. Calls `ResultHandler.ProcessPayloadMessage(payloadMessage)`
3. ResultHandler delegates to current state handler
4. AwaitingResultsStateHandler:
   - Calls AzureStorageService.UploadBlobStream(messageStream) → streams data to Azure Storage
   - Streaming occurs with fixed buffer (4-8MB), not full message in memory
   - Receives blob URI after upload completes
   - Updates SeriesInfo for the series type
   - Calls KVStoreService.WriteBlobMapping()
   - Checks if series is complete
   - Checks if all series are complete
   - If all complete:
     - Persists context
     - Transitions to SendingCompletionStateHandler
   - Otherwise:
     - Persists context
     - Continues in AwaitingResults state

### Completion Processing
1. ResultHandler transitions to SendingCompletionStateHandler
2. SendingCompletionStateHandler.OnEnter():
   - Instantiates Result Assembler
   - Result Assembler queries KV Store for all blob mappings
   - Organizes URIs by series type
   - Creates notification message with organized URIs
   - Publishes to Notification Queue
   - Updates transaction state in KV Store

### Crash Recovery
1. Process restarts after crash
2. Recovery manager scans KV Store for incomplete transactions
3. For each transaction:
   - Calls `TransactionContext.LoadFromKVStore(transactionId)`
   - Instantiates ResultHandler with recovered context
   - Sets appropriate state handler based on context.CurrentState
   - Resumes processing from last known state

---

## Concurrency Considerations

### Multi-Instance Processing
- Multiple ResultHandler instances can run concurrently
- Each instance handles different transactions (isolated by TransactionId)
- KV Store uses TransactionId as PartitionKey for isolation
- Azure Storage uses TransactionId in blob path for isolation

### Thread Safety
- ResultHandler instance is single-threaded per transaction
- Multiple threads can process different transactions
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
