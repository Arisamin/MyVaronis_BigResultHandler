# CV Bullet Challenge: Monolith to Microservices Migration

## The CV Bullet Under Review

> *"Transformed on-prem monolithic products into cloud-native SaaS, decomposing services into a microservice architecture and migrating embedded DB logic to C# application-layer code."*

---

## Initial Challenge

This bullet didn't appear in the original CV notes. The concern raised was:

> If you didn't actually do this, it's a fabrication Gemini hallucinated. This is a serious risk — interviewers will ask about it directly. Only keep it if it's genuinely true.

The response was that this was indeed real work — transforming services from on-prem to SaaS — but the architecture wasn't personally designed; it was dev work done together with the whole team, including some low-level design and picking up practices and principles along the way.

To determine whether the bullet is defensible, a structured set of interview questions was posed.

---

## Interview Questions & Answers

### Level 1 — "Did you actually do this?"

**Q1: What was the on-prem component you migrated? What did it do, and what was its replacement in the cloud?**

> The on-prem monolith was a service that received requests of different kinds of unrelated business flows.
> The service used a proprietary infrastructure to intercept a request on a main server, process and enrich it, save it in a DB, and then send it to a secondary server where it was executed (e.g., "fix permission on server A").
> The cloud replacement broke this monolith into two new microservices:
> - One was responsible for receiving a specific kind of request, processing and enriching it, and forwarding it in a standardized form to the main service.
> - The main service was responsible for saving it in the DB, forwarding it to the relevant secondary service, and executing it there.
> The proprietary messaging infrastructure between the main server and the secondary server was replaced with RabbitMQ.

**Q2: What specific dev work did you personally own?**

> Involved in the development of both services — some low-level design in both, and then development tasks.

**Q3: What was the team size and how was the work divided?**

> Team of 4 developers. Low-level design was done in pairs, then broken into tasks distributed among all team members.

---

### Level 2 — Architecture Decisions

**Q4: The original monolith handled "unrelated flows" in one place. When you broke it up, how did you decide which flows belonged together in the same microservice vs. which needed their own?**

> In the monolith, the different supported requests represented different business flows — separate DB tables, different GUIs that generated them. The reason all flows were handled within the monolith was because the request delivery mechanism and execution mechanism were common to all flows, and the code was structured such that it was easier to put all logic in one solution rather than breaking the shared infrastructure into something reusable across separate solutions.

**Q5: The new first service processes and enriches a request, then forwards it to the main service. How was that handoff done — synchronous or async? Why?**

> Async. The sender received a confirmation with a `RequestId` shortly after the request processing started. The actual work could sometimes take hours or days, so sync made no sense. The user could follow progress in a dedicated dashboard.

**Q6: You replaced the proprietary messaging infrastructure with RabbitMQ. What problems did the old system have? What did RabbitMQ give you?**

> The old infrastructure required DB access from both the main and the secondary servers. RabbitMQ provided a simpler solution for data transfer between the servers — decoupling them and removing the shared DB dependency.

*(Note for interview prep: if probed further, a stronger framing is: the proprietary system tightly coupled the two servers through a shared database, which was a reliability and scalability bottleneck. RabbitMQ decoupled them, added retry/durability semantics, and removed the need for both servers to have DB access.)*

**Q7: Did the two new microservices share a database, or did each get its own?**

> No shared tables at all. Each microservice had its own set of tables, identified by naming conventions. Each service saved its data in its own tables during processing. When enrichment was complete, it forwarded the request to the main service in a normalized/standardized form. The main service then used those standardized requests in its own tables, ready to be sent to the secondary server.

**Q8: "Migrating embedded DB logic to the application layer" — give a concrete example.**

> Part of the old request delivery infrastructure was implemented in DB stored procedures. Also, the translation from a flow-specific request to a standardized main-service request was previously done in stored procedures. After the migration, this translation was achieved via a contract hierarchy in C# code.

---

### Level 3 — Migration Process

**Q9: Was there a period where the monolith and new services ran simultaneously? How did you route traffic?**

> There was no support for running both simultaneously in the same flow. Even though they could coexist in the environment, they served completely separate flows and components from start to finish — no shared traffic routing was needed.

**Q10: What was the hardest technical problem you personally hit during this work?**

> Some of the infrastructure from the monolith was reused in the new microservices architecture. This resulted in a lot of compilation issues because those shared parts were still serving both the monolith and the microservices codebases at the same time, until the monolith sunset was completed.

---

## Verdict

**The bullet is defensible as written.**

The answers demonstrate genuine participation and developer-level understanding. The concrete details — the `RequestId` confirmation pattern, hours-long async flows, contract hierarchy replacing stored procedures, table ownership per service, shared-infrastructure compilation conflicts — are the kind of specifics that only come from someone who was actually there.

### What landed well:
- **Async design**: Complete, credible story with fire-and-forget, `RequestId`, and a progress dashboard.
- **DB to app layer**: Directly validates the bullet — stored procedures replaced by a C# contract hierarchy.
- **DB separation**: Clear ownership per service, standardized handoff contract.
- **Hardest problem**: A real, believable engineering war story unique to this migration.

### One weak spot:
The RabbitMQ motivation answer ("DB access from both servers") describes a symptom rather than the root cause. Worth internalizing the fuller answer (decoupling, retry/durability, removing shared DB dependency) before interviews.

---

## Final Recommended Wording

> *"Transformed on-prem monolithic products into cloud-native SaaS, decomposing services into a microservices architecture and migrating embedded DB logic to C# application-layer code."*

**Keep the original Gemini wording. No softening needed.**

### On ownership language

"Contributed" was considered but rejected — it undersells the work and sounds like a footnote. "Played a key role" was also considered but rejected — it implies doing more than the rest, which isn't accurate either (this was a team of 4 doing roughly equal shares).

The resolution: a CV bullet isn't a credit allocation document. It answers the question *"Have you done this kind of work? Can you speak to it?"* — and the answer is yes. Every developer on that team of 4 can honestly write the same bullet. That's normal and expected. Recruiters and interviewers know software is built by teams.

Standard CV convention is to state what was done in active voice, without ownership qualifiers. The substance is what matters — and that substance is defensible.
