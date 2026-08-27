using System;
using System.IO;
using System.Text;

namespace PennyPet
{
    // Shared parsing and failure semantics for the desktop executable and the
    // dedicated tools/self-test hosts. Every file-producing command now exits
    // non-zero and leaves a sibling error file when execution fails.
    internal static class CommandLineArguments
    {
        internal static bool HasFlag(string[] args, string expected)
        {
            if (args == null) return false;
            foreach (string argument in args)
                if (String.Equals(argument, expected,
                    StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        internal static bool TryGetPath(string[] args, string prefix,
            out string path)
        {
            path = null;
            if (args == null) return false;
            foreach (string argument in args)
            {
                if (argument == null || !argument.StartsWith(prefix,
                    StringComparison.OrdinalIgnoreCase)) continue;
                path = argument.Substring(prefix.Length).Trim('"');
                return !String.IsNullOrWhiteSpace(path);
            }
            return false;
        }

        internal static int RunOutputCommand(string outputPath, Action action)
        {
            try
            {
                action();
                return Environment.ExitCode;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                TryWriteErrorFile(outputPath, error);
                return 1;
            }
        }

        private static void TryWriteErrorFile(string outputPath,
            Exception error)
        {
            if (String.IsNullOrWhiteSpace(outputPath)) return;
            try
            {
                File.WriteAllText(outputPath + ".error.txt", error.ToString(),
                    new UTF8Encoding(false));
            }
            catch
            {
                // The original exception and non-zero exit code remain visible.
            }
        }
    }
}
