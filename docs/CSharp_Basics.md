# C# Basics

## Why was hash code included in C#?

The hash code in C# is used to efficiently organize and retrieve objects in hash-based collections, such as `Dictionary<TKey, TValue>` and `HashSet<T>`. The `GetHashCode()` method provides a numeric value (the hash code) that represents the object's contents for hashing purposes.

**Purpose and Benefits:**
- Allows collections to quickly locate objects by distributing them into "buckets" based on their hash code.
- Minimizes the number of equality checks needed during lookups, insertions, and deletions.
- Enables average-case constant time complexity ($O(1)$) for these operations.


**Summary:**
Hash codes enable fast and scalable data access in hash-based collections in C#.

## Remarks

When retrieving a value from a dictionary using a key:

1. The dictionary calls `GetHashCode()` on the key to find the correct bucket.
2. It then iterates through the entries in that bucket and calls `Equals()` to compare the requested key with each stored key.
3. If `Equals()` returns true, the value is returned.

Therefore, before returning the requested value, the dictionary will invoke the `Equals()` method to confirm key equality, especially when there are hash collisions.
