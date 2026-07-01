# BigResultHandler - Interview Presentation Overview

## The Broader Product Context
The ResultHandler is part of a Varonis product called **Global Permissions Remediation** — a data security feature targeting overly permissive access on enterprise file servers.

### What the product does
- A customer points it at a storage root — a file server share or drive.
- The system has been monitoring that storage, collecting access events (who accessed what, when) since the storage was first onboarded.
- A minimum collection window is required before the storage is eligible for remediation — you can't make sound decisions without sufficient access history.
- The product scans all files and folders under the root and identifies resources carrying **global permissions** — security groups like Everyone, Domain Users, or Authenticated Users that grant access to essentially all employees.
- For each such resource, it looks at the actual access event history: which real users accessed this resource?
- It then checks: do those users already have access via a specific, non-global group on this resource?
  - **If yes** — the global permission is redundant. Remove it.
  - **If no** — create a new dedicated security group for those users, grant it access on the resource, then remove the global permission.
- End result: the resource is no longer globally accessible, but no legitimate user loses their access.

### Where ResultHandler fits
The Cloud Service acts as the coordinator between the Rule Service and the On-Prem Service. It uses a single state machine per transaction to drive three phases: forwarding work instructions to On-Prem, awaiting the results back from On-Prem, and notifying the Rule Service when the results are fully assembled.

BigResultHandler is the component inside the Cloud Service that handles the return path — receiving the large result data sent back from On-Prem, accumulating the chunks of each result series into a single blob in Azure Storage, and triggering the final notification once all series are complete.

---

## The Story In The Diagram

The diagram shows a single state machine driving the full lifecycle of one remediation transaction.

**State 0 — Request Forwarding**
The Rule Service sends the Cloud Service a request with a blob URL pointing to a list of storage roots to remediate. The Cloud Service creates a transaction and a state machine. It downloads the instruction blob, breaks it into chunks, and sends a header message to the On-Prem Service declaring that a payload series is coming — followed by the instruction chunks as payload messages via the large-message RMQ queue. The On-Prem Service reconstructs those chunks into a single work-order file describing which roots to remediate. State machine transitions to State 1.

**State 1 — Awaiting Results**
The On-Prem Service executes the actual remediation on the file system — modifying permissions, creating AD groups where needed — and tracks what it does as it goes. This is a long-running operation. The Cloud Service waits.

When On-Prem finishes, it sends a header message back to the Cloud Service (via the small-message RMQ queue) declaring which result series are coming, what each series represents, and how many chunk messages each series contains. It then sends the payload chunks via the large-message RMQ queue.

The result series represent different aspects of the remediation outcome. For example:
- **Folders** — which folders were remediated
- **Files** — which files were remediated
- **Groups** — which Active Directory groups were created in the process

Each series arrives as an independent stream of ordered chunks.

The result series represent different aspects of the remediation outcome. In this product there were at least three: **Folders** (which folders were remediated), **Files** (which files were remediated), and **Groups** (which new Active Directory groups were created during the process).

BigResultHandler receives the chunks. For each chunk, it streams the data directly to Azure Blob Storage as its own independent blob, immediately receiving a URI back. It writes that URI to Azure Table Storage keyed by TransactionId, SeriesType, and Ordinal. Each chunk has its own blob and its own tracked reference — the series is represented as an ordered collection of blobs.

When all ordinals 1 through TotalCount are present in Table Storage for a series, that series is complete.

**State 2 — Sending Completion**
The Result Assembler collects all blob URIs from Table Storage — an ordered list per series — and organizes them by series type. The Notification Sender publishes the result back to the Rule Service: per series, a sorted list of blob URLs. The Rule Service downloads all chunks in order and reassembles the full result files from Azure Blob Storage.

Transaction complete. State machine terminates. The Autofac scope for this transaction is disposed.


## How I Introduce The Project (30 seconds)
I designed BigResultHandler to remove a hard scaling limit in our legacy pipeline. The old flow forced all result data into a single RabbitMQ message, which capped what we could process in practice. I redesigned it into a multi-message, state-machine-driven architecture: the On-Prem service sends results back as multiple series of chunks, each chunk is immediately uploaded as its own blob in Azure Storage, its URI recorded in Table Storage keyed by series and ordinal. Once all expected ordinals per series are confirmed, we publish a notification with the organized collection of blob URIs per series. This removed the size ceiling and gave us reliable, recoverable processing for very large transactions.

---

## My 2-Minute Version
The core business problem was scale and reliability. In the old design, all result data had to fit into one broker message. With message-size limits, that created a hard ceiling and caused failures for large enterprise file systems.

I introduced a new architecture around a per-transaction state machine and split processing into two paths:
- A header path that defines what series we expect and what each one means.
- A payload path that carries large chunks, each identified by series type and ordinal.

The result series represent different aspects of the remediation outcome — for example, which folders were remediated, which files were remediated, and which Active Directory groups were created. Each series produces one result file, delivered as an ordered sequence of chunks.

When payload chunks arrive, we do not accumulate them in memory. Each chunk is streamed directly to Azure Blob Storage as its own independent blob, receiving a URI immediately. That URI is written to Azure Table Storage keyed by TransactionId, SeriesType, and Ordinal. Completion per series is deterministic: once all ordinals 1 through TotalCount are present in Table Storage for a series, that series is complete. When all series are complete, the result assembler queries Table Storage and collects the ordered list of URIs per series, then publishes a completion notification to the Rule Service with those organized URI collections.

This design improved three things significantly:
- Scalability: very large transactions are split across many payload messages.
- Reliability: state persistence and durable queues allow recovery after crashes.
- Correctness: idempotent processing via ordinal deduplication prevents duplicate uploads on redelivery.

---

## End-To-End Flow I Present On Whiteboard
1. Rule Service → Cloud Service: remediation request with instruction blob URL.
2. Cloud Service creates transaction + state machine (State 0).
3. Cloud Service chunks instruction blob → header + payload chunks → On-Prem via RMQ.
4. On-Prem reconstructs chunks → work order file. Executes remediation: modifies permissions, creates AD groups, tracks which folders/files/groups were affected.
5. On-Prem → Cloud Service: header describing result series (e.g. Folders, Files, Groups — names, chunk counts) via small-message RMQ.
6. On-Prem → Cloud Service: result chunks via large-message RMQ, each tagged with SeriesType + Ordinal + TotalCount.
7. BigResultHandler streams each chunk directly to Azure Blob Storage → one blob per chunk, URI returned immediately.
8. URI written to Azure Table Storage keyed by TransactionId + SeriesType + Ordinal.
9. When all ordinals 1..TotalCount exist in Table Storage for a series → that series is complete.
10. When all series complete → state machine transitions to State 2.
11. Result Assembler queries Table Storage → collects ordered URI list per series (e.g. N URIs for Folders, M URIs for Files, K URIs for Groups).
12. Notification Sender publishes result URLs to Rule Service.
13. Transaction complete. State machine and Autofac scope disposed.

---

## Why This Design
### Why split header and payload
- Keeps control metadata separate from heavy data traffic.
- Allows independent tuning and scaling.
- Preserves backward compatibility for existing header-driven flows.

### Why use ordinals
- Detect missing chunks reliably.
- Reconstruct logical ordering.
- Enable precise idempotency checks.

### Why one blob per chunk (not one appended blob per series)
- Out-of-order arrival is handled trivially: each chunk is an independent upload with no dependency on other chunks in its series.
- Crash recovery is simple: query Table Storage for which (SeriesType, Ordinal) pairs already have a URI; re-upload only the missing ones. No partial or uncommitted blob state to reason about.
- Immediate durability: each blob is fully committed the moment it lands, not deferred until all chunks arrive.
- Idempotency is clean: before uploading, check if a URI already exists in Table Storage for that ordinal. If yes, skip.

### Why the result header comes before the payload chunks
The header is a **manifest** — it tells BigResultHandler exactly what is about to arrive before any payload chunk lands:
- Which series exist (e.g. Folders, Files, Groups)
- How many chunks each series has (TotalCount per series)

Without it, there is no way to know when processing is complete. The payload chunks carry their own SeriesType and Ordinal, but they don't tell you whether more series are still coming. The header provides the full picture upfront, enabling deterministic completion detection: done means every expected series has all its ordinals accounted for — no more, no less.

An additional practical reason: the header travels via the small-messages queue and arrives quickly, ahead of the heavy payload traffic. BigResultHandler can initialize its tracking state in Table Storage before the first chunk lands.
- Efficient key-based tracking by transaction namespace.
- Simple deduplication lookup by SeriesType + Ordinal.
- Cost-effective metadata store for high-volume progress records.

---

## Reliability Story (What I Say If Asked About Failures)
Recovery is built in through three layers:
1. State persistence: transaction state is persisted and resumed from last known state.
2. Durable messaging: unacknowledged RabbitMQ messages are redelivered after restart.
3. Idempotent processing: before uploading a chunk, we check whether a URI already exists in Table Storage for that (TransactionId, SeriesType, Ordinal). If it does, we skip the upload — the blob is already there.

This means crashes may cause replay, but not data loss or duplicate blob writes.

---

## Performance And Scale Story
- We keep memory bounded with stream-based upload and controlled in-flight message counts.
- We tune RabbitMQ prefetch and worker parallelism together.
- The design scales horizontally by running multiple service instances while preserving transaction correctness through durable shared state.

---

## Trade-Offs I Acknowledge
- More moving parts than the old single-message flow.
- Requires clear operational ownership of queue lifecycle and transaction cleanup.
- Relies on strong observability to detect stuck transactions and missing ordinals quickly.

I usually add: the added complexity is justified because it removes a hard business limit and improves resilience under real production load.

---

## My Closing Line
BigResultHandler turned result processing from a size-limited broker payload problem into a scalable, resumable data pipeline. We moved from a fragile single-message model to a three-state machine that coordinates both the outbound work instruction and the inbound result assembly — handling very large transactions with deterministic completion, bounded memory usage, and safe recovery.

---

## Optional 15-Second Backup Version
I designed BigResultHandler as a three-state machine coordinating the full lifecycle between the Rule Service, Cloud Service, and On-Prem Service. On-prem result data arrives as multiple series of chunks; each series is accumulated into one blob using staged uploads; and the notification delivers one URL per result file back to the requester.

---

---

# סקריפט הצגה בעברית

---

## חלק 1 — רקע: מה המוצר עושה

אני אתחיל בלספר בקצרה על המוצר שבתוכו הקומפוננטה הזאת חיה.

המוצר עוסק ב-Global Permissions Remediation — כלומר, תיקון הרשאות גלובליות על file servers ארגוניים.

הבעיה שהמוצר פותר היא כזאת: בארגונים גדולים, יש תיקיות וקבצים על ה-file server שמוגדרים עם הרשאות לקבוצות כמו Everyone, Domain Users, או Authenticated Users. הרשאות כאלה נותנות גישה לכל עובד בארגון — וזה בעיית אבטחה.

המוצר מקבל storage root — כלומר, כונן או share ספציפי — ועובר על כל התיקיות והקבצים תחתיו.
לכל resource שיש עליו הרשאה גלובלית, הוא בודק: מי בפועל ניגש ל-resource הזה?
הוא יכול לעשות את זה כי המערכת כבר עוקבת אחרי access events מהרגע שה-storage אונבורד — ויש חלון מינימלי שחייב לעבור כדי שיהיו מספיק נתונים לקבל החלטות.

ולפי המידע הזה, המוצר מקבל החלטה לגבי כל resource:
אם למשתמשים שניגשו ל-resource יש כבר הרשאה ספציפית שאינה גלובלית — מספיק להסיר את ההרשאה הגלובלית.
אם לא — המוצר יוצר AD group חדש, מוסיף אליו את המשתמשים הרלוונטיים, נותן לו הרשאה על ה-resource, ואז מסיר את ההרשאה הגלובלית.

התוצאה: אף user לגיטימי לא מאבד גישה, אבל הגישה כבר לא פתוחה לכולם.

---

## חלק 2 — שלושת הסרוויסים הראשיים

בסיפור ישנם שלושה סרוויסים עיקריים.

הראשון הוא ה-Rule Service — הוא זה שמתחיל את הכל. הוא שולח בקשה ל-Cloud Service עם URL לקובץ blob שמכיל את רשימת ה-roots לטפל בהם.

השני הוא ה-Cloud Service — הוא הקואורדינטור. הוא מקבל את הבקשה, יוצר transaction ו-state machine שמנהלים את כל מחזור החיים של הבקשה, ומתקשר עם שני הצדדים — ה-Rule Service מצד אחד וה-On-Prem Service מצד שני.

השלישי הוא ה-On-Prem Service — הוא נמצא בתשתית של הלקוח ויש לו גישה ישירה ל-file server. הוא זה שעושה את העבודה האמיתית — סורק, מנתח, ומבצע את שינויי ההרשאות בפועל.

---

## חלק 3 — State 0: שליחת ההוראות ל-On-Prem

ה-Cloud Service מקבל את הבקשה, יוצר transaction עם TransactionId ייחודי, ומאתחל state machine לטרנזקציה הזאת.

ה-state machine מתחיל ב-State 0 — Request Forwarding.

ה-Cloud Service מוריד את ה-instruction blob, שובר אותו לחתיכות — chunks — ושולח אותן ל-On-Prem דרך RabbitMQ.
קודם הוא שולח header message קטן שאומר ל-On-Prem: "מיד מגיעה אליך סדרת payload messages שמרכיבים יחד קובץ הוראות".
ואז הוא שולח את ה-chunks עצמם — כל אחד עם SeriesType, Ordinal, ו-TotalCount.

ה-On-Prem מרכיב את ה-chunks בחזרה לקובץ אחד — קובץ שאומר לו אילו roots לטפל בהם.
ה-state machine עובר ל-State 1.

---

## חלק 4 — State 1: ממתינים לתוצאות

עכשיו ה-On-Prem עושה את העבודה שלו — זה תהליך ארוך.
הוא מבצע את השינויים ב-file system, כאשר במהלכם הוא גם מזהה מי הקבצים והתיקיות שהושפעו ומה הקבוצות שנוצרו ב-AD.
ה-Cloud Service ממתין.

כשה-On-Prem מסיים, הוא שולח header message קטן בחזרה ל-Cloud Service, דרך ה-small messages queue.
ה-header הזה מגדיר: אילו series של messages עומדים להגיע, מה המשמעות של כל series, וכמה chunks יש בכל אחד.

הסיבה שהוא קודם שולח header ורק אחר כך את ה-chunks: ה-BigResultHandler צריך לדעת מראש מה הולך להגיע — כמה series יש, ומה ה-TotalCount של כל אחד. בלי המידע הזה אי אפשר לדעת מתי הכל הגיע. ה-header הוא בעצם ה-manifest של מה שמגיע.

ה-result מורכב ממספר סוגי קבצים — series — שכל אחד מייצג היבט אחר של התוצאה.
לדוגמה: אחד מכיל את רשימת התיקיות שרומדיאטו, אחד את הקבצים שרומדיאטו, ואחד את ה-AD groups שנוצרו בתהליך.

ואז ה-On-Prem שולח את ה-chunks עצמם — דרך ה-large messages queue — כל chunk שייך ל-series מסוים, ומסומן עם Ordinal ו-TotalCount.

---

## חלק 5 — BigResultHandler: לב הקומפוננטה

וכאן נכנס ה-BigResultHandler.

תפקידו הוא לקבל את ה-chunks האלה ולשמור כל אחד מהם כ-blob נפרד ב-Azure Blob Storage.

לכל chunk שמגיע, הוא עושה stream של הנתונים ישירות ל-Azure Blob Storage — כל chunk הופך ל-blob עצמאי משלו, ומיד מתקבל URI עבורו.
ה-URI הזה נרשם ב-Azure Table Storage עם המפתח: TransactionId + SeriesType + Ordinal — זה שירות ה-KV Store שלנו לצורך מעקב ו-idempotency.

כשכל ה-ordinals מ-1 עד TotalCount קיימים ב-Table Storage עבור series מסוים — ה-series הושלם.

ה-state machine בודק: האם כל ה-series הושלמו? כשהתשובה כן — עוברים ל-State 2.

---

## חלק 6 — State 2: שליחת ה-notification

ב-State 2, ה-Result Assembler שולף מה-Table Storage את כל ה-URIs — לכל series יש אוסף של URIs ממוין לפי ordinal — ומסדר אותם לפי series type.

ה-Notification Sender שולח את התוצאה חזרה ל-Rule Service: לכל series — רשימה מסודרת של blob URLs לפי ordinal.
ה-Rule Service יכול לגשת ישירות ל-Azure Blob Storage עם ה-URLs האלה, להוריד את כל ה-chunks לפי הסדר, ולהרכיב מהם את קבצי התוצאה המלאים.

הטרנזקציה הושלמה. ה-state machine מסתיים, ה-Autofac scope מתפרק, וכל המשאבים שנפתחו לטרנזקציה הזאת משוחררים.

---

## חלק 7 — מה פתרנו: הבעיה שגרמה לנו לבנות את זה מחדש

עכשיו שהסברתי את הפלואו, אסביר מה הייתה הבעיה שגרמה לנו לבנות את זה מחדש.

בעיצוב הישן, כל ה-result data של transaction היה צריך להיכנס להודעת RabbitMQ אחת.
המגבלה הייתה 100 מגה בייט להודעה.
זה יצר תקרה מאוד קונקרטית — file servers מעל גודל מסוים פשוט לא יכלו להתעבד.

הפתרון החדש מאפשר לשבור את ה-result למספר series, וכל series למספר chunks כמה שצריך.
כל chunk הוא הודעה נפרדת של עד 250 מגה.
ובאופן תיאורטי — אין יותר תקרה על גודל הטרנזקציה.

---

## חלק 8 — Reliability: מה קורה אם ה-service קורס

שאלה שמגיעה תמיד היא: מה קורה אם ה-service קורס באמצע?

יש כאן שלוש שכבות של הגנה.

הראשונה היא state persistence — ה-state machine שומר את ה-state הנוכחי שלו ב-Database. אחרי restart, הוא יודע מאיפה להמשיך.

השנייה היא durability של RabbitMQ — הודעות שלא קיבלו ACK נשארות בתור ויחזרו להישלח אחרי שה-service חוזר לחיים.

השלישית היא idempotency דרך ה-KV Store — לפני שאנחנו מעלים chunk, אנחנו בודקים אם כבר קיים URI ב-Table Storage עבור ה-(TransactionId, SeriesType, Ordinal) הזה. אם כן — ה-blob כבר שם, דילגנו על ה-upload, ACK-נו, והמשכנו. אין כפילויות.

השילוב של שלושת אלה מבטיח: אפשר לקרוס כמה פעמים שרוצים — הנתונים לא יאבדו ולא יוכפלו.

---

## חלק 9 — סיכום

אם אני מסכם:

בנינו pipeline שהופך תהליך remediation של file server ארגוני — שיכול לכלול עשרות מיליוני קבצים — לתהליך שניתן לעיבוד ולשחזור בכל נקודת כשל.

העיצוב מבוסס על state machine עם שלושה states שמנהל את כל מחזור החיים של הטרנזקציה — משליחת ההוראות ועד קבלת וארגון התוצאות בחזרה.
הנתונים עוברים בין שלושה סרוויסים דרך RabbitMQ ו-Azure Blob Storage, כשה-KV Store משמש כמנגנון המעקב והאידמפוטנטיות.

זה הפרויקט שאני הכי גאה בו מהעבודה שלי ב-Varonis — כי הוא שילב design מחושב עם פתרון לבעיה עסקית אמיתית ומוחשית.
