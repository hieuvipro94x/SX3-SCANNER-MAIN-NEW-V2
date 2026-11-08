using System.Reflection;

namespace SX3_SCANER.Helper
{
    internal static class ApplicationVersion
    {
        internal static string Current => Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
    }
}
