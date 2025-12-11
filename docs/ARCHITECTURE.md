# BigResultHandler - Architecture

## Overview
The BigResultHandler is a component that manages unlimited-size results by coordinating header and payload data received through separate RabbitMQ queues, ensuring complete result assembly before notification.

The component operates as a state machine with two states to enable recovery from crashes and continuation of processing from the last known state.

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
- Uploads every payload message data to Azure Storage as a blob
- Receives blob URI from Azure Storage
- Writes transaction metadata (including blob URI) to KV Store

### 4. **Result Assembler**
- Retrieves transaction metadata from KV Store
- Constructs final result object

### 5. **Result Notifier**
- Publishes complete result notification to Notification Queue

### 6. **KV Store (Key-Value Store)**
- External storage system
- Stores transaction metadata indexed by Transaction ID
- Contains blob URI references to Azure Storage
- Provides metadata for result assembly

### 7. **Azure Storage**
- External blob storage
- Stores complete assembled result data
- Returns blob URI after successful upload
- Enables handling of unlimited result sizes

## State Machine

The BigResultHandler operates as a state machine with two states:

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

## Data Flow

1. **Header Queue** receives header message with Transaction ID and expected message type series
2. **Payload Queue** receives payload messages from different type series, each with Transaction ID
3. **Header Consumer** consumes header message and forwards to Result Handler
4. **Payload Consumer** consumes payload messages and forwards to Result Handler (unaware of headers or coordination)
5. **Result Handler** receives header and interprets which message type series to expect
6. **Result Handler** receives payload messages from different series and enters **State 1: Awaiting Results**
7. **Result Handler** coordinates the different message series using header as the binder
8. **Result Handler** tracks completion of all message series specified in the header
9. When all expected messages are received, **Result Handler** transitions to **State 2: Sending Completion Message**
10. **Result Handler** uploads the complete result data to **Azure Storage** as a blob
11. **Azure Storage** returns the blob URI to **Result Handler**
12. **Result Handler** writes transaction metadata (including blob URI) to **KV Store**
13. **Result Assembler** retrieves metadata from **KV Store** to construct final result object
14. **Result Notifier** publishes notification to **Notification Queue** indicating result is ready

## Transaction ID Binding

- Every message (header and payload chunks) contains the same Transaction ID
- Transaction ID is the key for coordinating and assembling results
- Ensures correct matching of headers with their corresponding payloads

## Scalability Considerations

- Payload chunking at 250MB allows handling unlimited result sizes
- Component can handle multiple concurrent transactions
- Storage must support concurrent access by Transaction ID
