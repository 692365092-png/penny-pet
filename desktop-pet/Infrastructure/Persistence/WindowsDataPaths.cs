using System;
using System.IO;

namespace PennyPet
{
    internal static class WindowsDataPaths
    {
        internal static string LocalApplicationDataDirectory
        {
            get
            {
                return Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData);
            }
        }

        internal static string PennyPetDirectory
        {
            get
            {
                return Path.Combine(LocalApplicationDataDirectory, "PennyPet");
            }
        }
    }
}
