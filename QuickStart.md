# Quick Start: B+ Tree

The B+ Tree is a high-performance, disk-persistent B-Tree engine designed for O(1) seek times and robust data integrity.

## 1. Setup & Initialization

The engine uses a Fixed-Page strategy. You define the Order at startup to lock in the physical disk footprint of each node.

```csharp 
using WhiteArcOne;

// Initialize the tree.
var tree = new BTree("storage.dat", Order: 64);
```

## 2. Core Operations
Operations are immediately persisted. Every Insert or Delete triggers a physical disk flush to ensure your data survives a power loss.

```csharp 
// 1. Insertion
tree.Insert(500, 12345); // Key: 500, Data: 12345

// 2. Search
if (tree.TrySearch(500, out Element result))
{
    Console.WriteLine($"Found Data: {result.Data}");
}

// 3. Preventative Deletion
// The "Beef-up" strategy rebalances the tree top-down to prevent backtracking
tree.Delete(500);
```

## 3. Maintenance Tools
Keep your storage file lean and verified using the built-in utility methods.

```csharp 
// Reclaim space from deleted nodes (FreeList recovery)
tree.Compact();

// Run a full recursive audit of B-Tree invariants
tree.ValidateIntegrity();

```

