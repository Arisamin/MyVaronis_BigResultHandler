# Crash Recovery - BigResultHandler

## Overview

The BigResultHandler system implements a robust, multi-layered crash recovery mechanism that ensures **no data loss** and **no duplicate processing** when service crashes or restarts occur during transaction processing.

**Key Design Principles:**
1. **Idempotent State Handlers** - State handlers are created fresh on each run and can safely process from the beginning
2. **Database State Persistence** - Transaction state is persisted to Database at every state transition
3. **Message Durability** - RabbitMQ queues and messages survive service crashes
4. **Deduplication via KV Store** - Ordinal tracking prevents duplicate uploads

---

## Three-Layer Recovery Architecture

### Layer 1: Database State Persistence

**Purpose:** Track which state each transaction is in, enabling resumption from the correct point.

**Mechanism:**
- Generic `Context` is persisted to Database on every state transition
- Includes only: `ID` (TransactionID), `CurrentState`, `CreatedTimestamp`, `LastUpdateTimestamp`
- Database namespace for "Recoverable Transactions" tracks all in-progress transactions
- **Note:** Business-specific tracking (series progress, ordinals, blob paths) is in KV Store (Layer 3), NOT Database

**Recovery Flow:**
1. On service startup, ResultService queries Database for "Recoverable Transactions"
2. For each TransactionID found, ResultService recreates the Autofac lifetime scope
3. StateMachine constructor loads generic `Context` from Database
4. StateMachine resumes from the persisted `CurrentState` (0, 1, or 2)

**Code Example:**
```csharp
// StateMachine constructor (generic library)
this.context = databaseService.LoadContext(id);

if (this.context == null) {
    // New transaction
    this.context = new Context {
        ID = id,  // TransactionID in ResultService usage
        CurrentState = StateType.RequestForwarding,
        CreatedTimestamp = DateTime.UtcNow,
        LastUpdateTimestamp = DateTime.UtcNow
    };
} else {
    // Recovery scenario - generic context loaded from Database
    Log.Info($"Recovered transaction {id} in state {this.context.CurrentState}");
}

// Set current state handler based on (potentially recovered) context
this.currentStateHandler = stateHandlers[this.context.CurrentState];
```

---

### Layer 2: RabbitMQ Message Durability & ACK

**Purpose:** Ensure messages are not lost and are re-delivered if not acknowledged.

**Mechanism:**
- **Queue Durability:** RabbitMQ queues are external to the service and survive crashes
  - Queues have timeout-based cleanup (destroyed only when no consumers connected for extended period)
  - During normal processing, consumers are always connected, keeping queues alive
- **Message Acknowledgment:** Messages are only ACK'd after successful processing
  - Processed messages are removed from queue
  - Unprocessed messages remain in queue for reprocessing after crash

**Recovery Flow:**
1. Service crashes while processing messages
2. Messages that were fully processed: Already ACK'd and removed from queue
3. Messages that were partially processed or not processed: Still in queue (no ACK sent)
4. On recovery, queue consumers reconnect to same queues (deterministic names: `transaction-{TransactionId}-header/payload`)
5. RabbitMQ re-delivers unacknowledged messages to consumers
6. Messages are processed again (idempotency protects against duplicates)

**Queue Naming (Deterministic):**
```csharp
string headerQueueName = $"transaction-{TransactionId}-header";
string payloadQueueName = $"transaction-{TransactionId}-payload";
```
Same TransactionID always produces same queue names, enabling reconnection after crash.

---

### Layer 3: KV Store Ordinal Tracking (Idempotency)

**Purpose:** Prevent duplicate data uploads even if messages are reprocessed.

**Mechanism:**
- **KV Store Schema:** `(TransactionId, SeriesType, Ordinal) → BlobStoragePath`
- Before uploading a message payload to Blob Storage, check if ordinal already exists in KV Store
- If ordinal exists, skip upload and use existing Blob URI
- If ordinal is new, upload to Blob Storage and write mapping to KV Store

**Recovery Flow:**
1. Service crashes after uploading some payloads but before ACK'ing messages
2. Messages are redelivered after recovery
3. For each redelivered message:
   - Query KV Store: `GetBlobUri(TransactionId, SeriesType, Ordinal)`
   - If found: Message was already processed, skip upload, use existing Blob URI
   - If not found: New message, proceed with upload

**Code Example (from AwaitingResultsStateHandler):**
```csharp
// This method is part of AwaitingResultsStateHandler
public void HandlePayloadMessage(PayloadMessage message) {
    // Check if ordinal already processed
    var existingBlobUri = kvStoreService.GetBlobUri(
        message.TransactionId, 
        message.SeriesType, 
        message.Ordinal);
    
    if (existingBlobUri != null) {
        Log.Info($"Ordinal {message.Ordinal} already processed - skipping upload");
        // Message was already handled before crash, skip processing
        return;
    }
    
    // New message - upload to Blob Storage
    var blobUri = UploadToAzureBlob(message.Data);
    
    // Write mapping to KV Store (idempotent - upsert operation)
    kvStoreService.WriteOrdinalMapping(
        message.TransactionId,
        message.SeriesType,
        message.Ordinal,
        blobUri);
    
    Log.Info($"Uploaded ordinal {message.Ordinal} to {blobUri}");
}
```

---

## State-Specific Recovery Behavior

### State 0: Request Forwarding

**What Was Happening:**
- RequestForwardingStateHandler sends request to Producer with TransactionID and queue names
- (Queues were already created by Autofac during module build phase)
- State handler completes, then StateMachine transitions to AwaitingResults (generic SM responsibility)

**Crash Scenario:**
Service crashes before transitioning to State 1.

**Recovery:**
1. Database shows transaction in State 0 (generic Context with CurrentState = RequestForwarding)
2. ResultService recreates Autofac module for TransactionID
3. Queues are recreated with same deterministic names (by Autofac container)
4. StateMachine loads context from Database (only CurrentState and timestamps)
5. `RequestForwardingStateHandler.OnEnter()` is called
6. Request may be sent to Producer again (Producer should deduplicate via TransactionID)
7. State handler completes - StateMachine handles transition to State 1

> **Note:** State transitions are managed by the generic StateMachine library (calls `TransitionToState()` internally after state handler completes). This is not part of ResultHandler's responsibility.

**Idempotency Protection:**
- Producer should check if TransactionID already has active processing and avoid duplicate work
- Queue recreation is safe (queues with same name are reused if they exist)

---

### State 1: Awaiting Results

**What Was Happening:**
- Consuming header and payload messages from transaction-specific queues
- Uploading payloads to Blob Storage
- Writing ordinal mappings to KV Store
- Monitoring completion by querying KV Store for message counts

**Crash Scenario:**
Service crashes while processing messages. Some messages were processed and ACK'd, others were not.

**Recovery:**
1. Database shows transaction in State 1 (generic Context with CurrentState = AwaitingResults)
2. ResultService recreates Autofac module for TransactionID
3. Queues still exist in RabbitMQ (durable, consumers were connected before crash)
4. StateMachine loads context from Database (only CurrentState and timestamps)
5. AwaitingResultsStateHandler queries KV Store for message tracking progress (ordinals, blob paths)
6. Queue consumers reconnect to same queues (by deterministic names)
7. RabbitMQ redelivers unacknowledged messages
8. For each redelivered message:
   - **Layer 3 Protection:** Check KV Store for existing ordinal
   - If found: Skip upload (already processed before crash)
   - If not found: Process normally (new message)
9. Processing continues until all series complete (detected via KV Store query)
10. Transition to State 2

**Idempotency Protection:**
- KV Store ordinal check prevents duplicate Blob Storage uploads
- Message ACK only sent after successful processing - no data loss
- Completion detection queries KV Store (message count per series)

**Example Timeline:**
```
10:00 - Message ordinal 1 arrives, uploaded to Blob, written to KV Store, ACK'd
10:01 - Message ordinal 2 arrives, uploaded to Blob, written to KV Store, ACK'd
10:02 - Message ordinal 3 arrives, uploaded to Blob, written to KV Store
10:02 - CRASH (before ACK for ordinal 3)

10:05 - Service restarts
10:05 - Recovery: Recreate module for TransactionID
10:05 - Reconnect to queues
10:05 - RabbitMQ redelivers message ordinal 3 (not ACK'd)
10:06 - HandlePayloadMessage(ordinal 3):
        - Check KV Store: ordinal 3 exists (uploaded before crash)
        - Skip upload, use existing Blob URI
        - ACK message
10:07 - Continue processing new messages (ordinal 4, 5, ...)
```

---

### State 2: Sending Completion

**What Was Happening:**
- Assembling blob URIs from KV Store
- Sending completion notification to Notification Queue
- Cleaning up transaction state

**Crash Scenario:**
Service crashes before sending notification or before cleanup completes.

**Recovery:**
1. Database shows transaction in State 2
2. ResultService recreates Autofac module for TransactionID
3. StateMachine loads context from Database (CurrentState = SendingCompletion)
4. `SendingCompletionStateHandler.OnEnter()` is called
5. Re-assemble blob URIs from KV Store (same data, idempotent)
6. Send notification to Notification Queue
7. Mark transaction as complete in Database
8. Module disposal triggers cleanup

**Idempotency Protection:**
- ResultAssembler reads from KV Store (same data every time)
- Notification Queue consumer should deduplicate via TransactionID
- Database update is idempotent (mark as complete)

---

## Recovery Trigger: Service Startup

> **Note:** The recovery mechanism shown below is part of the generic StateMachine library, not the ResultHandler project. It's included here for completeness of the crash recovery explanation.

**ResultService.OnStartup():**
```csharp
public async Task OnStartup() {
    Log.Info("Service starting - checking for recoverable transactions");
    
    // Query Database for in-progress transactions (generic SM responsibility)
    var recoverableTransactions = databaseService.GetRecoverableTransactions();
    
    Log.Info($"Found {recoverableTransactions.Count} transactions to recover");
    
    foreach (var transactionId in recoverableTransactions) {
        Log.Info($"Recovering transaction {transactionId}");
        
        // Recreate Autofac lifetime scope for transaction
        var scope = CreateTransactionScope(transactionId);
        
        // Resolve StateMachine (constructor handles state restoration)
        var stateMachine = scope.Resolve<StateMachine>();
        
        // Resume processing
        stateMachine.Run();
    }
    
    Log.Info("Recovery complete - service ready for new requests");
}
```

---

## Failure Scenarios & Guarantees

### Scenario 1: Crash Before Any State Persistence
**Situation:** Service crashes immediately after receiving transaction request, before first Database write.

**Result:**
- Transaction not in Database "Recoverable Transactions"
- No recovery attempted
- External requestor should retry request (timeout-based)
- New transaction created on retry

**Guarantee:** No data corruption, clean retry.

---

### Scenario 2: Crash During Message Processing
**Situation:** Service crashes while processing messages in State 1.

**Result:**
- Transaction in Database with State 1 (generic Context: CurrentState, timestamps)
- Message tracking progress is in KV Store (ordinals → blob paths)
- Recovery recreates module, reconnects to queues
- Unacknowledged messages redelivered by RabbitMQ
- KV Store prevents duplicate uploads
- Processing continues from where it left off

**Guarantee:** No data loss, no duplicate uploads, eventual completion.

---

### Scenario 3: Multiple Crashes During Same Transaction
**Situation:** Service crashes repeatedly while processing the same transaction.

**Result:**
- Each restart triggers recovery
- StateMachine reloads generic state from Database (CurrentState, timestamps)
- State handlers are created fresh via Autofac injection (idempotent design)
- Progress accumulates across crashes in KV Store (ordinals → blob paths)
- Eventually completes if service stabilizes

**Guarantee:** Forward progress guaranteed, no data loss.

---

### Scenario 4: Crash After Completion Notification Sent
**Situation:** Service crashes after sending notification but before marking transaction complete in Database.

**Result:**
- Transaction still in Database "Recoverable Transactions"
- Recovery recreates module, enters State 2
- Notification sent again (duplicate)
- Notification Queue consumer should deduplicate via TransactionID

**Guarantee:** At-least-once notification delivery (consumer must deduplicate).

---

## Timeout & Cleanup

**Transaction Timeout:**
- `Context.CreatedTimestamp` and `LastUpdateTimestamp` tracked in Database
- Periodic cleanup job checks for abandoned transactions (too old, no progress)
- Abandoned transactions: Delete queues, clean up KV Store, mark as failed in Database

**Queue Cleanup:**
- Normal completion: Autofac scope disposal destroys queues
- Crash: Queues remain until recovery completes transaction
- Abandoned: Cleanup job destroys queues
- RabbitMQ also has timeout for queues with no consumers

---

## Testing Crash Recovery

**Chaos Engineering Approach:**
1. Start transaction processing
2. Inject crash at random point (kill process)
3. Restart service
4. Verify:
   - Transaction recovered from correct state
   - No data loss (all expected blobs present)
   - No duplicate uploads (KV Store ordinals match blob count)
   - Transaction completes successfully

**Test Cases:**
- Crash in State 0 (before Producer request)
- Crash in State 1 (after partial message processing)
- Crash in State 2 (before notification sent)
- Multiple consecutive crashes during same transaction
- Crash during Database write (partial state persistence)

---

## Summary

The BigResultHandler crash recovery mechanism provides strong guarantees through three complementary layers:

1. **Database State Persistence** - Enables resumption from correct state
2. **RabbitMQ Durability & ACK** - Ensures no message loss
3. **KV Store Ordinal Tracking** - Prevents duplicate processing

**Result:** The system can recover from crashes at any point during transaction processing, ensuring **exactly-once semantics** for data uploads and **at-least-once semantics** for completion notifications.
