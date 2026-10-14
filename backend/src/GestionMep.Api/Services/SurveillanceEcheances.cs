using GestionMep.Api.Donnees;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Services;

/// <summary>
/// Vérifie chaque minute les MEP en exécution ou en recette dont l'échéance de retour arrière est atteinte et les fait basculer
/// (la bascule est enregistrée, journalisée et notifiée par <see cref="ServiceMep.Charger"/>).
/// </summary>
public class SurveillanceEcheances(IServiceScopeFactory scopes, TimeProvider temps, ILogger<SurveillanceEcheances> journal) : BackgroundService
{
    public static readonly TimeSpan Periode = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken arret)
    {
        using var minuteur = new PeriodicTimer(Periode, temps);
        try
        {
            while (await minuteur.WaitForNextTickAsync(arret)) await Verifier(arret);
        }
        catch (OperationCanceledException) { /* arrêt du serveur */ }
    }

    /// <summary>Une passe de surveillance ; renvoie le nombre de MEP basculées en retour arrière.</summary>
    public async Task<int> Verifier(CancellationToken arret)
    {
        var bascules = 0;
        try
        {
            List<Guid> ids;
            using (var scope = scopes.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
                var maintenant = temps.GetUtcNow();
                ids = await db.Meps.AsNoTracking().Where(m => (m.Phase == Phase.Execution || m.Phase == Phase.Recette) && m.RetourArriereActive && m.EcheanceRepli != null && m.EcheanceRepli <= maintenant)
                    .Select(m => m.Id).ToListAsync(arret);
            }
            foreach (var id in ids)
            {
                // Un scope par MEP : une erreur sur l'une n'empêche pas les autres.
                try
                {
                    using var scope = scopes.CreateScope();
                    var svc = scope.ServiceProvider.GetRequiredService<ServiceMep>();
                    var mep = await svc.Charger(id); // applique l'échéance
                    if (mep.Phase == Phase.RetourArriere && mep.RetourArriereDeclenchePar is null) bascules++;
                }
                catch (ErreurHttp) { /* MEP supprimée entre-temps */ }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    journal.LogError(ex, "Échec de la surveillance de l'échéance de la MEP {MepId}.", id);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            journal.LogError(ex, "Échec de la surveillance des échéances.");
        }
        return bascules;
    }
}
