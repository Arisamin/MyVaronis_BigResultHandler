# LLD Interview Training Plan
**Start date:** June 18, 2026 | **Target:** 6-week structured preparation

---

## Overview

| Phase | Weeks | Focus |
|---|---|---|
| Foundations | Week 1 | Design patterns refresher (C# context) |
| Core LLD Problems | Weeks 2–3 | Tier 1 problems in interview format |
| Own Experience | Weeks 4–5 | Articulate your Varonis/NICE work in interview language |
| Mock Interviews | Week 6 | Full timed mocks, strict feedback |

---

## Week 1 — June 18–24: Design Patterns Refresher
*Not interview format yet. Goal: recover vocabulary and C# mappings.*

**Session per day: 30–45 min**

| Day | Topic | C# Pattern to Map |
|---|---|---|
| Wed 18 | Observer + Event-Driven | `event`, delegates, `IObservable<T>` |
| Thu 19 | Strategy + Command | Interface injection, `Func<>` delegates |
| Fri 20 | Repository + Unit of Work | `IRepository<T>`, EF Core |
| Sat 21 | Factory + Abstract Factory | `IServiceProvider`, factory methods |
| Sun 22 | Decorator + Chain of Responsibility | Middleware pipeline, `IHostedService` |
| Mon 23 | Review weak spots from this week | — |
| Tue 24 | Free / catch-up | — |

**Prompt to use:**
```
Quiz me on the [PATTERN] design pattern. I'm a senior C# backend developer.
Ask me: what it is, when to use it vs. alternatives, and how I'd implement it in C#/.NET.
Don't give the answer first — ask me, then correct me.
```

---

## Weeks 2–3 — June 25 – July 8: Tier 1 LLD Problems (Interview Format)

**2 sessions per week, 45–60 min each. Use the structured trainer prompt below.**

| Session | Date | Problem | Key Concepts |
|---|---|---|---|
| 1 | Jun 25 | **Rate Limiter** | Token bucket vs sliding window, Redis, distributed |
| 2 | Jun 27 | **Task / Job Queue** | Priority, dead-letter, retry — leverage RabbitMQ knowledge |
| 3 | Jun 30 | **Cache System** | LRU implementation, write-through vs write-behind, eviction |
| 4 | Jul 2 | **Notification System** | Fan-out, push vs pull, delivery guarantees |
| 5 | Jul 7 | **URL Shortener** | Hashing, collision, DB schema, redirect |
| 6 | Jul 8 | **Replay session** — redo the weakest of the 5 above | — |

---

## Weeks 4–5 — July 9–22: Tier 2 Problems + Own Experience

**Mix of new Tier 2 topics and translating your real work into interview language.**

| Session | Date | Problem | Notes |
|---|---|---|---|
| 7 | Jul 9 | **Distributed Lock** | Redis vs DB, deadlock prevention — hits your distributed systems background |
| 8 | Jul 11 | **Event-Driven Pipeline** | Exactly/at-least/at-most-once — this is BigResultHandler territory |
| 9 | Jul 14 | **"Design the Varonis data pipeline"** | Translate your 100TB pipeline into LLD interview language |
| 10 | Jul 16 | **Logging / Monitoring System** | Structured logging, aggregation, alerting — your observability experience |
| 11 | Jul 19 | **"Design the NICE event system"** | Translate WebSocket/WCF real-time system into design interview format |
| 12 | Jul 21 | **File Storage System** | Chunking, deduplication, metadata — Azure Blob directly applicable |
| — | Jul 22 | Buffer / catch-up | — |

---

## Week 6 — July 23–29: Mock Interviews

**Full 45-minute sessions. Timed. No hints. Strict feedback at end.**

| Session | Date | Format |
|---|---|---|
| Mock 1 | Jul 23 | Interviewer picks from Tier 1 — you don't know which one |
| Mock 2 | Jul 25 | Interviewer picks from Tier 2 — you don't know which one |
| Mock 3 | Jul 28 | Full mock: requirements gathering → design → deep dive → trade-offs |

**Mock prompt:**
```
Act as a strict senior interviewer at a top-tier tech company.
Give me an LLD problem without telling me the topic in advance.
I have 45 minutes. Do not give hints. Ask follow-up questions.
At the end, give structured feedback: what I got right, what I missed,
what a stronger answer would look like. Begin.
```

---

## Tier 3 — Optional / Bonus Topics
*Do these if time allows or if a specific company requires them.*

- **Search system** — inverted index, tokenization
- **Leaderboard** — Redis sorted sets
- **Chat system** — WebSockets (your NICE experience applies directly)

---

## Master Trainer Prompt
*Paste this at the start of every Tier 1/2 session:*

```
You are my LLD interview trainer.

My background: Senior C# backend developer, 10+ years experience at enterprise
companies (Varonis, NICE Systems). Returning after a career break and preparing
for LLD interviews.

Today's session: [TOPIC]

Training mode:
1. Give me a design problem.
2. I will propose a solution.
3. Challenge my decisions and ask follow-up questions (like a real interviewer).
4. After 3–4 exchanges, give structured feedback:
   - What I got right
   - What I missed
   - What a stronger answer would include
5. Then move to the next sub-topic or wrap up.

Do NOT give me the answer upfront. Make me work for it.

Additional: When I propose solutions, ask me to map them to C#/.NET specifics:
- Which design patterns? (Repository, Factory, Strategy, etc.)
- Which .NET abstractions? (IHostedService, Channel<T>, CancellationToken, etc.)
- Threading model? (async/await, TPL, etc.)
- Which NuGet packages would I use?

Start with the first problem.
```

---

## Answer Structure to Internalize

Every LLD answer should follow this arc:

1. **Clarify requirements** — functional + non-functional (scale, SLA, consistency)
2. **High-level design** — main components, data flow
3. **Component deep-dive** — pick 1–2 critical components and go deep
4. **Trade-offs** — why this approach vs. alternatives
5. **Handle follow-ups** — "What if load 10x?" / "What if a node fails?"

---

## Your Key Advantages (Don't Forget These)
- **100TB data pipelines** → scale, memory efficiency, throughput
- **RabbitMQ event systems** → queuing, delivery guarantees, dead-letter
- **Azure Blob + Table + ADX** → storage tiering, time-series, large-scale querying
- **Multi-threaded crash recovery** → state machines, idempotency, exactly-once semantics
- **Real-time WebSocket systems at NICE** → event handling, connection management

You're not fabricating examples — you lived them. The training is about articulating them under pressure.
