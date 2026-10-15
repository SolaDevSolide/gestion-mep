using GestionMep.Domain;

namespace GestionMep.Tests;

/// <summary>
/// Graphe libre (#76) et sections du plan (#60) : création rapide, insertion sur un lien, parallèle, déplacement, liens des
/// cercles compilés en préalables effectifs, cycles refusés, suppression d'une section ou d'un cercle, vérification du plan,
/// duplication. L'exécution ne lit que <see cref="Etape.Dependances"/>.
/// </summary>
public class GrapheLibreTests
{
    private static readonly Guid Alice = Guid.NewGuid(), Bob = Guid.NewGuid(), Claire = Guid.NewGuid();
    private static readonly DateTimeOffset Maintenant = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    private static Mep Nouvelle()
    {
        var m = new Mep { Outil = "Test" };
        m.Systemes.Add(new Systeme { MepId = m.Id, Nom = "BDD" });
        return m;
    }

    private static Etape Ajouter(Mep m, Regles.PlaceEtape? place = null, Section? section = null, TypePlan plan = TypePlan.Deploiement)
    {
        var e = new Etape { MepId = m.Id, Plan = plan, Code = Regles.ProchainCode(m.Etapes, plan) };
        Regles.AjouterEtape(m, e, place ?? new(), section?.Id);
        return e;
    }

    private static Section Section(Mep m, string titre, bool placee = true) =>
        Regles.CreerSection(m, TypePlan.Deploiement, titre, "", [], Alice, Maintenant, placee);

    private static PortSection Entree(Section s) => s.Ports.Where(p => p.Sens == SensPort.Entree).OrderBy(p => p.Ordre).First();
    private static PortSection Sortie(Section s) => s.Ports.Where(p => p.Sens == SensPort.Sortie).OrderBy(p => p.Ordre).First();
    private static string[] Codes(Mep m, IEnumerable<Guid> ids) => ids.Select(id => m.Etapes.Single(e => e.Id == id).Code).Order().ToArray();
    private static string[] P(Mep m, Etape e) => Codes(m, e.Dependances);
    private static string[] D(Mep m, Etape e) => Codes(m, e.DependancesDerivees);
    private static void Relier(Mep m, Guid de, Guid vers) => Regles.Relier(m, TypePlan.Deploiement, de, vers);

    /// <summary>
    /// A hors section → S (B → C, cercles Début et Fin) → T (D) ; E hors section attend la sortie de S.
    /// </summary>
    private static (Mep M, Etape A, Etape B, Etape C, Etape D, Etape E, Section S, Section T) Plan()
    {
        var m = Nouvelle();
        var a = Ajouter(m);
        var s = Section(m, "Base");
        var b = Ajouter(m, section: s); // relié d'office à Début et à Fin
        var c = Ajouter(m, new(Apres: b.Id)); // dans S (après B), relié d'office à Fin
        Relier(m, a.Id, Entree(s).Id);
        var t = Section(m, "Service");
        var d = Ajouter(m, section: t);
        Relier(m, Sortie(s).Id, Entree(t).Id);
        var e = Ajouter(m);
        Relier(m, Sortie(s).Id, e.Id);
        return (m, a, b, c, d, e, s, t);
    }

    // ---------- Compilation des cercles ----------

    [Fact]
    public void Les_cercles_donnent_les_preables_effectifs_et_seul_l_effectif_compte()
    {
        var (m, a, b, c, d, e, s, t) = Plan();
        Assert.Equal(["Début", "Fin"], s.Ports.Select(p => p.Titre));
        Assert.Equal([b.Id], Entree(s).Etapes);
        Assert.Equal([b.Id, c.Id], Sortie(s).Etapes);
        Assert.Equal([s.Id, s.Id], new[] { b.SectionId!.Value, c.SectionId!.Value });

        Assert.Equal(["E0001"], P(m, b));
        Assert.Equal(["E0001"], D(m, b));
        Assert.Equal(["E0002"], P(m, c));
        Assert.Empty(D(m, c)); // lien direct dans la section
        Assert.Equal(["E0002", "E0003"], P(m, d));
        Assert.Equal(["E0002", "E0003"], D(m, d));
        Assert.Equal(["E0002", "E0003"], P(m, e));
        Assert.Equal([Sortie(s).Id], e.PortsAttendus);
        Assert.Null(Regles.ErreurGraphe(m, TypePlan.Deploiement));
        // Lignes recalculées : profondeur dans le graphe effectif.
        Assert.Equal((0, 1, 2, 3, 3), (a.Ligne, b.Ligne, c.Ligne, d.Ligne, e.Ligne));

        // Un préalable aussi saisi directement reste quand le cercle est délié.
        Relier(m, a.Id, b.Id);
        Assert.Empty(D(m, b));
        Regles.Delier(m, TypePlan.Deploiement, a.Id, Entree(s).Id);
        Assert.Equal(["E0001"], P(m, b));
        // Un préalable qui ne vient que d'un cercle ne se retire pas directement.
        var ex = Assert.Throws<RegleMetierException>(() => Regles.Delier(m, TypePlan.Deploiement, b.Id, d.Id));
        Assert.Equal("E0004 attend E0002 par un cercle de section : délier le cercle.", ex.Message);
    }

    [Fact]
    public void Les_liens_impossibles_sont_refuses()
    {
        var (m, a, b, _, d, e, s, t) = Plan();
        Assert.Throws<RegleMetierException>(() => Relier(m, b.Id, Entree(s).Id)); // étape de la section vers son entrée
        Assert.Throws<RegleMetierException>(() => Relier(m, Entree(s).Id, a.Id)); // entrée vers une étape hors section
        Assert.Throws<RegleMetierException>(() => Relier(m, d.Id, Sortie(s).Id)); // sortie qui attendrait une étape d'ailleurs
        Assert.Throws<RegleMetierException>(() => Relier(m, Sortie(t).Id, d.Id)); // étape qui attendrait sa propre sortie
        Assert.Throws<RegleMetierException>(() => Relier(m, Entree(s).Id, Sortie(s).Id));
        Assert.Throws<RegleMetierException>(() => Relier(m, Sortie(s).Id, Entree(s).Id));
        Assert.Throws<RegleMetierException>(() => Relier(m, e.Id, e.Id));
        Assert.Throws<RegleMetierException>(() => Relier(m, Guid.NewGuid(), e.Id));
    }

    [Fact]
    public void Un_cycle_a_travers_les_cercles_est_refuse_sans_rien_changer()
    {
        var (m, _, b, _, d, _, s, _) = Plan();
        var avant = m.Etapes.ToDictionary(e => e.Code, e => (string.Join(",", P(m, e)), string.Join(",", D(m, e)), e.Revision));
        var sources = Entree(s).Sources.ToList();
        var revision = s.Revision;

        // D (dans T, qui attend la sortie de S) vers l'entrée de S : B attendrait D qui attend B.
        var ex = Assert.Throws<RegleMetierException>(() => Relier(m, d.Id, Entree(s).Id));
        Assert.StartsWith("Opération refusée : elle créerait un cycle", ex.Message);
        Assert.Equal(sources, Entree(s).Sources);
        Assert.Equal(revision, s.Revision);
        foreach (var e in m.Etapes) Assert.Equal(avant[e.Code], (string.Join(",", P(m, e)), string.Join(",", D(m, e)), e.Revision));

        // Cycle direct, et préalable saisi dans la fiche.
        Assert.Throws<RegleMetierException>(() => Relier(m, d.Id, b.Id));
        Assert.Throws<RegleMetierException>(() => Regles.DefinirDependances(m, b, [d.Id]));
        Assert.Throws<RegleMetierException>(() => Regles.DefinirDependances(m, b, [b.Id]));
        foreach (var e in m.Etapes) Assert.Equal(avant[e.Code], (string.Join(",", P(m, e)), string.Join(",", D(m, e)), e.Revision));
    }

    [Fact]
    public void La_fiche_d_une_etape_ne_retire_pas_les_preables_venus_des_cercles()
    {
        var (m, a, b, _, _, e, _, _) = Plan();
        var z = Ajouter(m);
        // Le client renvoie les préalables effectifs, ou seulement les directs : même résultat.
        Regles.DefinirDependances(m, b, [z.Id, a.Id]);
        Assert.Equal(["E0001", "E0006"], P(m, b));
        Assert.Equal(["E0001"], D(m, b));
        Regles.DefinirDependances(m, b, []);
        Assert.Equal(["E0001"], P(m, b));
        Assert.Equal(["E0002", "E0003"], P(m, e));
    }

    // ---------- Création rapide ----------

    [Fact]
    public void Inserer_sur_un_lien_d_etapes_ou_de_cercles()
    {
        var (m, a, b, c, _, _, s, _) = Plan();
        // Sur le lien B → C (dans S) : X est dans S, C attend X qui attend B.
        var x = Ajouter(m, new(EntreDe: b.Id, EntreVers: c.Id));
        Assert.Equal(s.Id, x.SectionId);
        Assert.Equal(["E0002"], P(m, x));
        Assert.Equal(["E0006"], P(m, c));
        Assert.DoesNotContain(x.Id, Sortie(s).Etapes); // déjà relié dans la section : pas de lien d'office vers Fin

        // Sur le lien Début → B : Y attend Début (donc A), B attend Y.
        var y = Ajouter(m, new(EntreDe: Entree(s).Id, EntreVers: b.Id));
        Assert.Equal(s.Id, y.SectionId);
        Assert.Equal([y.Id], Entree(s).Etapes);
        Assert.Equal(["E0001"], P(m, y));
        Assert.Equal(["E0007"], P(m, b));
        Assert.Empty(D(m, b));

        // Sur le lien A → Début (à l'extérieur) : Z hors section.
        var z = Ajouter(m, new(EntreDe: a.Id, EntreVers: Entree(s).Id));
        Assert.Null(z.SectionId);
        Assert.Equal([z.Id], Entree(s).Sources);
        Assert.Equal(["E0008"], P(m, y));

        var ex = Assert.Throws<RegleMetierException>(() => Ajouter(m, new(EntreDe: a.Id, EntreVers: c.Id)));
        Assert.StartsWith("Lien introuvable", ex.Message);
        Assert.Throws<RegleMetierException>(() => Ajouter(m, new(Apres: a.Id, ParalleleA: b.Id)));
        Assert.Throws<RegleMetierException>(() => Ajouter(m, new(EntreDe: a.Id)));
    }

    [Fact]
    public void En_parallele_memes_preables_et_memes_suivants_meme_dans_une_section()
    {
        var (m, _, b, c, d, e, s, _) = Plan();
        var x = Ajouter(m, new(ParalleleA: b.Id));
        Assert.Equal(s.Id, x.SectionId);
        Assert.Contains(x.Id, Entree(s).Etapes);
        Assert.Contains(x.Id, Sortie(s).Etapes);
        Assert.Equal(["E0001"], P(m, x));
        Assert.Equal(["E0002", "E0006"], P(m, c));
        Assert.Equal(["E0002", "E0003", "E0006"], P(m, d));
        Assert.Equal(["E0002", "E0003", "E0006"], P(m, e));
        // Rangée juste à droite de B.
        Assert.Equal((b.Ligne, b.Ordre + 1), (x.Ligne, x.Ordre));
        Assert.Throws<RegleMetierException>(() => Ajouter(m, new(ParalleleA: Entree(s).Id)));
    }

    [Fact]
    public void Une_etape_creee_dans_une_section_se_relie_toute_seule_a_ses_cercles()
    {
        var m = Nouvelle();
        var s = Section(m, "Base");
        var premiere = Ajouter(m, section: s);
        Assert.Equal([premiere.Id], Entree(s).Etapes);
        Assert.Equal([premiere.Id], Sortie(s).Etapes);
        // Après un cercle d'entrée : dans la section, sans autre lien d'entrée.
        var seconde = Ajouter(m, new(Apres: Entree(s).Id));
        Assert.Equal(s.Id, seconde.SectionId);
        Assert.Equal([premiere.Id, seconde.Id], Entree(s).Etapes);
        Assert.Equal([premiere.Id, seconde.Id], Sortie(s).Etapes);
        // Après un cercle de sortie : hors section, elle attend le cercle.
        var apres = Ajouter(m, new(Apres: Sortie(s).Id));
        Assert.Null(apres.SectionId);
        Assert.Equal(["E0001", "E0002"], P(m, apres));
    }

    // ---------- Déplacement et suppression ----------

    [Fact]
    public void Deplacer_une_carte_rebranche_ses_voisins_sans_lien_redondant()
    {
        var m = Nouvelle();
        var a = Ajouter(m);
        var b = Ajouter(m, new(Apres: a.Id));
        var c = Ajouter(m, new(Apres: b.Id));
        var d = Ajouter(m, new(Apres: a.Id));
        Relier(m, d.Id, c.Id); // C attend B et D
        var revisionC = c.Revision;

        // B après C : A → C ; C attendait déjà A par D, aucun lien redondant n'est ajouté.
        Regles.DeplacerEtape(m, b, new(Apres: c.Id));
        Assert.Equal(["E0004"], P(m, c));
        Assert.Equal(["E0003"], P(m, b));
        Assert.Equal(revisionC + 1, c.Revision);
        Assert.Equal((0, 1, 2, 3), (a.Ligne, d.Ligne, c.Ligne, b.Ligne));

        // Sur un lien : B quitte sa place, puis s'insère entre A et D.
        Regles.DeplacerEtape(m, b, new(EntreDe: a.Id, EntreVers: d.Id));
        Assert.Equal(["E0001"], P(m, b));
        Assert.Equal(["E0002"], P(m, d));
        Assert.Throws<RegleMetierException>(() => Regles.DeplacerEtape(m, b, new()));
        Assert.Throws<RegleMetierException>(() => Regles.DeplacerEtape(m, b, new(Apres: b.Id)));
        // Plus de règle « un lien descend toujours » : seul le cycle compte.
        Assert.Throws<RegleMetierException>(() => Regles.DeplacerEtape(m, a, new(Apres: Guid.NewGuid())));
    }

    [Fact]
    public void Entrer_dans_une_section_et_en_sortir()
    {
        var (m, a, b, c, _, e, s, _) = Plan();
        // E (hors section, attend la sortie de S) entre dans S : ses liens partent, elle se relie à Début et à Fin.
        Regles.DeplacerEtape(m, e, new(), sectionId: s.Id);
        Assert.Equal(s.Id, e.SectionId);
        Assert.Empty(e.PortsAttendus);
        Assert.Contains(e.Id, Entree(s).Etapes);
        Assert.Contains(e.Id, Sortie(s).Etapes);
        Assert.Equal(["E0001"], P(m, e));

        // C sort de S : ses liens de cercle disparaissent, et Fin attend ce que C attendait (B, déjà là).
        Regles.DeplacerEtape(m, c, new(), sortirDeSection: true);
        Assert.Null(c.SectionId);
        Assert.DoesNotContain(c.Id, Sortie(s).Etapes);
        Assert.Contains(b.Id, Sortie(s).Etapes);
        Assert.Empty(P(m, c));
        Assert.Throws<RegleMetierException>(() => Regles.DeplacerEtape(m, c, new(), s.Id, sortirDeSection: true));
        Assert.Null(Regles.ErreurGraphe(m, TypePlan.Deploiement));
        _ = a;
    }

    [Fact]
    public void Supprimer_une_etape_la_retire_des_cercles_et_rebranche_ses_suivants()
    {
        var (m, a, b, c, d, e, s, _) = Plan();
        Regles.RetirerEtape(m, b);
        Assert.DoesNotContain(b, m.Etapes);
        // C attend désormais ce que B attendait : le cercle Début (donc A).
        Assert.Equal([c.Id], Entree(s).Etapes);
        Assert.Equal(["E0001"], P(m, c));
        Assert.Equal([c.Id], Sortie(s).Etapes);
        Assert.Equal(["E0003"], P(m, d));
        Assert.Equal(["E0003"], P(m, e));
        _ = a;
    }

    // ---------- Sections et cercles ----------

    [Fact]
    public void Supprimer_une_section_garde_ses_etapes_et_leurs_preables_effectifs()
    {
        var (m, _, b, c, d, e, s, t) = Plan();
        var avant = m.Etapes.ToDictionary(x => x.Code, x => P(m, x));
        Regles.SupprimerSection(m, s);

        Assert.Equal([t], m.Sections);
        Assert.Null(b.SectionId);
        Assert.Null(c.SectionId);
        foreach (var x in m.Etapes) Assert.Equal(avant[x.Code], P(m, x));
        // Les préalables venus de ses cercles sont devenus directs.
        Assert.Empty(D(m, b));
        Assert.Empty(D(m, d));
        Assert.Empty(D(m, e));
        Assert.Empty(e.PortsAttendus);
        Assert.Empty(Entree(t).Sources);
        Assert.Equal(["E0002", "E0003"], P(m, d));
    }

    [Fact]
    public void Supprimer_un_cercle_retire_les_liens_qui_passaient_par_lui()
    {
        var (m, a, b, _, d, e, s, _) = Plan();
        var sortie = Sortie(s);
        Regles.SupprimerPort(m, s, sortie);
        Assert.DoesNotContain(sortie, s.Ports);
        Assert.Empty(P(m, d));
        Assert.Empty(P(m, e));
        Assert.Empty(e.PortsAttendus);
        Assert.Equal(["E0001"], P(m, b));

        // Ajout, titre par défaut, renommage, ordre.
        var s2 = Regles.AjouterPort(m, s, SensPort.Sortie, "", "");
        var s3 = Regles.AjouterPort(m, s, SensPort.Sortie, " Sauvegarde ", "Tables copiées");
        Assert.Equal(("Sortie 1", 0, "Sauvegarde", 1), (s2.Titre, s2.Ordre, s3.Titre, s3.Ordre));
        var revision = s.Revision;
        Regles.OrdonnerPorts(m, s, SensPort.Sortie, [s3.Id, s2.Id]);
        Assert.Equal((1, 0), (s2.Ordre, s3.Ordre));
        Regles.ModifierPort(m, s, s2, "Fin", "");
        Assert.Equal(revision + 2, s.Revision);
        Assert.Throws<RegleMetierException>(() => Regles.OrdonnerPorts(m, s, SensPort.Sortie, [s3.Id]));
        Assert.Throws<RegleMetierException>(() => Regles.ModifierPort(m, s, s2, " ", ""));
        _ = a;
    }

    [Fact]
    public void Une_section_dans_le_bac_bloque_le_go_et_les_cercles_vides_avertissent()
    {
        var m = DonneesDemo.Creer(Alice, Bob, Claire, Maintenant).Mep;
        Regles.Decider(m, Claire, DecisionApprobation.Approuvee, "", Maintenant);
        Assert.Empty(Regles.ErreursPlan(m, Maintenant, 1));
        var s = Regles.CreerSection(m, TypePlan.Deploiement, " Contrôles ", "", [m.Systemes[0].Id], Alice, Maintenant, placee: false);
        Assert.Equal("Contrôles", s.Titre);
        Assert.Equal(["Section « Contrôles » dans le bac : la placer dans le plan ou la supprimer."], Regles.ErreursPlan(m, Maintenant, 1));
        Assert.Contains("Section « Contrôles », cercle d'entrée « Début » : aucune étape n'y est reliée.", Regles.Avertissements(m));
        Assert.Contains("Section « Contrôles », cercle d'entrée « Début » : rien n'y arrive.", Regles.Avertissements(m));
        Assert.Contains("Section « Contrôles », cercle de sortie « Fin » : aucune étape n'y est reliée.", Regles.Avertissements(m));
        Assert.Throws<RegleMetierException>(() => Regles.PasserEnExecution(m, Alice, DonneesDemo.CriteresGoNoGo().Select(c => c.Id).ToList(),
            DonneesDemo.CriteresGoNoGo(), 1, Maintenant));

        Assert.True(Regles.PlacerSection(m, s, true));
        Assert.False(Regles.PlacerSection(m, s, true));
        Assert.Empty(Regles.ErreursPlan(m, Maintenant, 1));
        // Retour arrière désactivé : ses sections ne sont pas vérifiées.
        Regles.CreerSection(m, TypePlan.RetourArriere, "Repli", "", [], Alice, Maintenant, placee: false);
        Assert.Contains("Retour arrière : section « Repli » dans le bac : la placer dans le plan ou la supprimer.", Regles.ErreursPlan(m, Maintenant, 1));
        m.RetourArriereActive = false;
        Assert.DoesNotContain(Regles.ErreursPlan(m, Maintenant, 1), x => x.Contains("Repli", StringComparison.Ordinal));

        Assert.Throws<RegleMetierException>(() => Regles.CreerSection(m, TypePlan.Deploiement, " ", "", [], Alice, Maintenant, true));
        Assert.Throws<RegleMetierException>(() => Regles.CreerSection(m, TypePlan.Deploiement, "X", "", [Guid.NewGuid()], Alice, Maintenant, true));
        Regles.RetirerSystemeDesSections(m, m.Systemes[0].Id);
        Assert.Empty(s.SystemeIds);
    }

    [Fact]
    public void Hors_preparation_le_graphe_et_les_sections_ne_changent_plus()
    {
        var (m, a, b, _, _, _, s, _) = Plan();
        m.Phase = Phase.Execution;
        Assert.Throws<RegleMetierException>(() => Ajouter(m));
        Assert.Throws<RegleMetierException>(() => Regles.DeplacerEtape(m, b, new(Apres: a.Id)));
        Assert.Throws<RegleMetierException>(() => Relier(m, a.Id, b.Id));
        Assert.Throws<RegleMetierException>(() => Regles.SupprimerSection(m, s));
        Assert.Throws<RegleMetierException>(() => Regles.AjouterPort(m, s, SensPort.Entree, "X", ""));
        Assert.Throws<RegleMetierException>(() => Regles.ModifierSection(m, s, "X", "", []));
    }

    [Fact]
    public void Une_mep_sans_section_garde_exactement_ses_liens()
    {
        var m = DonneesDemo.Creer(Alice, Bob, Claire, Maintenant).Mep;
        var avant = m.Etapes.ToDictionary(e => e.Id, e => (e.Dependances.ToList(), e.Ligne, e.Ordre, e.Revision));
        Regles.CompilerSections(m, TypePlan.Deploiement);
        Regles.CompilerSections(m, TypePlan.RetourArriere);
        foreach (var e in m.Etapes)
        {
            Assert.Equal(avant[e.Id].Item1, e.Dependances);
            Assert.Equal((avant[e.Id].Ligne, avant[e.Id].Ordre, avant[e.Id].Revision), (e.Ligne, e.Ordre, e.Revision));
            Assert.Empty(e.DependancesDerivees);
        }
    }

    [Fact]
    public void La_duplication_reprend_les_sections_et_leurs_liens()
    {
        var (m, _, _, _, _, _, s, t) = Plan();
        m.Sections[1].Placee = false;
        var copie = Regles.Dupliquer(m, false, Alice, Maintenant);

        Assert.Equal(["Base", "Service"], copie.Sections.Select(x => x.Titre));
        Assert.Equal([true, false], copie.Sections.Select(x => x.Placee));
        Assert.DoesNotContain(copie.Sections, x => x.Id == s.Id || x.Id == t.Id);
        Assert.DoesNotContain(copie.Sections.SelectMany(x => x.Ports), p => m.Sections.SelectMany(x => x.Ports).Any(q => q.Id == p.Id));
        foreach (var e in m.Etapes)
        {
            var c = copie.Etapes.Single(x => x.Code == e.Code);
            Assert.Equal(P(m, e), P(copie, c));
            Assert.Equal(D(m, e), D(copie, c));
            Assert.Equal(m.Sections.FirstOrDefault(x => x.Id == e.SectionId)?.Titre, copie.Sections.FirstOrDefault(x => x.Id == c.SectionId)?.Titre);
        }
        var cs = copie.Sections[0];
        var ct = copie.Sections[1];
        Assert.Equal([Sortie(cs).Id], Entree(ct).Sources);
        Assert.Equal([copie.Etapes.Single(x => x.Code == "E0001").Id], Entree(cs).Sources);
        Assert.Equal([Sortie(cs).Id], copie.Etapes.Single(x => x.Code == "E0005").PortsAttendus);
        // La copie se compile à l'identique.
        Regles.CompilerSections(copie, TypePlan.Deploiement);
        foreach (var e in m.Etapes) Assert.Equal(P(m, e), P(copie, copie.Etapes.Single(x => x.Code == e.Code)));
    }
}
