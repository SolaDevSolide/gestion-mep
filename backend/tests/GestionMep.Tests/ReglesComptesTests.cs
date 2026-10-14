using GestionMep.Domain;

namespace GestionMep.Tests;

/// <summary>Règles pures : comptes reliés d'une même personne, annuaire des personnes et noms d'équipe.</summary>
public class ReglesComptesTests
{
    private static Utilisateur Compte(string login, Guid? principal = null) =>
        new() { Login = login, NomAffiche = login.ToUpperInvariant(), ComptePrincipalId = principal };

    [Fact]
    public void Relier_ne_fait_qu_un_niveau_et_refuse_soi_meme()
    {
        var jean = Compte("jean");
        var sso = Compte("jean.sso");
        var autre = Compte("autre");
        Regles.Relier(sso, jean, [jean, sso, autre]);
        Assert.Equal(jean.Id, sso.ComptePrincipalId);

        Assert.Throws<RegleMetierException>(() => Regles.Relier(sso, jean, [jean, sso]));
        Assert.Throws<RegleMetierException>(() => Regles.Relier(autre, sso, [jean, sso, autre]));
        var e = Assert.Throws<RegleMetierException>(() => Regles.Relier(jean, autre, [jean, sso, autre]));
        Assert.Contains("compte principal de jean.sso", e.Message);
        Assert.Throws<RegleMetierException>(() => Regles.Relier(autre, autre, [autre]));

        Regles.Delier(sso);
        Assert.Null(sso.ComptePrincipalId);
        Assert.Throws<RegleMetierException>(() => Regles.Delier(sso));
    }

    [Fact]
    public void L_annuaire_rassemble_les_comptes_relies_en_une_personne()
    {
        var jean = Compte("jean");
        var sso = Compte("jean.sso", jean.Id);
        var seul = Compte("seul");
        var equipeA = new Equipe { Nom = "A", MembreIds = [sso.Id] };
        var equipeB = new Equipe { Nom = "B", MembreIds = [seul.Id, jean.Id] };
        var annuaire = AnnuairePersonnes.De([jean, sso, seul], [equipeA, equipeB]);

        Assert.Equal(jean.Id, annuaire.Personne(sso.Id));
        Assert.Equal("JEAN", annuaire.NomPersonne(sso.Id));
        Assert.Equal("SEUL", annuaire.NomPersonne(seul.Id));
        Assert.Equal([jean.Id, sso.Id], annuaire.ComptesDeLaPersonne(sso.Id).Select(c => c.Id));
        Assert.Equal(new HashSet<Guid> { equipeA.Id, equipeB.Id }, annuaire.EquipesDeLaPersonne(jean.Id));
        Assert.Equal([equipeA.Id], annuaire.EquipesDuCompte(sso.Id));
        Assert.Equal(["JEAN", "jean", "JEAN.SSO", "jean.sso"], annuaire.TermesDeRecherche(sso.Id));
        var inconnu = Guid.NewGuid();
        Assert.Equal(inconnu, annuaire.Personne(inconnu));
        Assert.Null(annuaire.NomPersonne(inconnu));
    }

    [Fact]
    public void Un_nom_d_equipe_est_obligatoire_et_unique_sans_tenir_compte_de_la_casse()
    {
        var existantes = new[] { new Equipe { Nom = "Exploitation" } };
        Assert.Equal("Réseau", Regles.NomEquipe("  Réseau ", existantes));
        Assert.Throws<RegleMetierException>(() => Regles.NomEquipe("EXPLOITATION", existantes));
        Assert.Throws<RegleMetierException>(() => Regles.NomEquipe(" ", existantes));
        Assert.Throws<RegleMetierException>(() => Regles.NomEquipe(new string('x', Regles.LongueurMaxNomEquipe + 1), existantes));
    }

    [Fact]
    public void Le_pilote_ne_peut_pas_approuver_avec_un_compte_relie()
    {
        var jean = Compte("jean");
        var sso = Compte("jean.sso", jean.Id);
        var claire = Compte("claire");
        var annuaire = AnnuairePersonnes.De([jean, sso, claire], []);
        var mep = new Mep();
        var pilote = new Participant { MepId = mep.Id, Nom = "Jean", UtilisateurId = jean.Id };
        mep.Participants.Add(pilote);
        mep.PiloteId = pilote.Id;

        Regles.ExigerApprobateursDistincts(mep, [claire.Id], annuaire);
        var e = Assert.Throws<RegleMetierException>(() => Regles.ExigerApprobateursDistincts(mep, [sso.Id], annuaire));
        Assert.Contains("JEAN est le pilote", e.Message);
        mep.PiloteId = null;
        Assert.Throws<RegleMetierException>(() => Regles.ExigerApprobateursDistincts(mep, [jean.Id, sso.Id], annuaire));
    }
}
