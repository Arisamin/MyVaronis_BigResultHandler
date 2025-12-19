# State Machine - Low Level Design

> **Note:** This document is partly Copilot-generated content based on high-level architecture discussions.

## Overview
This document provides the low-level design for the **StateMachine** and its related components (TransactionContext, State Handlers, and SeriesInfo) within the BigResultHandler system.

The **StateMachine** is a per-transaction coordinator that:
- Is instantiated by ResultManager for a specific TransactionID
- Maintains TransactionContext throughout state transitions
- Contains state handler objects (AwaitingResultsStateHandler, SendingCompletionStateHandler)
- Processes messages and manages state transitions based on transaction progress
- Enables crash recovery by persisting and restoring context

For the overall system architecture, see ARCHITECTURE.md.

## Class Structure

### StateMachine
Per-transaction state coordinator instantiated for a specific TransactionID.

**Dependencies (Injected via IOC):**
- `resultHandler: ResultHandler` - Handles actual message processing (upload, KV Store writes)
- `resultAssembler: ResultAssembler` - Assembles blob URIs when transaction completes
- `notificationSender: NotificationSender` - Sends completion notifications
- `kvStoreService: IKVStoreService` - Azure Table Storage operations for context persistence

**Properties:**
- `transactionId: string` - The specific transaction this StateMachine is processing
- `context: TransactionContext` - Maintains state and tracking information
- `currentStateHandler: IStateHandler` - Current state handler instance
- `stateHandlers: Dictionary<StateType, IStateHandler>` - Map of all state handlers

**Constructor:**
```csharp
public StateMachine(
    string transactionId,
    ResultHandler resultHandler,
    ResultAssembler resultAssembler,
    NotificationSender notificationSender,
    IKVStoreService kvStoreService)
{
    this.transactionId = transactionId;
    this.resultHandler = resultHandler;
    this.resultAssembler = resultAssembler;
    this.notificationSender = notificationSender;
    this.kvStoreService = kvStoreService;
    
    // Initialize context
    this.context = new TransactionContext {
        TransactionId = transactionId,
        CurrentState = StateType.AwaitingResults,
        CreatedTimestamp = DateTime.UtcNow,
        LastUpdateTimestamp = DateTime.UtcNow
    };
    
    // Initialize state handlers
    this.stateHandlers = new Dictionary<StateType, IStateHandler> {
        { StateType.AwaitingResults, new AwaitingResultsStateHandler(this, resultHandler, kvStoreService) },
        { StateType.SendingCompletion, new SendingCompletionStateHandler(this, resultAssembler, notificationSender, kvStoreService) }
    };
    
    // Set initial state
    this.currentStateHandler = stateHandlers[StateType.AwaitingResults];
}
```

**Methods:**
- `Run(): void`
  - Entry point called by ResultManager to start transaction processing
  - Calls currentStateHandler.OnEnter() to begin processing
  - State handlers drive the state machine through state transitions
  
- `TransitionToState(newState: StateType): void`
  - Called by state handlers to transition to a new state
  - Calls currentStateHandler.OnExit()
  - Updates context.CurrentState
  - Updates currentStateHandler to new state handler
  - Persists context to KV Store
  - Calls newStateHandler.OnEnter()
  
- `PersistContext(): void`
  - Saves TransactionContext to KV Store
  - Updates LastUpdateTimestamp
  - Enables crash recovery

**Crash Recovery:**
```csharp
public static StateMachine Restore(string transactionId, IKVStoreService kvStoreService, /* other dependencies */) {
    // Load context from KV Store
    var context = kvStoreService.LoadTransactionContext(transactionId);
    
    // Create StateMachine instance
    var stateMachine = new StateMachine(transactionId, /* dependencies */);
    
    // Restore context
    stateMachine.context = context;
    
    // Set current state handler based on context.CurrentState
    stateMachine.currentStateHandler = stateMachine.stateHandlers[context.CurrentState];
    
    return stateMachine;
}
```

---

### TransactionContext
Context object maintained throughout state transitions, accessible by all state handlers.

**Properties:**
- `TransactionId: string` - Unique transaction identifier
- `ExpectedSeriesTypes: List<string>` - Series types from header message
- `ExpectedCounts: Dictionary<string, int>` - Expected message count per series type
- `SeriesTracker: Dictionary<string, SeriesInfo>` - Tracks completion per series type
- `CurrentState: StateType` - Current state for persistence
- `CreatedTimestamp: DateTime` - Transaction creation time for timeout tracking
- `LastUpdateTimestamp: DateTime` - Last state update time

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

---

### AwaitingResultsStateHandler
Handles State 1: Awaiting all expected messages for the transaction.

**Dependencies:**
- `stateMachine: StateMachine` - Parent state machine (for context access and state transitions)
- `resultHandler: ResultHandler` - For checking completion status via KV Store queries
- `kvStoreService: IKVStoreService` - For loading transaction metadata and querying blob mappings

**Constructor:**
```csharp
public AwaitingResultsStateHandler(
    StateMachine stateMachine,
    ResultHandler resultHandler,
    IKVStoreService kvStoreService)
{
    this.stateMachine = stateMachine;
    this.resultHandler = resultHandler;
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
- `stateMachine: StateMachine` - Parent state machine (for context access)
- `resultAssembler: ResultAssembler` - Assembles blob URIs from KV Store
- `notificationSender: NotificationSender` - Sends notifications to Notification Queue
- `kvStoreService: IKVStoreService` - For marking transaction complete

**Constructor:**
```csharp
public SendingCompletionStateHandler(
    StateMachine stateMachine,
    ResultAssembler resultAssembler,
    NotificationSender notificationSender,
    IKVStoreService kvStoreService)
{
    this.stateMachine = stateMachine;
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
