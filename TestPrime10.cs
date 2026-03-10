using ArcOne;
using UnitTestOne;

namespace UnitTestTen
{

    [TestClass]
    public sealed class UnitTen
    {
        private bool IsDebugStress = TestSettings.CanDebugStress;

        [TestMethod]
        public void GeneratePrimesTest()
        {
            // 1. Setup.
            const int MinFileSize = 127800;
            string path = "prime.txt";
            if (File.Exists(path)) return;

            // 2. Generate primes.
            Util.WritePrimesToFile(path);

            // 3. Assert,
            Assert.IsTrue(File.Exists(path), "File should exist.");

            // 4. Check the file length.
            long fileLength = Util.GetFileLength(path);
            Assert.IsTrue(fileLength > MinFileSize, $"File Length must be greater than {MinFileSize} bytes.");

            // 5. Check the prime numbers (first 10 primes).
            string[] lines = File.ReadLines(path).Take(10).ToArray();
            Assert.AreEqual("1, 2", lines[0]);
            Assert.AreEqual("2, 3", lines[1]);
            Assert.AreEqual("3, 5", lines[2]);
            Assert.AreEqual("4, 7", lines[3]);
            Assert.AreEqual("5, 11", lines[4]);
            Assert.AreEqual("6, 13", lines[5]);
            Assert.AreEqual("7, 17", lines[6]);
            Assert.AreEqual("8, 19", lines[7]);
            Assert.AreEqual("9, 23", lines[8]);
            Assert.AreEqual("10, 29", lines[9]);
        }


        [TestMethod]
        [DataRow(64, 1000)]
        [DataRow(64, 5000)]
        [DataRow(64, 10000)]
        public void StressTestEcho(int order, int count)
        {
            if (!IsDebugStress)
            {
                Assert.Inconclusive("Skipped");
            }

            Random rnd = new Random();
            string myPath = TestHelper.GetTempDb();  
            File.Delete(myPath);

            // 1. Generate sorted data
            List<Element> data = new List<Element>();

            string primePath = "prime.txt";

            int lineNo = 0;
            foreach (string line in File.ReadLines(primePath))
            {
                lineNo++;
                if (lineNo > count) break;

                string[] part = line.Split(',');
                if (part.Length >= 2)
                {
                    int id = int.Parse(part[0]);
                    int prime = int.Parse(part[1]);
                    data.Add(new Element(id, prime));
                }
            }

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


                // 10. Search every key and check data.
                foreach (var item in data)
                {
                    Element pair;
                    Assert.IsTrue(tree.TrySearch(item.Key, out pair), $"Missing Key {item.Key}");
                    Assert.AreEqual(item.Data, pair.Data, $"Data Mismatch for Key {item.Key}");
                }
            }
            File.Delete(myPath);
        }

    }
}
