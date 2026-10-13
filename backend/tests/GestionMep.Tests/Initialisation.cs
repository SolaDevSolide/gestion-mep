using System.Runtime.CompilerServices;

namespace GestionMep.Tests;

internal static class Initialisation
{
    /// <summary>
    /// Les tests ne lisent jamais le fichier de configuration d'une installation sans Docker présente sur la machine
    /// (/etc/gestion-mep/gestion-mep.conf…) : il remplacerait leur base de test par la base installée.
    /// </summary>
    [ModuleInitializer]
    internal static void IgnorerFichierInstallation() =>
        Environment.SetEnvironmentVariable("GESTION_MEP_CONFIG", Path.Combine(Path.GetTempPath(), "gestion-mep-tests-sans-fichier.conf"));
}
