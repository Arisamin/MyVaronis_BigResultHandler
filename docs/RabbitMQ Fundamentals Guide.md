# RabbitMQ Fundamentals: Broker, Queue, Topic, Cluster & Routing

A practical guide to understanding RabbitMQ concepts and how they relate to the BigResultHandler architecture.

## Core Components

### 1. **Broker**
- A **single running instance of RabbitMQ** server
- Hostname: `localhost`, `rabbitmq.example.com`, or IP address
- Port: `5672` (AMQP protocol, default)
- The server that stores messages and delivers them to consumers

**In Your Context:**
- When BigResultHandler creates transaction-specific queues, they exist on a particular **broker**
- If you scale out to multiple instances, each could connect to the same broker or different brokers
- This is the infrastructure piece you've been abstracting away

### 2. **Queue**
- A **message buffer** that stores messages temporarily
- Identified by name: `my-queue`, `transaction-12345-header`, etc.
- Messages are stored here until a consumer processes them
- **Only stored inside a queue** — if no queue, nowhere to put the message
- FIFO (First In, First Out) by default
- Consumer connects to queue and receives messages

**In Your Context:**
```
transaction-{TransactionID}-header
transaction-{TransactionID}-payload
```
These are separate queues per transaction.

**Key Property:** Durable vs. Non-Durable
- **Durable**: Queue persists if broker restarts (messages survive crash)
- **Non-durable**: Queue is lost if broker restarts (transient only)

### 3. **Exchange**
- A **router** that accepts messages from producers
- Routes messages to one or more queues based on rules
- Never stores messages itself (just routes them)
- Types: `direct`, `fanout`, `topic`, `headers`

**Example Flow:**
```
Producer → Exchange (router) → Queue → Consumer
```

**In Your Context:**
- Your design currently routes directly to transaction-specific queues (likely using `direct` exchange or no exchange)
- The Producer knows which queue names to send to

### 4. **Topic** (in messaging terminology, NOT the RabbitMQ "topic exchange")
- A **category or subject** of messages
- Examples: "email", "orders", "logs", "user-updates"
- Not a RabbitMQ object — it's a **concept** for organizing message types

**In Your Context:**
- Header messages vs. Payload messages are conceptually different "topics"
- But they're **routed to different queues**, not by exchange routing

### 5. **Binding**
- Connects an **Exchange to a Queue**
- Defines the routing rule: "messages to this exchange matching X pattern go to this queue"
- Producer sends to Exchange → Binding rules determine which Queues get the message

**Example:**
```
Exchange: "results" with type "topic"
  ↓ (binding with pattern "transaction.*.header")
Queue: "transaction-123-header"
  ↓ (binding with pattern "transaction.*.payload")
Queue: "transaction-123-payload"
```

**In Your Context:**
- Your design bypasses this complexity by routing directly to queues by name
- Producers construct the queue name themselves: `transaction-{ID}-header`

### 6. **Cluster**
- Multiple RabbitMQ **brokers connected together**
- Share the same virtual hosts and user definitions
- Messages on one broker can be consumed by clients connected to any broker in the cluster
- Provides **high availability** (if one broker fails, others continue)

**Example Cluster:**
```
RabbitMQ-Node-1 (rabbitmq-1.example.com:5672)
RabbitMQ-Node-2 (rabbitmq-2.example.com:5672)
RabbitMQ-Node-3 (rabbitmq-3.example.com:5672)
         ↓
    Cluster shared state
```

**In Your Context:**
- All queues could live on a single broker (simpler, no clustering complexity)
- Or on a cluster for fault tolerance
- But transaction-specific queues are still **bound to a specific broker's storage**

---

## How Messages Flow

### Simple Flow (Your Current Design)

```
1. Producer connects to Broker: localhost:5672
   ↓
2. Producer declares queues (idempotent - creates if not exists):
   - transaction-A-header
   - transaction-A-payload
   ↓
3. Producer sends header message to transaction-A-header queue
   Producer sends payload messages to transaction-A-payload queue
   ↓
4. Consumer (same Broker) connects and subscribes to:
   - transaction-A-header
   - transaction-A-payload
   ↓
5. Broker delivers messages to Consumer
```

**In Your Context:**
```
StateMachine (on Instance A):
  - Creates transaction-123-header and transaction-123-payload
  - Tells Producer: "Send my messages to these queue names on this broker"
  
Producer:
  - Sends header → transaction-123-header on that broker
  - Sends payloads → transaction-123-payload on that broker
  
Consumers (registered callbacks):
  - HeaderConsumer listens to transaction-123-header
  - PayloadConsumer listens to transaction-123-payload
  
Instance A:
  - Receives all messages for transaction-123 (because it created those queues)
```

### Complex Flow with Exchange/Binding

```
Producer → Exchange (topic: "results.*")
            ↓ (binding rule: "results.transaction.123.#" → transaction-123-header queue)
            ↓ (binding rule: "results.transaction.123.#" → transaction-123-payload queue)
            Queue: transaction-123-header
            Queue: transaction-123-payload
            ↓
            Consumer subscribes and receives
```

---

## Connection Concepts

### Channel
- Logical connection **multiplexed over a single TCP connection**
- Think of it as a **sub-connection** within the main connection
- Lightweight to create many channels per connection
- Each channel has its own prefetch and message handling

```csharp
IConnection connection = factory.CreateConnection();  // TCP connection to broker
IModel channel = connection.CreateModel();              // Logical channel
channel.BasicConsume(queueName, noAck: false, ...);    // Consume on this channel
```

**In Your Context:**
- Each ResultHandler or queue consumer might use a channel
- Prefetch on a channel controls how many messages are delivered at once

### Prefetch (QoS - Quality of Service)
- **How many messages** the broker delivers to a consumer at once
- Default: unlimited (broker sends as fast as possible)
- Set it to control memory/backpressure

```csharp
channel.BasicQos(0, prefetchSize: 1, isGlobal: false);  // Deliver 1 message at a time
```

**In Your Context:**
- Important for handling 250 MB payload messages
- Low prefetch = controlled memory usage
- High prefetch = faster throughput but more memory

---

## Your Architecture & RabbitMQ

### Current Design (What You've Learned)
```
┌─────────────────────────────────────────────────────┐
│                    Broker                           │
│  (Single RabbitMQ instance on some hostname:port)   │
│                                                      │
│  ┌────────────────────────────────────────────────┐ │
│  │ Virtual Host: /                                │ │
│  │                                                 │ │
│  │ Queues:                                         │ │
│  │ ├─ transaction-A-header                        │ │
│  │ ├─ transaction-A-payload                       │ │
│  │ ├─ transaction-B-header                        │ │
│  │ ├─ transaction-B-payload                       │ │
│  │ ├─ transaction-C-header                        │ │
│  │ └─ transaction-C-payload                       │ │
│  │                                                 │ │
│  └────────────────────────────────────────────────┘ │
│                                                      │
│  Consumers (registered callbacks):                   │
│  Instance-A → subscribes to transaction-A queues   │
│  Instance-B → subscribes to transaction-B queues   │
│  Instance-C → subscribes to transaction-C queues   │
│                                                      │
└─────────────────────────────────────────────────────┘
```

### Scaling Issues Exposed
1. **Single Broker Bottleneck**: All transactions compete on one broker
2. **Instance Affinity**: Instance A can only handle Transaction A (its queues)
3. **Failover Problem**: If Instance A crashes, transaction-A queues are orphaned (on that broker, no consumer)
4. **No Broker Failover**: If the broker goes down, all queues are inaccessible

---

## Key Relationships

| Component | Purpose | In Your Design |
|-----------|---------|-----------------|
| **Broker** | Message server | Single RabbitMQ instance |
| **Queue** | Message buffer | One pair per transaction |
| **Exchange** | Router | Direct queue addressing (no explicit exchange) |
| **Binding** | Exchange → Queue routing | Manual queue naming |
| **Channel** | Logical connection | Used by consumers for message delivery |
| **Prefetch** | Flow control | Limits in-flight messages to consumer |
| **Cluster** | HA for brokers | Not mentioned in your design (single broker) |

---

## Recommended Resources

**Official RabbitMQ .NET Tutorials:**
- Tutorial 1 (Hello World): https://www.rabbitmq.com/tutorials/tutorial-one-dotnet.html
- Covers basic Producer → Queue → Consumer flow
- Uses C# and very practical

**Key Concepts:**
- https://www.rabbitmq.com/tutorials/amqp-concepts
- Explains AMQP 0-9-1 protocol (what RabbitMQ uses)

**For Your Scale-Out Challenge:**
- Look into "Consistent Hashing Plugin" or "RabbitMQ Clustering"
- Or "Priority Queue" feature for routing decisions
- But honestly, you'll likely need a custom transaction registry/orchestrator

---

## Summary

The **hidden infrastructure piece** you've been abstracting away:
1. Each transaction gets **its own queue pair** on a **specific broker**
2. The StateMachine **creates these queues** and tells the Producer where they are
3. The Producer **knows the queue names** and sends to those specific queues
4. Consumers on the StateMachine's instance **subscribe to those queues**
5. Messages arrive at the **correct instance** because it created those unique queues

For scale-out, you need to answer:
- **Which broker** will transaction X's queues live on?
- **How does a new instance find** the queues for a failed transaction?
- **How do you prevent conflicts** if multiple instances try to claim the same transaction?

These answers require infrastructure beyond RabbitMQ itself—you need a transaction registry or orchestrator.

---

*This guide is tailored to clarifying the design decisions in BigResultHandler, particularly the implicit transaction affinity through queue naming.*
