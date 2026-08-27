using System;
using System.Diagnostics;
using System.IO;

namespace PennyPet
{
    internal enum StickyLinkOpenResult
    {
        Opened,
        Missing,
        Cancelled,
        Failed
    }

    // Thin Windows adapter. Detection and risk policy live in Core; this class
    // only confirms risky targets, checks local existence and invokes the shell.
    internal static class StickyLinkService
    {
        internal static StickyLinkOpenResult Open(string target,
            bool localPath,
            Func<StickyLinkOpenRisk, string, bool> confirm,
            out Exception error)
        {
            error = null;
            if (String.IsNullOrWhiteSpace(target))
                return StickyLinkOpenResult.Missing;

            // Confirm before probing the filesystem. This is especially
            // important for UNC paths, whose existence check can contact a
            // remote machine and disclose activity before user consent.
            StickyLinkOpenRisk risk = StickyLinkPolicy.Classify(target,
                localPath);
            if (risk != StickyLinkOpenRisk.None &&
                (confirm == null || !confirm(risk, target)))
                return StickyLinkOpenResult.Cancelled;
            if (localPath && !File.Exists(target) &&
                !Directory.Exists(target))
                return StickyLinkOpenResult.Missing;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = target,
                    UseShellExecute = true
                });
                return StickyLinkOpenResult.Opened;
            }
            catch (Exception caught)
            {
                error = caught;
                return StickyLinkOpenResult.Failed;
            }
        }
    }
}
