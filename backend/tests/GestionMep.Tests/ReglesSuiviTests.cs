using GestionMep.Api.Services;
using GestionMep.Domain;

namespace GestionMep.Tests;

/// <summary>Suivi d'une MEP lancée (#61) : fin estimée et retard, avancement, synthèse et points bloquants du rapport de fin.</summary>
public class ReglesSuiviTests
{
    private static readonly DateTimeOffset Go = new(2026, 10, 16, 13, 0, 0, TimeSpan.Zero);

    private static Etape Etape(string code, int? duree, EtatEtape etat = EtatEtape.AFaire, DateTimeOffset? debut = null, params Etape[] avant) => new()
    {
        Code = code, Action = $"Action {code}", Plan = TypePlan.Deploiement, DureeEstimeeMinutes = duree, Etat = etat, DebutReel = debut,
        Dependances = [.. avant.Select(e => e.Id)],
    };

    /// <summary>
    /// A (20 min, réussie) puis, en parallèle, B (30 min, en cours depuis 40 min : dépasse), C (15 min, en échec) et D (10 min,
    /// à faire après C) ; E (10 min) attend B et D. Plan prévu : A + max(B, C + D) + E = 60 min.
    /// </summary>
    private static Mep MepEnCours(out Etape a, out Etape b, out Etape c, out Etape d, out Etape e)
    {
        a = Etape("E0001", 20, EtatEtape.Reussie, Go);
        a.FinReelle = Go.AddMinutes(20);
        b = Etape("E0002", 30, EtatEtape.EnCours, Go.AddMinutes(20), a);
        c = Etape("E0003", 15, EtatEtape.Echec, Go.AddMinutes(20), a);
        d = Etape("E0004", 10, EtatEtape.AFaire, null, c);
        e = Etape("E0005", 10, EtatEtape.AFaire, null, b, d);
        return new Mep { Phase = Phase.Execution, DemarrageAutoriseLe = Go, Etapes = [a, b, c, d, e] };
    }

    [Fact]
    public void La_fin_estimee_suit_le_plus_long_chemin_des_durees_restantes()
    {
        var mep = MepEnCours(out _, out _, out _, out _, out _);
        var maintenant = Go.AddMinutes(60);
        var f = Regles.EstimerFin(mep, maintenant);
        Assert.Equal(TypePlan.Deploiement, f.Plan);
        Assert.Equal(60, f.PrevuMinutes);
        Assert.Equal(Go.AddMinutes(60), f.FinPrevue);
        // Reste : B dépasse (5 min), C en échec (15 + 10), D (10), puis E (10) : max(5, 25 + 10) + 10 = 45 min.
        Assert.Equal(maintenant.AddMinutes(45), f.FinEstimee);
        Assert.Equal(45, f.RetardMinutes);
        Assert.False(f.Terminee);
        Assert.False(f.SansDuree);
        Assert.Equal("1/5 étapes faites (20 %)", Regles.Avancement(mep));
    }

    [Fact]
    public void Etape_en_cours_dans_les_temps_compte_son_reste_et_la_fin_d_un_plan_termine_est_reelle()
    {
        var mep = MepEnCours(out _, out var b, out var c, out var d, out var e);
        foreach (var x in new[] { c, d, e })
        {
            x.Etat = EtatEtape.Reussie;
            x.FinReelle = Go.AddMinutes(45);
        }
        // B a commencé il y a 10 min sur 30 : il reste 20 min.
        Assert.Equal(Go.AddMinutes(50), Regles.EstimerFin(mep, Go.AddMinutes(30)).FinEstimee);
        Assert.Equal(20, Regles.TempsRestant(b, Go.AddMinutes(30)));
        Assert.Equal(Regles.SurcoutDepassementMinutes, Regles.TempsRestant(b, Go.AddMinutes(80)));

        b.Etat = EtatEtape.Reussie;
        b.FinReelle = Go.AddMinutes(72);
        var fin = Regles.EstimerFin(mep, Go.AddMinutes(200));
        Assert.True(fin.Terminee);
        Assert.Equal(Go.AddMinutes(72), fin.FinEstimee);
        Assert.Equal(12, fin.RetardMinutes);
    }

    [Fact]
    public void Ni_fin_estimee_avant_le_Go_ni_pour_une_mep_sans_durees()
    {
        var mep = MepEnCours(out _, out _, out _, out _, out _);
        mep.DemarrageAutoriseLe = null;
        var avant = Regles.EstimerFin(mep, Go);
        Assert.Null(avant.FinEstimee);
        Assert.Null(avant.FinPrevue);
        Assert.Equal(60, avant.PrevuMinutes);

        var simplifiee = MepEnCours(out _, out _, out _, out _, out _);
        foreach (var x in simplifiee.Etapes) x.DureeEstimeeMinutes = null;
        var f = Regles.EstimerFin(simplifiee, Go.AddMinutes(30));
        Assert.True(f.SansDuree);
        Assert.Null(f.FinEstimee);
        Assert.Equal(0, f.RetardMinutes);
    }

    [Fact]
    public void Le_retour_arriere_se_suit_depuis_son_declenchement()
    {
        var mep = MepEnCours(out _, out _, out _, out _, out _);
        var r1 = Etape("R0001", 15);
        r1.Plan = TypePlan.RetourArriere;
        mep.Etapes.Add(r1);
        mep.Phase = Phase.RetourArriere;
        mep.RetourArriereDeclencheLe = Go.AddMinutes(90);
        var f = Regles.EstimerFin(mep, Go.AddMinutes(95));
        Assert.Equal(TypePlan.RetourArriere, f.Plan);
        Assert.Equal(Go.AddMinutes(105), f.FinPrevue);
        Assert.Equal(Go.AddMinutes(110), f.FinEstimee);
        Assert.Equal("0/1 étape faite (0 %)", Regles.Avancement(mep));
    }

    [Fact]
    public void Le_rapport_tire_les_points_bloquants_du_journal()
    {
        // Gestion des stocks (démonstration) : E0003 en échec, résolution type, puis E0003 refaite et réussie.
        Guid alice = Guid.NewGuid(), bob = Guid.NewGuid(), claire = Guid.NewGuid();
        var maintenant = new DateTimeOffset(2026, 10, 16, 15, 0, 0, TimeSpan.Zero);
        var jeu = DonneesDemo.Creer(alice, bob, claire, maintenant);
        var demo = DonneesDemo.Scenarios(alice, bob, claire, jeu.Systemes, jeu.Resolutions, jeu.Criteres, maintenant)
            .Single(s => s.Mep.Outil == DonneesDemo.OutilReussie);
        long id = 0;
        var journal = demo.Journal.Select(e => new EvenementDto(++id, e.Quand, e.Auteur, e.Type, e.Detail, null, e.EtapeId)).Reverse().ToList();

        var (synthese, points) = RapportsFin.Calculer(demo.Mep, journal);
        var p = Assert.Single(points);
        Assert.Equal(("E0003", "Bob", "API : réponse de test absente", StatutResolution.Reussie), (p.Code, p.Par, p.Resolution, p.StatutResolution));
        Assert.Equal("Erreur fictive : /sante répond 503 après le redémarrage.", p.Motif);
        Assert.Equal(5, p.DureeResolutionMinutes);
        Assert.Equal(8, p.ArretMinutes);
        Assert.NotNull(p.RepriseReussieLe);
        Assert.Equal((5, 5, 1, 1, 1), (synthese.Etapes, synthese.EtapesReussies, synthese.Echecs, synthese.EchecsRepris, synthese.Resolutions));
        Assert.Equal((77, 75), (synthese.DureeReelleMinutes, synthese.DureePrevueMinutes));
        Assert.Equal((4, 4, 0), (synthese.RecetteTests, synthese.RecetteConformes, synthese.RecetteEchecs));
        Assert.False(synthese.RetourArriere);
        // Le bilan de démonstration parle de cet incident.
        Assert.Contains("E0003", demo.Mep.Bilan!.Incidents);
    }
}
