using GestionMep.Api.Donnees;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Services;

/// <summary>
/// Réponse de la sonde « prête » : <c>statut</c> « prete » ou « indisponible », puis chaque contrôle : <c>base</c> (« ok »,
/// « injoignable »), <c>schema</c> (« ok », « migrations-en-attente », « plus-recent », « inconnu »), <c>echeances</c>
/// (« ok », « en-retard », « desactivee »).
/// </summary>
public record SantePreteDto(string Statut, string Base, string Schema, string Echeances);

/// <summary>
/// Sonde « prête » (<c>/api/sante/prete</c>) : l'application peut servir. La base répond, son schéma est celui de cette
/// version (ni migration en attente, ni migration inconnue, signe d'une base mise à jour par une version plus récente),
/// et la surveillance des échéances de retour arrière a tourné récemment. La sonde « vivant » (<c>/api/sante</c>) dit
/// seulement que le processus répond.
/// </summary>
public sealed class SantePrete(TimeProvider temps, IConfiguration config)
{
    /// <summary>Au-delà sans passage réussi (un passage par minute), la surveillance des échéances est jugée arrêtée.</summary>
    public static readonly TimeSpan SilenceMaxSurveillance = TimeSpan.FromMinutes(5);

    /// <summary>Délai laissé à la base pour répondre à la sonde.</summary>
    public static readonly TimeSpan DelaiBase = TimeSpan.FromSeconds(3);

    private readonly DateTimeOffset _creation = temps.GetUtcNow();
    private long _reference; // ticks UTC du dernier passage réussi (ou du démarrage de la surveillance) ; 0 : aucun

    /// <summary>Appelé par <see cref="SurveillanceEcheances"/> à son démarrage, puis après chaque passage réussi.</summary>
    public void SurveillanceActive() => Interlocked.Exchange(ref _reference, temps.GetUtcNow().UtcTicks);

    private bool SurveillanceAttendue => config.GetValue("Surveillance:Activer", true);

    /// <summary>Contrôles de la sonde « prête » : statut « prete » seulement si tous passent.</summary>
    public async Task<SantePreteDto> Verifier(MepDbContext db, CancellationToken arret)
    {
        string etatBase, schema;
        using var delai = CancellationTokenSource.CreateLinkedTokenSource(arret);
        delai.CancelAfter(DelaiBase);
        try
        {
            var appliquees = (await db.Database.GetAppliedMigrationsAsync(delai.Token)).ToHashSet();
            var connues = db.Database.GetMigrations().ToHashSet();
            etatBase = "ok";
            schema = appliquees.Except(connues).Any() ? "plus-recent"
                : connues.Except(appliquees).Any() ? "migrations-en-attente"
                : "ok";
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !arret.IsCancellationRequested)
        {
            etatBase = "injoignable";
            schema = "inconnu";
        }

        string echeances;
        if (!SurveillanceAttendue) echeances = "desactivee";
        else
        {
            var reference = Interlocked.Read(ref _reference) is > 0 and var t ? new DateTimeOffset(t, TimeSpan.Zero) : _creation;
            echeances = temps.GetUtcNow() - reference <= SilenceMaxSurveillance ? "ok" : "en-retard";
        }

        var prete = etatBase == "ok" && schema == "ok" && echeances is ("ok" or "desactivee");
        return new SantePreteDto(prete ? "prete" : "indisponible", etatBase, schema, echeances);
    }
}
