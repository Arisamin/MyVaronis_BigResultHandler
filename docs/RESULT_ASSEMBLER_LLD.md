# Result Assembler - Low Level Design

> **Note:** This document is Copilot-generated content based on high-level architecture discussions. A user-verified version will be created later.

## Overview
The Result Assembler is responsible for retrieving all blob URIs for a completed transaction from KV Store (Azure Table Storage), organizing them by series type and ordinal, and preparing structured metadata for the Result Notifier.

**Key Principle:** The Result Assembler does NOT assemble actual data - it only organizes references (blob URIs) to where data is stored in Azure Storage.

## Class Structure

### ResultAssembler
Main class that orchestrates blob URI retrieval and organization.

**Properties:**
- `kvStoreService: IKVStoreService` - Service for querying Azure Table Storage
- `notificationService: INotificationService` - Service for publishing to Notification Queue

**Methods:**
- `AssembleAndNotify(transactionId: string): void` - Main entry point
- `RetrieveBlobMappings(transactionId: string): List<BlobMapping>` - Query KV Store for all blob URIs
- `OrganizeBySeriesType(blobMappings: List<BlobMapping>): Dictionary<string, List<BlobReference>>` - Group and sort URIs
- `ValidateCompleteness(organizedData: Dictionary<string, List<BlobReference>>): bool` - Verify no gaps in ordinals
- `CreateNotificationMessage(transactionId: string, organizedData: Dictionary<string, List<BlobReference>>): NotificationMessage` - Build notification
- `PublishNotification(notificationMessage: NotificationMessage): void` - Send to queue

---

### BlobMapping
Represents a single entry retrieved from KV Store.

**Properties:**
- `TransactionId: string` - Transaction identifier (from PartitionKey)
- `SeriesType: string` - Series type identifier
- `Ordinal: int` - Position within series
- `BlobUri: string` - URI to blob in Azure Storage
- `Timestamp: DateTime` - When the entry was created

---

### BlobReference
Organized reference to a blob within a series.

**Properties:**
- `Ordinal: int` - Position within series
- `BlobUri: string` - URI to blob in Azure Storage

---

### SeriesData
Container for all blob references within a single series type.

**Properties:**
- `SeriesType: string` - Type identifier
- `TotalCount: int` - Total number of blobs in series
- `BlobReferences: List<BlobReference>` - Ordered list of blob references
- `IsComplete: bool` - True if all ordinals from 1 to TotalCount are present

**Methods:**
- `ValidateSequence(): bool` - Check for gaps in ordinal sequence
- `Sort(): void` - Sort BlobReferences by Ordinal ascending

---

### NotificationMessage
Message published to Notification Queue for consumer retrieval.

**Properties:**
- `TransactionId: string` - Transaction identifier
- `CompletionTimestamp: DateTime` - When the transaction completed
- `SeriesCollection: List<SeriesData>` - All series with their blob references
- `TotalBlobCount: int` - Total number of blobs across all series
- `TotalDataSize: long` - Total size in bytes (if tracked)

**Methods:**
- `Serialize(): string` - Convert to JSON for queue message

---

## Supporting Services

### IKVStoreService
Interface for Azure Table Storage operations (implemented in Result Handler shared services).

**Methods:**
- `GetAllBlobMappings(transactionId: string): List<BlobMapping>`
  - Queries Azure Table Storage for all entries with PartitionKey = transactionId
  - Excludes special rows (e.g., `__context__`)
  - Returns all blob mappings for the transaction
  - Example query: `PartitionKey eq 'transactionId' and RowKey ne '__context__'`

---

### INotificationService
Interface for publishing notification messages to RabbitMQ.

**Methods:**
- `PublishNotification(notificationMessage: NotificationMessage): void`
  - Serializes notification to JSON
  - Publishes to Notification Queue
  - Includes transaction ID in message headers for routing
  - Implements retry logic for transient failures

---

## Processing Flow

### Main Assembly Flow
```
1. Entry Point
   ↓
   ResultAssembler.AssembleAndNotify(transactionId)
   
2. Retrieve Data
   ↓
   RetrieveBlobMappings(transactionId)
   → Query KV Store (Azure Table Storage)
   → Get all entries with PartitionKey = transactionId
   → Parse RowKey format: "{SeriesType}:{Ordinal}"
   → Return List<BlobMapping>
   
3. Organize Data
   ↓
   OrganizeBySeriesType(blobMappings)
   → Group by SeriesType
   → For each series:
      - Create SeriesData object
      - Add all BlobReferences
      - Sort by Ordinal ascending
      - Set TotalCount = max(Ordinal)
   → Return Dictionary<string, List<BlobReference>>
   
4. Validate
   ↓
   ValidateCompleteness(organizedData)
   → For each series:
      - Check ordinals are sequential: 1, 2, 3, ..., TotalCount
      - Identify any gaps
      - Return false if gaps found
   → Return true if all series complete
   
5. Create Notification
   ↓
   CreateNotificationMessage(transactionId, organizedData)
   → Build NotificationMessage
   → Include transaction ID
   → Include all SeriesData objects
   → Calculate total blob count
   → Set completion timestamp
   
6. Publish
   ↓
   PublishNotification(notificationMessage)
   → Serialize to JSON
   → Publish to Notification Queue
   → Log success
```

---

## Detailed Method Implementations

### AssembleAndNotify
```csharp
public void AssembleAndNotify(string transactionId) {
    try {
        // Step 1: Retrieve all blob mappings from KV Store
        var blobMappings = RetrieveBlobMappings(transactionId);
        
        if (blobMappings.Count == 0) {
            throw new InvalidOperationException(
                $"No blob mappings found for transaction {transactionId}");
        }
        
        // Step 2: Organize by series type
        var organizedData = OrganizeBySeriesType(blobMappings);
        
        // Step 3: Validate completeness
        if (!ValidateCompleteness(organizedData)) {
            throw new InvalidOperationException(
                $"Transaction {transactionId} has incomplete series");
        }
        
        // Step 4: Create notification message
        var notificationMessage = CreateNotificationMessage(
            transactionId, organizedData);
        
        // Step 5: Publish notification
        PublishNotification(notificationMessage);
        
        Log.Info($"Successfully assembled and notified transaction {transactionId}");
    }
    catch (Exception ex) {
        Log.Error($"Failed to assemble transaction {transactionId}", ex);
        throw;
    }
}
```

### RetrieveBlobMappings
```csharp
private List<BlobMapping> RetrieveBlobMappings(string transactionId) {
    // Query Azure Table Storage
    var mappings = kvStoreService.GetAllBlobMappings(transactionId);
    
    // Parse each mapping from Table Storage format
    var blobMappings = new List<BlobMapping>();
    
    foreach (var entity in mappings) {
        // RowKey format: "{SeriesType}:{Ordinal}"
        var parts = entity.RowKey.Split(':');
        
        if (parts.Length != 2) {
            Log.Warning($"Invalid RowKey format: {entity.RowKey}");
            continue;
        }
        
        var mapping = new BlobMapping {
            TransactionId = transactionId,
            SeriesType = parts[0],
            Ordinal = int.Parse(parts[1]),
            BlobUri = entity.BlobUri,
            Timestamp = entity.Timestamp
        };
        
        blobMappings.Add(mapping);
    }
    
    return blobMappings;
}
```

### OrganizeBySeriesType
```csharp
private Dictionary<string, List<BlobReference>> OrganizeBySeriesType(
    List<BlobMapping> blobMappings) {
    
    var organized = new Dictionary<string, List<BlobReference>>();
    
    // Group by series type
    foreach (var mapping in blobMappings) {
        if (!organized.ContainsKey(mapping.SeriesType)) {
            organized[mapping.SeriesType] = new List<BlobReference>();
        }
        
        organized[mapping.SeriesType].Add(new BlobReference {
            Ordinal = mapping.Ordinal,
            BlobUri = mapping.BlobUri
        });
    }
    
    // Sort each series by ordinal
    foreach (var seriesType in organized.Keys) {
        organized[seriesType] = organized[seriesType]
            .OrderBy(br => br.Ordinal)
            .ToList();
    }
    
    return organized;
}
```

### ValidateCompleteness
```csharp
private bool ValidateCompleteness(
    Dictionary<string, List<BlobReference>> organizedData) {
    
    foreach (var kvp in organizedData) {
        var seriesType = kvp.Key;
        var blobRefs = kvp.Value;
        
        if (blobRefs.Count == 0) {
            Log.Warning($"Series {seriesType} has no blob references");
            return false;
        }
        
        // Expected ordinals: 1, 2, 3, ..., blobRefs.Count
        for (int i = 0; i < blobRefs.Count; i++) {
            int expectedOrdinal = i + 1;
            int actualOrdinal = blobRefs[i].Ordinal;
            
            if (actualOrdinal != expectedOrdinal) {
                Log.Warning(
                    $"Series {seriesType} has gap: expected {expectedOrdinal}, found {actualOrdinal}");
                return false;
            }
        }
    }
    
    return true;
}
```

### CreateNotificationMessage
```csharp
private NotificationMessage CreateNotificationMessage(
    string transactionId,
    Dictionary<string, List<BlobReference>> organizedData) {
    
    var seriesCollection = new List<SeriesData>();
    int totalBlobCount = 0;
    
    foreach (var kvp in organizedData) {
        var seriesData = new SeriesData {
            SeriesType = kvp.Key,
            TotalCount = kvp.Value.Count,
            BlobReferences = kvp.Value,
            IsComplete = true
        };
        
        seriesCollection.Add(seriesData);
        totalBlobCount += kvp.Value.Count;
    }
    
    return new NotificationMessage {
        TransactionId = transactionId,
        CompletionTimestamp = DateTime.UtcNow,
        SeriesCollection = seriesCollection,
        TotalBlobCount = totalBlobCount
    };
}
```

### PublishNotification
```csharp
private void PublishNotification(NotificationMessage notificationMessage) {
    try {
        notificationService.PublishNotification(notificationMessage);
        
        Log.Info($"Published notification for transaction {notificationMessage.TransactionId} " +
                 $"with {notificationMessage.TotalBlobCount} blobs");
    }
    catch (Exception ex) {
        Log.Error($"Failed to publish notification for transaction {notificationMessage.TransactionId}", ex);
        throw;
    }
}
```

---

## Error Handling

### KV Store Query Failures
- **Transient Failures**: Retry with exponential backoff (3 attempts)
- **No Data Found**: Throw InvalidOperationException (transaction doesn't exist or not ready)
- **Partial Data**: Logged but not treated as error (validation will catch)
- **Connection Issues**: Circuit breaker pattern to prevent cascading failures

### Validation Failures
- **Missing Ordinals**: Log detailed gap information, throw InvalidOperationException
- **Empty Series**: Log warning, throw InvalidOperationException
- **Incomplete Transaction**: Should not happen if state machine is working correctly - alert immediately

### Notification Publishing Failures
- **Queue Unavailable**: Retry with exponential backoff (5 attempts)
- **Serialization Errors**: Log full message content, throw exception
- **Persistent Failures**: Alert operations team, leave transaction in "pending notification" state

---

## Concurrency Considerations

### Read-Only Operations
- Result Assembler only reads from KV Store (no writes)
- Multiple assemblers can safely read the same transaction
- No locking required for read operations

### Idempotency
- If invoked multiple times for same transaction, produces identical result
- Notification publishing should be idempotent (consumers handle duplicate notifications)
- Transaction ID in notification allows consumers to deduplicate

### Parallel Processing
- Different transactions can be assembled in parallel
- Each assembler instance operates independently
- No shared state between assembler instances

---

## Performance Considerations

### KV Store Query Optimization
- Single query retrieves all blob mappings for transaction (PartitionKey query)
- Azure Table Storage partition query is O(n) where n = number of messages
- Typical transaction: 100-1000 messages = < 100ms query time
- Large transaction: 10000 messages = < 1 second query time

### Memory Footprint
- Only blob URIs are loaded (not actual data)
- Typical blob URI: ~200 bytes
- 1000 messages = ~200KB memory
- 10000 messages = ~2MB memory
- Memory usage scales linearly with message count, not data size

### Processing Time
- Retrieval: O(n) where n = number of messages
- Organization: O(n log n) due to sorting
- Validation: O(n)
- Total: O(n log n) - dominated by sorting
- Expected processing time: < 1 second for typical transactions

---

## Monitoring & Observability

### Metrics
- `assembly_duration` - Histogram of time to assemble transaction
- `blob_count_per_transaction` - Histogram of blob counts
- `series_count_per_transaction` - Histogram of series counts
- `assembly_failures` - Counter of failed assemblies by error type
- `notification_publish_duration` - Histogram of notification publishing time
- `validation_failures` - Counter of validation failures by type

### Logging
- Assembly start/completion (with transaction ID)
- Blob mapping retrieval (count retrieved)
- Series organization (series types and counts)
- Validation results (success/failure with details)
- Notification publishing (success/failure)
- Error conditions with full context

### Alerts
- Validation failures (incomplete transactions reaching assembly)
- Notification publishing failures (queue unavailable)
- Assembly duration exceeds threshold (e.g., > 10 seconds)
- High rate of assembly failures

---

## Integration Points

### Called By
- **SendingCompletionStateHandler.OnEnter()** - When transaction completes
- Assembler is instantiated and invoked during state transition

### Dependencies
- **KV Store Service**: Query Azure Table Storage for blob mappings
- **Notification Service**: Publish notification to RabbitMQ
- **Logging Service**: Record operations and errors
- **Metrics Service**: Track performance and failures

### Output
- **Notification Message**: Published to Notification Queue
- Format: JSON containing transaction ID, completion timestamp, and organized series data
- Consumers: External systems/clients waiting for transaction completion

---

## Configuration

### Retry Policies
- `KVStoreQueryRetries` - Number of retry attempts for queries (e.g., 3)
- `KVStoreQueryRetryDelay` - Initial retry delay (e.g., 1 second)
- `NotificationPublishRetries` - Number of retry attempts (e.g., 5)
- `NotificationPublishRetryDelay` - Initial retry delay (e.g., 2 seconds)

### Timeouts
- `KVStoreQueryTimeout` - Max time for query operation (e.g., 30 seconds)
- `NotificationPublishTimeout` - Max time for publish operation (e.g., 10 seconds)
- `AssemblyTimeout` - Max total time for entire assembly process (e.g., 60 seconds)

### Validation
- `EnableStrictValidation` - Fail on any validation warnings (default: true)
- `MaxSeriesPerTransaction` - Alert if exceeded (e.g., 100)
- `MaxBlobsPerSeries` - Alert if exceeded (e.g., 10000)

---

## Example Notification Message

**Note:** This example shows the JSON representation for readability. The actual message published to RabbitMQ is serialized using **Protocol Buffers (protobuf)** based on shared `.proto` schema definitions.

```json
{
  "transactionId": "tx-12345-67890",
  "completionTimestamp": "2025-12-18T10:30:45.123Z",
  "totalBlobCount": 15,
  "seriesCollection": [
    {
      "seriesType": "UserData",
      "totalCount": 5,
      "isComplete": true,
      "blobReferences": [
        { "ordinal": 1, "blobUri": "https://storage.azure.com/tx-12345/UserData/1.blob" },
        { "ordinal": 2, "blobUri": "https://storage.azure.com/tx-12345/UserData/2.blob" },
        { "ordinal": 3, "blobUri": "https://storage.azure.com/tx-12345/UserData/3.blob" },
        { "ordinal": 4, "blobUri": "https://storage.azure.com/tx-12345/UserData/4.blob" },
        { "ordinal": 5, "blobUri": "https://storage.azure.com/tx-12345/UserData/5.blob" }
      ]
    },
    {
      "seriesType": "Metadata",
      "totalCount": 10,
      "isComplete": true,
      "blobReferences": [
        { "ordinal": 1, "blobUri": "https://storage.azure.com/tx-12345/Metadata/1.blob" },
        { "ordinal": 2, "blobUri": "https://storage.azure.com/tx-12345/Metadata/2.blob" },
        // ... ordinals 3-9 ...
        { "ordinal": 10, "blobUri": "https://storage.azure.com/tx-12345/Metadata/10.blob" }
      ]
    }
  ]
}
```

---

## Testing Considerations

### Unit Tests
- Test organization logic with various series counts and ordinal patterns
- Test validation with missing ordinals
- Test validation with duplicate ordinals
- Test empty transaction handling
- Test serialization/deserialization of notification messages

### Integration Tests
- Test with real Azure Table Storage (development environment)
- Test with real RabbitMQ (development environment)
- Test concurrent assembly of multiple transactions
- Test retry logic with simulated failures

### Load Tests
- Test with large transactions (10000+ messages)
- Test with many concurrent assemblies
- Measure memory usage and processing time
- Verify no memory leaks

### Edge Cases
- Single message per series
- Single series per transaction
- Maximum messages per series (e.g., 10000)
- Maximum series per transaction (e.g., 100)
- Transaction with no messages (error case)
