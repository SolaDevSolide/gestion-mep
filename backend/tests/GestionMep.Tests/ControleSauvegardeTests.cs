using GestionMep.Api.Services;

namespace GestionMep.Tests;

/// <summary>
/// Contrôle qui suit la sauvegarde automatique (<see cref="Sauvegardes.Comparer"/>) : une table reprise par une migration
/// dans une autre table n'est pas une perte, une vraie perte reste signalée.
/// </summary>
public class ControleSauvegardeTests
{
    private static Sauvegardes.Releve Releve(params (string Table, long Lignes)[] tables) =>
        new(tables.ToDictionary(t => t.Table, t => t.Lignes), []);

    [Fact]
    public void Les_livrables_repris_dans_les_objets_impactes_ne_sont_pas_une_perte()
    {
        var avant = Releve(("Meps", 3), ("Livrables", 4));
        Assert.Empty(Sauvegardes.Comparer(avant, Releve(("Meps", 3), ("ObjetsImpactes", 4))));
        // Des lignes en plus à l'arrivée ne sont pas un écart.
        Assert.Empty(Sauvegardes.Comparer(avant, Releve(("Meps", 3), ("ObjetsImpactes", 6))));
        Assert.Equal("ObjetsImpactes", Sauvegardes.Reprises["Livrables"]);
    }

    [Fact]
    public void Une_reprise_incomplete_est_signalee_sur_la_table_d_arrivee()
    {
        var ecarts = Sauvegardes.Comparer(Releve(("Meps", 3), ("Livrables", 4)), Releve(("Meps", 3), ("ObjetsImpactes", 3)));
        Assert.Equal(["table ObjetsImpactes : 4 ligne(s) attendue(s) (dont 4 reprise(s) de Livrables), 3 trouvée(s)"], ecarts);
    }

    [Fact]
    public void Les_lignes_reprises_s_ajoutent_a_celles_deja_presentes_dans_la_table_d_arrivee()
    {
        var avant = Releve(("Livrables", 4), ("ObjetsImpactes", 2));
        Assert.Empty(Sauvegardes.Comparer(avant, Releve(("ObjetsImpactes", 6))));
        Assert.Equal(["table ObjetsImpactes : 6 ligne(s) attendue(s) (dont 4 reprise(s) de Livrables), 5 trouvée(s)"],
            Sauvegardes.Comparer(avant, Releve(("ObjetsImpactes", 5))));
    }

    [Fact]
    public void Une_table_disparue_sans_reprise_ou_sans_table_d_arrivee_reste_signalee()
    {
        Assert.Equal(["table Meps disparue (3 ligne(s))"], Sauvegardes.Comparer(Releve(("Meps", 3)), Releve()));
        Assert.Equal(["table Livrables disparue (4 ligne(s))"], Sauvegardes.Comparer(Releve(("Livrables", 4)), Releve(("Meps", 0))));
    }

    [Fact]
    public void Une_table_d_origine_encore_presente_se_controle_seule()
    {
        // Migration pas encore appliquée : les livrables sont toujours là et se comptent dans leur propre table.
        Assert.Equal(["table Livrables : 4 ligne(s) attendue(s), 3 trouvée(s)"],
            Sauvegardes.Comparer(Releve(("Livrables", 4)), Releve(("Livrables", 3), ("ObjetsImpactes", 10))));
    }

    [Fact]
    public void Les_reprises_successives_aboutissent_a_la_premiere_table_presente()
    {
        var reprises = new Dictionary<string, string> { ["A"] = "B", ["B"] = "C", ["C"] = "A" };
        Assert.Empty(Sauvegardes.Comparer(Releve(("A", 2), ("B", 1)), Releve(("C", 3)), reprises: reprises));
        // Boucle de reprises sans table présente : la table reste disparue.
        Assert.Equal(["table A disparue (2 ligne(s))"], Sauvegardes.Comparer(Releve(("A", 2)), Releve(), reprises: reprises));
    }

    [Fact]
    public void Apres_une_importation_l_egalite_s_applique_aussi_a_la_table_d_arrivee()
    {
        Assert.Single(Sauvegardes.Comparer(Releve(("Livrables", 4)), Releve(("ObjetsImpactes", 5)), egalite: true));
        Assert.Empty(Sauvegardes.Comparer(Releve(("Livrables", 4)), Releve(("ObjetsImpactes", 4)), egalite: true));
    }
}
