using System.Collections.Concurrent;

namespace GestionMep.Api.Securite;

/// <summary>
/// Protection des connexions par mot de passe, en mémoire (instance unique, voir DiffuseurMep ; tout repart de zéro au redémarrage) :
/// <list type="bullet">
/// <item>Blocage progressif par couple login + adresse IP : après <see cref="EchecsAvantBlocage"/> échecs consécutifs depuis une
/// adresse, ce login y est bloqué 1 minute, puis 2, 4, 8… jusqu'à 15 minutes. Un tiers qui se trompe exprès depuis son adresse ne
/// bloque donc pas la personne, qui se connecte depuis la sienne. Une connexion réussie remet le compteur à zéro.</item>
/// <item>Alerte : <see cref="SeuilAlerte"/> échecs sur un même login en une heure, toutes adresses confondues (attaque répartie),
/// signalés une fois par heure (journal d'administration).</item>
/// <item>Mémoire bornée : au plus <see cref="TailleMax"/> entrées par table ; au-delà, les plus anciennes sont oubliées.</item>
/// <item>Calcul borné : au plus quelques contrôles de mot de passe (hachage coûteux) en même temps, les autres attendent.</item>
/// </list>
/// Complète la limite par adresse IP (<see cref="ProtectionsHttp.PolitiqueConnexion"/>).
/// </summary>
public sealed class LimiteurConnexion(TimeProvider temps) : IDisposable
{
    public const int EchecsAvantBlocage = 5;
    public const int SeuilAlerte = 20;
    /// <summary>Au-delà, la saisie est refusée sans contrôle ni suivi (aucun compte n'a un login aussi long).</summary>
    public const int LongueurMaxLogin = 256;
    public const int LongueurMaxMotDePasse = 1024;
    public static readonly TimeSpan BlocageMax = TimeSpan.FromMinutes(15);
    public const int TailleMax = 10_000;
    private static readonly TimeSpan Oubli = TimeSpan.FromHours(1);
    private static readonly TimeSpan AttenteCalcul = TimeSpan.FromSeconds(10);

    private sealed record Etat(int Echecs, DateTimeOffset BloqueJusqua, DateTimeOffset Derniere);
    private sealed record Cumul(int Echecs, DateTimeOffset Debut, DateTimeOffset Derniere, bool Signale);

    private readonly ConcurrentDictionary<(string Login, string Ip), Etat> _etats = new();
    private readonly ConcurrentDictionary<string, Cumul> _cumuls = new();
    private readonly SemaphoreSlim _calculs = new(Math.Clamp(Environment.ProcessorCount, 2, 8));

    /// <summary>Saisie de taille raisonnable (sinon : refus immédiat, sans hachage ni entrée en mémoire).</summary>
    public static bool SaisieAdmissible(string login, string? motDePasse) =>
        login.Length is > 0 and <= LongueurMaxLogin && (motDePasse ?? "").Length <= LongueurMaxMotDePasse;

    /// <summary>Fin du blocage si le login est bloqué en ce moment depuis cette adresse, sinon null.</summary>
    public DateTimeOffset? BloqueJusqua(string login, string ip) =>
        _etats.TryGetValue((login, ip), out var e) && e.BloqueJusqua > temps.GetUtcNow() ? e.BloqueJusqua : null;

    /// <summary>Compte un échec ; renvoie vrai quand le login vient d'atteindre <see cref="SeuilAlerte"/> échecs dans l'heure.</summary>
    public bool Echec(string login, string ip)
    {
        var maintenant = temps.GetUtcNow();
        _etats.AddOrUpdate((login, ip),
            _ => new Etat(1, DateTimeOffset.MinValue, maintenant),
            (_, e) =>
            {
                var echecs = e.Echecs + 1;
                if (echecs < EchecsAvantBlocage) return e with { Echecs = echecs, Derniere = maintenant };
                var minutes = Math.Min(Math.Pow(2, echecs - EchecsAvantBlocage), BlocageMax.TotalMinutes);
                return new Etat(echecs, maintenant.AddMinutes(minutes), maintenant);
            });
        var cumul = _cumuls.AddOrUpdate(login,
            _ => new Cumul(1, maintenant, maintenant, false),
            (_, c) => c.Debut < maintenant - Oubli ? new Cumul(1, maintenant, maintenant, false) : c with { Echecs = c.Echecs + 1, Derniere = maintenant });
        if (_etats.Count > TailleMax) Borner(_etats, e => e.BloqueJusqua < maintenant && e.Derniere < maintenant - Oubli, e => e.Derniere);
        if (_cumuls.Count > TailleMax) Borner(_cumuls, c => c.Derniere < maintenant - Oubli, c => c.Derniere);
        if (cumul.Echecs < SeuilAlerte || cumul.Signale) return false;
        // Une seule alerte par fenêtre : seul l'appel qui pose « Signale » la déclenche.
        return _cumuls.TryUpdate(login, cumul with { Signale = true }, cumul);
    }

    public void Reussite(string login, string ip) => _etats.TryRemove((login, ip), out _);

    /// <summary>
    /// Exécute un contrôle de mot de passe en limitant le nombre de calculs simultanés ; null si le serveur est saturé
    /// (rien n'est exécuté).
    /// </summary>
    public async Task<(bool Execute, T? Resultat)> Calculer<T>(Func<Task<T>> controle, CancellationToken ct)
    {
        if (!await _calculs.WaitAsync(AttenteCalcul, ct)) return (false, default);
        try
        {
            return (true, await controle());
        }
        finally
        {
            _calculs.Release();
        }
    }

    public void Dispose() => _calculs.Dispose();

    public int NombreEntrees => _etats.Count + _cumuls.Count;

    /// <summary>Oublie les entrées périmées, puis, si la table reste trop grande, les plus anciennes jusqu'à 90 % de la limite.</summary>
    private static void Borner<TCle, TVal>(ConcurrentDictionary<TCle, TVal> table, Func<TVal, bool> perime, Func<TVal, DateTimeOffset> derniere)
        where TCle : notnull
    {
        foreach (var (cle, v) in table)
            if (perime(v)) table.TryRemove(cle, out _);
        var exces = table.Count - TailleMax * 9 / 10;
        if (exces <= 0) return;
        foreach (var (cle, _) in table.ToArray().OrderBy(x => derniere(x.Value)).Take(exces))
            table.TryRemove(cle, out _);
    }
}
