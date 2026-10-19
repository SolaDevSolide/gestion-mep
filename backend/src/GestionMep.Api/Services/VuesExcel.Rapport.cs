using System.Globalization;
using GestionMep.Domain;
using GestionMep.Tableur;

namespace GestionMep.Api.Services;

/// <summary>Rapport de fin d'une MEP (#61) en classeur Excel, tel qu'il a été figé : mêmes parties que la page du rapport.</summary>
public static partial class VuesExcel
{
    /// <summary>« rapport-Outil-6.3.xlsx ».</summary>
    public static string NomFichierRapport(MepDto mep) => ExcelCommun.NomFichier("rapport", NomMep(mep));

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
    public static byte[] Rapport(RapportFinDto r, DateTimeOffset maintenant, PaletteExcel? palette = null)
    {
        var mep = r.Mep;
        using var classeur = Classeur("Rapport de fin", mep, maintenant, palette);
        var s = r.Synthese;
        var f = mep.FuseauHoraire;
        var nom = NomMep(mep);
        var contexte = $"{Contexte(mep)} · rapport figé le {Le(r.FigeLe, f)}";
        string LeOuVide(DateTimeOffset? instant) => instant is { } i ? Le(i, f) : "";
        var simplifiee = mep.Type == TypeMep.Simplifiee;

        var synthese = new List<(string, object?)>
        {
            ("MEP", null),
            ("Application", mep.Outil),
        };
        if (!simplifiee) synthese.Add(("Versions", $"{mep.VersionActuelle} → {mep.VersionCible}"));
        synthese.AddRange(
        [
            ("Objet", mep.Objet), ("Type de MEP", Libelle(mep.Type)), ("Issue", LibellePhase(mep)),
            ("Pilote", Participant(mep, mep.PiloteId)), ("Go", LeOuVide(s.Go)),
            ("Clôture", mep.Cloture is { } c ? $"{Le(c.Le, f)}{(c.Par is null ? "" : $" par {c.Par}")}" : ""),
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
                ? $"Déclenché le {Le(ra.Le, f)}{(ra.Par is null ? " (automatiquement)" : $" par {ra.Par}")}{(ra.Motif.Length > 0 ? $" : {ra.Motif}" : "")}"
                : "Non"),
        ]);
        if (mep.Bilan is { } b)
            synthese.AddRange([("Bilan", null), ("Ce qui a marché", b.CeQuiAMarche), ("Incidents", b.Incidents), ("Actions", b.Actions),
                ("Rédigé par", b.RedigePar ?? "")]);
        synthese.AddRange([("Rapport", null), ("Figé le", Le(r.FigeLe, f) + (r.CalculeApres ? " (MEP clôturée avant les rapports de fin : figé à sa première demande)" : ""))]);
        Fiche(classeur, "Synthèse", $"Rapport de fin · {nom}", contexte, synthese);

        Tableau(classeur, "Points bloquants", $"Points bloquants · {nom}", contexte,
            [Col("Étape", 9, TypeColonne.Code), Col("Plan", 14, TypeColonne.Court), Col("Action", 34), Col("Échec le", 16, TypeColonne.DateHeure),
                Col("Par", 18), Col("Motif", 42), Col("Résolution", 30) with { Facultative = true },
                Col("Statut", 11, TypeColonne.Court) with { Facultative = true, Tons = ExcelCommun.TonsResolution },
                Col("Durée de la résolution", 12, TypeColonne.Duree) with { Facultative = true },
                Col("Reprise le", 16, TypeColonne.DateHeure) with { Facultative = true }, Col("Réussie le", 16, TypeColonne.DateHeure) with { Facultative = true },
                Col("Arrêt", 9, TypeColonne.Duree) with { Facultative = true, Note = "Temps entre l'échec et la reprise réussie." }],
            r.PointsBloquants.Select(p => new object?[]
            {
                p.Code, p.Plan == TypePlan.RetourArriere ? "Retour arrière" : "Déploiement", p.Action, Quand(p.EchecLe, f), p.Par, p.Motif,
                p.Resolution ?? "", p.StatutResolution is { } st ? Libelle(st) : "", p.DureeResolutionMinutes, Quand(p.RepriseLe, f),
                Quand(p.RepriseReussieLe, f), p.ArretMinutes,
            }), "Aucun échec d'étape.");

        var codes = mep.Etapes.ToDictionary(e => e.Id, e => e.Code);
        Tableau(classeur, "Résolutions", $"Résolutions utilisées · {nom}", contexte,
            [Col("Étape", 9, TypeColonne.Code), Col("Résolution", 36), Col("Statut", 12, TypeColonne.Court) with { Tons = ExcelCommun.TonsResolution },
                Col("Démarrée par", 20), Col("Démarrée le", 16, TypeColonne.DateHeure), Col("Terminée le", 16, TypeColonne.DateHeure),
                Col("Durée", 9, TypeColonne.Duree), Col("Corrections", 16, TypeColonne.Court), Col("Vérification", 44)],
            mep.Resolutions.Select(x => new object?[]
            {
                codes.TryGetValue(x.EtapeId, out var code) ? code : "", x.Titre, Libelle(x.Statut), x.DemarreePar, Quand(x.DemarreeLe, f),
                Quand(x.TermineeLe, f), MinutesEntre(x.DemarreeLe, x.TermineeLe),
                $"{x.Corrections.Count(c => c.Resultat == ResultatControle.Conforme)}/{x.Corrections.Count} conforme(s)",
                x.VerificationResultat is { } vr ? $"{Resultat(vr)}{(x.VerificationObservation.Length > 0 ? $" : {x.VerificationObservation}" : "")}" : "",
            }), "Aucune résolution exécutée.");

        Tableau(classeur, "Tests unitaires", $"Tests unitaires · {nom}", ContexteTests(r.TestsUnitaires), ColonnesTests(etat: false),
            r.TestsUnitaires.Tests.Select(t => LigneTest(t, f, etat: false)), "Aucun test unitaire.");

        var sections = mep.Sections.ToDictionary(x => x.Id, x => x.Titre);
        object?[] LigneEtape(EtapeDto e) =>
        [
            e.Code, e.SectionId is { } sid && sections.TryGetValue(sid, out var t) ? t : "", e.Action, Libelle(e.Etat), Participant(mep, e.ExecutantId),
            Quand(e.DebutReel, f), Quand(e.FinReelle, f), MinutesEntre(e.DebutReel, e.FinReelle), e.DureeEstimeeMinutes,
        ];
        ColonneTableau[] colonnesEtapes = [Col("Code", 9, TypeColonne.Code), Col("Section", 20) with { Facultative = true }, Col("Action", 40),
            Col("État", 11, TypeColonne.Court) with { Tons = ExcelCommun.TonsEtatEtape }, Col("Exécutant", 20),
            Col("Début", 16, TypeColonne.DateHeure), Col("Fin", 16, TypeColonne.DateHeure), Col("Durée réelle", 10, TypeColonne.Duree),
            Col("Durée prévue", 10, TypeColonne.Duree) with { Facultative = true }];
        Tableau(classeur, "Étapes", $"Qui a fait chaque étape · {nom}",
            "Début et fin de la dernière exécution de chaque étape de déploiement (une étape refaite après un échec garde sa reprise).",
            colonnesEtapes, mep.Etapes.Where(e => e.Plan == TypePlan.Deploiement).OrderBy(e => e.Ligne).ThenBy(e => e.Ordre).Select(LigneEtape),
            "Aucune étape de déploiement.");

        // ---------- Annexes ----------
        var noms = mep.Systemes.ToDictionary(x => x.Id, x => x.Nom);
        Tableau(classeur, "Recette", $"Annexe : recette · {nom}", contexte,
            [Col("Fiche", 26), Col("Système", 12), Col("N°", 5, TypeColonne.Entier), Col("Instructions", 44), Col("Résultat attendu", 36),
                Col("Résultat", 11, TypeColonne.Court) with { Tons = ExcelCommun.TonsResultat }, Col("Observation", 32), Col("Par", 18),
                Col("Le", 16, TypeColonne.DateHeure), Col("Validée par", 22) with { Facultative = true }],
            mep.FichesRecette.OrderBy(x => x.Ordre).SelectMany(fiche => fiche.Etapes.OrderBy(t => t.Ordre).Select(t => new object?[]
            {
                fiche.Titre, fiche.SystemeId is { } sid && noms.TryGetValue(sid, out var n) ? n : "", t.Ordre,
                BlocsMarkdown.ToutEnTexte(t.Instructions), BlocsMarkdown.ToutEnTexte(t.ResultatAttendu), Resultat(t.Resultat), t.Observation,
                t.Par ?? "", Quand(t.Le, f), t.ValideePar.Length > 0 ? $"{t.ValideePar}{(t.ValideeLe is { } vl ? $" le {vl:dd/MM/yyyy}" : "")}" : "",
            })), "Aucune fiche de recette.");

        if (mep.RetourArriere is { } repli)
            Tableau(classeur, "Retour arrière", $"Annexe : retour arrière · {nom}",
                $"Déclenché le {Le(repli.Le, f)}{(repli.Par is null ? " (automatiquement)" : $" par {repli.Par}")}{(repli.Motif.Length > 0 ? $" : {repli.Motif}" : "")}",
                colonnesEtapes,
                mep.Etapes.Where(e => e.Plan == TypePlan.RetourArriere).OrderBy(e => e.Ligne).ThenBy(e => e.Ordre).Select(LigneEtape),
                "Aucune étape de retour arrière.");

        Tableau(classeur, "Journal", $"Annexe : journal complet · {nom}", contexte + $" · {r.Journal.Count} événement(s)",
            ColonnesJournal, r.Journal.Select(e => LigneJournal(e, f)), "Aucun événement.");
        return classeur.Enregistrer();
    }
}
