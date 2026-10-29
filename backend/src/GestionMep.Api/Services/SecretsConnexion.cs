using System.Security.Cryptography;
using GestionMep.Domain;
using Microsoft.AspNetCore.DataProtection;

namespace GestionMep.Api.Services;

/// <summary>
/// Chiffrement des secrets des connexions (adresse secrète, authentification, signature, en-têtes secrets) avec les clés de
/// l'application (Data Protection, dossier des clés), comme le secret de la connexion unique. Un secret n'est jamais
/// renvoyé : l'API n'en dit que l'état (<see cref="SecretEtatDto"/>).
/// </summary>
public sealed class SecretsConnexion(IDataProtectionProvider cles)
{
    public const string Usage = "GestionMep.ConnexionsApi.Secret";

    private IDataProtector Protecteur => cles.CreateProtector(Usage);

    public SecretConnexion Chiffrer(string valeur, string auteur, DateTimeOffset quand) =>
        new() { Chiffre = Protecteur.Protect(valeur), RemplaceLe = quand, RemplacePar = auteur };

    /// <summary>Valeur en clair, ou null si le secret est absent ou illisible (clés changées, par exemple après une restauration).</summary>
    public string? Dechiffrer(SecretConnexion? s)
    {
        if (s is null || s.Chiffre.Length == 0) return null;
        try
        {
            return Protecteur.Unprotect(s.Chiffre);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public SecretEtatDto? Etat(SecretConnexion? s) =>
        s is null ? null : new SecretEtatDto(true, Dechiffrer(s) is not null, s.RemplaceLe, s.RemplacePar);
}
