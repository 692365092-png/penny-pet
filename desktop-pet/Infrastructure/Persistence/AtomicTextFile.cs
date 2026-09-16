using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PennyPet
{
    internal static class AtomicTextFile
    {
        internal static void WriteAllLines(string filePath,
            IEnumerable<string> lines, bool keepBackup)
        {
            string fullPath = Path.GetFullPath(filePath);
            string directory = Path.GetDirectoryName(fullPath);
            if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            // The file owner orders writes. Unique staging files avoid a global
            // lock between unrelated settings, workspace and export files.
            string temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllLines(temporary, lines ?? new string[0], new UTF8Encoding(false));
                if (File.Exists(fullPath))
                    File.Replace(temporary, fullPath, keepBackup ? fullPath + ".bak" : null, true);
                else
                    File.Move(temporary, fullPath);
            }
            finally
            {
                // Cleanup must not replace the original write failure.
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
