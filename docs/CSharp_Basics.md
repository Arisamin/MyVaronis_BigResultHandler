# C# Basics

## Why was hash code included in C#?

The hash code in C# is used to efficiently organize and retrieve objects in hash-based collections, such as `Dictionary<TKey, TValue>` and `HashSet<T>`. The `GetHashCode()` method provides a numeric value (the hash code) that represents the object's contents for hashing purposes.

**Purpose and Benefits:**
- Allows collections to quickly locate objects by distributing them into "buckets" based on their hash code.
- Minimizes the number of equality checks needed during lookups, insertions, and deletions.
- Enables average-case constant time complexity ($O(1)$) for these operations.

## Guidelines for Implementing GetHashCode

To implement a correct and effective `GetHashCode` function in C#:

1. **Consistency:** If two objects are equal according to `Equals`, they must return the same hash code.
2. **Uniform Distribution:** Distribute hash codes as evenly as possible to minimize collisions.
3. **Use Immutable Fields:** Base the hash code on fields that do not change during the object’s lifetime.
4. **Combine Fields:** If your object has multiple significant fields, combine their hash codes (e.g., using XOR, or `HashCode.Combine` in .NET Core+).
5. **Avoid Expensive Computation:** The hash code should be quick to compute.
6. **Never Throw Exceptions:** `GetHashCode` should never throw.
7. **Match Equals:** If you override `Equals`, you must also override `GetHashCode`.

Tip: Use `HashCode.Combine(field1, field2, ...)` for a robust implementation in modern .NET.

## Remarks

When retrieving a value from a dictionary using a key:

1. The dictionary calls `GetHashCode()` on the key to find the correct bucket.
2. It then iterates through the entries in that bucket and calls `Equals()` to compare the requested key with each stored key.
3. If `Equals()` returns true, the value is returned.


Therefore, before returning the requested value, the dictionary will invoke the `Equals()` method to confirm key equality, especially when there are hash collisions.

**Note on Performance:**
The average-case $O(1)$ lookup time in a dictionary assumes that `GetHashCode` distributes keys uniformly across buckets, minimizing collisions. If `GetHashCode` is poorly implemented (e.g., returns the same value for many keys), collisions increase and lookups degrade toward $O(n)$, since more `Equals` checks are needed.

Thus, $O(1)$ is the expected average case—assuming a good hash function. Actual performance depends on how well `GetHashCode` spreads keys. Proper hash code design is critical for maintaining fast lookups.

**Summary:**
Hash codes enable fast and scalable data access in hash-based collections in C#.
