using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

// This small .NET Framework launcher contains only a compressed runtime payload.
internal static class SingleFileLauncher
{
    [STAThread]
    private static int Main(string[] args)
    {
        bool verify = args.Length == 1 && args[0] == "--verify-package";
        try
        {
            Assembly self = Assembly.GetExecutingAssembly();
            byte[] payload;
            using (Stream input = self.GetManifestResourceStream("SX3.Payload"))
            using (var memory = new MemoryStream())
            {
                input.CopyTo(memory);
                payload = memory.ToArray();
            }
            string id;
            using (var sha = SHA256.Create())
                id = BitConverter.ToString(sha.ComputeHash(payload)).Replace("-", "");
            string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SX3_SCANER", "runtime", id);
            using (var mutex = new Mutex(false, @"Local\SX3_Package_" + id))
            {
                try { mutex.WaitOne(); } catch (AbandonedMutexException) { }
                try
                {
                    Directory.CreateDirectory(cache);
                    using (var memory = new MemoryStream(payload, false))
                    using (var archive = new ZipArchive(memory, ZipArchiveMode.Read))
                    {
                        foreach (ZipArchiveEntry entry in archive.Entries)
                        {
                            string target = Path.GetFullPath(Path.Combine(cache, entry.FullName));
                            if (!target.StartsWith(cache + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                                throw new InvalidDataException("Invalid package entry.");
                            if (entry.Name.Length == 0) continue;
                            Directory.CreateDirectory(Path.GetDirectoryName(target));
                            // Compare bytes before writing, so a running instance's DLLs are never overwritten.
                            byte[] expected;
                            using (Stream input = entry.Open())
                            using (var bytes = new MemoryStream())
                            {
                                input.CopyTo(bytes);
                                expected = bytes.ToArray();
                            }
                            if (!Matches(target, expected))
                                File.WriteAllBytes(target, expected);
                        }
                    }
                }
                finally { mutex.ReleaseMutex(); }
            }
            var start = new ProcessStartInfo(Path.Combine(cache, "SX3 SCANER.exe"))
            {
                UseShellExecute = false,
                WorkingDirectory = cache,
                Arguments = verify ? "--package-smoke-test" : JoinArguments(args)
            };
            start.EnvironmentVariables["SX3_SINGLE_FILE_EXE"] = self.Location;
            using (Process child = Process.Start(start))
            {
                child.WaitForExit();
                return child.ExitCode;
            }
        }
        catch (Exception ex)
        {
            if (!verify) MessageBox.Show(ex.Message, "SX3 Scanner", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static bool Matches(string path, byte[] expected)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != expected.Length) return false;
        using (var sha = SHA256.Create())
        using (Stream input = File.OpenRead(path))
            return Convert.ToBase64String(sha.ComputeHash(input)) == Convert.ToBase64String(sha.ComputeHash(expected));
    }

    private static string JoinArguments(string[] args)
    {
        var result = new StringBuilder();
        foreach (string argument in args)
        {
            if (result.Length > 0) result.Append(' ');
            result.Append('"');
            int slashes = 0;
            foreach (char c in argument)
            {
                if (c == '\\') { slashes++; continue; }
                result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
                result.Append(c);
                slashes = 0;
            }
            result.Append('\\', slashes * 2);
            result.Append('"');
        }
        return result.ToString();
    }
}
