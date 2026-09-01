using System;
using System.IO;
using System.Reflection;

namespace PennyPet
{
    internal static class EmbeddedAssemblyResolver
    {
        private const string AstronomyAssemblyName = "astronomy";
        private const string AstronomyResourceName =
            "PennyPet.Dependencies.astronomy.dll";

        internal static void Register()
        {
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        }

        private static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            if (!String.Equals(new AssemblyName(args.Name).Name,
                AstronomyAssemblyName, StringComparison.OrdinalIgnoreCase))
                return null;
            Assembly owner = typeof(EmbeddedAssemblyResolver).Assembly;
            using (Stream stream = owner.GetManifestResourceStream(
                AstronomyResourceName))
            {
                if (stream == null) return null;
                using (MemoryStream bytes = new MemoryStream())
                {
                    stream.CopyTo(bytes);
                    return Assembly.Load(bytes.ToArray());
                }
            }
        }
    }
}
