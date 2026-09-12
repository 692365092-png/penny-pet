using System;
using System.Diagnostics;
namespace PennyPet
{
    // Only the Windows diagnostic sink is substituted. Repository, codec and
    // atomic file I/O below it are the actual production implementations.
    internal static class ApplicationDiagnostics
    {
        internal static void ReportNonFatal(string context, Exception error)
        {
            Trace.WriteLine(context + ": " + error);
        }
    }
}
