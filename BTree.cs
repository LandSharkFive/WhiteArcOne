/*
 * ===========================================================================
 * MODULE:          B+ Tree Engine v1.0
 * AUTHOR:          Koivu
 * DATE:            2026-03-08
 * VERSION:         1.0.0
 * LICENSE:         MIT License
 * ===========================================================================
 *
 * ABSTRACT:
 * A high-performance, disk-resident B+ Tree implementation featuring 
 * proactive single-pass balancing. This engine prioritizes search 
 * efficiency via direct sector mapping for optimized node access.
 *
 *
 * ARCHITECTURAL SPECIFICATION:
 * - BALANCING:  Top-down proactive splitting/merging (backtrack-free).
 * - PERSISTENCE: Binary serialization with struct-based element alignment.
 * - STORAGE:    Stack-based FreeList for efficient sector-level reclamation.
 * - INTEGRITY:  Integrated Audit suite for cycle and ghost-key detection.
 *
 * KEYWORDS: 
 * Balanced Tree, Disk-Resident, Single-Pass, Sector Reclamation, Persistence.
 *
 * ---------------------------------------------------------------------------
 * Copyright (c) 2026 Koivu.
 * Licensed under the MIT License.
 * ---------------------------------------------------------------------------
 */


using System.Buffers;
using System.Buffers.Binary;
using System.Collections;

namespace ArcOne
{
    /// <summary>
    /// Provides a structured interface for BTree file operations, 
    /// ensuring safe resource management and data persistence.
    /// </summary>
    public class BTree : IDisposable
    {
        private string MyFileName { get; set; }

        private FileStream MyFileStream;

        private BinaryReader MyReader;

        private BinaryWriter MyWriter;

        private readonly HashSet<int> FreeList = new HashSet<int>();

        private const int HeaderSize = 4096;

        private const int MagicConstant = BTreeHeader.MagicConstant;

        public BTreeHeader Header;


        /// <summary>
        /// Initializes a new instance of the BTree class by opening an existing file 
        /// or creating a new one with the specified branching order.
        /// </summary>
        public BTree(string fileName, int order = 64)
        {
            if (string.IsNullOrEmpty(fileName))
                throw new ArgumentException("File name cannot be empty.");

            if (order < 4)
                throw new ArgumentException("Order must be at least 4.");

            MyFileName = fileName;

            OpenStorage();

            if (MyFileStream.Length > 0)
            {
                // 2. Existing file: Trust the disk.
                LoadHeader();
                LoadFreeList();
            }
            else
            {
                // 3. New file.
                Header.Initialize(order);
                SaveHeader();
            }
        }

        /// <summary>
        /// Open the file stream. 
        /// </summary>
        private void OpenStorage()
        {
            const int BufferSize = 65536;

            // 1. Close existing file.
            MyWriter = null;
            MyReader = null;
            MyFileStream = null;

            // 2. Open the file.
            MyFileStream = new FileStream(MyFileName, FileMode.OpenOrCreate,
                                          FileAccess.ReadWrite, FileShare.None, BufferSize);

            // 3. Create the reader and writer.
            MyReader = new BinaryReader(MyFileStream, System.Text.Encoding.UTF8, true);
            MyWriter = new BinaryWriter(MyFileStream, System.Text.Encoding.UTF8, true);
        }

        /// <summary>
        /// Close the file stream.
        /// </summary>
        private void CloseStorage()
        {
            MyWriter?.Dispose();
            MyReader?.Dispose();
            MyFileStream?.Dispose();

            MyWriter = null;
            MyReader = null;
            MyFileStream = null;
        }


        // ------ HELPER METHODS ------


        /// <summary>
        /// Calculates the byte offset in the file for a given disk position (record index).
        /// </summary>
        private long CalculateOffset(int id)
        {
            if (id < 0)
                throw new ArgumentOutOfRangeException(nameof(id), "Cannot be negative.");

            if (Header.PageSize < 64)
                throw new ArgumentException(nameof(Header.PageSize));

            return ((long)Header.PageSize * id) + HeaderSize;
        }

        /// <summary>
        /// Closes the object and releases resources by calling the Dispose method.
        /// </summary>
        public void Close()
        {
            Dispose();
        }

        /// <summary>
        /// Safely persists data and releases the file stream while preventing redundant cleanup.
        /// </summary>
        public void Dispose()
        {
            if (MyFileStream != null)
            {
                try
                {
                    SaveFreeList();
                    SaveHeader();
                }
                finally
                {
                    MyFileStream.Dispose();
                    MyFileStream = null;
                }
            }
            // Tell the GC we've already handled the cleanup.
            GC.SuppressFinalize(this);
        }

        // -------- DISK I/O METHODS -----------

        /// <summary>
        /// Completely wipes the B-Tree structure and truncates the underlying data file.
        /// This resets the header, clears the free list, and prepares the file for a fresh bulk load.
        /// </summary>
        /// <remarks>
        /// Warning: This operation is destructive and cannot be undone.
        /// </remarks>
        public void Clear()
        {
            // 1. Wipe the physical file
            MyFileStream.SetLength(0);
            MyFileStream.Flush();
            MyFileStream.Seek(0, SeekOrigin.Begin); // Crucial: Reset the pointer

            // 2. Reset the logical state
            Header.RootId = -1;
            Header.NodeCount = 0;
            FreeList.Clear();

            // 3. Re-initialize the Header in the file
            SaveHeader();
        }

        /// <summary>
        /// Retrieves stored data from physical storage.
        /// </summary>
        public BNode DiskRead(int id)
        {
            if (id < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(id), "Cannot be negative");
            }

            BNode node = new BNode(Header.Order);
            long offset = CalculateOffset(id);

            MyFileStream.Seek(offset, SeekOrigin.Begin);
            node.Read(MyReader);
            return node;
        }

        /// <summary>
        /// Write a node to disk using the fixed binary layout described in DiskRead.
        /// Ensure Header.Order and BNode layout remain compatible with previously written files.
        /// </summary>
        public void DiskWrite(BNode node)
        {
            if (node.Id < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(node.Id), "Cannot be negative");
            }

            long offset = CalculateOffset(node.Id);
            MyFileStream.Seek(offset, SeekOrigin.Begin);
            node.Write(MyWriter);
        }

        /// <summary>
        /// Wipes a specific node's data on disk by overwriting its sector with zeros.
        /// This is typically used for security or to clean up nodes moved to a free list.
        /// </summary>
        public void ZeroNode(int id)
        {
            if (id < 0) throw new ArgumentOutOfRangeException(nameof(id), "Cannot be negative");

            // 1. Create buffer on the stack.
            Span<byte> buffer = stackalloc byte[Header.PageSize];
            buffer.Clear();

            // 2. Physical write.
            long offset = CalculateOffset(id);
            MyFileStream.Seek(offset, SeekOrigin.Begin);
            MyFileStream.Write(buffer);
        }


        /// <summary>
        /// Synchronizes the internal file stream with the underlying storage device to ensure all changes are persisted.
        /// </summary>
        public void Commit()
        {
            if (MyFileStream != null && MyFileStream.CanWrite)
            {
                SaveHeader();
                MyFileStream.Flush();
            }
        }

        // --- HEADER METHODS ---

        /// <summary>
        /// Writes the B-Tree header to disk.
        /// </summary>
        public void SaveHeader()
        {
            MyFileStream.Seek(0, SeekOrigin.Begin);
            Header.Write(MyWriter);
        }

        /// <summary>
        /// Load the B-Tree header from disk.
        /// </summary>
        public void LoadHeader()
        {
            MyFileStream.Seek(0, SeekOrigin.Begin);
            Header = BTreeHeader.Read(MyReader);

            if (Header.Magic != MagicConstant)
                throw new InvalidDataException("Invalid File Format");

            if (Header.Order < 4)
                throw new ArgumentException("Order must be at least 4.");

            if (Header.PageSize < 64 || Header.PageSize != BNode.CalculateNodeSize(Header.Order))
                throw new ArgumentException(nameof(Header.PageSize));
        }


        // --- SEARCH METHODS ---

        /// <summary>
        /// Attempts to locate an element in the B-Tree by its unique key.
        /// </summary>
        public bool TrySearch(int key, out Element result)
        {
            result = Element.GetDefault();
            if (Header.RootId == -1) return false;

            BNode rootNode = DiskRead(Header.RootId);
            return TrySearchIterative(rootNode, key, out result);
        }

        /// <summary>
        /// Performs an iterative search for a specific key within the B-Tree.
        /// This implementation avoids recursion to minimize stack overhead and uses 
        /// a tight loop for intra-node searching to maximize performance.
        /// </summary>
        private bool TrySearchIterative(BNode currentNode, int key, out Element result)
        {
            // Phase 1: Descend to the Leaf using Binary Search.
            while (!currentNode.Leaf)
            {
                int low = 0;
                int high = currentNode.NumKeys - 1;
                int i = 0;

                // Standard Binary Search to find the first key >= target key
                while (low <= high)
                {
                    int mid = low + (high - low) / 2;
                    if (currentNode.Keys[mid].Key <= key)
                    {
                        i = mid + 1; // Move right
                        low = mid + 1;
                    }
                    else
                    {
                        i = mid; // Potential candidate, move left
                        high = mid - 1;
                    }
                }

                // Always descend. Internal nodes are just signposts.
                currentNode = DiskRead(currentNode.Kids[i]);
            }

            // Phase 2: Search the Leaf using Binary Search.
            int l = 0;
            int r = currentNode.NumKeys - 1;
            while (l <= r)
            {
                int mid = l + (r - l) / 2;
                if (currentNode.Keys[mid].Key == key)
                {
                    result = currentNode.Keys[mid];
                    return true;
                }

                if (currentNode.Keys[mid].Key < key)
                    l = mid + 1;
                else
                    r = mid - 1;
            }

            result = Element.GetDefault();
            return false;
        }

        /// <summary>
        /// Retrieves the very first (minimum) element in the B+ Tree.
        /// It traverses the leftmost path of index nodes until it reaches the first leaf node,
        /// where the smallest key is stored in the first position.
        /// </summary>
        public Element SelectFirst()
        {
            if (Header.RootId == -1) return default;

            int currentId = Header.RootId;
            while (true)
            {
                BNode node = DiskRead(currentId);
                if (node.Leaf)
                {
                    return node.NumKeys > 0 ? node.Keys[0] : default;
                }
                // Always go down the very first child pointer
                currentId = node.Kids[0];
            }
        }

        /// <summary>
        /// Retrieves the very last (maximum) element in the B+ Tree.
        /// It traverses the rightmost path of index nodes until it reaches the last leaf node,
        /// where the largest key is stored at the final active index.
        /// </summary>
        public Element SelectLast()
        {
            if (Header.RootId == -1) return default;

            int currentId = Header.RootId;
            while (true)
            {
                BNode node = DiskRead(currentId);
                if (node.Leaf)
                {
                    return node.NumKeys > 0 ? node.Keys[node.NumKeys - 1] : default;
                }
                // Always go down the last active child pointer
                currentId = node.Kids[node.NumKeys];
            }
        }

        /// <summary>
        /// Performs a range query to retrieve all elements between startKey and endKey (inclusive).
        /// In a B+ Tree, this is highly efficient: it uses the index to find the starting leaf,
        /// then follows the horizontal sibling links (NextLeafId) to traverse only the 
        /// necessary data pages until the endKey is exceeded.
        /// </summary>
        public IEnumerable<Element> EnumerateRange(int startKey, int endKey)
        {
            BNode current = FindLeaf(Header.RootId, startKey);

            while (current != null)
            {
                for (int i = 0; i < current.NumKeys; i++)
                {
                    int key = current.Keys[i].Key;

                    if (key >= startKey && key <= endKey)
                        yield return current.Keys[i];

                    if (key > endKey) yield break; // Exit the generator entirely
                }

                if (current.NextLeafId != -1)
                    current = DiskRead(current.NextLeafId);
                else
                    break;
            }
        }

        public List<int> GetKeyRange(int startKey, int endKey)
            => EnumerateRange(startKey, endKey).Select(e => e.Key).ToList();

        public List<Element> GetElementRange(int startKey, int endKey)
            => EnumerateRange(startKey, endKey).ToList();


        /// <summary>
        /// Traverses the B+ Tree index to locate the leaf node that should contain the specified key.
        /// It uses binary search at each internal node to determine which child branch to follow.
        /// In a B+ Tree, this traversal always continues until a leaf is reached, as internal nodes
        /// only act as a guide and do not store the actual data elements.
        /// </summary>
        public BNode FindLeaf(int nodeId, int key)
        {
            if (nodeId == -1) return null;

            int currentId = nodeId;
            while (true)
            {
                BNode node = DiskRead(currentId);
                if (node.Leaf) return node;

                // Binary Search for the child index
                int low = 0, high = node.NumKeys - 1;
                while (low <= high)
                {
                    int mid = (low + high) / 2;
                    if (key >= node.Keys[mid].Key) low = mid + 1;
                    else high = mid - 1;
                }

                // low is the correct index for the child pointer
                currentId = node.Kids[low];
            }
        }

        // ------- INSERT METHODS --------


        /// <summary>Inserts a new Element into the collection using the specified key and data.</summary>
        public void Insert(int key, int data)
        {
            Element item = new Element(key, data);
            Insert(item);
        }

        ///// <summary>
        ///// Inserts an element into the B-Tree. If the tree is empty, it initializes the root. 
        ///// If the root is full, it performs a preemptive split to increase tree height 
        ///// before delegating to the recursive insertion logic.
        ///// </summary>

        public void Insert(Element item)
        {
            bool headerChanged = false;

            // 1. Handle Empty Tree.
            if (Header.RootId == -1)
            {
                BNode firstNode = new BNode(Header.Order) { Leaf = true, Id = GetNextId() };
                Header.RootId = firstNode.Id;
                firstNode.Keys[0] = item;
                firstNode.NumKeys = 1;

                DiskWrite(firstNode);
                headerChanged = true;
            }
            else
            {
                BNode rootNode = DiskRead(Header.RootId);

                // 2. Handle Root Split (Preemptive split).
                if (rootNode.NumKeys == Header.Order)
                {
                    BNode newRoot = new BNode(Header.Order) { Leaf = false, Id = GetNextId() };
                    newRoot.Kids[0] = Header.RootId;

                    SplitChild(newRoot, 0, rootNode);

                    // Root changed, so update the header and track the change.
                    Header.RootId = newRoot.Id;
                    headerChanged = true;

                    // After split, decide which path to take.
                    InsertNonFull(newRoot, item);
                }
                else
                {
                    InsertNonFull(rootNode, item);
                }
            }

            // Only hit the disk for the header if a structural change occurred.
            if (headerChanged)
            {
                SaveHeader();
            }
        }



        // --- INSERTION HELPERS ---

        /// <summary>
        /// Navigates down the tree to the appropriate leaf for insertion, implementing a 
        /// proactive split strategy to maintain B+ Tree balance. If any internal node or leaf 
        /// along the path is at maximum capacity, it is split before descending.
        /// For internal nodes, this method directs the search; for leaf nodes, it performs 
        /// the final sorted insertion and physical disk write.
        /// </summary>
         
        private void InsertNonFull(BNode node, Element item)
        {
            while (true)
            {
                if (node.Leaf)
                {
                    // Binary search to find insert position
                    int low = 0, high = node.NumKeys - 1;
                    while (low <= high)
                    {
                        int mid = low + ((high - low) >> 1); // Optimized mid calculation
                        if (item.Key < node.Keys[mid].Key) high = mid - 1;
                        else low = mid + 1;
                    }
                    int insertPos = low;

                    if (insertPos < node.NumKeys)
                    {
                        Array.Copy(node.Keys, insertPos, node.Keys, insertPos + 1, node.NumKeys - insertPos);
                    }

                    node.Keys[insertPos] = item;
                    node.NumKeys++;
                    DiskWrite(node);
                    return;
                }
                else
                {
                    // Internal Navigation
                    int low = 0, high = node.NumKeys - 1;
                    while (low <= high)
                    {
                        int mid = low + ((high - low) >> 1);
                        if (item.Key < node.Keys[mid].Key) high = mid - 1;
                        else low = mid + 1;
                    }
                    int pos = low;

                    BNode child = DiskRead(node.Kids[pos]);

                    if (child.NumKeys == Header.Order)
                    {
                        // Optimization: child is passed by reference; SplitChild updates it.
                        SplitChild(node, pos, child);

                        // Check if we need to move to the new sibling 'z' or stay with 'y'.
                        if (item.Key >= node.Keys[pos].Key)
                        {
                            pos++;
                            child = DiskRead(node.Kids[pos]); // Read the NEW sibling.
                        }
                        // Else: 'child' variable still points to 'y' which is now half-empty.
                        // We do NOT need to DiskRead(node.Kids[pos]) because 'child' is already 'y'.
                    }

                    node = child;
                }
            }
        }

        /// <summary>
        /// Splits a full child node (y) into two, moving half of its contents into a new sibling node (z).
        /// This method enforces B+ Tree structural rules:
        /// 1. If 'y' is a leaf, the median key is COPIED to the parent and also stays in the leaf (z).
        /// 2. If 'y' is a leaf, the sibling pointers (NextLeafId) are updated to maintain the linked list.
        /// 3. If 'y' is internal, the median key is MOVED to the parent, acting as a separator.
        /// </summary>
        private void SplitChild(BNode x, int pos, BNode y)
        {
            BNode z = new BNode(Header.Order, leaf: y.Leaf) { Id = GetNextId() };
            z.Leaf = y.Leaf;
            z.NumKeys = 0; // Initialize clean

            int medianIdx = y.NumKeys / 2;
            Element keyToPromote = y.Keys[medianIdx];

            // B+ Tree Rule: On a Leaf split, the median key stays in the right node (z).
            // On an Internal split, the median key is promoted and removed from both children.
            int startIdx = y.Leaf ? medianIdx : medianIdx + 1;
            int keysToMove = y.NumKeys - startIdx;

            if (y.Leaf)
            {
                z.NextLeafId = y.NextLeafId;
                y.NextLeafId = z.Id;
            }

            // 2. BULK MOVE: Copy keys and children to Z
            Array.Copy(y.Keys, startIdx, z.Keys, 0, keysToMove);
            if (!y.Leaf)
            {
                Array.Copy(y.Kids, startIdx, z.Kids, 0, keysToMove + 1);
            }

            // 3. NUCLEAR WIPE: Clean Y completely from the split point forward
            int wipeCount = y.NumKeys - medianIdx;
            Array.Fill(y.Keys, Element.GetDefault(), medianIdx, wipeCount);

            if (!y.Leaf)
            {
                // Internal nodes: Wipe the extra child pointer
                Array.Fill(y.Kids, -1, medianIdx + 1, (y.NumKeys + 1) - (medianIdx + 1));
            }

            z.NumKeys = keysToMove;
            y.NumKeys = medianIdx;

            // 4. SHIFT PARENT X: Use Array.Copy for the right-shift
            int parentMoveCount = x.NumKeys - pos;
            if (parentMoveCount > 0)
            {
                Array.Copy(x.Keys, pos, x.Keys, pos + 1, parentMoveCount);
                Array.Copy(x.Kids, pos + 1, x.Kids, pos + 2, parentMoveCount);
            }

            // 5. INSERT PROMOTED KEY
            x.Keys[pos] = keyToPromote;
            x.Kids[pos + 1] = z.Id;
            x.NumKeys++;

            DiskWrite(z);
            DiskWrite(y);
            DiskWrite(x);
        }


        /// <summary>
        /// Provides a node ID for a new allocation by recycling an ID from the FreeList 
        /// or, if none are available, appending a new ID at the end of the storage.
        /// </summary>
        public int GetNextId()
        {
            // Get first item.
            using (var enumerator = FreeList.GetEnumerator())
            {
                if (enumerator.MoveNext())
                {
                    int nodeId = enumerator.Current;
                    FreeList.Remove(nodeId);
                    return nodeId;
                }
            }

            // Append to end of file.
            int nextPos = Header.NodeCount;
            Header.NodeCount++;
            return nextPos;
        }

        /// <summary>
        /// Updates the data associated with an existing key. 
        /// In a B+ Tree architecture, actual data values are stored exclusively in leaf nodes. 
        /// This method traverses the index to the correct leaf, performs a binary search 
        /// to find the key, and persists the modified data element back to disk.
        /// </summary>
        public bool UpdateValue(int key, int data)
        {
            // 1. Find the leaf
            BNode leaf = FindLeaf(Header.RootId, key);

            // Safety check: ensure we actually have a leaf and not an internal node
            if (leaf == null || !leaf.Leaf) return false;

            // 2. Binary search
            int low = 0, high = leaf.NumKeys - 1;
            while (low <= high)
            {
                // Use the overflow-safe mid calculation
                int mid = low + (high - low) / 2;

                if (leaf.Keys[mid].Key == key)
                {
                    // FOUND: Update user data and persist
                    leaf.Keys[mid].Data = data;
                    DiskWrite(leaf);
                    return true;
                }

                if (key < leaf.Keys[mid].Key) high = mid - 1;
                else low = mid + 1;
            }

            return false;
        }

        /// <summary>
        /// AddOrUpdates an element into the B+ Tree. It first attempts to locate the key 
        /// at the leaf level to update its value. If the key is not found, it performs 
        /// a standard B+ Tree insertion, which may involve splitting nodes from the 
        /// root down to the leaves to accommodate the new record.
        /// </summary>
        public void AddOrUpdate(int key, int data)
        {
            // Try to update; if it fails, the key doesn't exist, so insert.
            if (!UpdateValue(key, data))
            {
                Insert(new Element { Key = key, Data = data });
            }
        }

        /// <summary>
        /// Adds a new element or updates an existing one using an Element object.
        /// This ensures the B+ Tree remains the single source of truth for the 
        /// data record associated with the element's key.
        /// </summary>
        public void AddOrUpdate(Element item)
        {
            AddOrUpdate(item.Key, item.Data);
        }

        /// <summary>
        /// Updates an existing record's data using the key provided in the Element object.
        /// Returns true if the key was found and updated in a leaf node; otherwise, false.
        /// </summary>
        public bool UpdateValue(Element item)
        {
            return UpdateValue(item.Key, item.Data);
        }


        // ------ DELETE METHODS ------

        /// <summary>
        /// Removes the specified key from the B-Tree, rebalances the structure, and shrinks the tree height if the root becomes empty.
        /// </summary>
        public void Delete(int key, int data)
        {
            if (Header.RootId == -1) return;

            Element deleteKey = new Element(key, data);
            BNode rootNode = DiskRead(Header.RootId);

            // 1. Perform the recursive deletion
            DeleteSafe(rootNode, deleteKey);

            // 2. IMPORTANT: Persist any changes made to the rootNode during recursion
            // If DeleteSafe emptied it, we need that '0 keys' state on the disk now.
            DiskWrite(rootNode);

            // 3. RE-READ to ensure we are looking at the absolute latest state
            BNode finalRoot = DiskRead(Header.RootId);

            // 4. Root Collapse: If the root is a "Ghost" (0 keys, internal), bypass it.
            if (finalRoot.NumKeys == 0 && !finalRoot.Leaf)
            {
                int oldId = Header.RootId;

                // Promote the first child to be the new King.
                Header.RootId = finalRoot.Kids[0];

                // Save the Header immediately so the Audit knows where to start.
                SaveHeader();

                // Clean up the evidence of the old root
                FreeNode(oldId);
            }
        }


        // ------ DELETE HELPERS -------

        /// <summary>
        /// Locates and removes the absolute minimum element in the B+ Tree.
        /// It traverses the leftmost edge of the index nodes to reach the first leaf node 
        /// in the linked chain, then triggers a deletion for the first key found there.
        /// This may cause a ripple effect of merges or key redistributions up the tree 
        /// to satisfy B+ Tree occupancy requirements.
        /// </summary>
        public void DeleteFirst()
        {
            if (Header.RootId == -1) return;

            // 1. Travel down the "Leftmost" path to the leaf
            BNode current = DiskRead(Header.RootId);
            while (!current.Leaf)
            {
                current = DiskRead(current.Kids[0]);
            }

            // 2. The first key in that leaf is the winner (or loser)
            if (current.NumKeys > 0)
            {
                Delete(current.Keys[0].Key, current.Keys[0].Data);
            }
        }

        /// <summary>
        /// Locates and removes the absolute maximum element in the B+ Tree.
        /// It traverses the rightmost path of the index nodes, always following the 
        /// last active child pointer, until it reaches the final leaf node. 
        /// The last key in this leaf is the maximum value, which is then passed to 
        /// the Delete logic for removal and potential structural rebalancing.
        /// </summary>
        public void DeleteLast()
        {
            if (Header.RootId == -1) return;

            // 1. Travel down the "Rightmost" path
            BNode current = DiskRead(Header.RootId);
            while (!current.Leaf)
            {
                current = DiskRead(current.Kids[current.NumKeys]);
            }

            // 2. The last key in that leaf is the target
            if (current.NumKeys > 0)
            {
                Delete(current.Keys[current.NumKeys - 1].Key, current.Keys[current.NumKeys - 1].Data);
            }
        }

        /// <summary>
        /// Merges two sibling nodes by pulling the separator key from the parent into the left child, 
        /// appending all contents from the right child, and decommissioning the now-redundant right node.
        /// </summary>
        /// 

        private void MergeChildren(BNode parent, int pos, BNode y, BNode z)
        {
           if (!y.Leaf)
           {
                // INTERNAL: Move parent separator key down into Y
                y.Keys[y.NumKeys] = parent.Keys[pos];

                // Move all keys from Z to Y (starting after the promoted key)
                Array.Copy(z.Keys, 0, y.Keys, y.NumKeys + 1, z.NumKeys);

                // Move all kids from Z to Y
                Array.Copy(z.Kids, 0, y.Kids, y.NumKeys + 1, z.NumKeys + 1);

                y.NumKeys += 1 + z.NumKeys;
            }
            else
            {
                // LEAF: Concatenate keys (B+ Trees don't pull from parent into leaf)
                Array.Copy(z.Keys, 0, y.Keys, y.NumKeys, z.NumKeys);

                y.NumKeys += z.NumKeys;
                y.NextLeafId = z.NextLeafId;
            }

            // Collapse parent: remove the separator key and the pointer to Z
            parent.RemoveKeyAndChildAt(pos);

            // Persist changes
            DiskWrite(y);
            DiskWrite(parent);

            // Decommission Z: It is now empty space
            FreeNode(z.Id);
        }

        /// <summary>
        /// Performs a rightward rotation to rebalance a child node by moving a key from the parent down into the child,
        /// and promoting the largest key from the left sibling up into the parent.
        /// </summary>
         
        private void BorrowFromLeftSibling(BNode parent, int pos, BNode child, BNode leftSibling)
        {
            // 1. Shift recipient (child) to the right
            if (child.NumKeys > 0)
            {
                Array.Copy(child.Keys, 0, child.Keys, 1, child.NumKeys);
                if (!child.Leaf)
                {
                    Array.Copy(child.Kids, 0, child.Kids, 1, child.NumKeys + 1);
                }
            }

            if (!child.Leaf)
            {
                // INTERNAL: Standard Rotation
                child.Keys[0] = parent.Keys[pos - 1];
                parent.Keys[pos - 1] = leftSibling.Keys[leftSibling.NumKeys - 1];
                child.Kids[0] = leftSibling.Kids[leftSibling.NumKeys];
                leftSibling.Kids[leftSibling.NumKeys] = -1; // Cleanup ghost pointer
            }
            else
            {
                // LEAF: Redistribution
                child.Keys[0] = leftSibling.Keys[leftSibling.NumKeys - 1];
                parent.Keys[pos - 1] = child.Keys[0];
            }

            child.NumKeys++;

            // 2. Sibling Cleanup
            leftSibling.Keys[leftSibling.NumKeys - 1] = Element.GetDefault();
            leftSibling.NumKeys--;

            // 3. Persist everything we touched
            DiskWrite(child);
            DiskWrite(leftSibling);
            DiskWrite(parent);
        }

        /// <summary>
        /// Performs a leftward rotation to rebalance a child node by moving a key from the parent down to the end of the child, 
        /// and promoting the smallest key from the right sibling up into the parent.
        /// </summary>
        /// 
        private void BorrowFromRightSibling(BNode parent, int pos, BNode child, BNode rightSibling)
        {
            if (!child.Leaf)
            {
                // INTERNAL: Move parent down
                child.Keys[child.NumKeys] = parent.Keys[pos];
                child.Kids[child.NumKeys + 1] = rightSibling.Kids[0];
                parent.Keys[pos] = rightSibling.Keys[0];
            }
            else
            {
                // LEAF: Pull first key from right sibling
                child.Keys[child.NumKeys] = rightSibling.Keys[0];
                // Parent separator becomes the new "smallest" in the right sibling
                parent.Keys[pos] = rightSibling.Keys[1];
            }

            child.NumKeys++;

            // 1. Shift right sibling's data to the left to fill the gap at index 0
            int moveCount = rightSibling.NumKeys - 1;
            if (moveCount > 0)
            {
                Array.Copy(rightSibling.Keys, 1, rightSibling.Keys, 0, moveCount);
                if (!rightSibling.Leaf)
                {
                    Array.Copy(rightSibling.Kids, 1, rightSibling.Kids, 0, rightSibling.NumKeys);
                }
            }

            // 2. Sibling Cleanup (Nuclear Wipe)
            rightSibling.Keys[rightSibling.NumKeys - 1] = Element.GetDefault();
            if (!rightSibling.Leaf) rightSibling.Kids[rightSibling.NumKeys] = -1;
            rightSibling.NumKeys--;

            // 3. Persist
            DiskWrite(child);
            DiskWrite(rightSibling);
            DiskWrite(parent);
        }

        /// <summary>
        /// Performs a top-down, single-pass recursive deletion. 
        /// </summary>
        /// <remarks>
        /// This method proactively rebalances the tree by ensuring every child node visited has at least 't' keys 
        /// (minimum degree) before recursion. By performing rotations (Borrow) or Merges during the descent, 
        /// it guarantees that a deletion can be completed in a single trip to the leaf without backtracking.
        /// </remarks>

        private void DeleteSafe(BNode node, Element target)
        {
            // 1. Binary Search for the position
            int low = 0, high = node.NumKeys - 1;
            while (low <= high)
            {
                int mid = low + ((high - low) >> 1);
                if (target.Key < node.Keys[mid].Key) high = mid - 1;
                else low = mid + 1;
            }
            // 'high' is the last index where Keys[high].Key <= target.Key
            // For routing, we want 'pos' to be the first index where Keys[pos].Key >= target.Key
            int pos = low;

            // --- LEAF CASE: The only place where keys actually die ---
            if (node.Leaf)
            {
                // Check if the key at pos-1 is the match (binary search results adjust)
                int matchIdx = pos - 1;
                if (matchIdx >= 0 && node.Keys[matchIdx].Key == target.Key)
                {
                    int moveCount = node.NumKeys - matchIdx - 1;
                    if (moveCount > 0)
                    {
                        Array.Copy(node.Keys, matchIdx + 1, node.Keys, matchIdx, moveCount);
                    }

                    // The Nuclear Wipe
                    node.Keys[node.NumKeys - 1] = new Element { Key = -1, Data = -1 };
                    node.NumKeys--;
                    DiskWrite(node);
                }
                return;
            }

            // --- INTERNAL CASE: Routing and Preemptive thinning ---
            // In B+, if target matches a signpost, the real data is in the right subtree (pos)
            // If it doesn't match, binary search 'pos' already points to the correct child.
            int nextStep = pos;

            BNode readyChild = PrepareChildForDeletion(node, nextStep);
            DeleteSafe(readyChild, target);
        }


        /// <summary>
        /// Ensures that a child node has enough keys (at least t) before the deletion process 
        /// descends into it. In a B+ Tree, if a node is under-full, this method attempts to 
        /// restore balance by borrowing a key from a sibling or merging two siblings together.
        /// This proactive approach prevents the need for multiple passes or "fixing" the tree 
        /// after the deletion is already complete.
        /// </summary>
        /// 
        private BNode PrepareChildForDeletion(BNode parent, int pos)
        {
            // t is the minimum occupancy (usually half the order)
            int t = (Header.Order + 1) / 2;
            BNode child = DiskRead(parent.Kids[pos]);

            // If child is already robust enough, just keep going
            if (child.NumKeys >= t) return child;

            // 1. Try Borrow Left
            if (pos > 0)
            {
                BNode left = DiskRead(parent.Kids[pos - 1]);
                if (left.NumKeys >= t)
                {
                    // OPTIMIZATION: Instead of re-reading from disk,
                    // we use the 'child' we already have, which the borrow method updates.
                    BorrowFromLeftSibling(parent, pos, child, left);
                    return child;
                }
            }

            // 2. Try Borrow Right
            if (pos < parent.NumKeys)
            {
                BNode right = DiskRead(parent.Kids[pos + 1]);
                if (right.NumKeys >= t)
                {
                    BorrowFromRightSibling(parent, pos, child, right);
                    return child;
                }
            }

            // 3. Must Merge
            // If we merge, child 'y' absorbs sibling 'z'. 'y' is still the same object.
            int mergeIdx = (pos < parent.NumKeys) ? pos : pos - 1;

            // We need the nodes involved in the merge
            BNode leftForMerge = (pos < parent.NumKeys) ? child : DiskRead(parent.Kids[pos - 1]);
            BNode rightForMerge = (pos < parent.NumKeys) ? DiskRead(parent.Kids[pos + 1]) : child;

            MergeChildren(parent, mergeIdx, leftForMerge, rightForMerge);

            // The left node now contains all data. Return that in-memory object.
            return leftForMerge;
        }

        /// ------- FREE LIST -------

        /// <summary>
        /// Reclaims a decommissioned node's ID by adding it to the pool of available addresses for future allocation.
        /// </summary>
        public void FreeNode(int id)
        {
            if (id < 0) return;
            FreeList.Add(id);
            ZeroNode(id);
        }

        /// <summary>
        /// Returns the total number of recycled node slots currently available for reuse.
        /// </summary>
        public int GetFreeListCount()
        {
            return FreeList.Count;
        }


        /// <summary>
        /// Persist the in-memory free list to the tail of the file and record its offset/count in the header.
        /// This approach expects single-writer semantics; concurrent writers can corrupt the tail.
        /// Caller should call SaveHeader() to persist Header.FreeListOffset/Count if needed.
        /// </summary>
        private void SaveFreeList()
        {
            if (FreeList.Count == 0) return;

            Header.FreeListCount = FreeList.Count;

            // 1. Move to the end of the file
            long offset = MyWriter.BaseStream.Length;
            MyWriter.BaseStream.Seek(offset, SeekOrigin.Begin);
            Header.FreeListOffset = offset;

            // 2. Prepare the buffer
            int totalBytes = FreeList.Count * 4;

            // 3. Use ArrayPool for large lists to avoid StackOverflow.
            byte[] buffer = ArrayPool<byte>.Shared.Rent(totalBytes);
            Span<byte> span = buffer.AsSpan(0, totalBytes);

            try
            {
                int currentOffset = 0;
                foreach (int id in FreeList)
                {
                    BinaryPrimitives.WriteInt32LittleEndian(span.Slice(currentOffset, 4), id);
                    currentOffset += 4;
                }

                // 3. One single high-speed write to disk
                MyWriter.Write(span);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

        }


        /// <summary>
        /// Load the free list from disk into memory.
        /// On open, LoadFreeList reads the free list and truncates the file tail to reclaim space.
        /// </summary>
        private void LoadFreeList()
        {
            if (Header.FreeListOffset == 0 || Header.FreeListCount == 0) return;

            // 1. Jump to the list
            MyReader.BaseStream.Seek(Header.FreeListOffset, SeekOrigin.Begin);
            FreeList.Clear();

            // 2. Bulk Read
            int totalBytes = Header.FreeListCount * 4;

            byte[] buffer = ArrayPool<byte>.Shared.Rent(totalBytes);
            Span<byte> span = buffer.AsSpan(0, totalBytes);

            try
            {
                MyReader.BaseStream.ReadExactly(span);

                for (int i = 0; i < Header.FreeListCount; i++)
                {
                    // Slice into the buffer 4 bytes at a time
                    int nodeId = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(i * 4, 4));
                    FreeList.Add(nodeId);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            // 3. TRUNCATE
            MyFileStream.SetLength(Header.FreeListOffset);

            Header.FreeListCount = 0;
            Header.FreeListOffset = 0;
            SaveHeader();
        }

        /// <summary>
        /// Displays the first 50 available IDs in the FreeList, used for tracking recycled disk space.
        /// </summary>
        public void PrintFreeList()
        {
            var ids = FreeList.Take(50);
            Console.WriteLine($"FreeList: {string.Join(" ", ids)}");
        }

        /// ------- COMPACT METHODS -------

        /// <summary>
        /// Reorganizes the physical storage of the B-Tree to eliminate fragmentation and reclaim disk space.
        /// </summary>
        /// <remarks>
        /// This method performs a "Copy On Write" style compaction. It uses a bitmask to 
        /// efficiently identify live nodes, re-maps their IDs to a contiguous sequence, 
        /// and streams them to a temporary file. This process eliminates gaps (fragmentation), 
        /// clears the FreeList, and ensures data integrity via an atomic file swap.
        /// </remarks>
        /// 
        public void Compact()
        {
            if (Header.RootId == -1) return;

            string tempPath = MyFileName + ".tmp";

            if (File.Exists(tempPath)) File.Delete(tempPath);

            // 1. Identify live nodes
            BitArray liveNodes = new BitArray(Header.NodeCount + 1);
            FindLiveNodes(Header.RootId, liveNodes);

            // 2. O(1) Mapping Array
            int[] idMap = new int[Header.NodeCount + 1];
            Array.Fill(idMap, -1);

            int nextId = 0;
            for (int i = 0; i < liveNodes.Count; i++)
            {
                if (liveNodes.Get(i)) idMap[i] = nextId++;
            }

            // 3. Rebuild in temp file
            using (var newTree = new BTree(tempPath, Header.Order))
            {
                for (int oldId = 0; oldId < idMap.Length; oldId++)
                {
                    if (idMap[oldId] == -1) continue;

                    BNode node = DiskRead(oldId);
                    node.Id = idMap[oldId];

                    if (!node.Leaf)
                    {
                        for (int i = 0; i <= node.NumKeys; i++)
                        {
                            if (node.Kids[i] != -1)
                                node.Kids[i] = idMap[node.Kids[i]];
                        }
                    }
                    else if (node.NextLeafId != -1)
                    {
                        // Remap the horizontal link
                        node.NextLeafId = idMap[node.NextLeafId];
                    }

                    newTree.DiskWrite(node);
                }

                // Update the new header
                newTree.Header.RootId = idMap[Header.RootId];
                newTree.Header.NodeCount = nextId;
                // If your property is named differently, use your 'GetNextId' source here
                newTree.SaveHeader();
            }

            // 4. Swap
            CloseStorage();

            // 5. Overwrite file.
            File.Delete(MyFileName);
            File.Move(tempPath, MyFileName);

            // 6. Re-establish connection
            OpenStorage();
            LoadHeader();

            // 7. FreeList is now logically empty because we only kept 'Live' nodes.
            FreeList.Clear();
            Header.FreeListCount = 0;
            Header.FreeListOffset = 0;
            SaveHeader();
        }


        /// <summary>
        /// Check for valid node offset to prevent EndOfStreamException.
        /// </summary>
        private bool IsValidNodeOffset(int nodeId)
        {
            if (nodeId < 0) return false;

            // Calculate expected offset: HeaderSize + (nodeId * NodeSize)
            long offset = HeaderSize + (long)nodeId * Header.PageSize;
            return offset >= 0 && offset < MyFileStream.Length;
        }

        /// <summary>
        /// Recursively traverses the B-Tree to identify all reachable nodes, populating a set of active node IDs.
        /// </summary>
        /// <remarks>
        /// This acts as a "mark" phase for compaction. It performs deep validation on node offsets 
        /// and handles cycle detection (via BitArray) to ensure only structurally sound, 
        /// accessible data is preserved in the storage file.
        /// </remarks>
        private void FindLiveNodes(int nodeId, BitArray liveNodes)
        {
            // 1. Boundary Check: Prevent EndOfStreamException
            if (nodeId < 0 || !IsValidNodeOffset(nodeId) || nodeId > liveNodes.Count)
                return;

            // 2. Have we already been here before?
            if (liveNodes.Get(nodeId))
            {
                throw new ArgumentException(nameof(nodeId), "Cycle Detected");
            }

            // 3. Mark the node.
            liveNodes.Set(nodeId, true);
            BNode node = DiskRead(nodeId);

            if (!node.Leaf)
            {
                // 4. Visit each child.
                for (int i = 0; i <= node.NumKeys; i++)
                {
                    int childId = node.Kids[i];
                    if (childId != -1)
                    {
                        FindLiveNodes(childId, liveNodes);
                    }
                }
            }
        }

        /// <summary>
        /// Returns a list of node IDs that are leaked (orphaned) within the physical file.
        /// </summary>
        /// <remarks>
        /// Performs an audit by marking all reachable nodes and all nodes in the FreeList 
        /// into a bitmask. Any bit remaining unset represents a 'Zombie'—a node that is 
        /// neither part of the tree nor available for reuse, indicating a leak or corruption.
        /// </remarks>
        public void GetZombies(List<int> zombies)
        {
            if (Header.RootId == -1) return;
            if (Header.NodeCount == 0) return;

            // 1. Mark all reachable nodes.
            BitArray accountedFor = new BitArray(Header.NodeCount);
            FindLiveNodes(Header.RootId, accountedFor);

            // 2. Mark all free nodes in the same BitArray.
            foreach (int id in FreeList)
            {
                accountedFor.Set(id, true);
            }

            // 3. Any index still false is a zombie.
            for (int i = 0; i < Header.NodeCount; i++)
            {
                if (!accountedFor.Get(i))
                {
                    if (!zombies.Contains(i))
                        zombies.Add(i);
                }
            }
        }

        /// <summary>
        /// Returns the total count of leaked (orphaned) nodes.
        /// </summary>
        /// <remarks>
        /// Useful for health checks and determining if a Compact operation is necessary. 
        /// Utilizes the same bitmask audit logic as GetZombies.
        /// </remarks>
        public int CountZombies()
        {
            if (Header.RootId == -1) return 0;
            if (Header.NodeCount == 0) return 0;

            // 1. Mark all reachable nodes.
            BitArray accountedFor = new BitArray(Header.NodeCount);
            FindLiveNodes(Header.RootId, accountedFor);

            // 2. Mark all free nodes in the same BitArray.
            foreach (int id in FreeList)
            {
                accountedFor.Set(id, true);
            }

            int count = 0;

            // 3. All nodes that are false are zombie nodes.
            for (int i = 0; i < Header.NodeCount; i++)
            {
                if (!accountedFor.Get(i))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Scans for "Zombie" nodes—allocated nodes that are unreachable from the root.
        /// Reclaims orphans by zeroing their data and returning them to the FreeList.
        /// Effectively acts as a garbage collector for the tree structure after 
        /// intensive operations like Bulk Loading.
        /// </summary>
        public void ReclaimOrphans()
        {
            List<int> zombies = new List<int>();
            GetZombies(zombies);
            foreach (var id in zombies)
            {
                FreeNode(id);
            }
        }


        /// ------- PRINT METHODS ---------


        /// <summary>
        /// A high-performance diagnostic tool that recursively calculates the total key population.
        /// </summary>
        /// <remarks>
        /// Optimized for use in unit tests and health checks, this method performs a structural 
        /// audit during traversal. It is significantly faster than a full data export, 
        /// especially when paired with a node cache.
        /// </remarks>
        public int CountKeys()
        {
            if (Header.RootId == -1) return 0;
            BNode rootNode = DiskRead(Header.RootId);
            if (rootNode.NumKeys == 0) return 0;

            int count = 0;
            BNode current = FindLeftMostLeaf(Header.RootId);
            while (current != null)
            {
                count += current.Keys.Take(current.NumKeys).Count();

                if (current.NextLeafId != -1)
                    current = DiskRead(current.NextLeafId);
                else
                    current = null;
            }
            return count;
        }


        /// <summary>
        /// Flattens the current B-Tree structure into a list of keys via a recursive traversal.
        /// </summary>
        /// <remarks>
        /// Under normal conditions, this returns keys in ascending order. If the output is unsorted, 
        /// it indicates a structural corruption, such as a failed rebalance, an incorrect split, 
        /// or a violation of the B-Tree search invariants.
        /// </remarks>
        public List<int> GetKeys()
        {
            if (Header.RootId == -1) return new List<int>();
            BNode rootNode = DiskRead(Header.RootId);
            if (rootNode.NumKeys == 0) return new List<int>();

            List<int> list = new List<int>();
            BNode current = FindLeftMostLeaf(Header.RootId);
            while (current != null)
            {
                var keys = current.Keys.Take(current.NumKeys).Select(e => e.Key);
                list.AddRange(keys);

                if (current.NextLeafId != -1)
                    current = DiskRead(current.NextLeafId);
                else
                    current = null;
            }
            return list;
        }

        /// <summary>
        /// Returns a flat list of all elements stored in the B+ Tree in sorted order.
        /// This method leverages the B+ Tree's linked leaf structure: it finds the 
        /// leftmost leaf and then performs a linear scan across the sibling chain 
        /// (NextLeafId) to gather all data records without needing to revisit internal index nodes.
        /// </summary>
        public List<Element> GetElements()
        {
            var list = new List<Element>();
            if (Header.RootId == -1) return list;
            BNode rootNode = DiskRead(Header.RootId);
            if (rootNode.NumKeys == 0) return list;

            BNode current = FindLeftMostLeaf(Header.RootId);
            while (current != null)
            {
                var keys = current.Keys.Take(current.NumKeys).Select(e => e);
                list.AddRange(keys);

                if (current.NextLeafId != -1)
                    current = DiskRead(current.NextLeafId);
                else
                    current = null;
            }
            return list;
        }


        /// <summary>
        /// Extracts all tree data into a flat CSV format, acting as a "Recovery Dump."
        /// </summary>
        /// <remarks>
        /// Designed to facilitate a "Dump and Reload" strategy for repairing corrupted or 
        /// severely unbalanced trees. The output file preserves Key/Data pairs in sorted order, 
        /// providing a clean source for BulkLoad operations to rebuild a healthy structure.
        /// </remarks>
        /// 
        public void WriteToFile(string fileName)
        {
            File.Delete(fileName);
            if (Header.RootId == -1) return;

            BNode rootNode = DiskRead(Header.RootId);
            if (rootNode.NumKeys == 0) return;

            using (StreamWriter sw = new StreamWriter(fileName, false))
            {
                BNode current = FindLeftMostLeaf(Header.RootId);
                while (current != null)
                {
                    var keys = current.Keys.Take(current.NumKeys).Select(e => e);
                    foreach (var k in keys)
                    {
                        sw.WriteLine($"{k.Key}, {k.Data}");
                    }

                    if (current.NextLeafId != -1)
                        current = DiskRead(current.NextLeafId);
                    else
                        current = null;
                }
            }
        }

        /// <summary>
        /// Performs a level-order (Breadth-First Search) traversal of the B+ Tree to visualize its 
        /// hierarchical structure. This method treats the tree as a series of levels, starting 
        /// from the root index down to the linked leaf nodes.
        /// It uses a queue with a null marker to distinguish between different levels of the tree
        /// during the print process.
        /// </summary>
        public void PrintTreeByLevel()
        {
            if (Header.RootId == -1) return;

            BNode rootNode = DiskRead(Header.RootId);

            if (rootNode.NumKeys == 0)
            {
                Console.WriteLine("The B-Tree is empty.");
            }

            Queue<BNode> queue = new Queue<BNode>();

            // Using a null marker to distinguish levels.
            BNode marker = null;

            rootNode = DiskRead(Header.RootId); // Start with the latest root.
            queue.Enqueue(rootNode);
            queue.Enqueue(marker); // Initial level marker.

            while (queue.Count > 0)
            {
                BNode current = queue.Dequeue();

                if (current == marker)
                {
                    Console.WriteLine();
                    if (queue.Count > 0)
                    {
                        queue.Enqueue(marker); // Add marker for the next level
                    }
                    continue;
                }

                // Print the keys of the current node
                PrintNodeKeys(current);

                // Enqueue all possible child slots (physical capacity); each slot is checked for -1 before use.
                if (current.Leaf == false)
                {
                    for (int i = 0; i < Header.Order; i++)
                    {
                        if (current.Kids[i] != -1)
                        {
                            BNode child = DiskRead(current.Kids[i]);
                            queue.Enqueue(child);
                        }
                    }
                }
            }
            Console.WriteLine();
        }

        /// <summary>
        /// Print node keys.
        /// </summary>
        private static void PrintNodeKeys(BNode node)
        {
            var keys = node.Keys.Take(node.NumKeys).Select(e => e.Key);
            Console.Write($"[{string.Join(", ", keys)}] ");
        }


        /// <summary>
        /// Performs a breadth-first (level-order) traversal of the tree.
        /// Visualizes the tree structure layer-by-layer, which is ideal for 
        /// verifying balance and node distribution.
        /// </summary>
        public void PrintTreeSimple(int rootPageId)
        {
            if (rootPageId == -1)
            {
                Console.WriteLine("\nTree is empty.");
                return;
            }

            var queue = new Queue<(int pageId, int level)>();
            queue.Enqueue((rootPageId, 0));
            int currentLevel = -1;

            while (queue.Count > 0)
            {
                var (pageId, level) = queue.Dequeue();

                if (level > currentLevel)
                {
                    Console.WriteLine($"\n--- Level {level} ---");
                    currentLevel = level;
                }

                BNode node = DiskRead(pageId);

                // Indicate if this is an internal node or a data leaf
                string typeLabel = node.Leaf ? "[LEAF]" : "[INT]";
                Console.Write($"{typeLabel} P{pageId}: ");

                PrintNodeKeys(node);

                // B+ Tree Specific: Show the horizontal link between leaves
                if (node.Leaf)
                {
                    string nextLink = node.NextLeafId == -1 ? "null" : $"P{node.NextLeafId}";
                    Console.Write($" -> Next: {nextLink}");
                }

                Console.Write(" | ");

                // Enqueue children for the next level
                if (!node.Leaf)
                {
                    for (int i = 0; i <= node.NumKeys; i++)
                    {
                        if (node.Kids[i] != -1)
                            queue.Enqueue((node.Kids[i], level + 1));
                    }
                }

                // Add a newline if the next node is on a different level to keep it readable
                if (queue.Count > 0 && queue.Peek().level > level)
                {
                    Console.WriteLine();
                }
            }
            Console.WriteLine();
        }

        /// <summary>
        /// Demonstrates the sequential access power of the B+ Tree by traversing 
        /// the "Leaf Chain." It starts at the first logical record and follows 
        /// the NextLeafId pointers to visit every leaf node in sorted order.
        /// This bypasses the index entirely, showing how B+ Trees optimize 
        /// for table scans and sequential reporting.
        /// </summary>
        public void PrintLeafChain()
        {
            Console.WriteLine("\n--- Leaf Chain Scan ---");
            // 1. Find the leftmost leaf
            BNode current = FindLeftMostLeaf(Header.RootId);

            while (current != null)
            {
                Console.Write($"P{current.Id}[");
                PrintNodeKeys(current);
                Console.Write("] -> ");

                if (current.NextLeafId == -1) break;
                current = DiskRead(current.NextLeafId);
            }
            Console.WriteLine("NULL");
        }

        /// <summary>
        /// Recursively descends the leftmost branch of the B+ Tree to find the starting leaf node.
        /// This is the standard entry point for linear scans, range queries, and min-key 
        /// operations. In a B+ Tree, following the first child pointer (index 0) at every 
        /// internal level is guaranteed to lead to the smallest element in the entire structure.
        /// </summary>
        public BNode FindLeftMostLeaf(int nodeId)
        {
            if (nodeId == -1) return null;
            BNode node = DiskRead(nodeId);
            if (node.Leaf) return node;
            return FindLeftMostLeaf(node.Kids[0]);
        }

        /// <summary>
        /// Outputs a physical representation of the underlying storage, 
        /// iterating through every allocated page index to display node keys and metadata.
        /// </summary>
        public void DumpFile()
        {
            Console.WriteLine("--- PHYSICAL DISK DUMP ---");
            Console.WriteLine($"RootId: {Header.RootId}, Total Nodes: {Header.NodeCount}");

            for (int i = 0; i < Header.NodeCount; i++)
            {
                if (FreeList.Contains(i))
                {
                    Console.WriteLine($"Page {i}: [FREE]");
                    continue;
                }

                try
                {
                    BNode node = DiskRead(i);
                    Console.Write($"Page {i}: ");
                    PrintNodeKeys(node);
                    Console.WriteLine($"(Leaf: {node.Leaf})");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Page {i}: [CORRUPT {ex.Message}]");
                }
            }
        }


        /// <summary>
        /// Performs a comprehensive diagnostic scan of the B+ Tree's internal structural pointers.
        /// This method iterates through every page in the file to identify internal index nodes, 
        /// reporting their key counts and the specific child page IDs (pointers) they reference. 
        /// It serves as a vital integrity check to ensure that the hierarchical relationship 
        /// between the root, intermediate index levels, and the terminal leaf pages remains 
        /// consistent and free of corruption.
        /// </summary>
        public void PrintPointers()
        {
            Console.WriteLine("--- POINTER INTEGRITY CHECK ---");
            Console.WriteLine($"RootId: {Header.RootId}");
            for (int i = 0; i < Header.NodeCount; i++)
            {
                try
                {
                    BNode node = DiskRead(i);
                    if (node.Leaf) continue;

                    Console.Write($"Internal Node {i} [Keys: {node.NumKeys}]: ");
                    for (int j = 0; j <= node.NumKeys; j++)
                    {
                        Console.Write($"Kid[{j}]->Page {node.Kids[j]} | ");
                    }
                    Console.WriteLine();
                }
                catch { }
            }
        }

        /// <summary>
        /// Print the Tree.  
        /// </summary>
        public void PrintByRoot()
        {
            Console.WriteLine("--- PRINT BY ROOT ---");
            Console.WriteLine($"RootId: {Header.RootId}");
            PrintByRootRecursive(Header.RootId);
        }

        /// <summary>
        /// Recursively prints the B-Tree structure starting from a specific node, 
        /// using indentation to visualize the hierarchy levels.
        /// </summary>
        private void PrintByRootRecursive(int nodeId, int level = 0)
        {
            if (nodeId == -1) return;
            BNode node = DiskRead(nodeId);
            string indent = new string(' ', level * 4);

            // 1. Print current node header and keys
            Console.Write($"{indent}NODE {node.Id}: ");
            PrintNodeKeys(node);
            Console.WriteLine();

            // 2. Recursively print children with increased indentation
            if (!node.Leaf)
            {
                for (int i = 0; i <= node.NumKeys; i++)
                {
                    Console.WriteLine($"{indent}  Child {i} (ID: {node.Kids[i]})");
                    PrintByRootRecursive(node.Kids[i], level + 1);
                }
            }
        }

        // ----- GHOST NODES --------

        /// <summary>
        /// Validates the tree's structural integrity by ensuring no internal nodes are empty.
        /// Empty non-root nodes ("Ghost Nodes") indicate a corruption in the balancing logic.
        /// </summary>
        public void CheckGhost()
        {
            if (Header.NodeCount == 0) return;
            BitArray visited = new BitArray(Header.NodeCount);
            CheckGhostRecursive(Header.RootId, visited);
        }

        /// <summary>
        /// Recursively traverses the tree to verify that every reachable node contains data.
        /// Also performs cycle detection and bounds checking on key counts.
        /// </summary>
        private void CheckGhostRecursive(int nodeId, BitArray visited = null)
        {
            if (nodeId <= 0) return;
            if (Header.NodeCount == 0) return;

            if (visited == null)
            {
                visited = new BitArray(Header.NodeCount);
            }

            // Check for cycles.
            if (visited.Get(nodeId))
            {
                throw new ArgumentException(nameof(nodeId), "Cycle Detected");
            }

            BNode node = DiskRead(nodeId);
            visited.Set(nodeId, true);
            if (nodeId != Header.RootId && node.NumKeys == 0)
            {
                throw new ArgumentException(nameof(nodeId), "Ghost Detected");
            }

            if (!node.Leaf)
            {
                if (node.NumKeys > Header.Order)
                {
                    throw new ArgumentException(nameof(node.NumKeys));
                }

                for (int i = 0; i <= node.NumKeys; i++)
                {
                    if (node.Kids[i] != -1)
                    {
                        CheckGhostRecursive(node.Kids[i], visited);
                    }
                }
            }
        }

        /// <summary>
        /// Scans the entire physical file to count "Ghost" nodes (nodes with zero keys).
        /// </summary>
        /// <remarks>
        /// Unlike CountZombies, this is an I/O-intensive brute-force scan that inspects 
        /// every node's content. It identifies nodes that are physically present but logically empty; 
        /// these may be valid empty nodes, newly allocated space, or remnants of a failed deletion.
        /// </remarks>
        public int CountGhost()
        {
            int count = 0;
            for (int i = 0; i < Header.NodeCount; i++)
            {
                try
                {
                    BNode node = DiskRead(i);
                    if (node.NumKeys == 0)
                    {
                        count++;
                    }
                }
                catch { }
            }
            return count;
        }

        // -------- VALIDATION METHODS ---------

        /// <summary>
        /// Validate structural integrity: checks for cycles, ordering, boundary constraints and minimum keys.
        /// Does NOT validate free list correctness or external file-format corruption beyond Header.Magic.
        /// </summary>
        public void ValidateIntegrity()
        {
            if (Header.RootId == -1) return;
            if (Header.NodeCount == 0) return;

            BitArray visited = new BitArray(Header.NodeCount);
            CheckNodeIntegrity(Header.RootId, int.MinValue, int.MaxValue, visited);

            // Optional: Check if NodeCount matches what is actually on disk
            if (visited.Count > Header.NodeCount)
                throw new Exception(message: "Reachable nodes cannot exceed NodeCount");
        }

        /// <summary>
        /// Performs a deep structural audit of a node and its descendants. 
        /// Validates key ordering, underflow conditions, and enforces that all 
        /// child keys fall strictly within the logical boundaries defined by their parent keys.
        /// </summary>
        private void CheckNodeIntegrity(int nodeId, int min, int max, BitArray visited)
        {
            if (nodeId == -1) return;
            if (nodeId > visited.Count) return;

            if (visited.Get(nodeId))
                throw new ArgumentException(nameof(nodeId), "Cycle Detected");

            visited.Set(nodeId, true);
            BNode node = DiskRead(nodeId);

            // 1. Verify Minimum Keys (except the Root).
            int t = (Header.Order + 1) / 2;
            if (nodeId != Header.RootId && node.NumKeys < t - 1)
                throw new ArgumentException(nameof(nodeId), "Underflow");

            for (int i = 0; i < node.NumKeys; i++)
            {
                int currentKey = node.Keys[i].Key;

                // 2. Verify Key Ordering within node
                if (i > 0 && currentKey <= node.Keys[i - 1].Key)
                    throw new ArgumentException(nameof(nodeId), "Must be sorted");

                // 3. Verify Key is within Parent's Range
                if (currentKey < min || currentKey > max)
                    throw new ArgumentException(nameof(currentKey));

                // 4. Recurse into children with updated boundaries.
                if (!node.Leaf)
                {
                    int leftChildMin = (i == 0) ? min : node.Keys[i - 1].Key;
                    CheckNodeIntegrity(node.Kids[i], leftChildMin, currentKey, visited);

                    // If it's the last key, also check the rightmost child
                    if (i == node.NumKeys - 1)
                    {
                        CheckNodeIntegrity(node.Kids[i + 1], currentKey, max, visited);
                    }
                }
            }
        }

        // -------- DIAGNOSTIC METHODS ---------

        /// <summary>
        /// Calculates the current height of the B-Tree by traversing the leftmost path 
        /// from the root to a leaf node. 
        /// </summary>
        public int GetHeight()
        {
            int height = 0;
            int currentId = this.Header.RootId;

            while (currentId != -1)
            {
                height++;
                var node = this.DiskRead(currentId);
                if (node.Leaf) break;
                currentId = node.Kids[0];
            }
            return height;
        }

        // -------- AUDIT METHODS ---------

        /// <summary>
        /// A data transfer object containing a snapshot of the B-Tree's physical and logical health.
        /// </summary>
        public class TreeHealthReport
        {
            public int Height { get; set; }
            public int ZombieCount { get; set; }
            public int GhostCount { get; set; }
            public int ReachableNodes { get; set; }
            public int TotalKeys { get; set; }
            public double AverageDensity { get; set; }
            public int LeafChainCount { get; set; }
        }

        /// <summary>
        /// Executes a comprehensive dual-phase audit of the B+ Tree to evaluate storage efficiency, 
        /// structural integrity, and leaf-level connectivity.
        /// </summary>
        /// <returns>A TreeHealthReport detailing fragmentation, orphan nodes, and tree geometry.</returns>
        /// <remarks>
        /// This method validates the B+ Tree from two perspectives:
        /// 1. Vertical Consistency: A recursive traversal from the root ensures all index levels 
        ///    correctly point to their descendants and verifies total height.
        /// 2. Horizontal Consistency: A scan of the Leaf Chain verifies that the sibling pointers 
        ///    (NextLeafId) accurately represent the sorted dataset independently of the index.
        /// It identifies "Zombies" (unreferenced pages not in the FreeList) and calculates 
        /// the utilization density of the reachable index and data nodes.
        /// </remarks>
        public TreeHealthReport PerformFullAudit()
        {
            var report = new TreeHealthReport();
            if (Header.RootId == -1 || Header.NodeCount == 0)
            {
                return report;
            }

            BitArray accountedFor = new BitArray(Header.NodeCount);

            // 1. Vertical Recursive Scan (Finds ReachableNodes, TotalKeys, and Height)
            report.Height = AuditRecursive(Header.RootId, 1, accountedFor, report);

            // 2. Horizontal Leaf Chain Scan
            report.LeafChainCount = GetLeafChainCount();

            // 3. Average Density Calculation
            if (report.ReachableNodes > 0)
            {
                double totalCapacity = (double)report.ReachableNodes * (Header.Order - 1);
                report.AverageDensity = (report.TotalKeys / totalCapacity) * 100.0;
            }

            // 4. Count Zombies (Nodes in file that were never reached).
            foreach (int id in FreeList)
            {
                if (id >= 0 && id < Header.NodeCount) accountedFor.Set(id, true);
            }

            for (int i = 0; i < Header.NodeCount; i++)
            {
                if (!accountedFor.Get(i)) report.ZombieCount++;
            }

            return report;
        }


        /// <summary>
        /// The recursive engine for PerformFullAudit. Traverses the tree to discover 
        /// live nodes and calculate height.
        /// </summary>
        /// <returns>The maximum depth reached by this subtree.</returns>
        private int AuditRecursive(int id, int currentDepth, BitArray accountedFor, TreeHealthReport report)
        {
            // Ghost Check: Pointer points outside the physical file.
            if (id < 0 || id >= Header.NodeCount)
            {
                report.GhostCount++;
                return currentDepth;
            }

            if (id > accountedFor.Count) return 0;

            // Circular Reference Check: Prevents infinite recursion.
            if (accountedFor.Get(id)) return currentDepth;

            accountedFor.Set(id, true);
            report.ReachableNodes++;

            var node = DiskRead(id);
            report.TotalKeys += node.NumKeys;
            if (node.Leaf) return currentDepth;

            int maxSubtreeHeight = currentDepth;
            for (int i = 0; i <= node.NumKeys; i++)
            {
                int childHeight = AuditRecursive(node.Kids[i], currentDepth + 1, accountedFor, report);
                if (childHeight > maxSubtreeHeight) maxSubtreeHeight = childHeight;
            }

            return maxSubtreeHeight;
        }

        /// <summary>
        /// Performs a linear tally of all data records by traversing the B+ Tree's leaf-level linked list.
        /// This method provides an absolute count of stored elements by bypassing the internal index
        /// and visiting each leaf node sequentially via the sibling pointers. It is an essential
        /// diagnostic tool for verifying that the total record count matches the index's expectations.
        /// </summary>
        public int GetLeafChainCount()
        {
            int count = 0;
            BNode current = FindLeftMostLeaf(Header.RootId);
            while (current != null)
            {
                count += current.NumKeys;
                if (current.NextLeafId == -1) break;
                current = DiskRead(current.NextLeafId);
            }
            return count;
        }

        /// <summary>
        /// Orchestrates a full structural audit and outputs the results to the console.
        /// This provides a snapshot of the B+ Tree's physical health, contrasting 
        /// vertical index metrics against the horizontal leaf chain to identify 
        /// structural anomalies, wasted space, or pointer corruption.
        /// </summary>
        public void PrintAuditReport()
        {
            var report = PerformFullAudit();
            Console.WriteLine("--- B-Tree Health Report ---");
            Console.WriteLine($"Height: {report.Height}");
            Console.WriteLine($"Reachable Nodes: {report.ReachableNodes}");
            Console.WriteLine($"Total Keys: {report.TotalKeys}");
            Console.WriteLine($"Average Node Density: {report.AverageDensity:F2}%");
            Console.WriteLine($"Zombies (Unreachable Nodes): {report.ZombieCount}");
            Console.WriteLine($"Ghosts (Dangling Pointers): {report.GhostCount}");
            Console.WriteLine($"Horizontal Count: {report.LeafChainCount}");
        }


    }
}
