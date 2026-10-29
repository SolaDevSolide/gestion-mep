using GestionMep.Api.Donnees;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Services;

/// <summary>
/// Service d'envoi des appels aux API externes (file en base, <see cref="AppelApi"/>). Réveillé après chaque écriture qui
/// met des appels en file (<see cref="SignalAppels"/>), sinon toutes les 5 s. Prend au plus <see cref="Parallele"/> appels
/// à la fois (<c>FOR UPDATE SKIP LOCKED</c>), jamais un appel qui a devant lui un appel non terminé de la même connexion pour
/// la même MEP (l'ordre est gardé, sans bloquer les autres). Tentatives : tout de suite, 30 s, 2 min, puis 10 min ; un
/// Retry-After est respecté jusqu'à 10 min ; seules les erreurs passagères (réseau, délai, 408, 425, 429, 5xx) sont
/// réessayées. Fin : ligne au journal de la MEP, et pour un échec définitif, journal d'administration et carte « Blocages et
/// échecs » (événement <c>appels</c> du flux de la MEP). Au démarrage, les appels restés en cours repartent ; l'historique
/// est purgé après <c>api-appels-conservation-jours</c>. Réglage <c>api-envois-actifs</c> à false : tout reste en file.
/// </summary>
public sealed class EnvoiAppelsApi(IServiceScopeFactory scopes, SignalAppels signal, ClientConnexionsApi client, DiffuseurMep diffuseur,
    IConfiguration config, TimeProvider temps, ILogger<EnvoiAppelsApi> journal) : BackgroundService
{
    public const int Parallele = 4;
    public static readonly TimeSpan Passage = TimeSpan.FromSeconds(5);
    public const string CleEnvoisActifs = "ConnexionsApi:EnvoisActifs";
    public const string CleConservation = "ConnexionsApi:ConservationJours";
    private DateTimeOffset _prochainePurge;

    /// <summary>Attente avant la tentative suivante, après <paramref name="faites"/> tentatives.</summary>
    public static TimeSpan Attente(int faites) => faites switch
    {
        1 => TimeSpan.FromSeconds(30),
        2 => TimeSpan.FromMinutes(2),
        _ => TimeSpan.FromMinutes(10),
    };

    protected override async Task ExecuteAsync(CancellationToken arret)
    {
        try
        {
            await Reprendre(arret);
            while (!arret.IsCancellationRequested)
            {
                try
                {
                    await Purger(arret);
                    while (await Traiter(arret) > 0) { }
                }
                catch (Exception ex) when (!arret.IsCancellationRequested)
                {
                    journal.LogWarning(ex, "Envoi des appels aux API externes interrompu ; nouvel essai au prochain passage.");
                }
                await signal.Attendre(Passage, arret);
            }
        }
        catch (OperationCanceledException) when (arret.IsCancellationRequested) { /* arrêt du serveur */ }
    }

    /// <summary>Appels restés « en cours » (serveur arrêté pendant un envoi) : remis en file. Le destinataire écarte un éventuel doublon (X-Gestion-Mep-Appel).</summary>
    public async Task<int> Reprendre(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
        var maintenant = temps.GetUtcNow();
        return await db.AppelsApi.Where(a => a.Etat == EtatAppel.EnCours)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Etat, EtatAppel.EnAttente).SetProperty(a => a.ProchaineTentative, maintenant), ct);
    }

    private async Task Purger(CancellationToken ct)
    {
        var maintenant = temps.GetUtcNow();
        if (maintenant < _prochainePurge) return;
        _prochainePurge = maintenant.AddHours(1);
        var jours = Math.Clamp(config.GetValue(CleConservation, 90), 1, 3650);
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
        var limite = maintenant.AddDays(-jours);
        await db.AppelsApi.Where(a => a.CreeLe < limite && (a.Etat == EtatAppel.Reussi || a.Etat == EtatAppel.Echec)).ExecuteDeleteAsync(ct);
    }

    /// <summary>Un passage : prend les appels prêts et fait une tentative de chacun. Renvoie le nombre d'appels traités.</summary>
    public async Task<int> Traiter(CancellationToken ct)
    {
        if (!config.GetValue(CleEnvoisActifs, true)) return 0;
        List<Guid> ids;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
            var maintenant = temps.GetUtcNow();
            ids = await db.Database.SqlQuery<Guid>($"""
                UPDATE "AppelsApi" SET "Etat" = 'EnCours'
                WHERE "Id" IN (
                    SELECT a."Id" FROM "AppelsApi" a
                    WHERE a."Etat" = 'EnAttente' AND a."ProchaineTentative" <= {maintenant}
                      AND NOT EXISTS (SELECT 1 FROM "AppelsApi" b
                          WHERE b."ConnexionId" = a."ConnexionId" AND b."MepId" IS NOT DISTINCT FROM a."MepId"
                            AND (b."CreeLe" < a."CreeLe" OR (b."CreeLe" = a."CreeLe" AND b."Id" < a."Id"))
                            AND b."Etat" IN ('EnAttente', 'EnCours'))
                    ORDER BY a."CreeLe", a."Id"
                    LIMIT {Parallele}
                    FOR UPDATE SKIP LOCKED)
                RETURNING "Id" AS "Value"
                """).ToListAsync(ct);
        }
        await Task.WhenAll(ids.Select(id => Envoyer(id, ct)));
        return ids.Count;
    }

    private async Task Envoyer(Guid id, CancellationToken ct)
    {
        Guid? mepNotifiee = null;
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
            var appel = await db.AppelsApi.FirstOrDefaultAsync(a => a.Id == id, ct);
            var c = appel is null ? null : await db.ConnexionsApi.AsNoTracking().FirstOrDefaultAsync(x => x.Id == appel.ConnexionId, ct);
            if (appel is null || c is null) return;
            var resultat = c.Active
                ? await client.Envoyer(c, appel, ClientConnexionsApi.LimiteHistorique, ct)
                : new ResultatTentative(new TentativeAppel
                {
                    Debut = temps.GetUtcNow(), TypeErreur = "desactivee",
                    Erreur = "La connexion a été désactivée avant l'envoi : rien n'est parti. Réactivez-la, puis renvoyez.",
                }, false, false, null, false);
            if (Conclure(appel, resultat, temps.GetUtcNow()))
            {
                await Journaliser(db, appel, c, temps.GetUtcNow());
                mepNotifiee = appel.MepId;
            }
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            journal.LogWarning(ex, "Envoi de l'appel {AppelId} interrompu ; il repartira au redémarrage.", id);
        }
        if (mepNotifiee is { } mep) diffuseur.NotifierAppels(mep);
    }

    /// <summary>Inscrit la tentative et décide de la suite ; renvoie vrai si l'appel est terminé (réussi ou en échec).</summary>
    public static bool Conclure(AppelApi appel, ResultatTentative r, DateTimeOffset maintenant)
    {
        appel.Tentatives = [.. appel.Tentatives, r.Tentative];
        appel.DernierCode = r.Tentative.Code;
        var faites = appel.Tentatives.Count;
        if (r.Reussi) appel.Etat = EtatAppel.Reussi;
        else if (r.Reessayable && faites < appel.TentativesMax)
        {
            appel.Etat = EtatAppel.EnAttente;
            appel.ProchaineTentative = maintenant + (r.Attente ?? Attente(faites));
            return false;
        }
        else appel.Etat = EtatAppel.Echec;
        appel.ProchaineTentative = null;
        appel.TermineLe = maintenant;
        return true;
    }

    /// <summary>« 503 après 3 tentatives », « pas de réponse après 3 tentatives », « adresse refusée ».</summary>
    public static string ResultatCourt(AppelApi a)
    {
        var derniere = a.Tentatives.LastOrDefault();
        var quoi = a.DernierCode is { } code ? code.ToString(System.Globalization.CultureInfo.InvariantCulture) : derniere?.TypeErreur switch
        {
            "delai" => "pas de réponse",
            "reseau" => "outil injoignable",
            "adresse" => "adresse refusée",
            "certificat" => "certificat refusé",
            "secret" => "secret illisible",
            "authentification" => "jeton OAuth2 refusé",
            "desactivee" => "connexion désactivée",
            "message" => "message invalide",
            _ => "erreur",
        };
        var n = a.Tentatives.Count(t => t.TypeErreur is not ("message" or "desactivee"));
        return n > 1 ? $"{quoi} après {n} tentatives" : quoi;
    }

    /// <summary>
    /// Fin d'un appel (hors essai) : ligne au journal de la MEP (« API externes », sans changer la MEP ni son rapport figé),
    /// sauf pour un envoi réussi d'un déclencheur fréquent (étape démarrée ou réussie) ; échec définitif : journal
    /// d'administration (auteur « Application »).
    /// </summary>
    public static async Task Journaliser(MepDbContext db, AppelApi a, ConnexionApi c, DateTimeOffset maintenant)
    {
        if (a.Origine == OrigineAppel.Essai) return;
        var reussi = a.Etat == EtatAppel.Reussi;
        var frequent = DeclencheursConnexion.Trouver(a.TypeEvenement)?.Frequent == true;
        var par = a.DemandePar.Length > 0 ? $" par {a.DemandePar}" : "";
        var verbe = a.Origine == OrigineAppel.Renvoi ? "renvoyé" : "envoyé";
        if (a.MepId is { } mepId && !(reussi && frequent) && await db.Meps.AnyAsync(m => m.Id == mepId && !m.EstModele))
            db.Journal.Add(new EvenementJournal
            {
                MepId = mepId, Quand = maintenant, Auteur = ServiceMep.AuteurSysteme, Type = reussi ? "api.envoye" : "api.echec",
                Detail = reussi
                    ? $"Message « {c.Nom} » {verbe}{par} : {a.Resume} ({a.DernierCode})"
                    : $"Échec de l'envoi « {c.Nom} »{(a.Origine == OrigineAppel.Automatique ? "" : $" ({verbe}{par})")} : {a.Resume} ({ResultatCourt(a)})",
            });
        if (!reussi)
            JournalAdmin.Ecrire(db, null, "connexion-api.echec",
                $"{c.Nom}{(a.MepNom.Length > 0 ? $", {a.MepNom}" : "")}, {a.LibelleEvenement} : {ResultatCourt(a)}", JournalAdmin.AuteurApplication);
    }
}
