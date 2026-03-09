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


    }
}
