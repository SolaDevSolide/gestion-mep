using System.Security.Claims;
using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Endpoints;

/// <summary>
/// Communication d'une MEP et modèles de texte des équipes. Lecture : tout connecté. Section Communication : éditeurs en
/// préparation, puis pilote, suppléants ou administrateurs jusqu'à la clôture. Modèles de texte et informations communes
/// d'une équipe : membres de l'équipe (par l'un de leurs comptes reliés) ou administrateurs ; le journal d'administration
/// garde chaque changement.
/// </summary>
public static class CommunicationEndpoints
{
    public const int LongueurMaxNom = 200;
    public const int LongueurMaxObjet = 500;
    public const int LongueurMaxCorps = 20_000;
    public const int LongueurMaxChamp = 4_000;

    private static string Limiter(string? s, int max, string champ)
    {
        var t = ServiceMep.Texte(s);
        if (t.Length > max) throw ErreurHttp.Saisie($"{champ} : {max} caractères au plus.");
        return t;
    }

    /// <summary>Texte sur plusieurs lignes : fins de ligne normalisées, espaces de fin retirés.</summary>
    private static string Lignes(string? s, int max, string champ) =>
        Limiter(string.Join('\n', (s ?? "").Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd())), max, champ);

    // ---------- Modèles fournis ----------

    /// <summary>
    /// Copie les modèles fournis manquants dans chaque équipe (équipes créées avant les modèles de texte, ou par une ancienne
    /// version). Sans effet une fois fait ; renvoie le nombre de modèles ajoutés.
    /// </summary>
    public static async Task<int> CompleterModelesFournis(MepDbContext db, DateTimeOffset maintenant)
    {
        var equipes = await db.Equipes.AsNoTracking().Select(e => e.Id).ToListAsync();
        var presents = (await db.ModelesTexte.AsNoTracking().Where(m => m.CleFournie != null).Select(m => new { m.EquipeId, m.CleFournie }).ToListAsync())
            .ToLookup(m => m.EquipeId, m => m.CleFournie);
        var ajouts = equipes.SelectMany(e => ModelesTexteFournis.Manquants(e, presents[e], maintenant)).ToList();
        if (ajouts.Count == 0) return 0;
        db.ModelesTexte.AddRange(ajouts);
        await db.SaveChangesAsync();
        return ajouts.Count;
    }

    // ---------- Droits ----------

    private static async Task<bool> PeutModifierEquipe(MepDbContext db, Acteur acteur, Guid equipeId) =>
        acteur.EstAdmin || (await Personnes.Charger(db)).EquipesDeLaPersonne(acteur.Id).Contains(equipeId);

    private static async Task ExigerMembre(MepDbContext db, Acteur acteur, Equipe equipe)
    {
        if (!await PeutModifierEquipe(db, acteur, equipe.Id))
            throw ErreurHttp.Interdit($"Seuls les membres de l'équipe {equipe.Nom} ou un administrateur modifient ses modèles de texte.");
    }

    private static async Task<Equipe> ChargerEquipe(MepDbContext db, Guid? id) =>
        await db.Equipes.FirstOrDefaultAsync(e => e.Id == id) ?? throw ErreurHttp.NonTrouve("Équipe introuvable.");

    private static async Task<ModeleTexte> ChargerModele(MepDbContext db, Guid id) =>
        await db.ModelesTexte.FirstOrDefaultAsync(m => m.Id == id) ?? throw ErreurHttp.NonTrouve("Modèle de texte introuvable.");

    // ---------- Modèles de texte ----------

    private static ModeleTexteDto Dto(ModeleTexte m, Dictionary<Guid, string> noms, bool peutModifier) =>
        new(m.Id, m.EquipeId, m.Nom, m.Usage, m.Objet, m.Corps, m.CleFournie is not null, ModelesTexteFournis.EstModifie(m), m.Version,
            m.ModifieLe, m.ModifieParId, m.ModifieParId is { } p && noms.TryGetValue(p, out var n) ? n : null, peutModifier);

    private static async Task<ModeleTexteDto> Dto(MepDbContext db, Acteur acteur, ModeleTexte m) =>
        Dto(m, await Personnes.Noms(db), await PeutModifierEquipe(db, acteur, m.EquipeId));

    /// <summary>Modèles d'une équipe (ou de toutes) : les modèles fournis d'abord, dans leur ordre, puis ceux de l'équipe par nom.</summary>
    public static async Task<List<ModeleTexteDto>> Lister(MepDbContext db, Acteur acteur, Guid? equipe)
    {
        var modeles = await db.ModelesTexte.AsNoTracking().Where(m => equipe == null || m.EquipeId == equipe).ToListAsync();
        var noms = await Personnes.Noms(db);
        var annuaire = await Personnes.Charger(db);
        var miennes = annuaire.EquipesDeLaPersonne(acteur.Id);
        var ordre = ModelesTexteFournis.Tous.Select(f => f.Cle).ToList();
        return modeles
            .OrderBy(m => m.CleFournie is null ? int.MaxValue : ordre.IndexOf(m.CleFournie))
            .ThenBy(m => m.Nom, StringComparer.OrdinalIgnoreCase)
            .Select(m => Dto(m, noms, acteur.EstAdmin || miennes.Contains(m.EquipeId))).ToList();
    }

    private static (string Nom, UsageModeleTexte Usage, string Objet, string Corps) Valider(ModeleTexteSaisie c)
    {
        var nom = Limiter(c.Nom, LongueurMaxNom, "Nom du modèle");
        if (nom.Length == 0) throw ErreurHttp.Saisie("Le nom du modèle est obligatoire.");
        var objet = Limiter(c.Objet, LongueurMaxObjet, "Objet du courriel").ReplaceLineEndings(" ");
        var corps = Lignes(c.Corps, LongueurMaxCorps, "Texte");
        return (nom, c.Usage ?? UsageModeleTexte.Autre, objet, corps);
    }

    // ---------- Valeurs des variables pour une MEP ----------

    /// <summary>
    /// Équipe proposée pour les textes d'une MEP : celle du pilote (par l'un de ses comptes reliés ; la première par nom s'il
    /// en a plusieurs), à défaut une équipe du compte courant, puis la première équipe ; null s'il n'y a aucune équipe.
    /// </summary>
    public static Guid? EquipeProposee(Mep mep, IReadOnlyList<Equipe> equipesParNom, AnnuairePersonnes annuaire, Guid acteurId)
    {
        Guid? Premiere(IReadOnlySet<Guid> ids) => equipesParNom.FirstOrDefault(e => ids.Contains(e.Id))?.Id;
        var comptePilote = mep.Participants.FirstOrDefault(p => p.Id == mep.PiloteId)?.UtilisateurId;
        return (comptePilote is { } cp ? Premiere(annuaire.EquipesDeLaPersonne(cp)) : null)
            ?? Premiere(annuaire.EquipesDeLaPersonne(acteurId))
            ?? (equipesParNom.Count > 0 ? equipesParNom[0].Id : null);
    }

    private static string? LienMep(IConfiguration config, HttpRequest requete, Guid id)
    {
        var baseUrl = config["Application:UrlPublique"] is { Length: > 0 } u ? u.TrimEnd('/') : $"{requete.Scheme}://{requete.Host}{requete.PathBase}";
        return $"{baseUrl}/meps/{id}";
    }

    public static void MapCommunication(this RouteGroupBuilder api)
    {
        // ----- Section Communication de la MEP -----

        api.MapPut("/meps/{id:guid}/communication", async (Guid id, CommunicationSaisie c, ClaimsPrincipal user, ServiceMep svc, MepDbContext db) =>
            Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
            ServiceMep.ExigerEditeur(mep, acteur);
            // Après le Go, le pilote complète encore la communication (preuve de validation, heure limite…), jusqu'à la clôture.
            if (Regles.EstTerminee(mep)) throw new RegleMetierException("La MEP est terminée (clôturée ou annulée) : la communication ne se modifie plus.");
            if (mep.Phase != Phase.Preparation)
                ServiceMep.ExigerPiloteOuAdmin(mep, acteur, "Après le Go, seuls le pilote, un suppléant ou un administrateur modifient la communication.");
            ServiceMep.ExigerVersion(mep, c.Version);
            if (c.PreuveValidationId is { } preuve && !await db.PiecesJointes.AnyAsync(p => p.Id == preuve && p.MepId == mep.Id))
                throw ErreurHttp.Saisie("La preuve de validation doit être une pièce jointe de la MEP.");

            var validee = c.ValideeParUtilisateurs;
            var nouvelle = new Communication
            {
                ReferenceDemande = Limiter(c.ReferenceDemande, LongueurMaxNom, "Référence de la demande"),
                NiveauRisque = c.NiveauRisque ?? NiveauRisque.NonEvalue,
                Benefices = Lignes(c.Benefices, LongueurMaxChamp, "Bénéfices et nouveautés"),
                UtilisateursImpactes = Limiter(c.UtilisateursImpactes, LongueurMaxChamp, "Utilisateurs impactés"),
                ArretApplicatif = c.ArretApplicatif,
                ActionUtilisateurs = Limiter(c.ActionUtilisateurs, LongueurMaxChamp, "Action demandée"),
                HeureLimite = c.HeureLimite,
                DateAnnoncee = c.DateAnnoncee,
                HeureDebutAnnoncee = c.HeureDebutAnnoncee,
                HeureFinAnnoncee = c.HeureFinAnnoncee,
                ValideeParUtilisateurs = validee,
                // Sans validation, ni auteur, ni date, ni preuve.
                ValideePar = validee ? Limiter(c.ValideePar, LongueurMaxNom, "Validée par") : "",
                ValideeLe = validee ? c.ValideeLe : null,
                PreuveValidationId = validee ? c.PreuveValidationId : null,
            };

            var a = mep.Communication;
            var changes = new List<string>();
            void Si<T>(T avant, T apres, string nom)
            {
                if (!EqualityComparer<T>.Default.Equals(avant, apres)) changes.Add(nom);
            }
            Si(a.ReferenceDemande, nouvelle.ReferenceDemande, "référence de la demande");
            Si(a.NiveauRisque, nouvelle.NiveauRisque, "niveau de risque");
            Si(a.Benefices, nouvelle.Benefices, "bénéfices");
            Si(a.UtilisateursImpactes, nouvelle.UtilisateursImpactes, "utilisateurs impactés");
            Si(a.ArretApplicatif, nouvelle.ArretApplicatif, "arrêt applicatif");
            Si(a.ActionUtilisateurs, nouvelle.ActionUtilisateurs, "action demandée");
            Si(a.HeureLimite, nouvelle.HeureLimite, "heure limite");
            Si((a.DateAnnoncee, a.HeureDebutAnnoncee, a.HeureFinAnnoncee), (nouvelle.DateAnnoncee, nouvelle.HeureDebutAnnoncee, nouvelle.HeureFinAnnoncee), "créneau annoncé");
            Si((a.ValideeParUtilisateurs, a.ValideePar, a.ValideeLe, a.PreuveValidationId),
                (nouvelle.ValideeParUtilisateurs, nouvelle.ValideePar, nouvelle.ValideeLe, nouvelle.PreuveValidationId), "validation par les utilisateurs");
            if (changes.Count == 0) return await svc.VersDto(mep, acteur.Id);
            mep.Communication = nouvelle;
            // Informations de communication : elles ne changent pas le plan, les approbations restent valables.
            return await svc.Enregistrer(mep, acteur, "communication.modifiee", "Communication modifiée : " + string.Join(", ", changes));
        })));

        // Valeurs des variables des modèles de texte pour cette MEP (?equipe= : équipe dont on prend les informations).
        api.MapGet("/meps/{id:guid}/variables-texte", async (Guid id, Guid? equipe, ClaimsPrincipal user, ServiceMep svc, MepDbContext db,
            IConfiguration config, HttpRequest requete) =>
        {
            var acteur = Acteur.Depuis(user);
            var mep = await svc.Charger(id);
            var annuaire = await Personnes.Charger(db);
            var equipesParNom = (await db.Equipes.AsNoTracking().ToListAsync()).OrderBy(e => e.Nom, EquipesEndpoints.OrdreDesNoms).ToList();
            var parId = equipesParNom.ToDictionary(e => e.Id);
            if (equipe is { } demandee && !parId.ContainsKey(demandee)) throw ErreurHttp.NonTrouve("Équipe introuvable.");
            var proposee = EquipeProposee(mep, equipesParNom, annuaire, acteur.Id);
            var choisie = equipe ?? proposee;
            var pieces = await db.PiecesJointes.AsNoTracking().Where(p => p.MepId == id).ToDictionaryAsync(p => p.Id, p => p.NomFichier);
            var valeurs = VariablesTexte.Valeurs(mep, choisie is { } c ? parId[c] : null, LienMep(config, requete, id), pieces, svc.Maintenant,
                Regles.Incidents(mep));
            return new VariablesMepDto(choisie, proposee,
                VariablesTexte.Toutes.Select(v => new ValeurVariableDto(v.Cle, valeurs[v.Cle].Texte, valeurs[v.Cle].Liste?.ToList())).ToList());
        });

        // ----- Informations communes d'une équipe -----

        api.MapPut("/equipes/{id:guid}/communication", async (Guid id, EquipeCommunicationSaisie c, ClaimsPrincipal user, MepDbContext db) =>
        {
            var acteur = Acteur.Depuis(user);
            var e = await ChargerEquipe(db, id);
            await ExigerMembre(db, acteur, e);
            var contact = Limiter(c.ContactSupport, LongueurMaxNom, "Contact support");
            var signature = Lignes(c.Signature, LongueurMaxNom * 2, "Signature");
            if (contact != e.ContactSupport || signature != e.Signature)
            {
                var quoi = new List<string>();
                if (contact != e.ContactSupport) quoi.Add("contact support");
                if (signature != e.Signature) quoi.Add("signature");
                JournalAdmin.Ecrire(db, acteur, "equipe.modifiee", $"Équipe « {e.Nom} » : {string.Join(" et ", quoi)} des modèles de texte modifiés");
                e.ContactSupport = contact;
                e.Signature = signature;
                await db.SaveChangesAsync();
            }
            return Results.Ok(EquipesEndpoints.Dto(e));
        });

        // ----- Modèles de texte -----

        api.MapGet("/modeles-texte/variables", () =>
            VariablesTexte.Toutes.Select(v => new VariableTexteDto(v.Cle, v.Libelle, v.Groupe, v.Description, v.Liste)).ToList());

        api.MapGet("/modeles-texte", async (Guid? equipe, ClaimsPrincipal user, MepDbContext db) => await Lister(db, Acteur.Depuis(user), equipe));

        api.MapGet("/modeles-texte/{id:guid}", async (Guid id, ClaimsPrincipal user, MepDbContext db) =>
            await Dto(db, Acteur.Depuis(user), await ChargerModele(db, id)));

        api.MapPost("/modeles-texte", async (ModeleTexteSaisie c, ClaimsPrincipal user, MepDbContext db, TimeProvider horloge) =>
        {
            var acteur = Acteur.Depuis(user);
            if (c.EquipeId is null) throw ErreurHttp.Saisie("L'équipe est obligatoire.");
            var e = await ChargerEquipe(db, c.EquipeId);
            await ExigerMembre(db, acteur, e);
            var (nom, usage, objet, corps) = Valider(c);
            var maintenant = horloge.GetUtcNow();
            var m = new ModeleTexte
            {
                EquipeId = e.Id, Nom = nom, Usage = usage, Objet = objet, Corps = corps, Version = 1,
                CreeLe = maintenant, ModifieLe = maintenant, ModifieParId = acteur.Id,
            };
            db.ModelesTexte.Add(m);
            JournalAdmin.Ecrire(db, acteur, "modele-texte.cree", $"Modèle de texte « {nom} » créé pour l'équipe « {e.Nom} »");
            await db.SaveChangesAsync();
            return Results.Created($"/api/modeles-texte/{m.Id}", await Dto(db, acteur, m));
        });

        api.MapPut("/modeles-texte/{id:guid}", async (Guid id, ModeleTexteSaisie c, ClaimsPrincipal user, MepDbContext db, TimeProvider horloge) =>
        {
            var acteur = Acteur.Depuis(user);
            var m = await ChargerModele(db, id);
            var e = await ChargerEquipe(db, m.EquipeId);
            await ExigerMembre(db, acteur, e);
            if (c.Version is null) throw ErreurHttp.Saisie("La version du modèle est obligatoire.");
            if (c.Version != m.Version)
                throw ErreurHttp.Conflit("Le modèle a été modifié par quelqu'un d'autre depuis votre dernière lecture. Rechargez-le puis recommencez.");
            if (c.EquipeId is { } autre && autre != m.EquipeId) throw ErreurHttp.Saisie("L'équipe d'un modèle ne change pas : dupliquez-le dans l'autre équipe.");
            var (nom, usage, objet, corps) = Valider(c);
            if (nom == m.Nom && usage == m.Usage && objet == m.Objet && corps == m.Corps) return Results.Ok(await Dto(db, acteur, m));
            var ancien = m.Nom;
            (m.Nom, m.Usage, m.Objet, m.Corps) = (nom, usage, objet, corps);
            m.Version++;
            m.ModifieLe = horloge.GetUtcNow();
            m.ModifieParId = acteur.Id;
            JournalAdmin.Ecrire(db, acteur, "modele-texte.modifie",
                $"Modèle de texte « {ancien} » de l'équipe « {e.Nom} » modifié{(ancien != nom ? $", renommé « {nom} »" : "")}");
            await db.SaveChangesAsync();
            return Results.Ok(await Dto(db, acteur, m));
        });

        api.MapPost("/modeles-texte/{id:guid}/retablir", async (Guid id, ClaimsPrincipal user, MepDbContext db, TimeProvider horloge) =>
        {
            var acteur = Acteur.Depuis(user);
            var m = await ChargerModele(db, id);
            var e = await ChargerEquipe(db, m.EquipeId);
            await ExigerMembre(db, acteur, e);
            ModelesTexteFournis.Retablir(m);
            m.Version++;
            m.ModifieLe = horloge.GetUtcNow();
            m.ModifieParId = acteur.Id;
            JournalAdmin.Ecrire(db, acteur, "modele-texte.retabli", $"Modèle de texte « {m.Nom} » de l'équipe « {e.Nom} » : texte fourni rétabli");
            await db.SaveChangesAsync();
            return Results.Ok(await Dto(db, acteur, m));
        });

        api.MapDelete("/modeles-texte/{id:guid}", async (Guid id, ClaimsPrincipal user, MepDbContext db) =>
        {
            var acteur = Acteur.Depuis(user);
            var m = await ChargerModele(db, id);
            var e = await ChargerEquipe(db, m.EquipeId);
            await ExigerMembre(db, acteur, e);
            if (m.CleFournie is not null)
                throw new RegleMetierException("Un modèle fourni ne se supprime pas : modifiez-le, ou rétablissez son texte.");
            db.ModelesTexte.Remove(m);
            JournalAdmin.Ecrire(db, acteur, "modele-texte.supprime", $"Modèle de texte « {m.Nom} » de l'équipe « {e.Nom} » supprimé");
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }
}
