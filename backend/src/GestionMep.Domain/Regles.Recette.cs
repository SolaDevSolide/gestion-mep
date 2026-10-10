namespace GestionMep.Domain;

// Règles métier : recette.
public static partial class Regles
{
    /// <summary>Nouvelle fiche de recette vide, ajoutée en fin (préparation uniquement). Le système doit appartenir à la MEP.</summary>
    public static FicheRecette AjouterFicheRecette(Mep mep, Guid? systemeId, string titre)
    {
        ExigerPreparation(mep);
        ExigerSystemeDeLaMep(mep, systemeId);
        var fiche = new FicheRecette
        {
            Ordre = mep.FichesRecette.Count == 0 ? 1 : mep.FichesRecette.Max(f => f.Ordre) + 1,
            SystemeId = systemeId, Titre = (titre ?? "").Trim(),
        };
        // Nouvelle liste : la colonne jsonb est réécrite.
        mep.FichesRecette = [.. mep.FichesRecette, fiche];
        return fiche;
    }

    /// <summary>
    /// Remplace le contenu d'une fiche de recette : les étapes reçues avec un identifiant connu gardent leur identité
    /// (et leur résultat éventuel), les autres sont créées ; l'ordre est celui reçu.
    /// </summary>
    public static void ModifierFicheRecette(Mep mep, FicheRecette fiche, Guid? systemeId, string titre,
        IEnumerable<(Guid? Id, string Instructions, string ResultatAttendu)> etapes)
    {
        ExigerPreparation(mep);
        ExigerSystemeDeLaMep(mep, systemeId);
        var anciennes = fiche.Etapes.ToDictionary(t => t.Id);
        var vues = new HashSet<Guid>();
        var nouvelles = new List<EtapeTest>();
        foreach (var (id, instructions, attendu) in etapes)
        {
            var t = id is { } i && anciennes.TryGetValue(i, out var existante) && vues.Add(i) ? existante : new EtapeTest();
            t.Ordre = nouvelles.Count + 1;
            t.Instructions = (instructions ?? "").Trim();
            t.ResultatAttendu = (attendu ?? "").Trim();
            nouvelles.Add(t);
        }
        fiche.SystemeId = systemeId;
        fiche.Titre = (titre ?? "").Trim();
        fiche.Etapes = nouvelles;
        mep.FichesRecette = [.. mep.FichesRecette];
    }

    /// <summary>Supprime une fiche de recette (préparation uniquement) ; les fiches restantes sont renumérotées 1..n.</summary>
    public static void SupprimerFicheRecette(Mep mep, FicheRecette fiche)
    {
        ExigerPreparation(mep);
        var restantes = mep.FichesRecette.Where(f => f.Id != fiche.Id).OrderBy(f => f.Ordre).ToList();
        for (var i = 0; i < restantes.Count; i++) restantes[i].Ordre = i + 1;
        mep.FichesRecette = restantes;
    }

    private static void ExigerSystemeDeLaMep(Mep mep, Guid? systemeId)
    {
        if (systemeId is { } s && mep.Systemes.All(x => x.Id != s)) throw new RegleMetierException("Système inconnu dans cette MEP.");
    }

    /// <summary>
    /// Résultat d'une étape de test, en phase de recette : par tout participant relié à un compte (pilote et suppléants compris)
    /// ou un administrateur ; observation obligatoire ; une étape déjà validée doit d'abord être refaite.
    /// </summary>
    public static void ValiderEtapeTest(Mep mep, FicheRecette fiche, EtapeTest test, ResultatControle resultat, string observation,
        Guid utilisateurId, bool estAdmin, DateTimeOffset maintenant)
    {
        if (!estAdmin && !mep.Participants.Any(p => p.UtilisateurId == utilisateurId))
            throw new RegleMetierException("Seuls les participants de la MEP reliés à un compte, le pilote ou un administrateur peuvent donner le résultat d'une recette.");
        if (mep.Phase != Phase.Recette) throw new RegleMetierException("Les recettes se déroulent en phase de recette.");
        if (test.Resultat is not null)
            throw new RegleMetierException($"{NomRecette(fiche)}, étape {test.Ordre} déjà validée : le pilote peut la faire refaire.");
        if (string.IsNullOrWhiteSpace(observation)) throw new RegleMetierException("Décrire ce qui a été observé.");
        test.Resultat = resultat;
        test.Observation = observation.Trim();
        test.ParId = utilisateurId;
        test.Le = maintenant;
        mep.FichesRecette = [.. mep.FichesRecette];
    }

    /// <summary>Le pilote fait refaire une étape de test (<paramref name="test"/> renseigné) ou toute la fiche : les résultats sont effacés.</summary>
    public static void RefaireRecette(Mep mep, FicheRecette fiche, EtapeTest? test, Guid utilisateurId)
    {
        if (!EstPilote(mep, utilisateurId)) throw new RegleMetierException("Seul le pilote ou un suppléant peut faire refaire une recette.");
        if (mep.Phase != Phase.Recette) throw new RegleMetierException("Les recettes se refont en phase de recette.");
        var cibles = test is null ? fiche.Etapes.Where(t => t.Resultat is not null).ToList() : [test];
        if (test is { Resultat: null }) throw new RegleMetierException($"{NomRecette(fiche)}, étape {test.Ordre} pas encore validée.");
        if (cibles.Count == 0) throw new RegleMetierException($"{NomRecette(fiche)} : aucune étape validée.");
        foreach (var t in cibles)
        {
            t.Resultat = null;
            t.Observation = "";
            t.ParId = null;
            t.Le = null;
        }
        mep.FichesRecette = [.. mep.FichesRecette];
    }
}
