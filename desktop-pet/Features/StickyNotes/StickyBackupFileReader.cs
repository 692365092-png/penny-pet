using System;
using System.IO;
using System.Text;

namespace PennyPet
{
    // Windows-only file boundary for B3. It reads and validates the whole
    // backup before returning any notes to the caller; it never touches the
    // live repository.
    internal static class StickyBackupFileReader
    {
        internal static StickyImportValidationResult Read(string filePath)
        {
            try
            {
                if (String.IsNullOrWhiteSpace(filePath))
                    return StickyImportValidationResult.Failure(
                        "Backup path is empty.");
                if (!File.Exists(filePath))
                    return StickyImportValidationResult.Failure(
                        "Backup file does not exist.");
                if (new FileInfo(filePath).Length >
                    StickyNoteLimits.MaximumDataFileBytes)
                    return StickyImportValidationResult.Failure(
                        "Sticky-note backup is too large.");
                return StickyImportBackupValidator.Validate(
                    File.ReadAllLines(filePath, new UTF8Encoding(false, true)));
            }
            catch (Exception error)
            {
                ApplicationDiagnostics.ReportNonFatal(
                    "sticky-notes-backup-validate", error);
                return StickyImportValidationResult.Failure(
                    "这个备份无法读取。请确认文件完整后重试。");
            }
        }
    }
}
