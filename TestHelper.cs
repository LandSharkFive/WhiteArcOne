using ArcOne;
using System.Collections;
using System.Reflection.PortableExecutable;

namespace UnitTestOne
{
    public class TestHelper
    {
        /// <summary>
        /// Get a random temporary file name.
        /// </summary>
        /// <returns></returns>
        public static string GetTempDb()
        {
            return Path.ChangeExtension(Path.GetRandomFileName(), "db");
        }

        /// <summary>
        /// Check node constraints.
        /// </summary>
        public void CheckNode(BNode node)
        {
            Assert.IsTrue(node.NumKeys >= 0, "NumKeys must be greater than or equal to zero.");
            Assert.IsTrue(node.NumKeys <= node.Order, "NumKeys must be less than or equal to Order.");

            if (!node.Leaf)
            {
                for (int i = 0; i <= node.NumKeys; i++)
                {
                    Assert.IsTrue(node.Kids[i] >= 0, "Child pointer must be greater than or equal to zero.");
                }
            }
        }


    }
}
