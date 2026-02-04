# Next Work Items / TODOs

This file tracks upcoming tasks, features, and improvements for the BigResultHandler project. Update this list regularly to keep the team aligned and focused.

## Current Work Items

1. [ ] Discuss ResultHandler scaling (rabbit load balancer? how to keep track of transactions)
2. [ ] Revisit what is the content that is passed in the payload messages - is it the result data or links to the result data, if so is the collector side uploading data to azure storage?
3. [ ] What is the advantage of .Net Core over .Net Framework and why was it made
4. [ ] How did the new architecture enable processing a bigger disk space? What was blocking such size in the old design?
5. [ ] Why did we choose to work with 2 queues rather than one
6. [ ] Discuss concurrent C# data structures (concurrent dictionary for example)

---
Add new items above and mark completed items with [x].
