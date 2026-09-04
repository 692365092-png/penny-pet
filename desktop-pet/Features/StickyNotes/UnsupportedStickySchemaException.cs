using System;

namespace PennyPet
{
    internal sealed class UnsupportedStickySchemaException : Exception
    {
        internal UnsupportedStickySchemaException(int detectedVersion,
            int maximumSupportedVersion, string sourcePath)
            : base("Sticky-note schema v" + detectedVersion +
                " is newer than supported v" + maximumSupportedVersion + ".")
        {
            DetectedVersion = detectedVersion;
            MaximumSupportedVersion = maximumSupportedVersion;
            SourcePath = sourcePath ?? String.Empty;
        }

        internal int DetectedVersion { get; private set; }

        internal int MaximumSupportedVersion { get; private set; }

        internal string SourcePath { get; private set; }
    }
}
