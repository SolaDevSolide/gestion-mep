using System.Collections.Concurrent;

namespace GestionMep.Api.Securite;

/// <summary>
/// Échecs de connexion par login : après <see cref="EchecsAvantBlocage"/> échecs consécutifs, le login est bloqué
/// 1 minute, puis 2, 4, 8… jusqu'à 15 minutes. Une connexion réussie remet le compteur à zéro.
/// Complète la limite par adresse IP (<see cref="ProtectionsHttp.PolitiqueConnexion"/>), qu'un attaquant contourne en
/// changeant d'adresse. En mémoire : les compteurs repartent de zéro au redémarrage (instance unique, voir DiffuseurMep).
/// </summary>
public class LimiteurConnexion(TimeProvider temps)
{
    public const int EchecsAvantBlocage = 5;
    public static readonly TimeSpan BlocageMax = TimeSpan.FromMinutes(15);
    /// <summary>Au-delà de ce nombre de logins suivis, les entrées inactives depuis une heure sont oubliées.</summary>
    private const int TailleMax = 10_000;

    private sealed record Etat(int Echecs, DateTimeOffset BloqueJusqua, DateTimeOffset Derniere);

    private readonly ConcurrentDictionary<string, Etat> _etats = new();

    /// <summary>Fin du blocage si le login est bloqué en ce moment, sinon null.</summary>
    public DateTimeOffset? BloqueJusqua(string login) =>
        _etats.TryGetValue(login, out var e) && e.BloqueJusqua > temps.GetUtcNow() ? e.BloqueJusqua : null;

    public void Echec(string login)
    {
        var maintenant = temps.GetUtcNow();
        _etats.AddOrUpdate(login,
            _ => new Etat(1, DateTimeOffset.MinValue, maintenant),
            (_, e) =>
            {
                var echecs = e.Echecs + 1;
                if (echecs < EchecsAvantBlocage) return e with { Echecs = echecs, Derniere = maintenant };
                var minutes = Math.Min(Math.Pow(2, echecs - EchecsAvantBlocage), BlocageMax.TotalMinutes);
                return new Etat(echecs, maintenant.AddMinutes(minutes), maintenant);
            });
        if (_etats.Count > TailleMax) Purger(maintenant);
    }

    public void Reussite(string login) => _etats.TryRemove(login, out _);

    private void Purger(DateTimeOffset maintenant)
    {
        foreach (var (login, e) in _etats)
            if (e.BloqueJusqua < maintenant && e.Derniere < maintenant.AddHours(-1)) _etats.TryRemove(login, out _);
    }
}
