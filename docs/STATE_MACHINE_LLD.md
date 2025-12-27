# State Machine - Low Level Design

> **Note:** This document is partly Copilot-generated content based on high-level architecture discussions.

## Overview
This document provides the low-level design for the **StateMachine** (a generic state management library) and its configuration within the BigResultHandler system, including ResultService-specific context extensions (TransactionContext, SeriesInfo) and state handlers.

The **StateMachine** is a generic, reusable state coordinator library that:
- Manages state transitions and persistence
- Maintains a Context object with generic ID, CurrentState, and timestamps
- Supports state handlers implementing IStateHandler interface
- Enables crash recovery through state persistence

**ResultService's usage of StateMachine:**
- Instantiates one StateMachine per TransactionID within a dedicated Autofac lifetime scope
- Extends generic Context to TransactionContext with business-specific properties (SeriesInfo tracking)
- Configures three states: Request Forwarding → Awaiting Results → Sending Completion
- Implements custom state handlers for each state

For the overall system architecture, see ARCHITECTURE.md.

## Transaction Lifecycle & Component Instantiation

### ResultService
The entry point that manages transaction lifecycle:

1. **Request Received**: External request arrives with TransactionID
2. **Module Creation**: ResultService creates dedicated Autofac lifetime scope for this transaction
3. **Module Build Phase**: Autofac instantiates components in dependency order:
   - RabbitMQ queues (per-transaction: `transaction-{TransactionId}-header` and `transaction-{TransactionId}-payload`)
   - Queue consumers (injected with references to transaction-specific queues)
   - ResultHandler (receives queue consumer references)
   - StateMachine (receives ResultHandler, starts in State 0: Request Forwarding)
4. **Processing**: StateMachine progresses through states (0 → 1 → 2)
5. **Cleanup**: When transaction completes, ResultService disposes the Autofac scope, destroying all components including queues

### Crash Recovery
On service startup, ResultService:
1. Checks Database for "Recoverable Transactions" (in-progress transactions)
2. For each TransactionID found, recreates the Autofac lifetime scope
3. StateMachine is instantiated and restores state from Database
4. Processing resumes from the persisted state (idempotent state handlers allow safe re-execution)

For detailed crash recovery mechanisms, see CRASH_RECOVERY.md.

---

## Class Structure

### StateMachine
Per-transaction state coordinator instantiated for a specific TransactionID.

**Dependencies (Injected via IOC):**
- `resultHandler: ResultHandler` - Handles actual message processing (upload, KV Store writes)
- `resultAssembler: ResultAssembler` - Assembles blob URIs when transaction completes
- `notificationSender: NotificationSender` - Sends completion notifications
- `kvStoreService: IKVStoreService` - Azure Table Storage operations for context persistence

**Properties:**
- `id: string` - Generic identifier for this state machine instance (in ResultService, this is the TransactionID)
- `context: Context` - Generic context object (extended to TransactionContext by ResultService)
- `currentStateHandler: IStateHandler` - Current state handler instance
- `stateHandlers: Dictionary<StateType, IStateHandler>` - Map of all state handlers

**Constructor:**
```csharp
public StateMachine(
    string id,
    ResultHandler resultHandler,
    ResultAssembler resultAssembler,
    NotificationSender notificationSender,
    IDatabaseService databaseService)
{
    this.id = id; // In ResultService usage, this is the TransactionID
    this.resultHandler = resultHandler;
    this.resultAssembler = resultAssembler;
    this.notificationSender = notificationSender;
    this.databaseService = databaseService;
    
    // Attempt to restore context from Database (crash recovery)
    // ResultService uses LoadTransactionContext which returns extended TransactionContext
    this.context = databaseService.LoadContext(id);
    
    if (this.context == null) {
        // New state machine instance - initialize context
        // ResultService creates TransactionContext (extended Context)
        this.context = new TransactionContext {
            ID = id,
            CurrentState = StateType.RequestForwarding,
            CreatedTimestamp = DateTime.UtcNow,
            LastUpdateTimestamp = DateTime.UtcNow,
            // TransactionContext-specific properties initialized separately
        };
    }
    
    // Initialize state handlers (ResultService-specific handlers)
    this.stateHandlers = new Dictionary<StateType, IStateHandler> {
        { StateType.RequestForwarding, new RequestForwardingStateHandler(producerClient) },
        { StateType.AwaitingResults, new AwaitingResultsStateHandler(kvStoreService) },
        { StateType.SendingCompletion, new SendingCompletionStateHandler(resultAssembler, notificationSender, kvStoreService) }
    };
    
    // Set current state based on context (for recovery scenarios)
    this.currentStateHandler = stateHandlers[this.context.CurrentState];
}
```

**Methods:**
- `Run(): void`
  - Entry point called by ResultService to start/resume transaction processing
  - Calls currentStateHandler.OnEnter() to begin processing from current state
  - State handlers drive the state machine through state transitions
  - Idempotent: Safe to call multiple times (crash recovery scenario)
  
- `TransitionToState(newState: StateType): void`
  - Called by state handlers to transition to a new state
  - Calls currentStateHandler.OnExit()
  - Updates context.CurrentState
  - Updates currentStateHandler to new state handler
  - Persists context to Database
  - Calls newStateHandler.OnEnter()
  
- `PersistContext(): void`
  - Saves TransactionContext to Database
  - Updates LastUpdateTimestamp
  - Marks transaction as "Recoverable" (in-progress)
  - Enables crash recovery

**Crash Recovery:**
The StateMachine constructor automatically handles crash recovery:
1. Attempts to load TransactionContext from Database using TransactionID
2. If found, restores the context (including CurrentState)
3. Sets currentStateHandler based on restored state
4. When Run() is called, processing resumes from the persisted state
5. State handlers are idempotent, allowing safe re-execution from any state

No separate Restore() method needed - recovery is built into constructor.

---

## Context Objects

### Context (Generic StateMachine Context)
The base context object used by the generic StateMachine library.

**Properties:**
- `ID: string` - Generic identifier for this state machine instance
- `CurrentState: StateType` - Current state enum value
- `CreatedTimestamp: DateTime` - When state machine was created
- `LastUpdateTimestamp: DateTime` - Last state update time

**Methods:**
- `Serialize(): string` - Serialize context for persistence
- `Deserialize(data: string): Context` - Static method to deserialize from storage

---

### TransactionContext (ResultService Extension)
ResultService-specific extension of the generic Context, adding business logic properties for tracking multi-part message series.

**Inheritance:** `TransactionContext : Context`

**Additional Properties:**
- `TransactionId: string` - Business identifier (typically same as ID, but semantically different)
- `ExpectedSeriesTypes: List<string>` - Series types from header message
- `ExpectedCounts: Dictionary<string, int>` - Expected message count per series type
- `SeriesTracker: Dictionary<string, SeriesInfo>` - Tracks completion per series type

**Methods:**
- `IsComplete(): bool` - Check if all series are complete
  ```csharp
  public bool IsComplete() {
      foreach (var seriesType in ExpectedSeriesTypes) {
          if (!SeriesTracker.ContainsKey(seriesType)) {
              return false; // Series not yet started
          }
          
          if (!SeriesTracker[seriesType].IsComplete()) {
              return false; // Series incomplete
          }
      }
      return true;
  }
  ```

- `UpdateSeriesProgress(seriesType: string, ordinal: int, totalCount: int, blobUri: string): void`
  ```csharp
  public void UpdateSeriesProgress(string seriesType, int ordinal, int totalCount, string blobUri) {
      // Get or create SeriesInfo for this series type
      if (!SeriesTracker.ContainsKey(seriesType)) {
          SeriesTracker[seriesType] = new SeriesInfo { 
              SeriesType = seriesType,
              TotalCount = totalCount
          };
          
          // Initialize expected count if this is first message of series
          if (!ExpectedCounts.ContainsKey(seriesType)) {
              ExpectedCounts[seriesType] = totalCount;
          }
      }
      
      var seriesInfo = SeriesTracker[seriesType];
      
      // Add ordinal and blob URI
      bool isNew = seriesInfo.AddOrdinal(ordinal, blobUri);
      
      if (!isNew) {
          Log.Warning($"Duplicate ordinal {ordinal} for series {seriesType} in transaction {TransactionId}");
      }
  }
  ```

- `Serialize(): string` - Serialize context for KV Store persistence
- `Deserialize(data: string): TransactionContext` - Static method to deserialize from KV Store

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
  ```csharp
  public bool IsComplete() {
      return ReceivedOrdinals.Count == TotalCount;
  }
  ```

- `AddOrdinal(ordinal: int, blobUri: string): bool` - Add ordinal and blob URI, returns false if duplicate
  ```csharp
  public bool AddOrdinal(int ordinal, string blobUri) {
      bool isNew = ReceivedOrdinals.Add(ordinal);
      
      if (isNew) {
          BlobMappings[ordinal] = blobUri;
      }
      
      return isNew; // false if duplicate
  }
  ```

- `GetMissingOrdinals(): List<int>` - Returns list of missing ordinals (1 to TotalCount)
  ```csharp
  public List<int> GetMissingOrdinals() {
      var missing = new List<int>();
      for (int i = 1; i <= TotalCount; i++) {
          if (!ReceivedOrdinals.Contains(i)) {
              missing.Add(i);
          }
      }
      return missing;
  }
  ```

---

## State Handlers

### IStateHandler
Interface implemented by all state handlers.

```csharp
public interface IStateHandler {
    void OnEnter();
    void OnExit();
    StateType GetStateType();
}
```

**Idempotency Requirement:**
All state handlers must be idempotent - they can be safely re-executed from the beginning if a crash occurs. This is achieved through:
- Checking existing state before performing actions
- Using transactional operations where possible
- Leveraging KV Store and Database for deduplication

---

### RequestForwardingStateHandler
Handles State 0: Forwarding processing request to Producer.

> **Note:** This state is not the primary focus of the BigResultHandler design. It handles communication with external Producer service.

**Responsibilities:**
1. Send processing request to Producer with TransactionID and queue names
2. State handler completes (StateMachine automatically transitions to AwaitingResults)

**Dependencies:**
- `producerClient: IProducerClient` - Interface for sending requests to Producer service

**Constructor:**
```csharp
public RequestForwardingStateHandler(IProducerClient producerClient)
{
    this.producerClient = producerClient;
}
```

**Methods:**

- `OnEnter(): void`
  ```csharp
  public void OnEnter() {
      Log.Info($"Entering RequestForwarding state for transaction {context.TransactionId}");
      
      // Queues are already created by Autofac during module build phase
      // Queue names are deterministic: transaction-{TransactionId}-header and -payload
      
      // Send request to Producer service to begin processing
      // (Producer will publish results to the transaction-specific queues)
      var request = new ProcessRequest {
          TransactionId = context.TransactionId,
          HeaderQueueName = $"transaction-{context.TransactionId}-header",
          PayloadQueueName = $"transaction-{context.TransactionId}-payload",
          // ... other request details (input data to process, etc.)
      };
      
      producerClient.SendRequest(request);
      
      Log.Info($"Request sent to Producer for transaction {context.TransactionId}");
      
      // Method returns - StateMachine detects completion and transitions to AwaitingResults
      // Producer may have already started publishing messages to queues by this point
  }
  ```

- `OnExit(): void`
  ```csharp
  public void OnExit() {
      Log.Info($"Exiting RequestForwarding state for transaction {context.TransactionId}");
      // No cleanup needed - queues remain active for message consumption
  }
  ```

**Idempotency:**
If crash occurs during this state and state machine is recreated:
- Queues are recreated with same deterministic names (by Autofac)
- Request may be sent to Producer again (Producer should handle duplicate requests via TransactionID)
- Safe to re-execute

---

### AwaitingResultsStateHandler
Handles State 1: Consuming and processing messages from transaction-specific queues.

**Responsibilities:**
- Monitor message arrival and completion status
- Track which messages have arrived vs. expected (ordinal tracking)
- Detect when all series are complete
- Trigger transition to SendingCompletion state

**Dependencies:**
- `kvStoreService: IKVStoreService` - For:
  - **Message tracking persistence**: Storing message ordinals → storage paths as messages arrive (written by ResultHandler during message processing)
  - **Completion detection**: Querying stored message count vs. expected count for each series
  - **Metadata loading**: Reading transaction metadata (series types, expected counts) written by ResultHandler.HandleHeaderMessage

> **Note:** DatabaseService is NOT a dependency. StateMachine uses DatabaseService to persist its own state, but state handlers only use KVStoreService for business-specific message tracking.

**Constructor:**
```csharp
public AwaitingResultsStateHandler(IKVStoreService kvStoreService)
{
    this.kvStoreService = kvStoreService;
}
```

**Methods:**

- `OnEnter(): void`
  ```csharp
  public void OnEnter() {
      Log.Info($"Entering AwaitingResults state for transaction {stateMachine.TransactionId}");
      
      // Load transaction metadata from KV Store (written by ResultHandler.HandleHeaderMessage)
      var metadata = kvStoreService.LoadTransactionMetadata(stateMachine.TransactionId);
      
      // Update context with expected series information
      stateMachine.context.ExpectedSeriesTypes = metadata.SeriesTypes;
      stateMachine.context.ExpectedCounts = metadata.SeriesCounts;
      
      // Start monitoring for completion
      StartCompletionMonitoring();
  }
  ```

- `StartCompletionMonitoring(): void`
  ```csharp
  private void StartCompletionMonitoring() {
      // Poll KV Store periodically to check for completion
      // (Alternative: event-driven notification from ResultHandler via callback)
      
      Timer checkTimer = new Timer(async (state) => {
          await CheckTransactionCompletion();
      }, null, TimeSpan.Zero, TimeSpan.FromSeconds(5)); // Check every 5 seconds
  }
  ```

- `CheckTransactionCompletion(): async Task`
  ```csharp
  private async Task CheckTransactionCompletion() {
      // Query KV Store for each expected series
      bool allComplete = true;
      
      foreach (var seriesType in stateMachine.context.ExpectedSeriesTypes) {
          int expectedCount = stateMachine.context.ExpectedCounts[seriesType];
          
          // Query KV Store for blob mappings
          var blobMappings = kvStoreService.GetBlobMappingsForSeries(
              stateMachine.TransactionId, 
              seriesType);
          
          // Update context tracking
          foreach (var mapping in blobMappings) {
              stateMachine.context.UpdateSeriesProgress(
                  seriesType, 
                  mapping.Ordinal, 
                  expectedCount, 
                  mapping.BlobUri);
          }
          
          // Check if series is complete
          if (blobMappings.Count < expectedCount) {
              allComplete = false;
          }
      }
      
      if (allComplete) {
          Log.Info($"Transaction {stateMachine.TransactionId} completed - all series complete");
          
          // Persist context before transition
          stateMachine.PersistContext();
          
          // Transition to SendingCompletion state
          stateMachine.TransitionToState(StateType.SendingCompletion);
      }
  }
  ```

- `OnExit(): void`
  ```csharp
  public void OnExit() {
      Log.Info($"Exiting AwaitingResults state for transaction {stateMachine.TransactionId}");
      
      // Stop completion monitoring
      // Clean up resources
  }
  ```

---

### SendingCompletionStateHandler
Handles State 2: Sending completion notification after all messages received.

**Dependencies:**
- `resultAssembler: ResultAssembler` - Assembles organized blob URIs from KV Store for notification payload
- `notificationSender: NotificationSender` - Sends completion notifications to Notification Queue
- `kvStoreService: IKVStoreService` - For:
  - Reading all blob mappings (message ordinals → storage paths) for ResultAssembler
  - Marking transaction as complete in persistent state

**Constructor:**
```csharp
public SendingCompletionStateHandler(
    ResultAssembler resultAssembler,
    NotificationSender notificationSender,
    IKVStoreService kvStoreService)
{
    this.resultAssembler = resultAssembler;
    this.notificationSender = notificationSender;
    this.kvStoreService = kvStoreService;
}
```

**Methods:**

- `OnEnter(): void`
  ```csharp
  public void OnEnter() {
      Log.Info($"Entering SendingCompletion state for transaction {stateMachine.TransactionId}");
      
      // Assemble blob URIs
      var organizedData = resultAssembler.AssembleBlobs(stateMachine.TransactionId);
      
      // Send notification
      notificationSender.SendNotification(stateMachine.TransactionId, organizedData);
      
      // Mark transaction as complete
      stateMachine.context.CurrentState = StateType.Completed;
      stateMachine.PersistContext();
      
      Log.Info($"Transaction {stateMachine.TransactionId} completed successfully");
  }
  ```

- `OnExit(): void`
  ```csharp
  public void OnExit() {
      Log.Info($"Exiting SendingCompletion state for transaction {stateMachine.TransactionId}");
      
      // Clean up resources
      // Transaction processing complete
  }
  ```

---

## State Transitions

### State Diagram
```
┌─────────────────────┐
│  AwaitingResults    │ ← Initial State
│                     │
│ - Monitor messages  │
│ - Track completion  │
└─────────┬───────────┘
          │
          │ All series complete
          │
          ▼
┌─────────────────────┐
│ SendingCompletion   │
│                     │
│ - Assemble blobs    │
│ - Send notification │
└─────────────────────┘
          │
          ▼
     Completed
```

### Transition Logic

**AwaitingResults → SendingCompletion:**
- Trigger: All expected series are complete (all ordinals received per series)
- Action: AwaitingResultsStateHandler.CheckTransactionCompletion() calls stateMachine.TransitionToState(StateType.SendingCompletion)
- Context: Updated with CurrentState = SendingCompletion, persisted to KV Store

**SendingCompletion → Completed:**
- Trigger: Notification sent successfully
- Action: SendingCompletionStateHandler.OnEnter() completes, updates context.CurrentState = Completed
- Context: Persisted to KV Store with final state

---

## Processing Flow Integration

### Transaction Initialization
1. ResultService receives trigger (e.g., first message for new transaction)
2. ResultService creates ResultManager instance (via IOC)
3. ResultManager instantiates StateMachine for specific TransactionID with dependencies:
   - ResultHandler (already instantiated with queue consumers registered)
   - ResultAssembler
   - NotificationSender
   - KVStoreService
4. ResultManager calls `stateMachine.Run()`
5. StateMachine calls `currentStateHandler.OnEnter()` (AwaitingResultsStateHandler)
6. AwaitingResultsStateHandler loads metadata and starts completion monitoring

### Message Processing Flow
1. ResultHandler.HandleHeaderMessage processes header:
   - Writes transaction metadata to KV Store
   - (AwaitingResultsStateHandler loads this metadata in OnEnter)

2. ResultHandler.HandlePayloadMessage processes each payload:
   - Uploads blob to Azure Storage
   - Writes blob mapping to KV Store

3. AwaitingResultsStateHandler monitors completion:
   - Periodically queries KV Store for all series
   - Updates TransactionContext.SeriesTracker with progress
   - When all series complete, transitions to SendingCompletion

4. SendingCompletionStateHandler sends notification:
   - Calls ResultAssembler to organize blob URIs
   - Calls NotificationSender to publish notification
   - Marks transaction complete

### Crash Recovery Flow
1. Process restarts after crash
2. ResultService recovery manager scans KV Store for incomplete transactions
3. For each incomplete transaction:
   - Loads TransactionContext from KV Store
   - Calls `StateMachine.Restore(transactionId, context, dependencies)`
   - StateMachine restores state handler based on context.CurrentState
   - Calls `stateMachine.Run()` to resume from last known state

---

## Concurrency Considerations

### Multi-Transaction Processing
- Multiple StateMachine instances run concurrently (one per transaction)
- Each StateMachine is isolated by its TransactionID
- ResultManager coordinates multiple StateMachine instances
- Shared dependencies (KV Store, Azure Storage, ResultHandler) are thread-safe

### Transaction Isolation
- KV Store uses TransactionId as PartitionKey for data isolation
- Each StateMachine instance operates on its own partition
- No cross-transaction interference
- SeriesInfo.ReceivedOrdinals (HashSet) provides O(1) duplicate detection per transaction

### State Handler Thread Safety
- Each StateMachine instance is single-threaded for its transaction
- State handlers do not share mutable state across transactions
- Context object is owned by single StateMachine instance
- KVStoreService uses optimistic concurrency (eTags) for context updates

---

## Error Handling

### Missing Messages
- Timeout mechanism tracks LastUpdateTimestamp in TransactionContext
- AwaitingResultsStateHandler can implement timeout check
- SeriesInfo.GetMissingOrdinals() reports gaps for monitoring
- Alert when transaction stuck in AwaitingResults beyond threshold

### Context Persistence Failures
- Critical operation - must succeed before state transitions
- StateMachine.PersistContext() implements retry with exponential backoff
- Alert on persistent failures
- Circuit breaker pattern to prevent cascading failures

### State Transition Failures
- If transition fails, StateMachine remains in current state
- Context retains last known good state
- Recovery can resume from last persisted state
- Log all transition attempts and failures

---

## Monitoring & Observability

### Metrics
- `state_machine_instances_active` - Gauge of active StateMachine instances
- `state_transitions_total` - Counter of state transitions by type
- `state_duration` - Histogram of time spent in each state
- `transaction_completion_time` - Time from creation to completion
- `context_persistence_duration` - Time to persist context to KV Store

### Logging
- StateMachine instantiation (TransactionId)
- State transitions (from → to, TransactionId)
- Context persistence operations
- Completion detection events
- Error conditions with context

### Alerts
- Transactions stuck in AwaitingResults for > threshold
- Context persistence failures
- State transition failures
- Memory threshold violations (too many active StateMachine instances)

---

## Configuration

### Timeouts
- `TransactionTimeout` - Max time in AwaitingResults state (e.g., 30 minutes)
- `CompletionCheckInterval` - Interval for checking transaction completion (e.g., 5 seconds)
- `StateTransitionTimeout` - Max time for state handler operations (e.g., 1 minute)

### Retry Policies
- `ContextPersistenceRetries` - Number of retry attempts (e.g., 5)
- `ContextPersistenceRetryDelay` - Initial retry delay (e.g., 1 second)

### Resource Limits
- `MaxConcurrentStateMachines` - Limit on active StateMachine instances (e.g., 1000)
- `MaxSeriesPerTransaction` - Limit on series types (e.g., 100)
- `MaxMessagesPerSeries` - Limit on ordinals (e.g., 10000)
