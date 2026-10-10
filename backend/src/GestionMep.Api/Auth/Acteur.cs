using System.Security.Claims;

namespace GestionMep.Api.Auth;

/// <summary>Utilisateur courant tel que lu dans le cookie (ou, plus tard, dans le jeton OIDC).</summary>
public record Acteur(Guid Id, string Nom, bool EstAdmin)
{
    public const string RoleAdmin = "admin";
    /// <summary>Copie de <see cref="GestionMep.Domain.Utilisateur.TamponSecurite"/> : une session plus ancienne est refusée.</summary>
    public const string ClaimTampon = "mep:tampon";
    /// <summary>Présent tant que le compte a un mot de passe provisoire à changer.</summary>
    public const string ClaimMotDePasseProvisoire = "mep:mdp-provisoire";

    public static Acteur Depuis(ClaimsPrincipal user)
    {
        var id = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(id, out var guid)) throw new Services.ErreurHttp(401, "Authentification requise.");
        return new Acteur(guid, user.FindFirstValue(ClaimTypes.Name) ?? "", user.IsInRole(RoleAdmin));
    }

    public static ClaimsPrincipal VersPrincipal(GestionMep.Domain.Utilisateur u, string schema)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, u.Id.ToString()),
            new(ClaimTypes.Name, u.NomAffiche),
        };
        if (u.EstAdmin) claims.Add(new Claim(ClaimTypes.Role, RoleAdmin));
        claims.Add(new Claim(ClaimTampon, u.TamponSecurite.ToString()));
        if (u.DoitChangerMotDePasse) claims.Add(new Claim(ClaimMotDePasseProvisoire, "1"));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, schema, ClaimTypes.Name, ClaimTypes.Role));
    }
}
