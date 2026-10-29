using System.Security.Claims;
using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GestionMep.Api.Endpoints;

/// <summary>
/// Connexions aux API externes (#111). Console (administrateurs) : connexions, aperçu et essai d'un brouillon, estimation,
/// historique des appels et renvoi. MEP (tout lecteur) : envois manuels proposés et échecs à montrer ; envoi et renvoi par
/// le pilote, ses suppléants et les administrateurs.
/// </summary>
public static class ConnexionsEndpoints
{
    private static readonly string[] ReglagesReseau = ["api-reseaux-autorises", "api-proxy", "api-appels-conservation-jours", "api-envois-actifs", "url-publique"];
    public const string MessageModele = "Un modèle de MEP n'envoie rien : créez une MEP à partir du modèle.";

    public static void MapConnexions(this RouteGroupBuilder api, IConfiguration config)
    {
        var admin = api.MapGroup("/admin/connexions").ExigerAdmin();

        admin.MapGet("", (ServiceConnexions svc) => svc.Lister());

        admin.MapGet("/catalogue", async (MepDbContext db, IConfiguration cfg) =>
            new CatalogueConnexionsDto(
                DeclencheursConnexion.Tous.Select(d => new DeclencheurDto(d.Cle, d.Libelle, d.Groupe, d.Frequent)).ToList(),
                ModelesConnexion.Tous.Select(m => new ModeleConnexionDto(m.Modele, m.Libelle, m.Description, [.. m.Etiquettes], m.Methode, m.Format,
                    m.Authentification, m.UrlSecrete, m.Signature, m.UrlExemple, m.Corps, [.. m.Declencheurs], m.EnvoiManuel, m.LibelleEnvoiManuel)).ToList(),
                VariablesEvenement.Catalogue.Select(v => new VariableTexteDto(v.Cle, v.Libelle, v.Groupe, v.Description, v.Liste)).ToList(),
                VersionApplication.Courante,
                (await Reglages.Lister(db, cfg)).Where(r => ReglagesReseau.Contains(r.Nom)).ToList()));

        admin.MapPost("", async (ConnexionApiSaisie s, ClaimsPrincipal user, ServiceConnexions svc, MepDbContext db) =>
        {
            var acteur = Acteur.Depuis(user);
            var c = new ConnexionApi { CreeeLe = svc.Maintenant, Active = s.Active ?? true };
            var changes = await svc.Appliquer(c, s with { Active = null }, acteur.Nom);
            c.ModifieeLe = svc.Maintenant;
            c.ModifieePar = acteur.Nom;
            db.ConnexionsApi.Add(c);
            var secretsSaisis = changes.Where(x => x.Contains("remplacé", StringComparison.Ordinal)).Select(x => x.Replace("remplacé", "saisi", StringComparison.Ordinal));
            JournalAdmin.Ecrire(db, acteur, "connexion-api.creee", $"{c.Nom}{Suite(secretsSaisis)}{(c.Active ? "" : " (désactivée)")}");
            await Enregistrer(db, c.Nom);
            return Results.Created($"/api/admin/connexions/{c.Id}", await svc.Dto(c));
        });

        admin.MapGet("/{id:guid}", async (Guid id, ServiceConnexions svc) => await svc.Dto(await svc.Charger(id)));

        admin.MapPut("/{id:guid}", async (Guid id, ConnexionApiSaisie s, ClaimsPrincipal user, ServiceConnexions svc, MepDbContext db) =>
        {
            var acteur = Acteur.Depuis(user);
            var c = await svc.Charger(id);
            if (s.Version is null) throw ErreurHttp.Saisie("La version de la connexion est obligatoire.");
            if (s.Version != c.Version) throw Modifiee();
            var changes = await svc.Appliquer(c, s, acteur.Nom);
            if (changes.Count > 0)
            {
                c.Version++;
                JournalAdmin.Ecrire(db, acteur, "connexion-api.modifiee", $"{c.Nom} : {string.Join(", ", changes)}");
                await Enregistrer(db, c.Nom);
            }
            return await svc.Dto(c);
        });

        admin.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, ServiceConnexions svc, MepDbContext db) =>
        {
            var acteur = Acteur.Depuis(user);
            var c = await svc.Charger(id);
            var appels = await db.AppelsApi.CountAsync(a => a.ConnexionId == id);
            db.ConnexionsApi.Remove(c);
            JournalAdmin.Ecrire(db, acteur, "connexion-api.supprimee", $"{c.Nom}{(appels > 0 ? $" (historique de {appels} appel{(appels > 1 ? "s" : "")} supprimé)" : "")}");
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        admin.MapPost("/{id:guid}/activer", (Guid id, ClaimsPrincipal user, ServiceConnexions svc, MepDbContext db) => ChangerEtat(id, true, user, svc, db));
        admin.MapPost("/{id:guid}/desactiver", (Guid id, ClaimsPrincipal user, ServiceConnexions svc, MepDbContext db) => ChangerEtat(id, false, user, svc, db));

        admin.MapPost("/apercu", async (ApercuConnexionSaisie s, ClaimsPrincipal user, ServiceConnexions svc, ServiceMep meps, MepDbContext db) =>
            (await Preparer(s, Acteur.Depuis(user), svc, meps, db, UsageSaisie.Apercu)).Apercu);

        admin.MapPost("/essai", async (ApercuConnexionSaisie s, ClaimsPrincipal user, ServiceConnexions svc, ServiceMep meps, MepDbContext db,
            ClientConnexionsApi client, CancellationToken ct) => await Essayer(s, Acteur.Depuis(user), svc, meps, db, client, ct));

        admin.MapPost("/estimation", async (ConnexionApiSaisie s, ClaimsPrincipal user, ServiceConnexions svc, ServiceMep meps, MepDbContext db) =>
        {
            const int jours = 30;
            var brouillon = await svc.Brouillon(s, null, Acteur.Depuis(user).Nom, UsageSaisie.Apercu);
            if (brouillon.Declencheurs.Count == 0) return new EstimationConnexionDto(0, 0, jours);
            var types = brouillon.Declencheurs.SelectMany(d => DeclencheursConnexion.Trouver(d)?.Types ?? []).ToList();
            if (brouillon.Declencheurs.Contains(DeclencheursConnexion.RecetteNonConforme)) types.Add("recette.test");
            var depuis = svc.Maintenant.AddDays(-jours);
            var journal = await db.Journal.AsNoTracking().Where(j => j.Quand >= depuis && types.Contains(j.Type))
                .Select(j => new { j.MepId, j.Type, j.Detail, j.EtapeId }).ToListAsync();
            var parId = (await meps.ChargerPlusieurs([.. journal.Select(j => j.MepId).Distinct()])).Where(m => !m.EstModele).ToDictionary(m => m.Id);
            var appels = 0;
            var touchees = new HashSet<Guid>();
            foreach (var j in journal)
            {
                if (!parId.TryGetValue(j.MepId, out var mep) || ServiceConnexions.CleJournal(j.Type, j.Detail) is not { } cle
                    || !brouillon.Declencheurs.Contains(cle) || !await svc.Correspond(brouillon, mep, cle, j.EtapeId)) continue;
                appels++;
                touchees.Add(mep.Id);
            }
            return new EstimationConnexionDto(appels, touchees.Count, jours);
        });

        admin.MapGet("/appels", async (Guid? connexion, string? etat, string? origine, DateTimeOffset? avant, int? limite, MepDbContext db) =>
        {
            var n = Math.Clamp(limite ?? 50, 1, 200);
            var q = db.AppelsApi.AsNoTracking();
            if (connexion is { } cid) q = q.Where(a => a.ConnexionId == cid);
            if (!string.IsNullOrWhiteSpace(etat))
            {
                if (!Enum.TryParse<EtatAppel>(etat, true, out var e)) throw ErreurHttp.Saisie("État inconnu : EnAttente, EnCours, Reussi ou Echec.");
                q = q.Where(a => a.Etat == e);
            }
            if (!string.IsNullOrWhiteSpace(origine))
            {
                if (!Enum.TryParse<OrigineAppel>(origine, true, out var o)) throw ErreurHttp.Saisie("Origine inconnue : Automatique, Manuel, Essai ou Renvoi.");
                q = q.Where(a => a.Origine == o);
            }
            if (avant is { } av) q = q.Where(a => a.CreeLe < av);
            var appels = await q.OrderByDescending(a => a.CreeLe).ThenByDescending(a => a.Id).Take(n + 1).ToListAsync();
            var noms = await db.ConnexionsApi.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Nom);
            var echecs = await db.AppelsApi.CountAsync(a => a.Etat == EtatAppel.Echec && a.Origine != OrigineAppel.Essai
                && (connexion == null || a.ConnexionId == connexion));
            return new PageAppelsDto(appels.Take(n).Select(a => ServiceConnexions.Resume(a, noms.GetValueOrDefault(a.ConnexionId, ""))).ToList(),
                appels.Count > n ? appels[n - 1].CreeLe : null, echecs);
        });

        admin.MapGet("/appels/{id:guid}", async (Guid id, MepDbContext db) =>
        {
            var a = await ChargerAppel(db, id);
            var c = await db.ConnexionsApi.AsNoTracking().FirstAsync(x => x.Id == a.ConnexionId);
            var entetes = RequeteConnexion.EntetesAffiches(c, a.TypeContenu, a.Entetes, a.AppelOrigineId ?? a.Id, a.TypeEvenement, VersionApplication.Courante)
                .Select(e => new EnteteAppelDto(e.Nom, e.Valeur)).ToList();
            return new AppelDto(ServiceConnexions.Resume(a, c.Nom), a.Methode, RequeteConnexion.UrlAffichee(a.Url, a.UrlSecrete), a.TypeContenu, entetes,
                a.Corps, a.Tentatives.Select(ServiceConnexions.Dto).ToList(), a.AppelOrigineId, ServiceConnexions.Curl(c, a, entetes), true);
        });

        admin.MapPost("/appels/{id:guid}/renvoyer", async (Guid id, ClaimsPrincipal user, ServiceConnexions svc, MepDbContext db) =>
        {
            var a = await ChargerAppel(db, id);
            var c = await db.ConnexionsApi.AsNoTracking().FirstAsync(x => x.Id == a.ConnexionId);
            var nouveau = Renvoyer(a, c, Acteur.Depuis(user), svc, true);
            db.AppelsApi.Add(nouveau);
            await db.SaveChangesAsync();
            svc.Reveiller();
            return Results.Accepted($"/api/admin/connexions/appels/{nouveau.Id}", new AppelCreeDto(nouveau.Id));
        });

        // ---------- Dans une MEP ----------

        api.MapGet("/meps/{id:guid}/connexions", async (Guid id, ClaimsPrincipal user, ServiceMep meps, MepDbContext db) =>
        {
            var acteur = Acteur.Depuis(user);
            var mep = await meps.Charger(id);
            var envois = mep.EstModele ? [] : await db.ConnexionsApi.AsNoTracking().Where(c => c.Active && c.EnvoiManuel).OrderBy(c => c.Nom)
                .Select(c => new ConnexionManuelleDto(c.Id, c.Nom, c.LibelleEnvoiManuel, c.Description)).ToListAsync();
            return new ConnexionsMepDto(envois, await Echecs(db, mep), !mep.EstModele && (acteur.EstAdmin || Regles.EstPilote(mep, acteur.Id)));
        });

        api.MapPost("/meps/{id:guid}/connexions/{cid:guid}/apercu", async (Guid id, Guid cid, EnvoiManuelSaisie s, ClaimsPrincipal user,
            ServiceMep meps, ServiceConnexions svc, MepDbContext db) =>
        {
            var acteur = Acteur.Depuis(user);
            var (mep, c) = await PreparerEnvoi(id, cid, acteur, meps, db);
            var ev = new ContexteEvenement(DeclencheursConnexion.EnvoiManuel, "", acteur.Nom, svc.Maintenant, Commentaire: Texte(s.Commentaire));
            var appelId = Guid.NewGuid();
            var r = RequeteConnexion.Rendre(c, await svc.Valeurs(mep, ev with { AppelId = appelId }, acteur.Id));
            // Ni adresse ni en-têtes pour qui n'est pas administrateur : seulement le message.
            var entetes = acteur.EstAdmin
                ? RequeteConnexion.EntetesAffiches(c, r.TypeContenu, r.Entetes, appelId, ev.Cle, VersionApplication.Courante).Select(e => new EnteteAppelDto(e.Nom, e.Valeur)).ToList()
                : [];
            var propose = new EvenementProposeDto(null, ev.Cle, ev.Libelle, "", ev.Quand);
            return new ApercuConnexionDto(r.Methode, acteur.EstAdmin ? RequeteConnexion.UrlAffichee(r.Url, r.UrlSecrete) : "", entetes, r.TypeContenu, r.Corps,
                RequeteConnexion.Lisible(c.Format, r.Corps), Manquantes(r), r.Inconnues, r.Erreurs, ServiceConnexions.NomMep(mep), propose, [propose]);
        });

        api.MapPost("/meps/{id:guid}/connexions/{cid:guid}/envoyer", async (Guid id, Guid cid, EnvoiManuelSaisie s, ClaimsPrincipal user,
            ServiceMep meps, ServiceConnexions svc, MepDbContext db, DiffuseurMep diffuseur) =>
        {
            var acteur = Acteur.Depuis(user);
            var (mep, c) = await PreparerEnvoi(id, cid, acteur, meps, db);
            var commentaire = Texte(s.Commentaire);
            if (commentaire.Length > 2000) throw ErreurHttp.Saisie("Commentaire : 2000 caractères au plus.");
            var ev = new ContexteEvenement(DeclencheursConnexion.EnvoiManuel, "", acteur.Nom, svc.Maintenant, Commentaire: commentaire);
            var appel = await svc.NouvelAppel(c, mep, ev, OrigineAppel.Manuel, acteur);
            db.AppelsApi.Add(appel);
            var termine = appel.Etat == EtatAppel.Echec;
            if (termine) await EnvoiAppelsApi.Journaliser(db, appel, c, svc.Maintenant);
            await db.SaveChangesAsync();
            if (termine) diffuseur.NotifierAppels(mep.Id);
            else svc.Reveiller();
            return Results.Accepted($"/api/meps/{id}/appels/{appel.Id}", new AppelCreeDto(appel.Id));
        });

        api.MapGet("/meps/{id:guid}/appels/{appelId:guid}", async (Guid id, Guid appelId, MepDbContext db) =>
        {
            var a = await db.AppelsApi.AsNoTracking().FirstOrDefaultAsync(x => x.Id == appelId && x.MepId == id && x.Origine != OrigineAppel.Essai)
                ?? throw ErreurHttp.NonTrouve("Cet envoi n'existe plus.");
            var nom = await db.ConnexionsApi.AsNoTracking().Where(c => c.Id == a.ConnexionId).Select(c => c.Nom).FirstOrDefaultAsync() ?? "";
            return ServiceConnexions.Resume(a, nom);
        });

        api.MapPost("/meps/{id:guid}/appels/{appelId:guid}/renvoyer", async (Guid id, Guid appelId, ClaimsPrincipal user, ServiceMep meps,
            ServiceConnexions svc, MepDbContext db) =>
        {
            var acteur = Acteur.Depuis(user);
            var mep = await meps.Charger(id);
            if (mep.EstModele) throw new ErreurHttp(422, MessageModele);
            ServiceMep.ExigerPiloteOuAdmin(mep, acteur, "Seuls le pilote, ses suppléants et les administrateurs renvoient un message.");
            var a = await db.AppelsApi.AsNoTracking().FirstOrDefaultAsync(x => x.Id == appelId && x.MepId == id && x.Origine != OrigineAppel.Essai)
                ?? throw ErreurHttp.NonTrouve("Cet envoi n'existe plus.");
            var c = await db.ConnexionsApi.AsNoTracking().FirstAsync(x => x.Id == a.ConnexionId);
            var nouveau = Renvoyer(a, c, acteur, svc, acteur.EstAdmin);
            db.AppelsApi.Add(nouveau);
            await db.SaveChangesAsync();
            svc.Reveiller();
            return Results.Accepted($"/api/meps/{id}/appels/{nouveau.Id}", new AppelCreeDto(nouveau.Id));
        });

        // ---------- Récepteur de démonstration ----------
        if (Demonstration.Active(config))
        {
            api.MapGet("/demo/recepteur", (RecepteurDemo recepteur) => recepteur.Messages()).ExigerAdmin();
            api.MapDelete("/demo/recepteur", (RecepteurDemo recepteur) =>
            {
                recepteur.Vider();
                return Results.NoContent();
            }).ExigerAdmin();
        }
    }

    private static string Texte(string? s) => (s ?? "").Trim();

    private static string Suite(IEnumerable<string> details) => string.Join(", ", details) is { Length: > 0 } t ? $" : {t}" : "";

    private static ErreurHttp Modifiee() =>
        ErreurHttp.Conflit("La connexion a été modifiée par un autre administrateur depuis votre lecture : rechargez-la, ou gardez vos modifications.");

    /// <summary>Enregistre ; deux connexions du même nom créées au même instant : 400, comme le contrôle préalable.</summary>
    private static async Task Enregistrer(MepDbContext db, string nom)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw ErreurHttp.Saisie($"Une connexion s'appelle déjà « {nom} » : choisissez un autre nom.");
        }
        catch (DbUpdateConcurrencyException)
        {
            throw Modifiee();
        }
    }

    /// <summary>Une connexion ne s'active qu'avec tous ses secrets (une connexion importée sans eux reste désactivée).</summary>
    internal static async Task ExigerSecrets(ServiceConnexions svc, ConnexionApi c)
    {
        await svc.Brouillon(ServiceConnexions.Saisie(c), c, c.ModifieePar, UsageSaisie.Importation);
        if (svc.SecretsManquants.Count > 0)
            throw ErreurHttp.Saisie($"Secrets à saisir avant d'activer « {c.Nom} » : {string.Join(", ", svc.SecretsManquants)}.");
    }

    private static async Task<IResult> ChangerEtat(Guid id, bool active, ClaimsPrincipal user, ServiceConnexions svc, MepDbContext db)
    {
        var acteur = Acteur.Depuis(user);
        var c = await svc.Charger(id);
        if (c.Active != active)
        {
            if (active) await ExigerSecrets(svc, c);
            c.Active = active;
            c.Version++;
            c.ModifieeLe = svc.Maintenant;
            c.ModifieePar = acteur.Nom;
            JournalAdmin.Ecrire(db, acteur, active ? "connexion-api.activee" : "connexion-api.desactivee", c.Nom);
            await Enregistrer(db, c.Nom);
        }
        return Results.Ok(await svc.Dto(c));
    }

    private static async Task<AppelApi> ChargerAppel(MepDbContext db, Guid id) =>
        await db.AppelsApi.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id)
        ?? throw ErreurHttp.NonTrouve("Cet appel n'existe plus (historique purgé, ou connexion supprimée).");

    /// <summary>
    /// Nouvel appel avec le même message que <paramref name="a"/> (pas de recalcul : c'est le message de l'événement d'origine),
    /// identifié auprès du destinataire par l'identifiant de l'appel d'origine.
    /// </summary>
    internal static AppelApi Renvoyer(AppelApi a, ConnexionApi c, Acteur? acteur, ServiceConnexions svc, bool estAdmin)
    {
        if (a.Origine == OrigineAppel.Essai) throw ErreurHttp.Saisie("Un essai ne se renvoie pas : essayez de nouveau depuis l'éditeur.");
        if (a.Etat is EtatAppel.EnAttente or EtatAppel.EnCours) throw ErreurHttp.Conflit("Cet appel est encore en cours d'envoi : attendez son résultat.");
        if (!c.Active)
            throw ErreurHttp.Saisie(estAdmin
                ? $"La connexion « {c.Nom} » est désactivée : activez-la avant de renvoyer."
                : $"La connexion « {c.Nom} » est désactivée : demandez à un administrateur de la réactiver.");
        if (a.Tentatives.Any(t => t.TypeErreur == "message"))
            throw ErreurHttp.Saisie("Le message de cet appel n'avait pas pu être rempli : corrigez la connexion ; les prochains envois partiront avec le message corrigé.");
        return new AppelApi
        {
            ConnexionId = a.ConnexionId, MepId = a.MepId, MepNom = a.MepNom, EvenementId = a.EvenementId, TypeEvenement = a.TypeEvenement,
            LibelleEvenement = a.LibelleEvenement, Resume = a.Resume, Origine = OrigineAppel.Renvoi, AppelOrigineId = a.AppelOrigineId ?? a.Id,
            DemandeParId = acteur?.Id, DemandePar = acteur?.Nom ?? JournalAdmin.AuteurLigneDeCommande, Methode = a.Methode, Url = a.Url, UrlSecrete = a.UrlSecrete, TypeContenu = a.TypeContenu,
            Corps = a.Corps, Entetes = [.. a.Entetes.Select(e => new EnteteAppel { Nom = e.Nom, Valeur = e.Valeur })], TentativesMax = c.Tentatives,
            DelaiSecondes = c.DelaiSecondes, CreeLe = svc.Maintenant, ProchaineTentative = svc.Maintenant,
        };
    }

    /// <summary>
    /// Envois en échec à montrer dans « Blocages et échecs » : par connexion, le dernier appel terminé s'il est en échec (un
    /// envoi réussi ensuite efface la carte). Rien pour une MEP clôturée, annulée ou un modèle.
    /// </summary>
    private static async Task<List<EchecEnvoiDto>> Echecs(MepDbContext db, Mep mep)
    {
        if (mep.EstModele || Regles.EstTerminee(mep)) return [];
        var appels = await db.AppelsApi.AsNoTracking().Where(a => a.MepId == mep.Id && a.Origine != OrigineAppel.Essai)
            .OrderByDescending(a => a.CreeLe).ToListAsync();
        if (appels.Count == 0) return [];
        var noms = await db.ConnexionsApi.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Nom);
        var echecs = new List<EchecEnvoiDto>();
        foreach (var groupe in appels.GroupBy(a => a.ConnexionId))
        {
            var termine = groupe.FirstOrDefault(a => a.Etat is EtatAppel.Reussi or EtatAppel.Echec);
            if (termine?.Etat != EtatAppel.Echec || !noms.TryGetValue(groupe.Key, out var nom)) continue;
            var enCours = groupe.Any(a => a.CreeLe > termine.CreeLe && a.Etat is EtatAppel.EnAttente or EtatAppel.EnCours);
            echecs.Add(new EchecEnvoiDto(termine.Id, groupe.Key, nom, termine.LibelleEvenement, termine.Resume, termine.DernierCode,
                termine.Tentatives.Count(t => t.TypeErreur is not ("message" or "desactivee")), termine.Tentatives.LastOrDefault()?.Erreur ?? "",
                termine.TermineLe ?? termine.CreeLe, enCours));
        }
        return [.. echecs.OrderByDescending(e => e.Quand)];
    }

    private static async Task<(Mep Mep, ConnexionApi Connexion)> PreparerEnvoi(Guid id, Guid cid, Acteur acteur, ServiceMep meps, MepDbContext db)
    {
        var mep = await meps.Charger(id);
        if (mep.EstModele) throw new ErreurHttp(422, MessageModele);
        ServiceMep.ExigerPiloteOuAdmin(mep, acteur, "Seuls le pilote, ses suppléants et les administrateurs envoient un message depuis la MEP.");
        var c = await db.ConnexionsApi.AsNoTracking().FirstOrDefaultAsync(x => x.Id == cid && x.Active && x.EnvoiManuel)
            ?? throw ErreurHttp.NonTrouve("Cette connexion n'est plus proposée pour un envoi depuis une MEP (désactivée ou supprimée).");
        return (mep, c);
    }

    /// <summary>Variables sans valeur dans la MEP (celles de l'événement, vides par nature selon l'événement, ne comptent pas).</summary>
    private static List<string> Manquantes(RequeteRendue r) =>
        [.. r.Manquantes.Where(m => VariablesEvenement.Toutes.All(v => v.Cle != m))];

    /// <summary>
    /// « Essayer » : envoie vraiment le brouillon (une tentative, sans file), précédé de « [Essai] » par {{essai}} ; inscrit
    /// l'essai dans les derniers appels de la connexion enregistrée et au journal d'administration. <paramref name="acteur"/>
    /// null : ligne de commande.
    /// </summary>
    internal static async Task<EssaiConnexionDto> Essayer(ApercuConnexionSaisie s, Acteur? acteur, ServiceConnexions svc, ServiceMep meps,
        MepDbContext db, ClientConnexionsApi client, CancellationToken ct)
    {
        var p = await Preparer(s, acteur, svc, meps, db, UsageSaisie.Essai);
        if (!p.Requete.Valide)
            throw ErreurHttp.Saisie(p.Requete.Erreurs is [var e, ..]
                ? $"Le message ne peut pas partir : {e.Message} ({ServiceConnexions.LibelleChamp(e.Champ)}, ligne {e.Ligne}, colonne {e.Colonne})."
                : $"Le message ne peut pas partir : variable inconnue {{{{{p.Requete.Inconnues[0]}}}}}.");
        var appel = new AppelApi
        {
            Id = p.AppelId, ConnexionId = p.Enregistree?.Id ?? Guid.Empty, MepId = p.Mep?.Id, MepNom = p.Mep is null ? "" : ServiceConnexions.NomMep(p.Mep),
            TypeEvenement = p.Evenement.Cle, LibelleEvenement = p.Evenement.Libelle,
            Resume = p.Mep is null ? p.Evenement.Libelle : ServiceConnexions.Resume(p.Mep, p.Evenement.Cle, p.Evenement.EtapeId),
            Origine = OrigineAppel.Essai, DemandeParId = acteur?.Id, DemandePar = acteur?.Nom ?? JournalAdmin.AuteurLigneDeCommande, TentativesMax = 1,
            DelaiSecondes = p.Brouillon.DelaiSecondes, CreeLe = svc.Maintenant,
        };
        svc.Remplir(appel, p.Brouillon, p.Requete);
        var resultat = await client.Envoyer(p.Brouillon, appel, ClientConnexionsApi.LimiteEssai, ct);
        EnvoiAppelsApi.Conclure(appel, resultat, svc.Maintenant);
        // Essai d'une connexion enregistrée : inscrit dans ses derniers appels (un brouillon jamais enregistré n'a pas d'historique).
        if (p.Enregistree is not null) db.AppelsApi.Add(appel);
        JournalAdmin.Ecrire(db, acteur, "connexion-api.essayee",
            $"{(p.Brouillon.Nom.Length > 0 ? p.Brouillon.Nom : "Nouvelle connexion")}{(p.Mep is null ? "" : $", sur {ServiceConnexions.NomMep(p.Mep)}")} : "
            + $"{EnvoiAppelsApi.ResultatCourt(appel)}{(p.Enregistree is null ? " (brouillon non enregistré)" : "")}");
        await db.SaveChangesAsync(CancellationToken.None);
        return new EssaiConnexionDto(p.Apercu, appel.Id, resultat.Reussi, ServiceConnexions.Dto(resultat.Tentative), resultat.Tronque);
    }

    internal sealed record Preparation(ApercuConnexionDto Apercu, ConnexionApi Brouillon, ConnexionApi? Enregistree, Mep? Mep,
        ContexteEvenement Evenement, RequeteRendue Requete, Guid AppelId);

    /// <summary>
    /// Brouillon de l'éditeur rendu pour une MEP et un événement : ligne du journal choisie, à défaut la dernière d'un
    /// déclencheur de la connexion, à défaut un événement simulé (envoi depuis la MEP en dernier recours).
    /// </summary>
    internal static async Task<Preparation> Preparer(ApercuConnexionSaisie s, Acteur? acteur, ServiceConnexions svc, ServiceMep meps,
        MepDbContext db, UsageSaisie usage)
    {
        var acteurNom = acteur?.Nom ?? JournalAdmin.AuteurLigneDeCommande;
        var enregistree = s.ConnexionId is { } cid ? await svc.Charger(cid) : null;
        var brouillon = await svc.Brouillon(s.Brouillon, enregistree, acteurNom, usage);
        var mep = s.MepId is { } mid ? await meps.Charger(mid) : null;
        var proposes = new List<(EvenementProposeDto Dto, EvenementJournal? Journal)>();
        if (mep is not null)
        {
            var journal = await db.Journal.AsNoTracking().Where(j => j.MepId == mep.Id).OrderByDescending(j => j.Id).Take(1000).ToListAsync();
            foreach (var j in journal)
            {
                if (ServiceConnexions.CleJournal(j.Type, j.Detail) is not { } cle) continue;
                if (brouillon.Declencheurs.Count > 0 && !brouillon.Declencheurs.Contains(cle)) continue;
                proposes.Add((new EvenementProposeDto(j.Id, cle, DeclencheursConnexion.Libelle(cle), j.Detail, j.Quand), j));
                if (proposes.Count >= 30) break;
            }
        }
        foreach (var cle in brouillon.Declencheurs.Where(d => proposes.All(p => p.Dto.Cle != d)))
            proposes.Add((new EvenementProposeDto(null, cle, DeclencheursConnexion.Libelle(cle), "", null), null));
        proposes.Add((new EvenementProposeDto(null, DeclencheursConnexion.EnvoiManuel, DeclencheursConnexion.LibelleEnvoiManuel, "", null), null));

        (EvenementProposeDto Dto, EvenementJournal? Journal) choisi;
        if (s.EvenementId is { } eid && mep is not null)
        {
            var j = await db.Journal.AsNoTracking().FirstOrDefaultAsync(x => x.Id == eid && x.MepId == mep.Id)
                ?? throw ErreurHttp.NonTrouve("Cet événement n'est pas dans le journal de la MEP.");
            var cle = ServiceConnexions.CleJournal(j.Type, j.Detail) ?? j.Type;
            choisi = (new EvenementProposeDto(j.Id, cle, DeclencheursConnexion.Libelle(cle), j.Detail, j.Quand), j);
        }
        else if (!string.IsNullOrWhiteSpace(s.Evenement))
            choisi = proposes.FirstOrDefault(p => p.Dto.Cle == s.Evenement) is { Dto: not null } trouve ? trouve
                : DeclencheursConnexion.Trouver(s.Evenement) is { } d ? (new EvenementProposeDto(null, d.Cle, d.Libelle, "", null), null)
                : throw ErreurHttp.Saisie($"Événement inconnu : « {s.Evenement} ».");
        else choisi = proposes[0];

        var appelId = Guid.NewGuid();
        var maintenant = svc.Maintenant;
        ContexteEvenement ev;
        if (choisi.Journal is { } ligne && mep is not null) ev = ServiceConnexions.Contexte(mep, ligne, choisi.Dto.Cle);
        else
        {
            // Événement simulé : une étape au hasard du plan pour les événements d'étape, l'administrateur pour auteur.
            var etape = mep is not null && DeclencheursConnexion.EstEvenementEtape(choisi.Dto.Cle) ? mep.Etapes.OrderBy(e => e.Code).FirstOrDefault() : null;
            ev = new ContexteEvenement(choisi.Dto.Cle, "", acteurNom, maintenant, etape?.Id);
        }
        ev = ev with { Commentaire = Texte(s.Commentaire), Essai = usage == UsageSaisie.Essai, AppelId = appelId };

        Dictionary<string, ValeurVariable> valeurs;
        if (mep is not null) valeurs = await svc.Valeurs(mep, ev, acteur?.Id ?? Guid.Empty);
        else
        {
            valeurs = VariablesEvenement.ValeursExemple();
            valeurs["evenement"] = new(ev.Libelle, null);
            valeurs["evenement_cle"] = new(ev.Cle, null);
            valeurs["essai"] = ev.Essai ? new("[Essai] ", null) : ValeurVariable.Manquante;
            valeurs["commentaire"] = ev.Commentaire.Length > 0 ? new(ev.Commentaire, null) : ValeurVariable.Manquante;
            valeurs["appel_id"] = new(appelId.ToString(), null);
            valeurs[VariablesEvenement.DonneesJson] = new(VariablesEvenement.DonneesJsonDe(valeurs), null);
        }
        var r = RequeteConnexion.Rendre(brouillon, valeurs);
        var entetes = RequeteConnexion.EntetesAffiches(brouillon, r.TypeContenu, r.Entetes, appelId, ev.Cle, VersionApplication.Courante)
            .Select(e => new EnteteAppelDto(e.Nom, e.Valeur)).ToList();
        var apercu = new ApercuConnexionDto(r.Methode, RequeteConnexion.UrlAffichee(r.Url, r.UrlSecrete), entetes, r.TypeContenu, r.Corps,
            RequeteConnexion.Lisible(brouillon.Format, r.Corps), mep is null ? [] : Manquantes(r), r.Inconnues, r.Erreurs,
            mep is null ? null : ServiceConnexions.NomMep(mep), choisi.Dto, [.. proposes.Select(p => p.Dto)]);
        return new Preparation(apercu, brouillon, enregistree, mep, ev, r, appelId);
    }
}
