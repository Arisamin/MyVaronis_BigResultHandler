# BigResultHandler - Architecture

## Overview
The BigResultHandler is a component that manages unlimited-size results by coordinating header and payload data received through separate RabbitMQ queues, ensuring complete result assembly before notification.

The component operates as a state machine with two states to enable recovery from crashes and continuation of processing from the last known state.

**Note on Terminology:** Throughout this document, "KV Store" refers to **Azure Table Storage**, a NoSQL key-value store that organizes data using PartitionKey (TransactionID) and RowKey (composite key of SeriesType and Ordinal). This allows efficient querying of all entries within a transaction namespace.

**Message Serialization:** All RabbitMQ messages (Header Queue, Payload Queue, and Notification Queue) use **Protocol Buffers (protobuf)** for serialization. The `.proto` schema definitions are shared across all participants in the flow, ensuring consistent message format and structure across producers and consumers.

## High-Level Architecture

See the architecture diagram: [architecture.mermaid](architecture.mermaid)

To view the diagram, open the `.mermaid` file in VS Code and use `Ctrl+Shift+P` → "Mermaid: Preview"

## Key Components

### 1. **Header Consumer**
- Listens to Header Queue
- Receives result headers with Transaction ID and payload series types
- Forwards header messages to Result Handler

### 2. **Payload Consumer**
- Listens to Payload Queue (handles 250MB messages)
- Receives payload messages with Transaction ID, series type, message ordinal, and total message count in the series
- Forwards payload messages to Result Handler

### 3. **Result Handler**
- Receives header and payload messages from consumers
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

### 4. **Result Assembler**
- Retrieves all blob URIs for a transaction from KV Store by querying the Transaction ID namespace
- Accesses KV Store namespace `TransactionID` and retrieves all key-value pairs:
  - Each entry has key `<SeriesType, Ordinal>` and value `BlobURI`
- Collects all tuples: `[(SeriesType, Ordinal, BlobURI), ...]`
- Organizes the blob URI references by series type
- Prepares structured metadata for the Result Notifier
- Does not assemble actual data - only organizes the references to where data is stored

### 5. **Result Notifier**
- Receives organized blob URIs from Result Assembler
- Publishes notification to Notification Queue containing:
  - Transaction ID
  - All blob URIs organized by series type and ordinal
- Client receives notification and can:
  - Access all pieces of every series type
  - Sort them by ordinal within each series
  - Retrieve actual data from Azure Storage using the blob URIs
  - Restore the complete result by assembling data from all blobs

### 6. **KV Store (Key-Value Store)**
- External storage system with namespace support
- Organized by Transaction ID namespaces
- Data structure:
  - **Namespace**: `TransactionID`
  - **Key**: `<SeriesType, Ordinal>`
  - **Value**: `BlobURI`
- Each payload message creates one entry within the Transaction ID namespace
- Result Assembler queries a Transaction ID namespace to retrieve all key-value pairs
- Provides metadata for organizing blob URI references

### 7. **Azure Storage**
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

The BigResultHandler operates as a state machine with two states:

### **State Machine Implementation**

The state machine is implemented using the **State Pattern** with the following design:

- **State Handler Objects**: Each state is implemented as a separate handler object designed to handle state-specific logic
  - `AwaitingResultsStateHandler`: Handles State 1 operations
  - `SendingCompletionStateHandler`: Handles State 2 operations

- **Context Object**: A context object is maintained throughout state transitions and is accessible from every state handler
  - Contains the **Transaction ID** for identifying the transaction being processed
  - Passed to each state handler when transitioning between states
  - Provides continuity and state information across the state machine lifecycle

- **Shared Resources**: Multiple state machine instances can exist in the process simultaneously to handle concurrent transactions
  - **KV Store**: Shared among all state machine instances
  - **Azure Storage**: Shared among all state machine instances
  - Each state machine instance operates on its own transaction data using the unique Transaction ID from the context object

- **Data Isolation**: State handlers interact with their relevant data in KV Store and Azure Storage by:
  - Using the **Transaction ID** from the context object as the key
  - Each transaction's data is isolated by its unique Transaction ID
  - Multiple state machines can safely coexist without interference

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
