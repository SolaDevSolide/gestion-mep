namespace GestionMep.Domain;

// Règles métier : bibliothèque d'étapes modèles et compteurs d'utilisations des bibliothèques.
public static partial class Regles
{
    /// <summary>Anomalies d'une étape modèle (vide si elle peut être enregistrée).</summary>
    public static List<string> ErreursModeleEtape(ModeleEtape m)
    {
        var erreurs = new List<string>();
        if (string.IsNullOrWhiteSpace(m.Action)) erreurs.Add("Donner un nom à l'étape modèle.");
        if (m.SystemeCatalogueIds.Count == 0) erreurs.Add("Choisir au moins un système du catalogue.");
        if (m.DureeEstimeeMinutes is < 0) erreurs.Add("La durée estimée ne peut pas être négative.");
        if (!Enum.IsDefined(m.Type)) erreurs.Add("Type d'étape inconnu.");
        if (!Enum.IsDefined(m.Fin)) erreurs.Add("Fin d'étape inconnue : test unitaire ou simple vérification.");
        return erreurs;
    }

    /// <summary>Une étape modèle standard n'a ni emplacement ni méthode de récupération de sauvegarde.</summary>
    public static void NormaliserModeleEtape(ModeleEtape m)
    {
        m.SystemeCatalogueIds = m.SystemeCatalogueIds.Distinct().ToList();
        m.ResolutionIds = m.ResolutionIds.Distinct().ToList();
        if (m.Type != TypeEtape.Sauvegarde)
        {
            m.SauvegardeEmplacement = "";
            m.SauvegardeMethodeRecuperation = "";
        }
    }

    /// <summary>
    /// « Transformer en étape modèle » : copie de la fiche d'une étape de la MEP, sous le nom donné (l'action de l'étape à
    /// défaut). Sont repris l'action, les systèmes (ceux qui viennent du catalogue), le type et les champs de sauvegarde, la
    /// criticité, la durée, les instructions, le résultat attendu, la fin et les résolutions. Jamais le point de non-retour,
    /// les liens, l'exécutant ni les résultats. Renvoie aussi les systèmes de l'étape qui ne viennent pas du catalogue.
    /// </summary>
    public static (ModeleEtape Modele, List<string> HorsCatalogue) ModeleDepuisEtape(Mep mep, Etape etape, string nom)
    {
        var systemes = etape.SystemeIds.Select(id => mep.Systemes.FirstOrDefault(s => s.Id == id)).OfType<Systeme>().ToList();
        var modele = new ModeleEtape
        {
            Action = string.IsNullOrWhiteSpace(nom) ? etape.Action.Trim() : nom.Trim(),
            SystemeCatalogueIds = systemes.Select(s => s.CatalogueId).OfType<Guid>().Distinct().ToList(),
            Type = etape.Type, Criticite = etape.Criticite, DureeEstimeeMinutes = etape.DureeEstimeeMinutes,
            Instructions = etape.Instructions.Trim(), ResultatAttendu = etape.ResultatAttendu.Trim(), Fin = etape.Fin,
            SauvegardeEmplacement = etape.SauvegardeEmplacement.Trim(), SauvegardeMethodeRecuperation = etape.SauvegardeMethodeRecuperation.Trim(),
            ResolutionIds = [.. etape.ResolutionIds],
        };
        NormaliserModeleEtape(modele);
        return (modele, systemes.Where(s => s.CatalogueId is null).Select(s => s.Nom).ToList());
    }

    /// <summary>
    /// Insère dans le plan (préparation) une copie d'une étape modèle : nouveau code, à la place donnée dans le graphe et dans la
    /// section donnée (comme une étape créée, voir <see cref="AjouterEtape"/>), jamais de point de non-retour. Les systèmes du modèle deviennent les systèmes de la MEP issus du même système du catalogue ;
    /// ceux qui ne sont pas dans la MEP ne sont pas ajoutés et sont renvoyés (le front prévient). Un système de la MEP qui n'a
    /// qu'un environnement le donne à la copie ; s'il en a plusieurs (ou aucun), la copie n'en a pas : à choisir dans l'étape,
    /// comme pour une étape saisie. Les environnements ne font pas partie du modèle. Seules les résolutions
    /// <paramref name="resolutionsActives"/> sont reprises. Dans une MEP simplifiée, la copie n'a ni durée, ni criticité,
    /// ni résolutions liées.
    /// </summary>
    public static (Etape Etape, List<Guid> SystemesAbsents) InsererEtapeModele(Mep mep, ModeleEtape modele, TypePlan plan, PlaceEtape place,
        Guid? sectionId, IReadOnlyCollection<Guid> resolutionsActives)
    {
        ExigerPreparation(mep);
        if (plan == TypePlan.RetourArriere && modele.Type != TypeEtape.Standard)
            throw new RegleMetierException("Le plan de retour arrière n'accepte pas d'étape de sauvegarde : choisir une étape modèle normale.");
        var systemes = modele.SystemeCatalogueIds
            .Select(c => (Catalogue: c, Systeme: mep.Systemes.FirstOrDefault(s => s.CatalogueId == c)))
            .ToList();
        var etape = new Etape
        {
            MepId = mep.Id, Plan = plan, Code = ProchainCode(mep.Etapes, plan), Action = modele.Action,
            SystemeIds = systemes.Select(s => s.Systeme?.Id).OfType<Guid>().Distinct().ToList(),
            Criticite = modele.Criticite, DureeEstimeeMinutes = modele.DureeEstimeeMinutes, Instructions = modele.Instructions,
            ResultatAttendu = modele.ResultatAttendu, Fin = modele.Fin,
            SauvegardeEmplacement = modele.SauvegardeEmplacement, SauvegardeMethodeRecuperation = modele.SauvegardeMethodeRecuperation,
            ResolutionIds = modele.ResolutionIds.Where(resolutionsActives.Contains).ToList(),
            ModeleEtapeId = modele.Id,
        };
        etape.Environnements = etape.SystemeIds
            .Select(id => mep.Systemes.First(s => s.Id == id))
            .Where(s => s.Environnements.Count == 1)
            .Select(s => new EnvironnementEtape { SystemeId = s.Id, Nom = s.Environnements[0].Nom })
            .ToList();
        DefinirType(etape, modele.Type, pointDeNonRetour: false);
        AppliquerTypeAEtape(mep, etape);
        AjouterEtape(mep, etape, place, sectionId);
        return (etape, systemes.Where(s => s.Systeme is null).Select(s => s.Catalogue).ToList());
    }

    /// <summary>Éléments des bibliothèques utilisés par la MEP : étapes modèles copiées, modèles de recette insérés, résolutions liées.</summary>
    public static HashSet<Guid> ElementsBibliotheque(Mep mep)
    {
        var ids = new HashSet<Guid>();
        foreach (var e in mep.Etapes)
        {
            if (e.ModeleEtapeId is { } m) ids.Add(m);
            ids.UnionWith(e.ResolutionIds);
        }
        foreach (var f in mep.FichesRecette)
            if (f.ModeleRecetteId is { } r) ids.Add(r);
        return ids;
    }

    /// <summary>
    /// Éléments des bibliothèques que la MEP utilise pour la première fois : ils sont notés dans
    /// <see cref="Mep.ElementsBibliothequeComptes"/> et leur compteur d'utilisations est à augmenter d'un. Un modèle de MEP
    /// ne compte pas (une MEP créée depuis lui comptera) ; un élément retiré puis remis ne compte qu'une fois.
    /// </summary>
    public static List<Guid> NouveauxElementsBibliotheque(Mep mep)
    {
        if (mep.EstModele) return [];
        var nouveaux = ElementsBibliotheque(mep).Except(mep.ElementsBibliothequeComptes).OrderBy(id => id).ToList();
        if (nouveaux.Count > 0) mep.ElementsBibliothequeComptes = [.. mep.ElementsBibliothequeComptes, .. nouveaux];
        return nouveaux;
    }
}
