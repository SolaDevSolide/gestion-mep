using System.Text.Json;
using GestionMep.Domain;

namespace GestionMep.Api.Services;

/// <summary>Bloc de formulaire d'une MEP, contrôlé par sa propre révision (<see cref="Mep.Revisions"/>).</summary>
public enum BlocMep { Identite, Echeance, Sauvegarde, PlanRepli, Communication, ResponsablesTechniques, Approbateurs }

/// <summary>
/// Révisions des blocs de formulaire d'une MEP : à la lecture (<see cref="ServiceMep.Charger"/>), l'empreinte de chaque bloc
/// est relevée ; à l'enregistrement, chaque bloc dont l'empreinte a changé voit sa révision augmenter, quelle que soit la
/// route qui l'a changé (un passage en MEP simplifiée efface les versions : la révision de l'intervention augmente aussi).
/// Le contenu de chaque bloc est celui du formulaire correspondant de l'interface.
/// </summary>
public static class BlocsMep
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Nom du bloc dans les messages (celui de la section de l'interface).</summary>
    public static string Libelle(BlocMep bloc) => bloc switch
    {
        BlocMep.Identite => "Intervention",
        BlocMep.Echeance => "Échéance de retour arrière",
        BlocMep.Sauvegarde => "Sauvegarde et validation",
        BlocMep.PlanRepli => "Plan de retour arrière",
        BlocMep.Communication => "Communication",
        BlocMep.ResponsablesTechniques => "Responsables techniques",
        BlocMep.Approbateurs => "Approbateurs",
        _ => bloc.ToString(),
    };

    private static object Contenu(Mep m, BlocMep bloc) => bloc switch
    {
        BlocMep.Identite => new
        {
            m.Outil, m.VersionActuelle, m.VersionCible, m.Objet, m.Impacts, m.FuseauHoraire, m.IndisponibiliteMinutes, m.PiloteId,
            m.SuppleantIds, m.BilanActive, m.RetourArriereActive,
        },
        BlocMep.Echeance => new { m.EcheanceRepli },
        BlocMep.Sauvegarde => m.Sauvegarde,
        BlocMep.PlanRepli => m.PlanRepli,
        BlocMep.Communication => m.Communication,
        BlocMep.ResponsablesTechniques => m.ResponsablesTechniques,
        BlocMep.Approbateurs => m.Approbations.Select(a => a.UtilisateurId).ToList(),
        _ => throw new ArgumentOutOfRangeException(nameof(bloc)),
    };

    public static int Revision(Mep m, BlocMep bloc) => bloc switch
    {
        BlocMep.Identite => m.Revisions.Identite,
        BlocMep.Echeance => m.Revisions.Echeance,
        BlocMep.Sauvegarde => m.Revisions.Sauvegarde,
        BlocMep.PlanRepli => m.Revisions.PlanRepli,
        BlocMep.Communication => m.Revisions.Communication,
        BlocMep.ResponsablesTechniques => m.Revisions.ResponsablesTechniques,
        BlocMep.Approbateurs => m.Revisions.Approbateurs,
        _ => throw new ArgumentOutOfRangeException(nameof(bloc)),
    };

    private static void Augmenter(Mep m, BlocMep bloc)
    {
        var r = m.Revisions;
        switch (bloc)
        {
            case BlocMep.Identite: r.Identite++; break;
            case BlocMep.Echeance: r.Echeance++; break;
            case BlocMep.Sauvegarde: r.Sauvegarde++; break;
            case BlocMep.PlanRepli: r.PlanRepli++; break;
            case BlocMep.Communication: r.Communication++; break;
            case BlocMep.ResponsablesTechniques: r.ResponsablesTechniques++; break;
            case BlocMep.Approbateurs: r.Approbateurs++; break;
        }
    }

    /// <summary>Empreintes des blocs d'une MEP, et de la sauvegarde de chacun de ses systèmes (clé : identifiant du système).</summary>
    public sealed record Empreintes(Dictionary<BlocMep, string> Blocs, Dictionary<Guid, string> Systemes);

    public static Empreintes Relever(Mep m) => new(
        Enum.GetValues<BlocMep>().ToDictionary(b => b, b => JsonSerializer.Serialize(Contenu(m, b), Options)),
        m.Systemes.ToDictionary(s => s.Id, s => JsonSerializer.Serialize(s.Sauvegarde, Options)));

    /// <summary>
    /// Augmente la révision des blocs (et des sauvegardes de systèmes) qui ont changé depuis <paramref name="avant"/> ; sans
    /// relevé (MEP créée ou dupliquée), rien ne change. Renvoie le nouveau relevé.
    /// </summary>
    public static Empreintes Reviser(Mep m, Empreintes? avant)
    {
        var apres = Relever(m);
        if (avant is null) return apres;
        foreach (var (bloc, empreinte) in apres.Blocs)
            if (avant.Blocs.GetValueOrDefault(bloc) != empreinte) Augmenter(m, bloc);
        foreach (var s in m.Systemes)
            if (avant.Systemes.TryGetValue(s.Id, out var e) && e != apres.Systemes[s.Id]) s.RevisionSauvegarde++;
        return apres;
    }
}
