namespace ArcOne
{
    public static class Util
    {
        private static readonly Random rnd = new Random();

        /// <summary>
        /// Performs an in-place Fisher-Yates shuffle to randomize the order of elements.
        /// </summary>
        /// <remarks>
        /// Used primarily by stress tests to ensure that B-Tree insertions and deletions 
        /// occur in a non-sequential order, forcing complex node splits and rebalancing.
        /// </remarks>
        public static void Shuffle(List<int> list)
        {
            int n = list.Count;
            while (n > 1)
            {
                n--;
                int k = rnd.Next(n + 1);
                int value = list[k];
                list[k] = list[n];
                list[n] = value;
            }
        }

        /// <summary>
        /// Shuffle a list of any type using the Fisher-Yates algorithm.
        /// </summary>
        public static void ShuffleList<T>(List<T> list)
        {
            int n = list.Count;
            while (n > 1)
            {
                n--;
                int k = rnd.Next(n + 1);
                T value = list[k];
                list[k] = list[n];
                list[n] = value;
            }
        }

        /// <summary>
        /// Print a list.
        /// </summary>
        public static void PrintList(List<int> list)
        {
            for (int i = 0; i < list.Count; i += 20)
            {
                var chunk = list.Skip(i).Take(20).ToArray();
                Console.WriteLine(string.Join(" ", chunk));
            }
            Console.WriteLine();
        }

        /// <summary>
        /// Optimized check to verify that a list of integers is in non-descending order.
        /// </summary>
        /// <remarks>
        /// Used as a high-speed pre-condition check for operations requiring ordered keys, 
        /// such as bulk loading or validating sequence-generated IDs.
        /// </remarks>
        public static bool IsSorted(List<int> a)
        {
            for (int i = 1; i < a.Count; i++)
            {
                if (a[i - 1] > a[i])
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Verifies that a list is in non-descending order based on the provided or default comparer.
        /// </summary>
        /// <remarks>
        /// Critical for BulkLoad operations, where unsorted input would violate B-Tree 
        /// invariants and lead to structural corruption.
        /// </remarks>
        public static bool IsSortedList<T>(List<T> list, IComparer<T>? comparer = default)
        {
            if (list.Count <= 1)
                return true;

            comparer ??= Comparer<T>.Default;

            for (var i = 1; i < list.Count; i++)
            {
                if (comparer.Compare(list[i - 1], list[i]) > 0)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Does the list have any duplicates?
        /// </summary>
        public static bool HasDuplicate(List<int> source)
        {
            var set = new HashSet<int>();
            foreach (var item in source)
            {
                if (!set.Add(item))
                    return true;
            }
            return false;
        }


    }
}
