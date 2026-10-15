using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Services;

/// <summary>
/// Mode démonstration : « Simuler des collègues ». Bob et Claire (comptes de démonstration) travaillent en vrai sur la MEP
/// « Facturation électronique » : ils l'ouvrent (présence), entrent dans une section, ouvrent une étape (verrou d'édition),
/// modifient un texte, ajoutent puis déplacent une étape, prennent la main sur l'étape d'une autre personne… Chaque action
/// passe par les mêmes règles, le même journal et le même temps réel que dans l'interface. La simulation s'arrête seule après
/// <see cref="Duree"/>, ou à la demande ; elle n'existe qu'en démonstration et ne touche aucune autre MEP.
/// </summary>
public sealed class SimulationDemo(IServiceScopeFactory scopes, DiffuseurMep diffuseur, IConfiguration config, TimeProvider temps,
    ILogger<SimulationDemo> journal) : IDisposable
{
    /// <summary>Intervalle entre deux actions des collègues (réglage <c>Demo:SimulationPasMs</c>, 3,5 s par défaut).</summary>
    public TimeSpan Pas => TimeSpan.FromMilliseconds(Math.Max(10, config.GetValue("Demo:SimulationPasMs", 3500)));

    /// <summary>Durée avant l'arrêt automatique (réglage <c>Demo:SimulationDureeS</c>, 3 minutes par défaut).</summary>
    public TimeSpan Duree => TimeSpan.FromSeconds(Math.Max(1, config.GetValue("Demo:SimulationDureeS", 180)));

    /// <summary>Titre de l'étape qu'ajoute Bob (retirée à la fin de chaque tour : la démonstration reste propre).</summary>
    public const string TitreEtapeSimulee = "Contrôle de l'export (ajoutée par la simulation)";

    private readonly object _verrou = new();
    private CancellationTokenSource? _arret;
    private Task? _tache;
    private DateTimeOffset? _jusqua;
    private Guid? _mepId;
    private string? _lanceePar;

    public EtatSimulationDto Etat()
    {
        lock (_verrou)
            return new EtatSimulationDto(_tache is { IsCompleted: false }, _jusqua, _mepId, DonneesDemo.OutilIncomplete, ["Bob", "Claire"], _lanceePar);
    }

    /// <summary>Lance la simulation (sans effet si elle tourne déjà). 422 si la MEP de démonstration n'est plus en préparation.</summary>
    public async Task<EtatSimulationDto> Demarrer(Acteur par)
    {
        lock (_verrou)
            if (_tache is { IsCompleted: false }) return Etat();
        Guid mepId;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
            var mep = await db.Meps.AsNoTracking().Where(m => m.Outil == DonneesDemo.OutilIncomplete && !m.EstModele)
                .Select(m => new { m.Id, m.Phase }).FirstOrDefaultAsync()
                ?? throw ErreurHttp.NonTrouve($"MEP « {DonneesDemo.OutilIncomplete} » introuvable : réinitialisez la démonstration.");
            if (mep.Phase != Phase.Preparation)
                throw new RegleMetierException($"La MEP « {DonneesDemo.OutilIncomplete} » n'est plus en préparation : réinitialisez la démonstration.");
            mepId = mep.Id;
        }
        lock (_verrou)
        {
            if (_tache is { IsCompleted: false }) return Etat();
            _arret = new CancellationTokenSource(Duree);
            _jusqua = temps.GetUtcNow() + Duree;
            _mepId = mepId;
            _lanceePar = par.Nom;
            var jeton = _arret.Token;
            _tache = Task.Run(() => Jouer(mepId, jeton));
        }
        journal.LogInformation("Simulation de collègues lancée par {Nom} sur {Outil}.", par.Nom, DonneesDemo.OutilIncomplete);
        return Etat();
    }

    /// <summary>Arrête la simulation et attend que les collègues aient quitté la MEP (présence et verrous retirés).</summary>
    public async Task<EtatSimulationDto> Arreter()
    {
        Task? tache;
        lock (_verrou)
        {
            _arret?.Cancel();
            tache = _tache;
        }
        if (tache is not null)
            try { await tache; }
            catch (Exception ex) when (ex is OperationCanceledException) { /* arrêt demandé */ }
        return Etat();
    }

    public void Dispose()
    {
        lock (_verrou) _arret?.Cancel();
    }

    /// <summary>Un collègue simulé : son compte, son onglet (flux de présence sans connexion) et l'étape qu'il a ouverte.</summary>
    private sealed class Collegue(Acteur acteur, DiffuseurMep.Abonnement abonnement) : IDisposable
    {
        public Acteur Acteur { get; } = acteur;
        public string Session => abonnement.Session;
        public Guid? Ouverte { get; set; }
        public void Dispose() => abonnement.Dispose();
    }

    private async Task Jouer(Guid mepId, CancellationToken ct)
    {
        var collegues = new List<Collegue>();
        try
        {
            Acteur? bobActeur, claireActeur;
            using (var scope = scopes.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
                var comptes = await db.Utilisateurs.AsNoTracking().Where(u => u.Login == "bob" || u.Login == "claire").ToListAsync(ct);
                bobActeur = comptes.FirstOrDefault(u => u.Login == "bob") is { } b ? new Acteur(b.Id, b.NomAffiche, b.EstAdmin) : null;
                claireActeur = comptes.FirstOrDefault(u => u.Login == "claire") is { } c ? new Acteur(c.Id, c.NomAffiche, c.EstAdmin) : null;
            }
            if (bobActeur is null || claireActeur is null) return;
            Collegue Ouvrir(Acteur a) =>
                new(a, diffuseur.Abonner(mepId, a.Id, a.Nom, $"simulation-{a.Id:N}"[..30]) ?? throw new InvalidOperationException("Flux saturés."));
            var bob = Ouvrir(bobActeur);
            collegues.Add(bob);
            await Attendre(ct);
            var claire = Ouvrir(claireActeur);
            collegues.Add(claire);

            var tour = 0;
            while (!ct.IsCancellationRequested)
            {
                tour++;
                foreach (var action in Scenario(mepId, bob, claire, tour))
                {
                    await Attendre(ct);
                    try { await action(); }
                    catch (Exception ex) when (ex is RegleMetierException or ErreurHttp or DbUpdateException)
                    {
                        // Une règle refuse (quelqu'un a changé le plan entre-temps) : le collègue passe à la suite, comme une personne.
                        journal.LogInformation("Simulation : action refusée ({Message}).", ex.Message);
                    }
                }
            }
        }
        catch (OperationCanceledException) { /* arrêt demandé ou durée atteinte */ }
        catch (Exception ex)
        {
            journal.LogWarning(ex, "Simulation de collègues interrompue.");
        }
        finally
        {
            foreach (var c in collegues)
            {
                if (c.Ouverte is { } e) diffuseur.RelacherVerrou(mepId, e, c.Acteur.Id, c.Session);
                c.Dispose();
            }
            // Ce que Bob a ajouté pendant le tour en cours est retiré : la démonstration reste propre.
            try { await RetirerEtapesSimulees(mepId, collegues.FirstOrDefault()?.Acteur); }
            catch (Exception ex) { journal.LogWarning(ex, "Simulation : nettoyage incomplet."); }
            lock (_verrou) _jusqua = null;
        }
    }

    private Task Attendre(CancellationToken ct) => Task.Delay(Pas, temps, ct);

    /// <summary>Un tour de simulation : une suite d'actions lisibles, à <see cref="Pas"/> d'intervalle.</summary>
    private IEnumerable<Func<Task>> Scenario(Guid mepId, Collegue bob, Collegue claire, int tour)
    {
        // Bob entre dans « Export et téléchargement », Claire dans « Tables factures ».
        yield return async () => await Aller(mepId, bob, await Section(mepId, "Export"), null);
        yield return async () => await Aller(mepId, claire, await Section(mepId, "Tables"), null);
        // Bob ouvre la première étape de sa section (verrou d'édition), Claire la dernière de la sienne.
        yield return () => OuvrirEtape(mepId, bob, "Export", premiere: true);
        yield return () => OuvrirEtape(mepId, claire, "Tables", premiere: false);
        // Bob modifie le résultat attendu de son étape (s'il a toujours la main).
        yield return () => ModifierTexte(mepId, bob, tour);
        // Claire prend la main sur l'étape qu'une autre personne a ouverte, s'il y en a une ; sinon elle modifie la sienne.
        yield return () => PrendreLaMainOuModifier(mepId, claire, tour);
        // Bob ferme son étape et ajoute une étape après elle, puis la déplace en parallèle de la suivante.
        yield return () => FermerEtape(mepId, bob);
        yield return () => AjouterEtape(mepId, bob);
        yield return () => DeplacerEtape(mepId, bob);
        // Claire ferme et rejoint la section de Bob ; Bob retire son étape de passage.
        yield return () => FermerEtape(mepId, claire);
        yield return async () => await Aller(mepId, claire, await Section(mepId, "Export"), null);
        yield return () => RetirerEtapesSimulees(mepId, bob.Acteur);
    }

    // ---------- Actions ----------

    private async Task<Guid?> Section(Guid mepId, string debutTitre) => (await Lire(mepId, debutTitre)).Section?.Id;

    /// <summary>Où se trouve le collègue : une section (vue de la section) et, s'il y en a une, l'étape ouverte.</summary>
    private Task Aller(Guid mepId, Collegue c, Guid? section, Guid? etape)
    {
        var elements = new List<string>();
        if (section is { } s) elements.AddRange([$"ici:section:{s}", $"section:{s}"]);
        if (etape is { } e) elements.AddRange([$"ici:etape:{e}", $"etape:{e}"]);
        diffuseur.DefinirElements(mepId, c.Acteur.Id, c.Session, elements);
        return Task.CompletedTask;
    }

    private async Task<(Mep Mep, Section? Section)> Lire(Guid mepId, string debutTitre)
    {
        using var scope = scopes.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ServiceMep>();
        var mep = await svc.Charger(mepId);
        return (mep, mep.Sections.FirstOrDefault(s => s.Titre.StartsWith(debutTitre, StringComparison.Ordinal)));
    }

    private async Task OuvrirEtape(Guid mepId, Collegue c, string debutTitre, bool premiere)
    {
        var (mep, section) = await Lire(mepId, debutTitre);
        if (section is null) return;
        var etapes = mep.Etapes.Where(e => e.SectionId == section.Id && e.Action != TitreEtapeSimulee).OrderBy(e => e.Code, StringComparer.Ordinal).ToList();
        var e = premiere ? etapes.FirstOrDefault() : etapes.LastOrDefault();
        if (e is null) return;
        // Comme le panneau : verrou si l'étape est libre ; sinon la personne regarde la fiche (sans prendre la main).
        var (pris, _) = diffuseur.PrendreVerrou(mepId, e.Id, c.Acteur.Id, c.Acteur.Nom, c.Session, prendreLaMain: false);
        c.Ouverte = pris ? e.Id : null;
        diffuseur.DefinirElements(mepId, c.Acteur.Id, c.Session,
            pris ? [$"ici:section:{section.Id}", $"ici:etape:{e.Id}", $"etape:{e.Id}"] : [$"ici:section:{section.Id}", $"ici:etape:{e.Id}"]);
    }

    private async Task FermerEtape(Guid mepId, Collegue c)
    {
        if (c.Ouverte is { } e)
        {
            diffuseur.RelacherVerrou(mepId, e, c.Acteur.Id, c.Session);
            var (mep, _) = await Lire(mepId, "");
            var sid = mep.Etapes.FirstOrDefault(x => x.Id == e)?.SectionId;
            await Aller(mepId, c, sid, null);
        }
        c.Ouverte = null;
    }

    /// <summary>Le collègue a-t-il toujours la main sur son étape ? (quelqu'un a pu la prendre entre-temps)</summary>
    private bool AToujoursLaMain(Guid mepId, Collegue c) =>
        c.Ouverte is { } e && diffuseur.Verrous(mepId).Any(v => v.EtapeId == e && v.UtilisateurId == c.Acteur.Id && v.Session == c.Session);

    private async Task ModifierTexte(Guid mepId, Collegue c, int tour)
    {
        if (!AToujoursLaMain(mepId, c)) return;
        var id = c.Ouverte!.Value;
        await Ecrire(mepId, c.Acteur, (mep, svc) =>
        {
            var e = ServiceMep.TrouverEtape(mep, id);
            var mention = $"(relu par {c.Acteur.Nom}, tour {tour})";
            var base_ = System.Text.RegularExpressions.Regex.Replace(e.ResultatAttendu, @"\s*\(relu par [^)]*\)$", "");
            e.ResultatAttendu = $"{base_} {mention}".Trim();
            e.Revision++;
            return svc.EnregistrerContenu(mep, c.Acteur, "etape.modifiee", $"{e.Code} modifiée");
        });
    }

    private async Task PrendreLaMainOuModifier(Guid mepId, Collegue c, int tour)
    {
        var autre = diffuseur.Verrous(mepId).FirstOrDefault(v => !v.Session.StartsWith("simulation-", StringComparison.Ordinal));
        if (autre is not null)
        {
            if (c.Ouverte is { } avant) diffuseur.RelacherVerrou(mepId, avant, c.Acteur.Id, c.Session);
            diffuseur.PrendreVerrou(mepId, autre.EtapeId, c.Acteur.Id, c.Acteur.Nom, c.Session, prendreLaMain: true);
            c.Ouverte = autre.EtapeId;
            var (mep, _) = await Lire(mepId, "");
            var sid = mep.Etapes.FirstOrDefault(x => x.Id == autre.EtapeId)?.SectionId;
            var elements = new List<string> { $"ici:etape:{autre.EtapeId}", $"etape:{autre.EtapeId}" };
            if (sid is { } s) elements.Insert(0, $"ici:section:{s}");
            diffuseur.DefinirElements(mepId, c.Acteur.Id, c.Session, elements);
            return;
        }
        await ModifierTexte(mepId, c, tour);
    }

    private async Task AjouterEtape(Guid mepId, Collegue c)
    {
        var (lu, section) = await Lire(mepId, "Export");
        if (section is null || lu.Etapes.Any(e => e.Action == TitreEtapeSimulee)) return;
        await Ecrire(mepId, c.Acteur, (mep, svc) =>
        {
            var apres = mep.Etapes.Where(e => e.SectionId == section.Id && e.Action != TitreEtapeSimulee).OrderBy(e => e.Code, StringComparer.Ordinal).FirstOrDefault();
            var etape = new Etape
            {
                MepId = mep.Id,
                Plan = TypePlan.Deploiement,
                Code = Regles.ProchainCode(mep.Etapes, TypePlan.Deploiement),
                Criticite = Criticite.Faible,
                Action = TitreEtapeSimulee,
                Instructions = "SIMULATION : étape de passage, retirée par Bob à la fin du tour.",
                SystemeIds = [.. section.SystemeIds.Take(1)],
            };
            Regles.AppliquerTypeAEtape(mep, etape);
            Regles.AjouterEtape(mep, etape, apres is null ? new Regles.PlaceEtape() : new Regles.PlaceEtape(Apres: apres.Id), section.Id);
            return svc.EnregistrerContenu(mep, c.Acteur, "etape.creee", $"{etape.Code} créée dans la section « {section.Titre} »");
        });
        var (apresAjout, _) = await Lire(mepId, "");
        var nouvelle = apresAjout.Etapes.FirstOrDefault(e => e.Action == TitreEtapeSimulee);
        if (nouvelle is not null) await Aller(mepId, c, section.Id, nouvelle.Id);
    }

    private async Task DeplacerEtape(Guid mepId, Collegue c)
    {
        var (lu, section) = await Lire(mepId, "Export");
        var simulee = lu.Etapes.FirstOrDefault(e => e.Action == TitreEtapeSimulee);
        var voisine = section is null ? null
            : lu.Etapes.Where(e => e.SectionId == section.Id && e.Action != TitreEtapeSimulee).OrderBy(e => e.Code, StringComparer.Ordinal).LastOrDefault();
        if (simulee is null || voisine is null) return;
        await Ecrire(mepId, c.Acteur, (mep, svc) =>
        {
            var e = ServiceMep.TrouverEtape(mep, simulee.Id);
            Regles.DeplacerEtape(mep, e, new Regles.PlaceEtape(ParalleleA: voisine.Id));
            return svc.EnregistrerContenu(mep, c.Acteur, "etape.deplacee", $"{e.Code} déplacée");
        });
    }

    /// <summary>Retire les étapes ajoutées par la simulation (ce qui les attendait attend ce qu'elles attendaient).</summary>
    private async Task RetirerEtapesSimulees(Guid mepId, Acteur? acteur)
    {
        if (acteur is null) return;
        var (lu, _) = await Lire(mepId, "");
        if (!lu.Etapes.Any(e => e.Action == TitreEtapeSimulee)) return;
        using var scope = scopes.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ServiceMep>();
        var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
        await svc.Modifier(mepId, async mep =>
        {
            var evenements = new List<ServiceMep.Evenement>();
            foreach (var e in mep.Etapes.Where(e => e.Action == TitreEtapeSimulee).ToList())
            {
                Regles.RetirerEtape(mep, e);
                db.Etapes.Remove(e);
                evenements.Add(new("etape.supprimee", $"{e.Code} supprimée"));
            }
            return await svc.EnregistrerContenu(mep, acteur, evenements);
        });
    }

    private async Task Ecrire(Guid mepId, Acteur acteur, Func<Mep, ServiceMep, Task<MepDto>> action)
    {
        using var scope = scopes.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ServiceMep>();
        await svc.Modifier(mepId, mep =>
        {
            ServiceMep.ExigerEditeur(mep, acteur);
            Regles.ExigerPreparation(mep);
            return action(mep, svc);
        });
    }
}
