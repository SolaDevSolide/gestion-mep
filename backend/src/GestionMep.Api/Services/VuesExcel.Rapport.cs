using System.Globalization;
using GestionMep.Domain;
using GestionMep.Tableur;

namespace GestionMep.Api.Services;

/// <summary>Rapport de fin d'une MEP (#61) en classeur Excel, tel qu'il a été figé : mêmes parties que la page du rapport.</summary>
public static partial class VuesExcel
{
    /// <summary>« rapport-Outil-6.3.xlsx ».</summary>
    public static string NomFichierRapport(MepDto mep) => $"rapport-{Assainir($"{mep.Outil} {mep.VersionCible}".Trim())}.xlsx";

    private static int? MinutesEntre(DateTimeOffset? de, DateTimeOffset? a) =>
        de is { } d && a is { } f ? (int)Math.Round((f - d).TotalMinutes) : null;

    private static string Resultat(ResultatControle? r) => r switch
    {
        ResultatControle.Conforme => "Conforme",
        ResultatControle.Echec => "Échec",
        _ => "Non fait",
    };

    /// <summary>
    /// Rapport de fin : synthèse (vue d'ensemble à la clôture et bilan), points bloquants, résolutions utilisées, tests
    /// unitaires, qui a fait chaque étape, puis en annexe la recette, le retour arrière s'il a eu lieu et le journal complet.
    /// </summary>
    public static byte[] Rapport(RapportFinDto r, DateTimeOffset maintenant)
    {
        using var classeur = new ClasseurExcel();
        var mep = r.Mep;
        var s = r.Synthese;
        var f = mep.FuseauHoraire;
        var contexte = $"{NomMep(mep)}{(mep.Objet.Length > 0 ? " · " + mep.Objet : "")} · {Libelle(mep.Phase, mep.Issue)} · "
            + $"rapport figé le {Locale(r.FigeLe, f):dd/MM/yyyy à HH:mm} · exporté le {Locale(maintenant, f):dd/MM/yyyy à HH:mm}";
        string Le(DateTimeOffset? instant) => instant is { } i ? $"{Locale(i, f):dd/MM/yyyy à HH:mm}" : "";
        var simplifiee = mep.Type == TypeMep.Simplifiee;

        var synthese = new List<(string, object?)>
        {
            ("MEP", null),
            ("Application", mep.Outil),
        };
        if (!simplifiee) synthese.Add(("Versions", $"{mep.VersionActuelle} → {mep.VersionCible}"));
        synthese.AddRange(
        [
            ("Objet", mep.Objet), ("Type de MEP", Libelle(mep.Type)), ("Issue", Libelle(mep.Phase, mep.Issue)),
            ("Pilote", Participant(mep, mep.PiloteId)), ("Go", Le(s.Go)),
            ("Clôture", mep.Cloture is { } c ? $"{Le(c.Le)}{(c.Par is null ? "" : $" par {c.Par}")}" : ""),
            ("Systèmes", string.Join(", ", mep.Systemes.Select(x => x.Nom))),
            ("Vue d'ensemble à la clôture", null),
            ("Étapes de déploiement", $"{s.EtapesReussies}/{s.Etapes} réussies"),
            ("Durée du déploiement", s.DureeReelleMinutes is { } d
                ? VariablesTexte.Duree(d) + (s.DureePrevueMinutes > 0 ? $" (prévue {VariablesTexte.Duree(s.DureePrevueMinutes)})" : "")
                : ""),
            ("Échecs d'étape", s.Echecs == 0 ? "Aucun" : $"{s.Echecs} (dont {s.EchecsRepris} repris avec succès)"),
            ("Résolutions exécutées", s.Resolutions.ToString(CultureInfo.InvariantCulture)),
            ("Tests unitaires", $"{r.TestsUnitaires.Faits} fait(s), {r.TestsUnitaires.NonFaits} non fait(s)"),
            ("Recette", s.RecetteTests == 0 ? "Aucun test" : $"{s.RecetteConformes}/{s.RecetteTests} conformes, {s.RecetteEchecs} en échec"),
            ("Retour arrière", mep.RetourArriere is { } ra
                ? $"Déclenché le {Le(ra.Le)}{(ra.Par is null ? " (automatiquement)" : $" par {ra.Par}")}{(ra.Motif.Length > 0 ? $" : {ra.Motif}" : "")}"
                : "Non"),
        ]);
        if (mep.Bilan is { } b)
            synthese.AddRange([("Bilan", null), ("Ce qui a marché", b.CeQuiAMarche), ("Incidents", b.Incidents), ("Actions", b.Actions),
                ("Rédigé par", b.RedigePar ?? "")]);
        synthese.AddRange([("Rapport", null), ("Figé le", Le(r.FigeLe) + (r.CalculeApres ? " (MEP clôturée avant les rapports de fin : figé à sa première demande)" : ""))]);
        Fiche(classeur, "Synthèse", "Rapport de fin de MEP", contexte, synthese);

        Tableau(classeur, "Points bloquants", "Points bloquants", contexte,
            [new("Étape", 9, true), new("Plan", 14), new("Action", 36), new("Échec le", 16), new("Par", 18), new("Motif", 44),
                new("Résolution", 32), new("Statut", 11), new("Durée de la résolution (min)", 14), new("Reprise le", 16), new("Réussie le", 16),
                new("Arrêt (min)", 10)],
            r.PointsBloquants.Select(p => new object?[]
            {
                p.Code, p.Plan == TypePlan.RetourArriere ? "Retour arrière" : "Déploiement", p.Action, Quand(p.EchecLe, f), p.Par, p.Motif,
                p.Resolution ?? "", p.StatutResolution is { } st ? Libelle(st) : "", p.DureeResolutionMinutes, Quand(p.RepriseLe, f),
                Quand(p.RepriseReussieLe, f), p.ArretMinutes,
            }), "Aucun échec d'étape.");

        var codes = mep.Etapes.ToDictionary(e => e.Id, e => e.Code);
        Tableau(classeur, "Résolutions", "Résolutions utilisées", contexte,
            [new("Étape", 9, true), new("Résolution", 36), new("Statut", 12), new("Démarrée par", 20), new("Démarrée le", 16),
                new("Terminée le", 16), new("Durée (min)", 10), new("Corrections", 16), new("Vérification", 44)],
            mep.Resolutions.Select(x => new object?[]
            {
                codes.TryGetValue(x.EtapeId, out var code) ? code : "", x.Titre, Libelle(x.Statut), x.DemarreePar, Quand(x.DemarreeLe, f),
                Quand(x.TermineeLe, f), MinutesEntre(x.DemarreeLe, x.TermineeLe),
                $"{x.Corrections.Count(c => c.Resultat == ResultatControle.Conforme)}/{x.Corrections.Count} conforme(s)",
                x.VerificationResultat is { } vr ? $"{Resultat(vr)}{(x.VerificationObservation.Length > 0 ? $" : {x.VerificationObservation}" : "")}" : "",
            }), "Aucune résolution exécutée.");

        Tableau(classeur, "Tests unitaires", "Tests unitaires",
            $"{r.TestsUnitaires.Faits} fait(s), {r.TestsUnitaires.NonFaits} non fait(s) · {r.TestsUnitaires.Verifications} étape(s) terminée(s) par une simple vérification (non listées)",
            [new("Code", 9, true), new("Plan", 14), new("Action", 36), new("Résultat attendu", 40), new("Résultat constaté", 40), new("Fait", 8),
                new("Exécutant", 20), new("Fin", 16)],
            r.TestsUnitaires.Tests.Select(t => new object?[]
            {
                t.Code, t.Plan == TypePlan.RetourArriere ? "Retour arrière" : "Déploiement", t.Action, BlocsMarkdown.ToutEnTexte(t.ResultatAttendu),
                t.ResultatConstate, OuiNon(t.Fait), t.Executant ?? "", Quand(t.FinReelle, f),
            }), "Aucun test unitaire.");

        var sections = mep.Sections.ToDictionary(x => x.Id, x => x.Titre);
        object?[] LigneEtape(EtapeDto e) =>
        [
            e.Code, e.SectionId is { } sid && sections.TryGetValue(sid, out var t) ? t : "", e.Action, Libelle(e.Etat), Participant(mep, e.ExecutantId),
            Quand(e.DebutReel, f), Quand(e.FinReelle, f), MinutesEntre(e.DebutReel, e.FinReelle), e.DureeEstimeeMinutes,
        ];
        List<Colonne> colonnesEtapes = [new("Code", 9, true), new("Section", 20), new("Action", 40), new("État", 11), new("Exécutant", 20),
            new("Début", 16), new("Fin", 16), new("Durée réelle (min)", 11), new("Durée prévue (min)", 11)];
        Tableau(classeur, "Étapes", "Qui a fait chaque étape",
            "Début et fin de la dernière exécution de chaque étape de déploiement (une étape refaite après un échec garde sa reprise).",
            colonnesEtapes, mep.Etapes.Where(e => e.Plan == TypePlan.Deploiement).OrderBy(e => e.Ligne).ThenBy(e => e.Ordre).Select(LigneEtape),
            "Aucune étape de déploiement.");

        // ---------- Annexes ----------
        var noms = mep.Systemes.ToDictionary(x => x.Id, x => x.Nom);
        Tableau(classeur, "Recette", "Annexe : recette", contexte,
            [new("Fiche", 26), new("Système", 12), new("N°", 5), new("Instructions", 44), new("Résultat attendu", 36), new("Résultat", 11),
                new("Observation", 32), new("Par", 18), new("Le", 16), new("Validée par", 22)],
            mep.FichesRecette.OrderBy(x => x.Ordre).SelectMany(fiche => fiche.Etapes.OrderBy(t => t.Ordre).Select(t => new object?[]
            {
                fiche.Titre, fiche.SystemeId is { } sid && noms.TryGetValue(sid, out var n) ? n : "", t.Ordre,
                BlocsMarkdown.ToutEnTexte(t.Instructions), BlocsMarkdown.ToutEnTexte(t.ResultatAttendu), Resultat(t.Resultat), t.Observation,
                t.Par ?? "", Quand(t.Le, f), t.ValideePar.Length > 0 ? $"{t.ValideePar}{(t.ValideeLe is { } vl ? $" le {vl:dd/MM/yyyy}" : "")}" : "",
            })), "Aucune fiche de recette.");

        if (mep.RetourArriere is { } repli)
            Tableau(classeur, "Retour arrière", "Annexe : retour arrière",
                $"Déclenché le {Le(repli.Le)}{(repli.Par is null ? " (automatiquement)" : $" par {repli.Par}")}{(repli.Motif.Length > 0 ? $" : {repli.Motif}" : "")}",
                colonnesEtapes,
                mep.Etapes.Where(e => e.Plan == TypePlan.RetourArriere).OrderBy(e => e.Ligne).ThenBy(e => e.Ordre).Select(LigneEtape),
                "Aucune étape de retour arrière.");

        Tableau(classeur, "Journal", "Annexe : journal complet", contexte + $" · {r.Journal.Count} événement(s)",
            [new("Quand", 16), new("Auteur", 22), new("Compte", 18), new("Type", 18), new("Détail", 80), new("Événement", 24, true)],
            r.Journal.Select(e => new object?[] { Quand(e.Quand, f), e.Auteur, e.AuteurLogin ?? "", LibelleFamille(e.Type), e.Detail, e.Type }),
            "Aucun événement.");
        return classeur.Enregistrer();
    }
}
