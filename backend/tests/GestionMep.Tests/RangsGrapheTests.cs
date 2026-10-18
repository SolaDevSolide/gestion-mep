using GestionMep.Domain;

namespace GestionMep.Tests;

/// <summary>
/// Ordre gauche-droite du graphe libre (#76) : choisi par les personnes (rangs partagés par les étapes et les sections d'un
/// plan), il ne change aucun lien ; une étape posée près d'une carte prend le rang suivant.
/// </summary>
public class RangsGrapheTests
{
    private static Mep Nouvelle() => new() { Outil = "Test" };

    private static Etape Ajouter(Mep m, Regles.PlaceEtape? place = null)
    {
        var e = new Etape { MepId = m.Id, Plan = TypePlan.Deploiement, Code = Regles.ProchainCode(m.Etapes) };
        Regles.AjouterEtape(m, e, place ?? new());
        return e;
    }

    [Fact]
    public void Sans_ordre_choisi_les_rangs_restent_a_zero()
    {
        var m = Nouvelle();
        var a = Ajouter(m);
        var b = Ajouter(m, new(Apres: a.Id));
        var s = Regles.CreerSection(m, TypePlan.Deploiement, "S", "", [], null, DateTimeOffset.UnixEpoch, true);
        Assert.Equal((0, 0, 0), (a.Rang, b.Rang, s.Rang));
        Assert.False(Regles.PlanOrdonne(m, TypePlan.Deploiement));
    }

    [Fact]
    public void Ordonner_donne_les_rangs_dans_l_ordre_et_les_autres_suivent_sans_toucher_aux_liens()
    {
        var m = Nouvelle();
        var a = Ajouter(m);
        var b = Ajouter(m, new(Apres: a.Id));
        var c = Ajouter(m, new(ParalleleA: b.Id));
        var s = Regles.CreerSection(m, TypePlan.Deploiement, "S", "", [], null, DateTimeOffset.UnixEpoch, true);
        var liens = m.Etapes.Select(e => e.Dependances.ToList()).ToList();
        Regles.Ordonner(m, TypePlan.Deploiement, [c.Id, s.Id, b.Id]);
        Assert.Equal((1, 2, 3, 4), (c.Rang, s.Rang, b.Rang, a.Rang));
        Assert.Equal(liens, m.Etapes.Select(e => e.Dependances.ToList()).ToList());
        Assert.Throws<RegleMetierException>(() => Regles.Ordonner(m, TypePlan.Deploiement, [c.Id, c.Id]));
        Assert.Throws<RegleMetierException>(() => Regles.Ordonner(m, TypePlan.Deploiement, [Guid.NewGuid()]));
    }

    [Fact]
    public void Une_etape_posee_pres_d_une_carte_prend_le_rang_suivant_et_decale_le_reste()
    {
        var m = Nouvelle();
        var a = Ajouter(m);
        var b = Ajouter(m, new(Apres: a.Id));
        var c = Ajouter(m, new(ParalleleA: b.Id));
        Regles.Ordonner(m, TypePlan.Deploiement, [a.Id, b.Id, c.Id]);
        var d = Ajouter(m, new(ParalleleA: b.Id));
        Assert.Equal((1, 2, 3, 4), (a.Rang, b.Rang, d.Rang, c.Rang));
        var isolee = Ajouter(m);
        Assert.Equal(5, isolee.Rang);
        // Déplacée sur le lien A → C : juste après C (carte d'arrivée).
        Regles.DeplacerEtape(m, isolee, new(EntreDe: a.Id, EntreVers: c.Id));
        Assert.Equal(c.Rang + 1, isolee.Rang);
        // Duplication : mêmes rangs.
        var copie = Regles.Dupliquer(m, false, Guid.NewGuid(), DateTimeOffset.UnixEpoch);
        Assert.Equal(m.Etapes.Select(e => (e.Code, e.Rang)).Order(), copie.Etapes.Select(e => (e.Code, e.Rang)).Order());
    }
}
