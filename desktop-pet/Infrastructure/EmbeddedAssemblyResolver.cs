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
        private const string LunarAssemblyName = "lunar";
        private const string LunarResourceName =
            "PennyPet.Dependencies.lunar.dll";

        internal static void Register()
        {
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        }

        private static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            string requested = new AssemblyName(args.Name).Name;
            string resourceName;
            if (String.Equals(requested, AstronomyAssemblyName,
                StringComparison.OrdinalIgnoreCase))
                resourceName = AstronomyResourceName;
            else if (String.Equals(requested, LunarAssemblyName,
                StringComparison.OrdinalIgnoreCase))
                resourceName = LunarResourceName;
            else
                return null;
            Assembly owner = typeof(EmbeddedAssemblyResolver).Assembly;
            using (Stream stream = owner.GetManifestResourceStream(
                resourceName))
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
