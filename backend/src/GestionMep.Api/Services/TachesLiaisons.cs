using GestionMep.Api.Donnees;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GestionMep.Api.Services;

/// <summary>
/// Repère chaque écriture d'une MEP ou de ses systèmes (création, modification, suppression), quel que soit le chemin
/// (console, déroulé, surveillance des échéances) : la MEP entre dans la file des déploiements à envoyer à Noosphere. Un
/// signal de trop est sans effet (seul un événement qui a changé part).
/// </summary>
public sealed class IntercepteurMeps(SignalLiaisons signal, TimeProvider temps) : SaveChangesInterceptor
{
    private void Relever(DbContext? db)
    {
        if (db is null) return;
        var maintenant = temps.GetUtcNow();
        foreach (var e in db.ChangeTracker.Entries())
        {
            if (e.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;
            switch (e.Entity)
            {
                case Mep m when !m.EstModele: signal.MepModifiee(m.Id, maintenant); break;
                case Systeme s: signal.MepModifiee(s.MepId, maintenant); break;
            }
        }
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Relever(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Relever(eventData.Context);
        return ValueTask.FromResult(result);
    }
}

/// <summary>
/// Tâches de fond de la liaison avec Noosphere : appairage au premier démarrage (<c>NOOSPHERE_URL</c> +
/// <c>NOOSPHERE_CODE_LIAISON</c>), relecture du catalogue toutes les 15 minutes et après chaque avis, envoi des événements de
/// déploiement (anti-rebond de 30 s, puis nouvel essai après 30 s, 2 min et 10 min), rapprochement complet au démarrage et
/// toutes les 15 minutes, purge du journal des échanges (90 jours) et des nonces (10 minutes). Désactivable
/// (<c>Liaisons:ArrierePlan=false</c>, utile en test : <see cref="Passage"/> se lance alors à la main).
/// </summary>
public sealed class TachesLiaisons(IServiceScopeFactory scopes, SignalLiaisons signal, IConfiguration config, TimeProvider temps,
    ILogger<TachesLiaisons> journal) : BackgroundService
{
    public static readonly TimeSpan Periode = TimeSpan.FromMinutes(15);
    public const int ConservationJours = 90;
    public static readonly TimeSpan DureeNonces = TimeSpan.FromMinutes(10);
    public const string CleUrl = "Liaison:NoosphereUrl";
    public const string CleCode = "Liaison:CodeAppairage";
    public const int LongueurCodeDemarrage = 16;
    public const int EssaisMax = 4;
    private readonly Dictionary<Guid, DateTimeOffset> _prochaineSynchro = [];
    private readonly Dictionary<Guid, (int Essais, DateTimeOffset Prochain)> _echecs = [];
    private DateTimeOffset _prochainRapprochement;
    private DateTimeOffset _prochainePurge;
    private DateTimeOffset? _prochainAmorcage;
    private int _essaisAmorcage;

    /// <summary>Attente avant le nouvel essai d'un envoi de déploiements, après <paramref name="faites"/> tentatives.</summary>
    public static TimeSpan Attente(int faites) => faites switch
    {
        1 => TimeSpan.FromSeconds(30),
        2 => TimeSpan.FromMinutes(2),
        _ => TimeSpan.FromMinutes(10),
    };

    public static string? UrlDemarrage(IConfiguration c) => (c[CleUrl] is { Length: > 0 } u ? u : c["NOOSPHERE_URL"])?.Trim() is { Length: > 0 } v ? v : null;

    public static string? CodeDemarrage(IConfiguration c) => (c[CleCode] is { Length: > 0 } u ? u : c["NOOSPHERE_CODE_LIAISON"])?.Trim() is { Length: > 0 } v ? v : null;

    protected override async Task ExecuteAsync(CancellationToken arret)
    {
        try
        {
            _prochainAmorcage = temps.GetUtcNow().AddSeconds(5);
            _prochainRapprochement = temps.GetUtcNow().AddSeconds(30);
            while (!arret.IsCancellationRequested)
            {
                try { await Passage(arret); }
                catch (Exception ex) when (!arret.IsCancellationRequested)
                {
                    journal.LogWarning(ex, "Tâches de la liaison avec Noosphere interrompues ; nouvel essai au prochain passage.");
                }
                var prochaine = signal.Deploiements.ProchaineEcheance();
                var attente = prochaine is { } p ? p - temps.GetUtcNow() : TimeSpan.FromSeconds(5);
                await signal.Attendre(attente < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : attente > TimeSpan.FromSeconds(5) ? TimeSpan.FromSeconds(5) : attente, arret);
            }
        }
        catch (OperationCanceledException) when (arret.IsCancellationRequested) { /* arrêt du serveur */ }
    }

    /// <summary>Un passage : amorçage, synchronisations dues, envois de déploiements dus, purges.</summary>
    public async Task Passage(CancellationToken ct)
    {
        var maintenant = temps.GetUtcNow();
        if (_prochainAmorcage is { } a && a <= maintenant) await Amorcer(ct);
        await Synchroniser(maintenant, ct);
        await EnvoyerDeploiements(signal.PrendreRapprochement() || maintenant >= _prochainRapprochement, ct);
        if (maintenant >= _prochainePurge) await Purger(ct);
    }

    /// <summary>
    /// Appairage au premier démarrage : avec une adresse et un code d'au moins 16 caractères, et aucune liaison vers cette
    /// adresse. Noosphere peut démarrer après gestion-mep : nouvel essai toutes les 30 s (10 fois) tant qu'il est injoignable ;
    /// un refus du code (404, 409, 410) est définitif. L'empreinte reste à confirmer des deux côtés.
    /// </summary>
    public async Task<bool> Amorcer(CancellationToken ct)
    {
        _prochainAmorcage = null;
        var url = UrlDemarrage(config);
        var code = CodeDemarrage(config);
        if (url is null || code is null) return false;
        if (CryptoLiaison.NormaliserCode(code).Length < LongueurCodeDemarrage)
        {
            journal.LogError("NOOSPHERE_CODE_LIAISON trop court ({Longueur} caractères, {Minimum} au moins) : aucun appairage au démarrage.",
                CryptoLiaison.NormaliserCode(code).Length, LongueurCodeDemarrage);
            return false;
        }
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
        var adresse = url.TrimEnd('/');
        if (await db.Liaisons.AnyAsync(l => l.Url == adresse, ct)) return false;
        var svc = scope.ServiceProvider.GetRequiredService<ServiceLiaisons>();
        try
        {
            var l = await svc.Appairer(adresse, code, null, "Démarrage (NOOSPHERE_CODE_LIAISON)", svc.UrlLocale(), ct);
            journal.LogWarning("Liaison avec Noosphere ({Url}) appairée au démarrage. Empreinte à confirmer des deux côtés : {Empreinte}", adresse, l.Empreinte);
            return true;
        }
        catch (ErreurLiaison e) when (++_essaisAmorcage < 10)
        {
            journal.LogWarning("Appairage avec Noosphere au démarrage ({Url}) : {Message} Nouvel essai dans 30 s.", adresse, e.Message);
            _prochainAmorcage = temps.GetUtcNow().AddSeconds(30);
        }
        catch (ErreurHttp e)
        {
            journal.LogError("Appairage avec Noosphere au démarrage ({Url}) impossible : {Message}", adresse, e.Message);
        }
        return false;
    }

    private async Task Synchroniser(DateTimeOffset maintenant, CancellationToken ct)
    {
        var demandees = signal.SynchrosDues(maintenant).ToHashSet();
        List<Guid> actives;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
            actives = await db.Liaisons.AsNoTracking().Where(l => l.Etat == EtatLiaison.Active && l.ConfirmeeIci).Select(l => l.Id).ToListAsync(ct);
        }
        foreach (var id in actives)
        {
            if (!demandees.Contains(id) && _prochaineSynchro.TryGetValue(id, out var prochaine) && prochaine > maintenant) continue;
            _prochaineSynchro[id] = maintenant + Periode;
            using var scope = scopes.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<ServiceLiaisons>();
            var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
            var l = await db.Liaisons.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (l is null || l.Etat != EtatLiaison.Active) continue;
            try
            {
                var (bilan, _) = await svc.Synchroniser(l, null, JournalAdmin.AuteurApplication, ct);
                if (bilan.AChange) signal.DemanderRapprochement();
            }
            catch (ErreurHttp e)
            {
                journal.LogInformation("Synchronisation de la liaison {Liaison} : {Message}", l.NomInstance, e.Message);
            }
        }
    }

    /// <summary>
    /// Envoie les événements des MEP dont l'anti-rebond est écoulé (ou de toutes les MEP suivies pour un rapprochement) à
    /// chaque liaison qui reçoit les MEP. Un échec remet les MEP en file pour un nouvel essai (30 s, 2 min, 10 min) ; après
    /// la quatrième tentative, l'échec est inscrit au journal d'administration et le prochain rapprochement reprendra.
    /// </summary>
    public async Task<int> EnvoyerDeploiements(bool rapprochement, CancellationToken ct)
    {
        var maintenant = temps.GetUtcNow();
        if (rapprochement) _prochainRapprochement = maintenant + Periode;
        var prets = signal.Deploiements.Prendre(maintenant);
        if (!rapprochement && prets.Count == 0) return 0;
        var envoyes = 0;
        List<Guid> liaisons;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
            liaisons = (await db.Liaisons.AsNoTracking().Where(l => l.Etat == EtatLiaison.Active).ToListAsync(ct))
                .Where(ServiceLiaisons.EnvoieLesMeps).Select(l => l.Id).ToList();
        }
        foreach (var id in liaisons)
        {
            if (_echecs.TryGetValue(id, out var echec) && echec.Prochain > maintenant && !rapprochement)
            {
                signal.Deploiements.Replanifier(prets, echec.Prochain);
                continue;
            }
            using var scope = scopes.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<ServiceLiaisons>();
            var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
            var l = await db.Liaisons.FirstAsync(x => x.Id == id, ct);
            List<EvenementDeploiement> evenements = [];
            try
            {
                evenements = await svc.Preparer(l, rapprochement ? null : prets);
                envoyes += await svc.Envoyer(l, evenements, ct);
                _echecs.Remove(id);
            }
            catch (ErreurHttp e)
            {
                var essais = (_echecs.TryGetValue(id, out var avant) ? avant.Essais : 0) + 1;
                await db.SaveChangesAsync(CancellationToken.None);
                if (essais >= EssaisMax)
                {
                    _echecs.Remove(id);
                    JournalAdmin.Ecrire(db, null, "liaison.envoi-echec",
                        $"Envoi de {evenements.Count} événement(s) de déploiement à {l.NomInstance} abandonné après {essais} tentatives : {e.Message} Nouvel essai au prochain rapprochement (15 min).",
                        JournalAdmin.AuteurApplication);
                    await db.SaveChangesAsync(CancellationToken.None);
                }
                else
                {
                    var prochain = temps.GetUtcNow() + Attente(essais);
                    _echecs[id] = (essais, prochain);
                    signal.Deploiements.Replanifier(evenements.Select(x => x.DeploiementId), prochain);
                }
            }
        }
        return envoyes;
    }

    private async Task Purger(CancellationToken ct)
    {
        var maintenant = temps.GetUtcNow();
        _prochainePurge = maintenant.AddMinutes(1);
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
        var nonces = maintenant - DureeNonces;
        await db.NoncesLiaison.Where(n => n.VuLe < nonces).ExecuteDeleteAsync(ct);
        var echanges = maintenant.AddDays(-ConservationJours);
        await db.EchangesLiaison.Where(e => e.Quand < echanges).ExecuteDeleteAsync(ct);
    }
}
