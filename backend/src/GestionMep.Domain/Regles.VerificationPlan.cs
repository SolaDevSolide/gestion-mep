namespace GestionMep.Domain;

// Règles métier : vérification du plan.
public static partial class Regles
{
    /// <summary>
    /// Équivalent de ErreursPlan, mais renvoie toutes les anomalies au lieu de la première :
    /// anomalies du contenu (<see cref="ErreursContenu"/>) puis du circuit d'approbation (<see cref="ErreursApprobation"/>).
    /// </summary>
    /// <param name="approbationsMinimum">Paramètre global <see cref="ParametresGlobaux.ApprobationsMinimum"/>.</param>
    public static List<string> ErreursPlan(Mep mep, DateTimeOffset maintenant, int approbationsMinimum)
    {
        var erreurs = ErreursContenu(mep, maintenant);
        erreurs.AddRange(ErreursApprobation(mep, approbationsMinimum));
        return erreurs;
    }

    /// <summary>
    /// Anomalies du contenu de la MEP. Ajouts par rapport à l'Excel : procédure relue, comptes reliés,
    /// plan de retour arrière obligatoire, sauvegarde de chaque système et fiches de recette.
    /// </summary>
    public static List<string> ErreursContenu(Mep mep, DateTimeOffset maintenant)
    {
        var erreurs = new List<string>();
        foreach (var plan in new[] { TypePlan.Deploiement, TypePlan.RetourArriere })
        {
            var graphe = ErreurGraphe(mep, plan);
            if (graphe is not null) erreurs.Add(plan == TypePlan.Deploiement ? graphe : $"Retour arrière : {graphe}");
        }
        if (mep.EcheanceRepli is null || mep.EcheanceRepli <= maintenant)
            erreurs.Add("Renseigner une échéance de retour arrière future.");

        var pilote = mep.Participants.FirstOrDefault(p => p.Id == mep.PiloteId);
        if (pilote is null) erreurs.Add("Renseigner le pilote.");
        else if (pilote.UtilisateurId is null) erreurs.Add($"Le pilote {pilote.Nom} n'est relié à aucun compte.");

        if (!mep.EtapesDu(TypePlan.Deploiement).Any()) erreurs.Add("Ajouter au moins une étape de déploiement.");
        if (!mep.EtapesDu(TypePlan.RetourArriere).Any()) erreurs.Add("Ajouter au moins une étape de retour arrière.");
        if (string.IsNullOrWhiteSpace(mep.PlanRepli.QuandDeclencher)) erreurs.Add("Retour arrière : préciser quand le déclencher.");
        erreurs.AddRange(ErreursSauvegarde(mep));

        foreach (var e in mep.EtapesTriees())
        {
            var manquants = new List<string>();
            if (string.IsNullOrWhiteSpace(e.Action)) manquants.Add("Action");
            if (e.SystemeIds.Count == 0) manquants.Add("Système");
            if (e.Criticite is null) manquants.Add("Criticité");
            if (string.IsNullOrWhiteSpace(e.Instructions)) manquants.Add("Instructions exactes");
            if (string.IsNullOrWhiteSpace(e.ResultatAttendu)) manquants.Add("Résultat attendu");
            if (manquants.Count > 0) erreurs.Add($"{e.Code} : compléter {string.Join(", ", manquants)}.");
            if (e.Action.TrimStart().StartsWith("Exemple", StringComparison.OrdinalIgnoreCase))
                erreurs.Add($"{e.Code} : remplacer l'action d'exemple par une action réelle.");
        }

        erreurs.AddRange(ErreursRecette(mep));

        if (mep.ProcedureRelueParId is null) erreurs.Add("La procédure doit être relue par un opérateur.");
        return erreurs;
    }

    /// <summary>Sauvegarde de chaque système : statut renseigné, justification si non requise, étape de type Sauvegarde si faite pendant la MEP.</summary>
    public static List<string> ErreursSauvegarde(Mep mep)
    {
        var erreurs = new List<string>();
        foreach (var s in mep.Systemes)
        {
            var sv = s.Sauvegarde;
            switch (sv.Statut)
            {
                case StatutSauvegarde.ARenseigner:
                    erreurs.Add($"Sauvegarde : indiquer si {s.Nom} doit être sauvegardé.");
                    break;
                case StatutSauvegarde.NonRequise when string.IsNullOrWhiteSpace(sv.Justification):
                    erreurs.Add($"Sauvegarde de {s.Nom} non requise : justifier.");
                    break;
                case StatutSauvegarde.Requise when sv.Moment == ModeSauvegarde.PendantMep
                    && !mep.EtapesDu(TypePlan.Deploiement).Any(e => e.Type == TypeEtape.Sauvegarde && e.SystemeIds.Contains(s.Id)):
                    erreurs.Add($"Sauvegarde de {s.Nom} pendant la MEP : ajouter une étape de déploiement de type Sauvegarde sur ce système.");
                    break;
            }
        }
        return erreurs;
    }

    /// <summary>Nom d'une fiche de recette dans les messages : « Recette « Titre » » ou « Recette 2 » sans titre.</summary>
    public static string NomRecette(FicheRecette f) =>
        string.IsNullOrWhiteSpace(f.Titre) ? $"Recette {f.Ordre}" : $"Recette « {f.Titre} »";

    /// <summary>Fiches de recette : au moins une, chacune titrée avec au moins une étape complète.</summary>
    public static List<string> ErreursRecette(Mep mep)
    {
        var erreurs = new List<string>();
        if (mep.FichesRecette.Count == 0) erreurs.Add("Ajouter au moins une fiche de recette.");
        foreach (var f in mep.FichesRecette.OrderBy(f => f.Ordre))
        {
            if (string.IsNullOrWhiteSpace(f.Titre)) erreurs.Add($"{NomRecette(f)} : indiquer un titre.");
            if (f.Etapes.Count == 0) erreurs.Add($"{NomRecette(f)} : ajouter au moins une étape de test.");
            foreach (var t in f.Etapes.OrderBy(t => t.Ordre))
                if (string.IsNullOrWhiteSpace(t.Instructions) || string.IsNullOrWhiteSpace(t.ResultatAttendu))
                    erreurs.Add($"{NomRecette(f)}, étape {t.Ordre} : compléter les instructions et le résultat attendu.");
        }
        return erreurs;
    }

    /// <summary>
    /// Avertissements non bloquants du plan (ils ne figurent pas dans <see cref="ErreursPlan"/>) : aucun responsable technique,
    /// pilote également responsable technique, responsable technique absent dont les étapes ne sont pas reprenables par un autre,
    /// aucun système sauvegardé, système sans fiche de recette.
    /// </summary>
    public static List<string> Avertissements(Mep mep)
    {
        var avertissements = new List<string>();
        string Nom(Guid id) => mep.Participants.FirstOrDefault(p => p.Id == id)?.Nom ?? "Un responsable technique";

        if (mep.ResponsablesTechniques.Count == 0)
            avertissements.Add("Aucun responsable technique renseigné.");
        else
        {
            if (mep.PiloteId is { } pilote && mep.ResponsablesTechniques.Any(r => r.ParticipantId == pilote))
                avertissements.Add($"{Nom(pilote)} est à la fois responsable technique et pilote : prévoir un suppléant qui connaît le plan.");

            foreach (var r in mep.ResponsablesTechniques.Where(r => r.Disponibilite == DisponibiliteJourJ.Absent))
                foreach (var e in mep.EtapesTriees())
                {
                    var concernee = e.SystemeIds.Any(r.SystemeIds.Contains);
                    if (!concernee) continue;
                    var reprenable = !string.IsNullOrWhiteSpace(e.Instructions) && !string.IsNullOrWhiteSpace(e.ResultatAttendu) && e.ResolutionIds.Count > 0;
                    if (!reprenable)
                        avertissements.Add($"{e.Code} : {Nom(r.ParticipantId)} sera absent, ajouter une résolution type pour que quelqu'un d'autre puisse la reprendre.");
                }
        }

        if (mep.Systemes.Count > 0 && mep.Systemes.All(s => s.Sauvegarde.Statut == StatutSauvegarde.NonRequise))
            avertissements.Add("Aucun système n'est sauvegardé.");
        foreach (var s in mep.Systemes.Where(s => mep.FichesRecette.All(f => f.SystemeId != s.Id)))
            avertissements.Add($"Aucune recette pour le système {s.Nom}.");
        return avertissements;
    }

    /// <summary>Anomalies du circuit d'approbation : approbations manquantes, refus, approbateur pilote.</summary>
    public static List<string> ErreursApprobation(Mep mep, int approbationsMinimum)
    {
        var erreurs = new List<string>();
        var compteDuPilote = CompteDuPilote(mep);
        foreach (var a in mep.Approbations.Where(a => a.UtilisateurId == compteDuPilote))
            erreurs.Add($"{a.Nom} ne peut pas approuver : c'est le pilote.");
        var approuvees = mep.Approbations.Count(a => a.Decision == DecisionApprobation.Approuvee && a.UtilisateurId != compteDuPilote);
        if (approuvees < approbationsMinimum) erreurs.Add($"Il faut {approbationsMinimum} approbation(s).");
        foreach (var a in mep.Approbations.Where(a => a.Decision == DecisionApprobation.Refusee))
            erreurs.Add($"{a.Nom} a refusé : {a.Commentaire}");
        return erreurs;
    }

    /// <summary>Anomalies d'une résolution type avant enregistrement.</summary>
    public static List<string> ErreursResolutionType(ResolutionType r)
    {
        var erreurs = new List<string>();
        if (string.IsNullOrWhiteSpace(r.Titre)) erreurs.Add("Donner un titre à la résolution.");
        if (string.IsNullOrWhiteSpace(r.Symptomes)) erreurs.Add("Décrire comment reconnaître le problème.");
        if (r.Corrections.Count == 0) erreurs.Add("Ajouter au moins une correction.");
        foreach (var c in r.Corrections.OrderBy(c => c.Ordre))
            if (string.IsNullOrWhiteSpace(c.Instruction) || string.IsNullOrWhiteSpace(c.ResultatAttendu))
                erreurs.Add($"Correction {c.Ordre} : préciser l'instruction et le résultat attendu.");
        if (string.IsNullOrWhiteSpace(r.VerificationInstruction) || string.IsNullOrWhiteSpace(r.VerificationAttendu))
            erreurs.Add("Préciser la vérification finale et son résultat attendu.");
        return erreurs;
    }
}
