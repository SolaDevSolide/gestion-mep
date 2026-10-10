using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace GestionMep.Api.Securite;

/// <summary>Taille maximale des corps de requête : basse par défaut, relevée seulement sur les routes de dépôt de fichiers.</summary>
public static class LimitesRequetes
{
    /// <summary>1 Mo : largement assez pour un JSON de saisie (instructions, preuve…), sans laisser passer un texte de 20 Mo.</summary>
    public const long CorpsParDefaut = 1024 * 1024;
}

/// <summary>
/// En-têtes de sécurité posés sur toutes les réponses : politique de contenu (CSP), interdiction d'être affiché dans un
/// cadre (protège le Go ou le retour arrière du « clickjacking »), pas de détection de type, référent limité au site.
/// </summary>
public static partial class EnTetesSecurite
{
    /// <summary>
    /// Construit la politique de contenu. Le script en ligne de <c>index.html</c> (thème appliqué avant l'affichage) est
    /// autorisé par son empreinte SHA-256, calculée au démarrage sur le fichier réellement servi.
    /// </summary>
    public static string PolitiqueContenu(string? cheminIndex)
    {
        var scripts = "'self'";
        if (cheminIndex is not null && File.Exists(cheminIndex))
        {
            foreach (Match m in ScriptEnLigne().Matches(File.ReadAllText(cheminIndex)))
                scripts += $" 'sha256-{Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(m.Groups[1].Value)))}'";
        }
        return string.Join("; ",
            "default-src 'self'",
            $"script-src {scripts}",
            // Les composants posent des styles en ligne (positionnement des menus, largeurs calculées).
            "style-src 'self' 'unsafe-inline'",
            "img-src 'self' data:",
            "font-src 'self' data:",
            "connect-src 'self'",
            "object-src 'none'",
            "base-uri 'self'",
            "form-action 'self'",
            "frame-ancestors 'none'");
    }

    public static IApplicationBuilder UtiliserEnTetesSecurite(this IApplicationBuilder app, string? cheminIndex)
    {
        var csp = PolitiqueContenu(cheminIndex);
        return app.Use(async (ctx, suivant) =>
        {
            var h = ctx.Response.Headers;
            h.ContentSecurityPolicy = csp;
            h.XContentTypeOptions = "nosniff";
            h.XFrameOptions = "DENY";
            h["Referrer-Policy"] = "same-origin";
            h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            await suivant();
        });
    }

    /// <summary>Contenu d'une balise &lt;script&gt; sans attribut <c>src</c>.</summary>
    [GeneratedRegex(@"<script(?![^>]*\bsrc=)[^>]*>(.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ScriptEnLigne();
}
