# Storage Density Analysis: B+ Tree Bulk Loader

## 1. Theoretical Framework
In a B+ Tree of **Order $M$**, the maximum number of keys per internal node is $M - 1$, while leaf nodes store both keys and their associated data records. Standard incremental insertions typically result in a density of **ln(2) ≈ 69.3%**. 

The Bulk Loader bypasses this by pre-selecting optimal separator keys to partition the dataset. It establishes the upper-level index structure (internal nodes) before recursively distributing the remaining data into a linked chain of leaf nodes.


## 2. Density Formula
The efficiency of the B+ Tree is measured by comparing utilized record slots in the leaf nodes against total allocated leaf space:

$$Density = \left( \frac{\text{Total Records}}{\text{Total Leaf Pages} \times \text{Leaf Capacity}} \right) \times 100$$

## 3. Example 
| Parameter | Value |
| :--- | :--- |
| **Order (Internal)** | 4 |
| **Max Keys Per Index Page** | 3 |
| **Total Pages in File** | 17 |
| **Total Record Capacity** | 51 Slots |
| **Actual Records Stored** | 50 |
| **Density** | **98%** |

## 4. Read and Write Efficiency
* **Reduced IOPS:** Higher density ensures that more data records are retrieved per single disk read, minimizing the total input/output operations required for large scans.
* **Search Depth:** By maximizing node capacity, the tree height remains as shallow as possible. In this test, 50 records are reachable in only 3 hops, even with an Order as small as 4.
* **Sequential Scan Performance:** Because leaf nodes are linked, the B+ Tree can perform range scans with $O(1)$ transitions between blocks, unlike a classic B-Tree which requires recursive traversal.
* **Minimal Disk Fragmentation:** With a 98% fill rate, the physical file contains almost zero "dead space," making the index highly compact for archival or transport.

## 5. Implementation Note
The density is controlled via the `LeafFactor` variable. 
* **1.0 Factor:** Maximum density (98%). Best for read-only archives or static datasets.
* **0.7 Factor:** Balanced density (70%). Best for trees expecting frequent future Insert operations to avoid immediate page splits.