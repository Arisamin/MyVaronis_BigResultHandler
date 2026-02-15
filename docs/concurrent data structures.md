# ConcurrentDictionary Executive Summary

## What It Is
ConcurrentDictionary is a thread-safe dictionary in C# that lets multiple threads read and write at the same time without crashing or corrupting data.

## The Problem It Solves
Regular Dictionary fails when multiple threads use it:
- Crashes with exceptions
- Loses data silently
- Corrupts internal state
- Throws errors during enumeration if modified

ConcurrentDictionary prevents all of these issues.

---

## Key Difference from Dictionary

| Feature | Dictionary | ConcurrentDictionary |
|---------|-----------|---------------------|
| Thread-safe? | ❌ No | ✅ Yes |
| Performance | Faster | Slightly slower |
| Concurrent reads | ❌ Unsafe | ✅ Safe |
| Concurrent writes | ❌ Unsafe | ✅ Safe |
| Enumeration during modification | ❌ Throws exception | ✅ Safe (snapshot) |
| Best for | Single-threaded | Multi-threaded |

---

## Special Methods (These Are What Make It Useful)

Beyond the standard dictionary operations, ConcurrentDictionary adds atomic methods:

**GetOrAdd(key, value)** - Get the value if key exists, otherwise add it. Atomic operation.

**AddOrUpdate(key, addValue, updateFunc)** - Add if new, update if exists. Atomic operation.

**TryAdd(key, value)** - Add only if key doesn't exist. Returns true/false.

**TryRemove(key, out value)** - Remove if exists, returns the old value.

**TryUpdate(key, newValue, expectedOldValue)** - Update only if current value matches expected.

---

## THE CRITICAL TRAP: When It's STILL Not Thread-Safe

This is the most important thing to understand:

**Just because ConcurrentDictionary is thread-safe doesn't mean your code using it is automatically thread-safe.**

### These patterns STILL have race conditions:

```
// WRONG - checking then updating is TWO operations
if (dict.ContainsKey(key))
    dict[key]++;

// WRONG - getting then updating is TWO operations  
if (dict.TryGetValue(key, out var value))
    dict[key] = value + 1;

// WRONG - reading then writing is TWO operations
var current = dict[key];
dict[key] = current + 1;
```

Another thread can modify the dictionary between your check and your update!

### Do this instead:

```
// RIGHT - single atomic operation
dict.AddOrUpdate(key, 1, (k, v) => v + 1);

// RIGHT - single atomic operation
var value = dict.GetOrAdd(key, new List<int>());
```

---

## What's Safe and What's Not

| Operation | Thread-Safe? | Why? |
|-----------|--------------|------|
| `dict[key] = value` | ✅ Safe | Single operation |
| `var x = dict[key]` | ✅ Safe | Single operation |
| `TryGetValue(key, out value)` | ✅ Safe | Single operation |
| `ContainsKey(key)` | ✅ Safe | Single operation |
| `foreach` enumeration | ✅ Safe | Snapshot of collection |
| `if (ContainsKey) then dict[key]++` | ❌ NOT SAFE | Two separate operations |
| `if (TryGetValue) then dict[key] = value + 1` | ❌ NOT SAFE | Two separate operations |
| `var x = dict[key]; dict[key] = x + 1;` | ❌ NOT SAFE | Two separate operations |
| `AddOrUpdate(key, add, update)` | ✅ Safe | Atomic operation |
| `GetOrAdd(key, value)` | ✅ Safe | Atomic operation |

---

## When to Use Which

**Use regular Dictionary:**
- Single-threaded code
- Performance is critical
- Only one thread will ever touch it

**Use ConcurrentDictionary:**
- Multiple threads reading and writing
- Web servers, parallel processing, async operations
- Shared caches, counters, or registries

**Use Dictionary + lock statement:**
- Complex multi-step logic that can't be made atomic
- Need consistency across multiple dictionary operations

---

## The Golden Rule

**Individual operations are thread-safe. Your multi-step logic is not.**

Think of it this way: Each method call is safe, but the time between method calls is where race conditions happen. Use the atomic methods (AddOrUpdate, GetOrAdd) to combine check-and-update into one atomic operation.

---

## Bottom Line

ConcurrentDictionary won't crash when multiple threads use it, but it doesn't magically make your check-then-update patterns thread-safe. You must use its atomic methods to avoid race conditions.

---

# Other Concurrent Data Structures

## 1. **ConcurrentQueue<T>**
- **Thread-safe FIFO queue**
- **Key methods:** `Enqueue(item)`, `TryDequeue(out item)`, `TryPeek(out item)`
- **Use case:** Producer-consumer scenarios, task queues, message processing
- **Note:** No `Count` property is truly safe—it's a snapshot and may change immediately after reading

## 2. **ConcurrentStack<T>**
- **Thread-safe LIFO stack**
- **Key methods:** `Push(item)`, `TryPop(out item)`, `TryPeek(out item)`
- **Use case:** Work-stealing algorithms, temporary storage, undo operations
- **Note:** Same as queue—`Count` is a snapshot

## 3. **ConcurrentBag<T>**
- **Thread-safe unordered collection**
- **Key methods:** `Add(item)`, `TryTake(out item)`, `TryPeek(out item)`
- **Use case:** When order doesn't matter, parallel aggregation, temporary storage
- **Special feature:** Optimized for scenarios where the same thread that adds items also removes them (thread-local storage optimization)
- **Caveat:** Enumeration is slow and creates a snapshot

## 4. **BlockingCollection<T>**
- **Thread-safe collection with blocking operations**
- **Key methods:** `Add(item)`, `Take()`, `TryAdd()`, `TryTake()`, `CompleteAdding()`
- **Use case:** Classic producer-consumer with blocking, bounded queues
- **Special feature:** Can block when empty (consumer waits) or full (producer waits)
- **Wraps:** Can wrap any `IProducerConsumerCollection<T>` (e.g., ConcurrentQueue, ConcurrentStack)
- **Bounded capacity:** Can limit max size with `new BlockingCollection<T>(boundedCapacity)`

---

## Key Differences Summary

| Collection | Order | Best For | Blocking? |
|-----------|-------|----------|-----------||
| ConcurrentQueue | FIFO | Task queues, messages | ❌ No |
| ConcurrentStack | LIFO | Work-stealing, undo | ❌ No |
| ConcurrentBag | Unordered | Parallel aggregation | ❌ No |
| BlockingCollection | Depends on wrapper | Producer-consumer | ✅ Yes |
| ConcurrentDictionary | Key-based | Caches, lookups | ❌ No |

---

## Common Patterns and Gotchas

### 1. **Don't trust `Count` property**
```csharp
// BAD - Count can change between check and dequeue
if (queue.Count > 0)
    queue.TryDequeue(out var item); // May fail!

// GOOD - Just try to dequeue
if (queue.TryDequeue(out var item))
{
    // Process item
}
```

### 2. **Use `TryXxx` methods**
All concurrent collections use `TryDequeue`, `TryPop`, `TryTake`, etc., which return `bool` to indicate success. Never assume success.

### 3. **BlockingCollection for producer-consumer**
```csharp
var queue = new BlockingCollection<int>(boundedCapacity: 100);

// Producer
Task.Run(() => {
    for (int i = 0; i < 1000; i++)
        queue.Add(i); // Blocks if queue is full
    queue.CompleteAdding();
});

// Consumer
Task.Run(() => {
    foreach (var item in queue.GetConsumingEnumerable())
        ProcessItem(item); // Blocks if queue is empty
});
```

### 4. **ConcurrentBag is NOT a thread-safe List**
- No indexing, no ordering guarantees
- Best when the same thread adds and removes
- Use ConcurrentQueue or BlockingCollection if you need FIFO

---

## When to Use Each

- **ConcurrentQueue:** Task scheduling, message queues, event processing
- **ConcurrentStack:** Undo/redo, work-stealing, temporary buffering
- **ConcurrentBag:** Parallel aggregation (e.g., `Parallel.ForEach` collecting results)
- **BlockingCollection:** Classic producer-consumer with backpressure and signaling
- **ConcurrentDictionary:** Shared caches, registries, counters

---

## The Golden Rule (Same as ConcurrentDictionary)

**Individual operations are thread-safe. Your multi-step logic is not.**

Even with concurrent collections, you must avoid check-then-act patterns. Use atomic operations or external synchronization (locks, semaphores) when needed.
