# Maintenance

## 1. Offline Compaction
As deletions occur in the B+ Tree, the file may develop holes. While the FreeList tracks these, a heavily modified tree might have a physical file size much larger than its logical data.

**How it works:**
1. Creates a temporary file.
2. Performs a Breadth-First Search (BFS) starting from the RootId to identify all live internal and leaf nodes.
3. Re-maps every live node to a new, contiguous ID, ensuring the linked list of leaf nodes remains sequentially ordered on disk for optimal range scans.
4. Swaps the old file with the new, optimized file.


## 2. Bulk Loading
To avoid the overhead of 'N' separate Insert operations, use the BulkLoad method. This is specifically optimized for B+ Trees by populating data exclusively in the leaf layer.

* **Sort First:** Ensure your Element list is sorted by Key ascending.
* **Leaf-Up Construction:** This method builds the tree from the bottom up, filling leaf pages to maximum capacity (or a configurable fill factor) and then constructing the index hierarchy above them.
* **Horizontal Linking:** As leaves are created during the bulk load, they are immediately linked to their predecessor, establishing the $O(1)$ transition path for range queries.

## 3. The FreeList Strategy
The FreeList is a stack of integers stored at the end of the file to manage disk space for both internal and leaf nodes. 
* **Push:** When a node (internal or leaf) becomes empty due to a merge, its ID is added to the Free List for future allocation.
* **Pop:** When an Insert or Split operation requires a new node ID, the system checks the stack before incrementing `Header.NodeCount`.