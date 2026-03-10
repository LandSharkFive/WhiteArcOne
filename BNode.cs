using System.Buffers.Binary;

namespace ArcOne
{
    /// <summary>
    /// Represents a node in a B+ Tree. 
    /// In this B+ Tree implementation, internal nodes act as a navigation index, 
    /// while leaf nodes contain the actual data elements and links to sibling leaves.
    /// </summary>
    public class BNode
    {
        public int Order { get; set; }   // The maximum number of children allowed (fan-out).
        public int PageSize { get; set; }  // The fixed size of the node record in bytes for disk I/O.
        public int NumKeys { get; set; }    // Number of active keys currently in the node.
        public bool Leaf { get; set; }     // True if this is a leaf node; false if it is an internal index node.
        public int Id { get; set; }  // Unique identifier/index of the node within the database file.

        // Arrays replace C pointers and dynamic allocation to facilitate fixed-size disk serialization.
        public Element[] Keys { get; set; } // Holds the keys (and data elements if this is a leaf).
        public int[] Kids { get; set; }     // Holds IDs of child nodes (internal nodes only).
        public int NextLeafId { get; set; } = -1; // Pointer to the next sibling leaf for fast range scans (B+ Tree specific).

        /// <summary>
        /// Initializes a new B+ Tree node.
        /// </summary>
        public BNode(int order, bool leaf)
        {
            Order = order;
            PageSize = CalculateNodeSize(order);
            Leaf = leaf;
            Keys = new Element[order];
            Kids = new int[order + 1];
            Array.Fill(Keys, new Element(-1, -1));
            Array.Fill(Kids, -1);
        }

        /// <summary>
        /// Initializes a new internal (navigation) node.
        /// </summary>
        public BNode(int order) : this(order, false) { }


        /// <summary>
        /// Calculates the fixed byte size of a node based on the B+ Tree order 
        /// to ensure consistent alignment on disk.
        /// </summary>
        public static int CalculateNodeSize(int order)
        {
            return (order * 12) + 20;
        }

        /// <summary>
        /// Removes a key and its associated child pointer by shifting subsequent elements to the left.
        /// This is used during node rebalancing and merging.
        /// Performs a "nuclear wipe" of trailing slots to ensure no stale data is written to disk.
        /// </summary>
        /// 

        public void RemoveKeyAndChildAt(int pos)
        {
            int moveCount = NumKeys - pos - 1;

            // 1. & 2. Shift Keys and Children to the left
            if (moveCount > 0)
            {
                Array.Copy(Keys, pos + 1, Keys, pos, moveCount);
                // We shift kids starting from pos + 2 to collapse the merged sibling's pointer
                Array.Copy(Kids, pos + 2, Kids, pos + 1, moveCount);
            }

            // 3. Decrement the logical count
            NumKeys--;

            // 4. THE NUCLEAR WIPE
            // Instead of loops, we clear the memory from the new NumKeys to the end.

            // Wipe unused Key slots
            int keysToClear = Keys.Length - NumKeys;
            Array.Fill(Keys, new Element { Key = -1, Data = -1 }, NumKeys, keysToClear);

            // Wipe unused Child pointers
            int startWipingKids = Leaf ? 0 : NumKeys + 1;
            int kidsToClear = Kids.Length - startWipingKids;
            if (kidsToClear > 0)
            {
                Array.Fill(Kids, -1, startWipingKids, kidsToClear);
            }
        }

        public override string ToString()
        {
            string keysStr = string.Join(" ", Keys.Take(NumKeys).Select(k => k.Key));
            string kidsStr = Leaf ? $"Next Leaf: {NextLeafId}" : string.Join(" ", Kids.Take(NumKeys + 1));

            return $"Node {Id} (Leaf: {Leaf}, Keys: {NumKeys})\n" +
                   $"Keys: [{keysStr}]\n" +
                   $"Links: [{kidsStr}]";
        }

        /// <summary>
        /// Hydrates the node by reading its binary representation from disk.
        /// Handles the metadata header and the fixed-size key/child arrays.
        /// </summary>
        public void Read(BinaryReader reader)
        {
            // 1. Read the raw node data into a stack-allocated buffer for efficiency
            Span<byte> buffer = stackalloc byte[PageSize];
            reader.Read(buffer);

            int offset = 0;

            // 2. Read B+ Tree Metadata Header (16 bytes)
            Leaf = BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(offset, 4)) == 1;
            offset += 4;
            NumKeys = BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(offset, 4));
            offset += 4;
            Id = BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(offset, 4));
            offset += 4;
            NextLeafId = BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(offset, 4));
            offset += 4;

            // 3. Read Keys and Data elements
            for (int i = 0; i < Order; i++)
            {
                Keys[i].Key = BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(offset, 4));
                Keys[i].Data = BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(offset + 4, 4));
                offset += 8;
            }

            // 4. Read Child node pointers
            for (int i = 0; i <= Order; i++)
            {
                Kids[i] = BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(offset, 4));
                offset += 4;
            }
        }

        /// <summary>
        /// Serializes the node into a binary format.
        /// Active keys are written normally, while unused slots are padded with -1 
        /// to maintain fixed page sizes on disk.
        /// </summary>
        public void Write(BinaryWriter writer)
        {
            // 1. Prepare buffer
            Span<byte> buffer = stackalloc byte[PageSize];
            int offset = 0;

            // 2. Pack Metadata Header
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(offset, 4), Leaf ? 1 : 0);
            offset += 4;
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(offset, 4), NumKeys);
            offset += 4;
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(offset, 4), Id);
            offset += 4;
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(offset, 4), NextLeafId);
            offset += 4;

            // 3. Pack Keys (Fixed size based on Order)
            for (int i = 0; i < Order; i++)
            {
                int keyVal = (i < NumKeys) ? Keys[i].Key : -1;
                int dataVal = (i < NumKeys) ? Keys[i].Data : -1;

                BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(offset, 4), keyVal);
                offset += 4;
                BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(offset, 4), dataVal);
                offset += 4;
            }

            // 4. Pack Children (Internal nodes only; Leaves write padding)
            for (int i = 0; i <= Order; i++)
            {
                int kidVal = (i <= NumKeys && !Leaf) ? Kids[i] : -1;
                BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(offset, 4), kidVal);
                offset += 4;
            }

            // 5. Perform the write to the file stream
            writer.Write(buffer);
        }

        /// <summary>
        /// Validates the structural integrity of the node.
        /// Ensures key counts are within bounds and internal nodes have the correct number of children.
        /// </summary>
        public bool Validate()
        {
            if (NumKeys < 0 || NumKeys > Order) return false;

            if (!Leaf)
            {
                // In a B+ Tree, an internal node with N keys must have N+1 valid child pointers.
                for (int i = 0; i <= NumKeys; i++)
                {
                    if (Kids[i] < 0) return false;
                }
            }
            return true;
        }

        public BNode WithId(int id)
        {
            this.Id = id;
            return this;
        }

        public BNode WithKeys(params int[] keys)
        {
            this.NumKeys = keys.Length;
            for (int i = 0; i < keys.Length; i++)
                this.Keys[i] = new Element(keys[i], keys[i]);
            return this;
        }

        public BNode WithChildren(params int[] childIds)
        {
            Array.Copy(childIds, this.Kids, childIds.Length);
            return this;
        }


    }
}