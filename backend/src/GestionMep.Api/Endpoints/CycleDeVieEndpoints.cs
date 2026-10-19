using System.Globalization;
using System.Security.Claims;
using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Api.Services;
using GestionMep.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Endpoints;

/// <summary>
/// Sorties du cycle de vie d'une MEP (#93) : annuler, reporter et reprendre la préparation, ajouter une étape corrective après
/// le Go, passer le pilotage (pilotage de secours pour un administrateur) et supprimer définitivement une MEP jamais lancée.
/// Les retours de phase (préparation, exécution) et la clôture en échec passent par <see cref="PhaseEndpoints"/>.
/// </summary>
public static class CycleDeVieEndpoints
{
    private static string Pluriel(int n, string singulier, string pluriel) => $"{n} {(n > 1 ? pluriel : singulier)}";

    public static void MapCycleDeVie(this RouteGroupBuilder api)
    {
        // ---------- Annuler, reporter, reprendre ----------
        api.MapPost("/meps/{id:guid}/annuler", async (Guid id, AnnulationSaisie c, ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
            if (!acteur.EstAdmin && !Regles.EstPilote(mep, acteur.Id))
                throw ErreurHttp.Interdit("Seul le pilote, un suppléant ou un administrateur peut annuler la MEP.");
            Regles.Annuler(mep, acteur.Id, acteur.EstAdmin, ServiceMep.Texte(c.Motif), ServiceMep.Texte(c.EtatProduction), svc.Maintenant);
            return await svc.Enregistrer(mep, acteur, "mep.annulee", Regles.DetailAnnulation(mep.Annulation!));
        })));

        api.MapPost("/meps/{id:guid}/reporter", async (Guid id, ReportSaisie c, ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
            if (!Regles.EstPilote(mep, acteur.Id)) throw ErreurHttp.Interdit("Seul le pilote ou un suppléant peut reporter la MEP.");
            Regles.Reporter(mep, acteur.Id, ServiceMep.Texte(c.Motif), c.DateVisee, svc.Maintenant);
            return await svc.Enregistrer(mep, acteur, "mep.reportee", Regles.DetailReport(mep.Report!));
        })));

        api.MapPost("/meps/{id:guid}/reprendre", async (Guid id, ClaimsPrincipal user, ServiceMep svc) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
            if (!Regles.EstPilote(mep, acteur.Id)) throw ErreurHttp.Interdit("Seul le pilote ou un suppléant peut reprendre la préparation.");
            Regles.ReprendrePreparation(mep, acteur.Id);
            return await svc.Enregistrer(mep, acteur, "mep.reprise", "Préparation reprise : report levé");
        })));

        // ---------- Corriger en avant : étape corrective après le Go ----------
        api.MapPost("/meps/{id:guid}/etapes-correctives", async (Guid id, EtapeCorrectiveSaisie c, ClaimsPrincipal user, ServiceMep svc, MepDbContext db) =>
            Results.Ok(await svc.Modifier(id, async mep =>
            {
                var acteur = Acteur.Depuis(user);
                if (!Regles.EstPilote(mep, acteur.Id)) throw ErreurHttp.Interdit("Seul le pilote ou un suppléant peut ajouter une étape corrective.");
                if (c.DureeEstimeeMinutes is < 0) throw ErreurHttp.Saisie("La durée estimée ne peut pas être négative.");
                var etape = new Etape
                {
                    MepId = mep.Id, Plan = TypePlan.Deploiement, Action = ServiceMep.Texte(c.Action), SystemeIds = ServiceMep.ValiderSystemeIds(mep, c.SystemeIds),
                    Instructions = ServiceMep.Texte(c.Instructions), ResultatAttendu = ServiceMep.Texte(c.ResultatAttendu),
                    DureeEstimeeMinutes = c.DureeEstimeeMinutes, Criticite = c.Criticite ?? Criticite.Moderee,
                };
                Regles.AppliquerTypeAEtape(mep, etape);
                Regles.AjouterEtapeCorrective(mep, etape, acteur.Id);
                // Motif du dernier retour en exécution depuis la recette, s'il y en a un : il explique la correction.
                const string marque = "Retour en exécution pour corriger : ";
                var retour = await db.Journal.AsNoTracking().Where(e => e.MepId == mep.Id && e.Type == "phase.execution" && e.Detail.StartsWith(marque))
                    .OrderByDescending(e => e.Id).Select(e => e.Detail).FirstOrDefaultAsync();
                var motif = retour is null ? "" : $" (retour en exécution : {retour[marque.Length..].Split(". Tests à refaire", 2)[0]})";
                return await svc.Enregistrer(mep, acteur, "etape.creee", $"{etape.Code} ajoutée après le Go (étape corrective) : {etape.Action}{motif}");
            })));

        // ---------- Pilotage après le Go : passation (pilote, suppléant) ou secours (administrateur) ----------
        api.MapPost("/meps/{id:guid}/pilotage", async (Guid id, PilotageSaisie c, ClaimsPrincipal user, ServiceMep svc, MepDbContext db) => Results.Ok(await svc.Modifier(id, async mep =>
        {
            var acteur = Acteur.Depuis(user);
            if (!acteur.EstAdmin && !Regles.EstPilote(mep, acteur.Id))
                throw ErreurHttp.Interdit("Seul le pilote, un suppléant ou un administrateur (pilotage de secours) peut changer le pilote.");
            if ((c.ParticipantId is null) == (c.UtilisateurId is null))
                throw ErreurHttp.Saisie("Désigner le nouveau pilote : une personne de la MEP (participantId) ou un compte (utilisateurId).");
            var evenements = new List<ServiceMep.Evenement>();
            Participant nouveau;
            if (c.ParticipantId is { } pid) nouveau = ServiceMep.TrouverParticipant(mep, pid, "Nouveau pilote");
            else
            {
                var compte = await db.Utilisateurs.AsNoTracking().FirstOrDefaultAsync(u => u.Id == c.UtilisateurId)
                    ?? throw ErreurHttp.Saisie("Compte utilisateur inconnu.");
                if (!compte.Actif) throw new RegleMetierException($"Le compte de {compte.NomAffiche} est désactivé : il ne peut pas piloter.");
                // Un compte qui ne fait pas partie de la MEP y est ajouté (créé seulement si le changement aboutit).
                nouveau = mep.Participants.FirstOrDefault(p => p.UtilisateurId == compte.Id)
                    ?? new Participant { MepId = mep.Id, Nom = compte.NomAffiche, Role = compte.Role.Length > 0 ? compte.Role : "Pilote", Contact = compte.Contact, UtilisateurId = compte.Id };
            }
            var ajoute = !mep.Participants.Contains(nouveau);
            var ancien = mep.Participants.FirstOrDefault(p => p.Id == mep.PiloteId)?.Nom;
            if (ajoute) mep.Participants.Add(nouveau);
            bool secours;
            try
            {
                Regles.ChangerPilote(mep, acteur.Id, acteur.EstAdmin, nouveau, c.AncienPiloteSuppleant ?? true, c.SuppleantsGardes, ServiceMep.Texte(c.Motif),
                    svc.Maintenant, out secours);
            }
            catch
            {
                if (ajoute) mep.Participants.Remove(nouveau);
                throw;
            }
            if (ajoute) evenements.Add(new("participant.ajoute", $"Participant ajouté après le Go : {nouveau.Nom}"));
            var motif = ServiceMep.Texte(c.Motif);
            var suppleants = Regles.Suppleants(mep).Select(s => mep.Participants.First(p => p.Id == s).Nom).ToList();
            var suite = suppleants.Count == 0 ? "" : $" Suppléant(s) : {string.Join(", ", suppleants)}.";
            var remplace = ancien is null ? $"{nouveau.Nom} devient pilote" : $"{nouveau.Nom} remplace {ancien} comme pilote";
            if (secours)
            {
                evenements.Add(new("pilotage.secours", $"Pilotage de secours par {acteur.Nom} : {remplace}. Motif : {motif}.{suite}"));
                JournalAdmin.Ecrire(db, acteur, "mep.pilotage-secours",
                    $"MEP « {Regles.NomComplet(mep)} » : pilotage " + (nouveau.UtilisateurId == acteur.Id ? "repris" : $"confié à {nouveau.Nom}")
                    + (ancien is null ? "" : $" à la place de {ancien}") + $". Motif : {motif}");
            }
            else evenements.Add(new("pilotage.passe", $"Pilotage passé : {remplace}. Motif : {motif}.{suite}"));
            return await svc.Enregistrer(mep, acteur, evenements);
        })));

        // ---------- Suppression définitive (administrateur, MEP jamais lancée ou modèle) ----------
        api.MapDelete("/meps/{id:guid}", async (Guid id, [FromBody] SuppressionMepSaisie corps, ClaimsPrincipal user, ServiceMep svc, MepDbContext db,
            StockagePiecesJointes stockage, DiffuseurMep diffuseur) =>
        {
            var acteur = Acteur.Depuis(user);
            // Les fichiers ne sont effacés qu'une fois la suppression enregistrée (l'action peut être rejouée).
            var (pieces, version) = await svc.Modifier(id, async mep =>
            {
                var motif = ServiceMep.Texte(corps.Motif);
                Regles.VerifierSuppression(mep, motif, ServiceMep.Texte(corps.Nom));
                var ids = await db.PiecesJointes.Where(p => p.MepId == id).Select(p => p.Id).ToListAsync();
                var createur = mep.CreeParUtilisateurId is { } cid ? await db.Utilisateurs.AsNoTracking().Where(u => u.Id == cid).Select(u => u.NomAffiche).FirstOrDefaultAsync() : null;
                var etapes = mep.Etapes.Count(e => e.Plan == TypePlan.Deploiement);
                var nature = mep.EstModele ? "Modèle" : "MEP";
                var etat = mep.EstModele ? "" : mep.Phase == Phase.Annulee ? "annulée, jamais lancée, " : "jamais lancée, ";
                JournalAdmin.Ecrire(db, acteur, "mep.supprimee",
                    $"{nature} « {Regles.NomComplet(mep)} » supprimé{(mep.EstModele ? "" : "e")} ({etat}créé{(mep.EstModele ? "" : "e")}"
                    + (createur is null ? "" : $" par {createur}") + $" le {mep.CreeLe.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}, "
                    + $"{Pluriel(etapes, "étape", "étapes")}, {Pluriel(ids.Count, "pièce jointe", "pièces jointes")}). Motif : {motif}");
                db.Meps.Remove(mep);
                await db.SaveChangesAsync(); // journal de la MEP et lignes des pièces jointes partent en cascade
                return (ids, mep.Version);
            });
            foreach (var p in pieces) stockage.Supprimer(p);
            // Les onglets ouverts sur la MEP rechargent, la trouvent supprimée et reviennent à la liste ; le suivi se met à jour.
            diffuseur.Notifier(id, version + 1);
            return Results.NoContent();
        }).ExigerAdmin();
    }
}
