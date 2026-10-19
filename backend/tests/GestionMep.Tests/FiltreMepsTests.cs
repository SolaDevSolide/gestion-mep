using System.Text.Json;
using GestionMep.Api.Services;
using GestionMep.Domain;

namespace GestionMep.Tests;

/// <summary>
/// Recherche avancée (#59), règles pures : filtre de la liste des MEP (mêmes cas que le front,
/// <c>frontend/src/vecteurs-filtre-meps.json</c>), lecture des paramètres, état d'une MEP et rôles des participants.
/// </summary>
public class FiltreMepsTests
{
    private static readonly JsonElement Vecteurs = JsonDocument.Parse(
        File.ReadAllText(Path.Combine(OpenApiTests.RacineDepot(), "frontend", "src", "vecteurs-filtre-meps.json"))).RootElement;

    public static TheoryData<string> Cas() => [.. Vecteurs.GetProperty("cas").EnumerateArray().Select(c => c.GetProperty("nom").GetString()!)];

    private static T Enum<T>(JsonElement e) where T : struct, System.Enum => System.Enum.Parse<T>(e.GetString()!);

    private static List<string> Textes(JsonElement e) => [.. e.EnumerateArray().Select(x => x.GetString()!)];

    /// <summary>MEP des vecteurs (identifiants « m1 »… changés en Guid) ; les champs absents des vecteurs ne servent pas au filtre.</summary>
    private static (List<MepResumeDto> Meps, Dictionary<Guid, string> Noms) Meps()
    {
        var noms = new Dictionary<Guid, string>();
        var meps = Vecteurs.GetProperty("meps").EnumerateArray().Select(m =>
        {
            var id = Guid.NewGuid();
            noms[id] = m.GetProperty("id").GetString()!;
            var phase = Enum<Phase>(m.GetProperty("phase"));
            var etat = Enum<EtatMep>(m.GetProperty("etat"));
            return new MepResumeDto(id, m.GetProperty("outil").GetString()!, m.GetProperty("versionCible").GetString()!, m.GetProperty("objet").GetString()!,
                null, phase, null, m.GetProperty("pilote").GetString(), 0, 0, 0, false, false, m.GetProperty("creeLe").GetDateTimeOffset(),
                etat == EtatMep.Repliee ? IssueMep.Repliee : etat == EtatMep.Echec ? IssueMep.Echec : phase == Phase.Cloturee ? IssueMep.Reussie : null, false,
                [.. m.GetProperty("equipeIds").EnumerateArray().Select(e => e.GetGuid())], Textes(m.GetProperty("personnes")),
                Enum<TypeMep>(m.GetProperty("type")), etat, DateOnly.Parse(m.GetProperty("jour").GetString()!, System.Globalization.CultureInfo.InvariantCulture),
                null, null, m.GetProperty("echecsSurvenus").GetInt32(),
                [.. m.GetProperty("systemes").EnumerateArray().Select(s => new SystemeResumeDto(
                    s.GetProperty("catalogueId").ValueKind == JsonValueKind.Null ? null : s.GetProperty("catalogueId").GetGuid(), s.GetProperty("nom").GetString()!,
                    [.. s.GetProperty("environnements").EnumerateArray().Select(e => new EnvironnementSystemeDto(e.GetProperty("nom").GetString()!, e.GetProperty("production").GetBoolean()))],
                    s.GetProperty("echecs").GetInt32()))],
                [.. m.GetProperty("intervenants").EnumerateArray().Select(i => new IntervenantDto(i.GetProperty("personneId").GetGuid(), i.GetProperty("nom").GetString()!,
                    [.. i.GetProperty("roles").EnumerateArray().Select(Enum<RoleMep>)]))], null, null, 0);
        }).ToList();
        return (meps, noms);
    }

    private static string? Param(JsonElement p, string nom) => p.TryGetProperty(nom, out var v) ? v.GetString() : null;

    [Theory]
    [MemberData(nameof(Cas))]
    public void Le_serveur_filtre_comme_la_page(string nom)
    {
        var cas = Vecteurs.GetProperty("cas").EnumerateArray().Single(c => c.GetProperty("nom").GetString() == nom);
        var p = cas.GetProperty("parametres");
        var filtre = FiltreMeps.Lire(new ParametresFiltreMeps(Param(p, "recherche"), Param(p, "systeme"), Param(p, "environnement"), Param(p, "equipe"),
            Param(p, "personne"), Param(p, "role"), Param(p, "etat"), Param(p, "phase"), Param(p, "type"), Param(p, "du"), Param(p, "au"), Param(p, "echec")));
        var (meps, noms) = Meps();
        Assert.Equal(Textes(cas.GetProperty("attendus")), filtre.Appliquer(meps).Select(m => noms[m.Id]));
    }

    [Theory]
    [InlineData("systeme", "BDD", "Système « BDD » invalide")]
    [InlineData("equipe", "dev", "Équipe « dev » invalide")]
    [InlineData("personne", "alice", "Personne « alice » invalide")]
    [InlineData("role", "chef", "Rôle « chef » inconnu : pilote, suppleant, responsable, executant, approbateur.")]
    [InlineData("etat", "abandonnee", "État « abandonnee » inconnu")]
    [InlineData("du", "18/10/2026", "format AAAA-MM-JJ")]
    [InlineData("echec", "peut-etre", "« echec » attend 1 ou 0.")]
    [InlineData("phase", "demain", "Phase inconnue")]
    public void Une_valeur_inconnue_est_une_erreur_de_saisie(string parametre, string valeur, string message)
    {
        var p = parametre switch
        {
            "systeme" => new ParametresFiltreMeps(Systeme: valeur),
            "equipe" => new ParametresFiltreMeps(Equipe: valeur),
            "personne" => new ParametresFiltreMeps(Personne: valeur),
            "role" => new ParametresFiltreMeps(Role: valeur),
            "etat" => new ParametresFiltreMeps(Etat: valeur),
            "du" => new ParametresFiltreMeps(Du: valeur),
            "echec" => new ParametresFiltreMeps(Echec: valeur),
            _ => new ParametresFiltreMeps(Phase: valeur),
        };
        var e = Assert.Throws<ErreurHttp>(() => FiltreMeps.Lire(p));
        Assert.Equal(400, e.Statut);
        Assert.Contains(message, e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Une_periode_inversee_est_refusee() =>
        Assert.Contains("« du » est après « au »", Assert.Throws<ErreurHttp>(() => FiltreMeps.Lire(new ParametresFiltreMeps(Du: "2026-10-02", Au: "2026-10-01"))).Message,
            StringComparison.Ordinal);

    [Fact]
    public void Les_filtres_se_decrivent_pour_l_export()
    {
        var bdd = Guid.NewGuid();
        var equipe = Guid.NewGuid();
        var alice = Guid.NewGuid();
        var f = FiltreMeps.Lire(new ParametresFiltreMeps("stocks", $"{bdd},libre:GED", "Production", equipe.ToString(), alice.ToString(), "pilote,executant",
            "incidents", null, "complete", "2026-01-01", "2026-06-30", "1"));
        Assert.False(f.EstVide);
        Assert.True(FiltreMeps.Lire(new ParametresFiltreMeps(Role: "pilote")).EstVide);
        Assert.True(FiltreMeps.Lire(new ParametresFiltreMeps()).EstVide);
        Assert.Equal(
            ["recherche « stocks »", "systèmes BDD, GED (hors catalogue)", "environnement Production", "avec un échec sur ces systèmes", "équipe Exploitation",
                "personne Alice Martin (pilote, exécutant)", "état réussie avec incidents", "MEP complètes", "du 01/01/2026 au 30/06/2026"],
            f.Decrire(id => id == bdd ? "BDD" : null, id => id == equipe ? "Exploitation" : null, id => id == alice ? "Alice Martin" : null));
    }

    [Theory]
    [InlineData(Phase.Preparation, null, 3, EtatMep.Preparation)]
    [InlineData(Phase.Execution, null, 1, EtatMep.Execution)]
    [InlineData(Phase.Recette, null, 0, EtatMep.Recette)]
    [InlineData(Phase.RetourArriere, null, 2, EtatMep.RetourArriere)]
    [InlineData(Phase.Cloturee, IssueMep.Reussie, 0, EtatMep.Reussie)]
    [InlineData(Phase.Cloturee, IssueMep.Reussie, 1, EtatMep.AvecIncidents)]
    [InlineData(Phase.Cloturee, IssueMep.Repliee, 1, EtatMep.Repliee)]
    [InlineData(Phase.Cloturee, IssueMep.Repliee, 0, EtatMep.Repliee)]
    public void L_etat_suit_la_phase_puis_l_issue(Phase phase, IssueMep? issue, int echecs, EtatMep attendu) =>
        Assert.Equal(attendu, Regles.EtatDe(phase, issue, echecs));

    [Fact]
    public void Les_roles_viennent_de_la_mep()
    {
        Participant P(string nom) => new() { Id = Guid.NewGuid(), Nom = nom };
        var (pilote, suppleant, rt, executant, simple) = (P("Pilote"), P("Suppléant"), P("RT"), P("Exécutant"), P("Simple"));
        var mep = new Mep
        {
            Type = TypeMep.Complete, PiloteId = pilote.Id, SuppleantIds = [suppleant.Id],
            ResponsablesTechniques = [new ResponsableTechnique { ParticipantId = rt.Id }],
            Participants = [pilote, suppleant, rt, executant, simple],
            Etapes = [new Etape { ExecutantId = executant.Id }, new Etape { ExecutantId = pilote.Id }, new Etape()],
        };
        var roles = Regles.RolesDesParticipants(mep);
        Assert.Equal([RoleMep.Pilote, RoleMep.Executant], roles[pilote.Id].Order());
        Assert.Equal([RoleMep.Suppleant], roles[suppleant.Id]);
        Assert.Equal([RoleMep.ResponsableTechnique], roles[rt.Id]);
        Assert.Equal([RoleMep.Executant], roles[executant.Id]);
        Assert.Empty(roles[simple.Id]);
    }
}
