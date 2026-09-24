using System.Reflection;

namespace TerminalQuest
{
    /// <summary>
    /// The product version declared in <c>TerminalQuest.csproj</c> (<c>&lt;Version&gt;</c>).
    /// <see cref="Current"/> is the bare version (<c>1.1.0</c>); <see cref="Display"/>
    /// adds the <c>v</c> prefix used for git tags and on-screen display (<c>v1.1.0</c>).
    /// </summary>
    internal static class AppVersion
    {
        /// <summary>Bare product version, e.g. <c>1.1.0</c>. Never empty; <c>"0.0.0"</c> when unknown.</summary>
        public static string Current => GetVersion();

        /// <summary>Display form with the <c>v</c> prefix, e.g. <c>v1.1.0</c>.</summary>
        public static string Display => $"v{Current}";

        private static string GetVersion()
        {
            var assembly = typeof(AppVersion).Assembly;
            var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(informational))
            {
                // InformationalVersion can carry a "+gitsha" suffix; the display version is the part before it.
                var plus = informational.IndexOf('+');
                var bare = (plus >= 0 ? informational[..plus] : informational).Trim();
                if (bare.Length > 0)
                {
                    return bare;
                }
            }

            var assemblyVersion = assembly.GetName().Version?.ToString();
            if (!string.IsNullOrWhiteSpace(assemblyVersion))
            {
                return assemblyVersion;
            }

            return "0.0.0";
        }
    }
}
