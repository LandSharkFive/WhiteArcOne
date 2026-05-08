using ArcOne;
using UnitTestOne;

namespace UnitTestSeven
{

    [TestClass]
    public sealed class UnitSeven
    {
        private bool IsDebugStress = TestSettings.CanDebugStress;

        /// <summary>Bulk Load with Low Order and Low Volume.</summary>
        [TestMethod]
        public void TestBulkLoadFullOrder4Keys16()
        {
            string myPath = "celery.db";
            File.Delete(myPath);

            // 1. Generate sorted data.
            var data = Enumerable.Range(1, 16).Select(i => new Element(i, i)).ToList();

            // 2. Run Bulk Loader.  Set fill factors to 1.0 (100%). 
            var builder = new TreeBuilder(order: 4, 1.0);
            builder.CreateFromSorted(myPath, data);

            using (var tree = new BTree(myPath))
            {
                // 3. Check Root.
                Assert.IsTrue(tree.Header.RootId != -1, "Root");

                // 4. Check keys counts.
                int total = tree.CountKeys();
                Assert.AreEqual(data.Count, total, "Missing Keys");

                // 5. Check keys.
                var list = tree.GetKeys();
                Assert.IsTrue(Util.IsSorted(list), "Keys must be sorted.");
                Assert.IsFalse(Util.HasDuplicate(list), "Duplicate found");

                // 7. Check searches. 
                foreach (var key in data)
                {
                    int i = key.Key;
                    Element pair;
                    Assert.IsTrue(tree.TrySearch(i, out pair), $"Missing Key {i}");
                }
            }

            File.Delete(myPath);
        }

        /// <summary>Bulk Load with Low Order and Low Volume, and Inserts.</summary>
        [TestMethod]
        public void BulkLoadFullOrder4Keys20()
        {
            string myPath = "cherry.db";
            File.Delete(myPath);

            // 1. Generate 16 sorted keys.
            List<Element> data = new List<Element>();
            for (int i = 1; i <= 16; i++) data.Add(new Element(i, i));

            // 2. Run Bulk Loader.  Set fill factors to 1.0 (100%). 
            var builder = new TreeBuilder(order: 4, 1.0);
            builder.CreateFromSorted(myPath, data);

            using (var tree = new BTree(myPath))
            {
                // 3. Check Root.
                Assert.IsTrue(tree.Header.RootId != -1, "Root");

                // 5. Verify all keys are present.
                int count = tree.CountKeys();
                Assert.AreEqual(data.Count, count, "Missing Keys");

                // 5. Check for zombies.
                Assert.AreEqual(0, tree.CountZombies(), "Zombies");
                Assert.AreEqual(0, tree.GetFreeListCount(), "Free Nodes");

                // 6. Insert 4 more keys to force splits.
                for (int i = 17; i <= 20; i++) tree.Insert(i, i);

                // 7. Check counts again.
                count = tree.CountKeys();
                Assert.AreEqual(20, count, "Missing Keys");

                // 8. Check for zombies again.
                Assert.AreEqual(0, tree.CountZombies(), "Zombies");
                Assert.AreEqual(0, tree.GetFreeListCount(), "Free Nodes");
            }

            File.Delete(myPath);
        }


        /// <summary>Bulk Load with Low Order and Low Volume, and Inserts.</summary>
        [TestMethod]
        public void BulkLoadFullOrder5Keys30()
        {
            string myPath = "oats.db";
            File.Delete(myPath);

            // 1. Generate 24 sorted keys. 
            List<Element> data = new List<Element>();
            for (int i = 1; i <= 24; i++) data.Add(new Element(i, i));

            // 2. Run Bulk Loader.  Set fill factors to 1.0 (100%). 
            var builder = new TreeBuilder(order: 5, 1.0);
            builder.CreateFromSorted(myPath, data);

            using (var tree = new BTree(myPath))
            {
                // 3. Check Root.
                Assert.IsTrue(tree.Header.RootId != -1, "Root");

                // 4. Verify all keys are present.
                int count = tree.CountKeys();
                Assert.AreEqual(data.Count, count, "Missing Keys");

                // 5. Check for zombies.
                Assert.AreEqual(0, tree.CountZombies(), "Zombies");
                Assert.AreEqual(0, tree.GetFreeListCount(), "Free Nodes");

                // 6. Insert 6 more keys to force splits.
                for (int i = 25; i <= 30; i++) tree.Insert(i, i);

                // 7. Check counts again.
                count = tree.CountKeys();
                Assert.AreEqual(30, count, "Missing Keys After Insert.");

                // 8. Check for zombies again.
                Assert.AreEqual(0, tree.CountZombies(), "Zombies");
                Assert.AreEqual(0, tree.GetFreeListCount(), "Free Nodes");
            }

            File.Delete(myPath);
        }

        /// <summary>
        /// Bulk Load with Low Order and Moderate Volume.  This test is designed to create a tree with multiple levels and to verify 
        /// that the bulk loader correctly handles the creation of internal nodes and the promotion of keys.  After the bulk load,
        /// we will insert additional keys to force splits and check that the tree remains consistent and that no zombies are created.
        /// </summary>
        [TestMethod]
        public void BulkLoadFullOrder4Keys50()
        {
            string myPath = "wheat.db";
            File.Delete(myPath);

            // 1. Generate 50 sorted keys.
            List<Element> data = new List<Element>();
            for (int i = 1; i <= 50; i++) data.Add(new Element(i, i));

            // 2. Run Bulk Loader.  Set fill factors to 1.0 (100%). 
            var builder = new TreeBuilder(order: 4, 1.0);
            builder.CreateFromSorted(myPath, data);

            using (var tree = new BTree(myPath))
            {
                // 3. Check Root.
                Assert.IsTrue(tree.Header.RootId != -1, "Root");

                // 4. Verify all keys are present.
                int count = tree.CountKeys();
                Assert.AreEqual(data.Count, count, "Missing Keys");

                // 5. Check for zombies.
                Assert.AreEqual(0, tree.CountZombies(), "Zombies");
                Assert.AreEqual(0, tree.GetFreeListCount(), "Free Nodes");

                // 6. Insert 10 more keys to force splits.
                for (int i = 51; i <= 60; i++) tree.Insert(i, i);

                // 7. Check counts again.
                count = tree.CountKeys();
                Assert.AreEqual(60, count, "Missing Keys After Insert.");

                // 8. Check for zombies again.
                Assert.AreEqual(0, tree.CountZombies(), "Zombies");
                Assert.AreEqual(0, tree.GetFreeListCount(), "Free Nodes");
            }

            File.Delete(myPath);
        }


        /// <summary>   
        /// Bulk Load with Low Order and High Volume, and at Full Capacity (100% fill).
        /// </summary>    
        [TestMethod]
        [DataRow(5, 5)]
        [DataRow(5, 255)]
        [DataRow(5, 500)]
        [DataRow(5, 1000)]
        public void StressTestAlpha(int order, int count)
        {
            if (!IsDebugStress)
            {
                Assert.Inconclusive("Skipped");
            }

            Random rnd = new Random();
            string myPath = TestHelper.GetTempDb();      //Path.ChangeExtension(Path.GetRandomFileName(), "db");
            File.Delete(myPath);

            // Generate sorted data
            var data = Enumerable.Range(1, count)
                                 .Select(i => new Element(i, i))
                                 .ToList();

            // 2. Run Bulk Loader.  Set fill factors to 1.0 (100%). 
            var builder = new TreeBuilder(order, 1.0);
            builder.CreateFromSorted(myPath, data);


            using (var tree = new BTree(myPath))
            {
                // 3. Check Root.
                Assert.IsTrue(tree.Header.RootId != -1, "Root");

                // 4. Run the high-speed single-pass audit
                var report = tree.PerformFullAudit();

                // 5. Check Root.
                Assert.IsTrue(tree.Header.RootId != -1, "Root");

                // 6. Get the keys for the sort and count checks.
                var keys = tree.GetKeys();

                // 7. Integrity Checks
                Assert.AreEqual(count, keys.Count, "Missing Keys");
                Assert.IsTrue(Util.IsSorted(keys), "Keys must be sorted.");
                Assert.IsFalse(Util.HasDuplicate(keys), "Duplicates");
                Assert.AreEqual(0, report.ZombieCount, "Zombie");
                Assert.AreEqual(0, report.GhostCount, "Ghost");
                Assert.IsTrue(report.Height < 10, "Height must be less than 10.");
                Assert.IsTrue(report.AverageDensity > 35.0, "Low Density");
                Assert.IsTrue(tree.GetFreeListCount() < 8, "Free Nodes");

                // 8. Check Max.
                Element? lastKey = tree.SelectLast();
                if (lastKey.HasValue)
                {
                    Assert.AreEqual(count, lastKey.Value.Key, "Max Key Search Failed.");
                }
                else
                {
                    Assert.Fail("Max Key Missing");
                }

                // 9. Get random keys.
                int[] targets = new int[10];
                for (int i = 0; i < targets.Length; i++)
                {
                    targets[i] = rnd.Next(1, data.Count);
                }

                // 10. Check searches. 
                foreach (int i in targets)
                {
                    Element pair;
                    Assert.IsTrue(tree.TrySearch(i, out pair), $"Missing Key {i}");
                }

            }
            File.Delete(myPath);
        }


        /// <summary>   
        /// Bulk Load with High Order and High Volume, and Default Capacity (80% fill).
        /// </summary>   
        [TestMethod]
        [DataRow(64, 65)]
        [DataRow(64, 255)]
        [DataRow(64, 500)]
        [DataRow(64, 1000)]
        public void StressTestCharlie(int order, int count)
        {
            if (!IsDebugStress)
            {
                Assert.Inconclusive("Skipped");
            }

            Random rnd = new Random();
            string myPath = TestHelper.GetTempDb();   // Path.ChangeExtension(Path.GetRandomFileName(), "db");
            File.Delete(myPath);

            // 1. Generate sorted data
            var data = Enumerable.Range(1, count)
                                 .Select(i => new Element(i, i))
                                 .ToList();

            // 2. Run Bulk Loader.  Default Capacity (80%).
            var builder = new TreeBuilder(order);
            builder.CreateFromSorted(myPath, data);


            using (var tree = new BTree(myPath, order))
            {
                // 3. Check Root.
                Assert.IsTrue(tree.Header.RootId != -1, "Root");


                // 4. Run the high-speed single-pass audit
                var report = tree.PerformFullAudit();


                // 5. Fetch keys for the sort/count check
                var keys = tree.GetKeys();

                // 6. Integrity Checks
                Assert.AreEqual(count, keys.Count, "Missing Keys");
                Assert.IsTrue(Util.IsSorted(keys), "Keys must be sorted.");
                Assert.IsFalse(Util.HasDuplicate(keys), "Duplicates");
                Assert.AreEqual(0, report.ZombieCount, "Zombie");
                Assert.AreEqual(0, report.GhostCount, "Ghost");
                Assert.IsTrue(report.Height < 10, "Height must be 10 or less.");
                Assert.IsTrue(tree.GetFreeListCount() < 8, "Free Nodes");
                if (tree.Header.NodeCount > 10)
                {
                    Assert.IsTrue(report.AverageDensity > 35.0, $"Density must be 35% or more.");
                }

                // 7. Check Max.
                Element? lastKey = tree.SelectLast();
                if (lastKey.HasValue)
                {
                    Assert.AreEqual(count, lastKey.Value.Key, "Max Key Search Failed");
                }
                else
                {
                    Assert.Fail("Max Key Missing");
                }

                // 8. Check Min.
                Element? firstKey = tree.SelectFirst();
                if (firstKey.HasValue)
                {
                    Assert.AreEqual(1, firstKey.Value.Key, "Min Key Search Failed");
                }
                else
                {
                    Assert.Fail("Min Key Missing");
                }


                // 9. Get random keys.
                int[] targets = new int[10];
                for (int i = 0; i < targets.Length; i++)
                {
                    targets[i] = rnd.Next(1, data.Count);
                }

                // 10. Check searches.
                foreach (int i in targets)
                {
                    Element pair;
                    Assert.IsTrue(tree.TrySearch(i, out pair), $"Missing Key {i}");
                }
            }
            File.Delete(myPath);
        }

    }
}
