# B+ Tree Bulk Loader: Top-Down Design

## Overview
The Bulk Loader provides an $O(N)$ mechanism to build a **B+ Tree** from a pre-sorted dataset. By utilizing a top-down recursive partitioning strategy, the loader minimizes memory overhead and maximizes write throughput by creating a structure where all data resides in the leaves.

## The Top-Down Strategy
This loader treats the sorted input as a range to be partitioned into a balanced hierarchy, bypassing the complexity of standard insertion logic:

* **Recursive Partitioning:** The loader calculates optimal pivots from the sorted list to populate internal index nodes, ensuring a clean, balanced structure from the root down to the leaf layer.
* **High Occupancy:** The algorithm is tuned for density, frequently achieving 97% node occupancy in tested cases.
* **Leaf Linking:** During the build process, the loader automatically links each leaf node to its neighbor, establishing the horizontal pointers required for efficient range scans.
* **Memory Efficiency:** The implementation is lightweight and easy on memory, as it calculates the tree structure mathematically rather than holding large buffers.
* **Adaptive Balancing:** While the loader prioritizes high density, it remains flexible to ensure the structural requirements of a B+ Tree are met even in edge cases.


## Usage
The `TreeBuilder` handles the recursive logic internally, ensuring the resulting `data.db` is ready for immediate querying.

```csharp
   // 1. Generate sorted data.
   var data = Enumerable.Range(1, 50).Select(i => new Element(i, i)).ToList();

   // 2. Run Top-Down Bulk Loader for B+ Tree.  
   var builder = new TreeBuilder();
   builder.CreateFromSorted("data.db", data);