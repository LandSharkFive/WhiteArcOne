# High-Performance B+ Tree

A disk-persistent B+ Tree implementation in C#. This project manages data on a `FileStream` using a fixed-page architecture, optimized for high fan-out, fast range queries, and proactive structural maintenance.

## 1. File Architecture
The B+ Tree is stored in a single binary file with a dedicated header, internal index nodes, and a linked chain of leaf data nodes.

### Physical Layout
| Section | Offset | Size | Purpose |
| :--- | :--- | :--- | :--- |
| **Header** | `0` | 4096 bytes | Stores RootId, NodeCount, and FirstLeafId. |
| **Nodes** | `4096` | Variable | Fixed-size pages. Internal nodes store keys; Leaf nodes store key-data pairs. |
| **FreeList** | EOF | Variable | A stack of Disk IDs available for structural re-allocation. |

### Node Serialization
Unlike a classic B-Tree, this implementation differentiates between Internal and Leaf nodes to maximize efficiency:
* **Internal Nodes**: Store only keys and child pointers. This maximizes fan-out, resulting in a shallower tree and fewer disk I/O operations.
* **Leaf Nodes**: Store the actual data records and a `NextLeafId` pointer, creating a horizontal linked list for $O(N)$ sequential scans.

`NodeSize = (order * 12) + 16 bytes`
`Offset = (NodeSize * DiskId) + 4096`

## 2. Technical Features
* **Leaf-Only Data Storage**: All data records are stored exclusively at the leaf level. Internal nodes act purely as a navigation index, ensuring consistent search depth for all keys.
* **Linked Leaf Traversal**: Supports highly efficient range-based queries. By following the `NextLeafId` pointers, the engine can perform sequential scans without re-traversing the root.
* **Top-Down Bulk Load**: Bypasses traditional insertion overhead by recursively partitioning the dataset and building the index hierarchy above the data leaves.
* **Proactive Maintenance**: Implements a preventative strategy during descent. If a path encounters a full node during insertion (or a near-empty node during deletion), it performs structural adjustments immediately to ensure single-pass operations.
* **Integrity Validation**: Includes a `ValidateIntegrity()` method to verify leaf-link continuity, key-range boundaries, and the balance of the tree.

## 3. Usage

```csharp
// Initialize a B+ Tree and perform operations.
using (var tree = new BTree("data.db")) 
{
    // Standard insertion (data is routed to leaf nodes)
    tree.Insert(42, 100); // Key: 42, Data: 100
    
    // Search always traverses to the leaf level
    if (tree.TrySearch(42, out Element result)) 
    {
        Console.WriteLine($"Found Data: {result.Data}");
    }

    // Range Query (leveraging the linked leaf structure)
    var range = tree.GetKeyRange(10, 50); 
}