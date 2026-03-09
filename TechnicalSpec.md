# Technical Specification: B+ Tree

## 1. Storage Architecture

### Fixed-Page Paging Logic
To achieve $O(1)$ disk seeking, the engine utilizes a fixed-page size strategy. The size of every node (page) in the file is identical, determined by the `Order` defined at initialization.

* **Node Size Formula**: `(Order * 12) + 16 bytes`
* **Disk Offset Calculation**: `Offset = (NodeSize * DiskId) + 4096`
    * *Note: The first 4096 bytes are reserved for the File Header.*

### Binary Serialization
The engine uses `BinaryReader` and `BinaryWriter` for high-performance, predictable I/O.
* **Immediate Persistence**: DiskWrite operations include a regular `FileStream.Flush()` to ensure physical disk synchronization.
* **Data Sanitization**: When keys are deleted or pointers are shifted, vacated slots are explicitly "Nuclear Wiped" (overwritten with `-1` or `default`) to prevent stale data from causing ghost references.

## 2. Core Logic & Invariants

### Root Management
The Header stores the `RootId`. The architecture handles two critical root transformations:
1.  **Root Split**: When the root reaches capacity, it splits. A new internal node is created to act as the parent, and `Header.RootId` is updated.
2.  **Root Collapse**: If a deletion leaves the root with 0 keys and at least one child, the primary child is promoted to `RootId`.



### Data Residency & Navigation
* **Internal Nodes**: These nodes store **Separator Keys** and child pointers. They act as "signposts" to guide the search: all keys in the left subtree are less than the separator, while keys in the right subtree are greater than or equal to it.
* **Leaf Nodes**: The only nodes containing actual data records. Each leaf also maintains a `NextLeafId` pointer to the adjacent leaf node on disk to facilitate sequential scans.

### Preventative Deletion 
The engine implements top-down preventative deletion. As it descends the tree, it ensures every visited node has at least 't' keys (minimum degree)