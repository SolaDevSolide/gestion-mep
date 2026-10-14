using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace GestionMep.Domain;

// Règles métier : recette.
public static partial class Regles
{
    /// <summary>Nouvelle fiche de recette vide, ajoutée en fin (préparation uniquement). Le système doit appartenir à la MEP.</summary>
    public static FicheRecette AjouterFicheRecette(Mep mep, Guid systemeId, string titre)
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
    /// Une fiche reste dans son système : <paramref name="systemeId"/> null le garde ; il ne sert qu'à rattacher une
    /// ancienne fiche sans système.
    /// </summary>
    public static void ModifierFicheRecette(Mep mep, FicheRecette fiche, Guid? systemeId, string titre,
        IEnumerable<(Guid? Id, string Instructions, string ResultatAttendu)> etapes)
    {
        ExigerPreparation(mep);
        if (systemeId is { } s)
        {
            ExigerSystemeDeLaMep(mep, s);
            if (fiche.SystemeId is { } actuel && actuel != s)
                throw new RegleMetierException("Une fiche de recette reste dans son système : créez une nouvelle fiche dans l'autre système.");
        }
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
        fiche.SystemeId = systemeId ?? fiche.SystemeId;
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

    private static void ExigerSystemeDeLaMep(Mep mep, Guid systemeId)
    {
        if (mep.Systemes.All(x => x.Id != systemeId)) throw new RegleMetierException("Système inconnu dans cette MEP.");
    }

    /// <summary>Saisie d'un résultat de recette : participant relié à un compte (pilote et suppléants compris) ou administrateur, en phase de recette.</summary>
    public static void ExigerSaisieRecette(Mep mep, Guid utilisateurId, bool estAdmin)
    {
        if (!estAdmin && !mep.Participants.Any(p => p.UtilisateurId == utilisateurId))
            throw new RegleMetierException("Seuls les participants de la MEP reliés à un compte, le pilote ou un administrateur peuvent donner le résultat d'une recette.");
        if (mep.Phase != Phase.Recette) throw new RegleMetierException("Les recettes se déroulent en phase de recette.");
    }

    /// <summary>
    /// Résultat d'une étape de test, en phase de recette : par tout participant relié à un compte (pilote et suppléants compris)
    /// ou un administrateur ; observation obligatoire ; une étape déjà validée doit d'abord être refaite.
    /// </summary>
    public static void ValiderEtapeTest(Mep mep, FicheRecette fiche, EtapeTest test, ResultatControle resultat, string observation,
        Guid utilisateurId, bool estAdmin, DateTimeOffset maintenant)
    {
        ExigerSaisieRecette(mep, utilisateurId, estAdmin);
        if (test.Resultat is not null)
            throw new RegleMetierException($"{NomRecette(fiche)}, étape {test.Ordre} déjà validée : le pilote peut la faire refaire.");
        if (string.IsNullOrWhiteSpace(observation)) throw new RegleMetierException("Décrire ce qui a été observé.");
        test.Resultat = resultat;
        test.Observation = observation.Trim();
        test.ParId = utilisateurId;
        test.Le = maintenant;
        test.ValideePar = "";
        test.ValideeLe = null;
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
            t.ValideePar = "";
            t.ValideeLe = null;
        }
        mep.FichesRecette = [.. mep.FichesRecette];
    }

    // ---------- Bibliothèque de recettes ----------

    /// <summary>Anomalies d'un modèle de recette avant enregistrement : titre, au moins une étape, chaque étape complète.</summary>
    public static List<string> ErreursModeleRecette(ModeleRecette m)
    {
        var erreurs = new List<string>();
        if (string.IsNullOrWhiteSpace(m.Titre)) erreurs.Add("Donner un titre au modèle.");
        if (m.Etapes.Count == 0) erreurs.Add("Ajouter au moins une étape de test.");
        foreach (var t in m.Etapes.OrderBy(t => t.Ordre))
            if (string.IsNullOrWhiteSpace(t.Instructions) || string.IsNullOrWhiteSpace(t.ResultatAttendu))
                erreurs.Add($"Étape {t.Ordre} : compléter le déroulé et le résultat attendu.");
        return erreurs;
    }

    /// <summary>Étapes d'une fiche de la MEP, copiées pour un modèle (sans résultat).</summary>
    public static List<EtapeModeleRecette> EtapesPourModele(FicheRecette fiche) =>
        fiche.Etapes.OrderBy(t => t.Ordre).Select((t, i) => new EtapeModeleRecette
        {
            Ordre = i + 1, Instructions = t.Instructions.Trim(), ResultatAttendu = t.ResultatAttendu.Trim(),
        }).ToList();

    /// <summary>Insère dans la MEP (préparation) une copie d'un modèle de la bibliothèque, en fin de recette, dans le système donné.</summary>
    public static FicheRecette InsererModeleRecette(Mep mep, Guid systemeId, ModeleRecette modele)
    {
        var fiche = AjouterFicheRecette(mep, systemeId, modele.Titre);
        fiche.Etapes = modele.Etapes.OrderBy(t => t.Ordre).Select((t, i) => new EtapeTest
        {
            Ordre = i + 1, Instructions = t.Instructions, ResultatAttendu = t.ResultatAttendu,
        }).ToList();
        mep.FichesRecette = [.. mep.FichesRecette];
        return fiche;
    }

    // ---------- Validation par les métiers (fichier Excel) ----------

    public const string LibelleValide = "Validé";
    public const string LibelleRefuse = "Refusé";
    /// <summary>Observation retenue pour une étape validée sans résultat constaté.</summary>
    public const string ObservationValideeSansCommentaire = "Conforme au résultat attendu.";

    /// <summary>
    /// Version de la recette : empreinte du contenu des fiches (identifiants, système, titre, déroulé et résultat attendu
    /// des étapes), indépendante des résultats saisis. Un fichier exporté n'est réimporté que si elle n'a pas changé.
    /// </summary>
    public static string VersionRecette(Mep mep)
    {
        var sb = new StringBuilder();
        foreach (var f in mep.FichesRecette.OrderBy(f => f.Ordre))
        {
            sb.Append('F').Append(f.Id).Append('\u001f').Append(f.SystemeId).Append('\u001f').Append(f.Titre).Append('\u001e');
            foreach (var t in f.Etapes.OrderBy(t => t.Ordre))
                sb.Append('T').Append(t.Id).Append('\u001f').Append(t.Instructions).Append('\u001f').Append(t.ResultatAttendu).Append('\u001e');
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))[..16];
    }

    /// <summary>Une ligne d'étape lue dans le fichier : où elle est (pour les messages) et les cases remplies par le métier.</summary>
    /// <param name="Lieu">Ex. « WEB, ligne 7 (Connexion, étape 2) ».</param>
    /// <param name="DateLisible">Faux si la case date contient autre chose qu'une date.</param>
    public record LigneValidation(Guid FicheId, Guid EtapeId, string Lieu, string Validation, string Constate, string ValidePar,
        DateOnly? Date, bool DateLisible = true);

    /// <summary>Changement à appliquer à une étape de test.</summary>
    public record ChangementRecette(FicheRecette Fiche, EtapeTest Etape, ResultatControle Resultat, string Observation, string ValidePar, DateOnly? Date);

    /// <summary>
    /// Résultat de l'analyse d'un fichier : changements à appliquer, étapes déjà à jour, lignes sans validation (ignorées)
    /// et anomalies. Une seule anomalie suffit à refuser tout le fichier.
    /// </summary>
    public record AnalyseValidation(List<ChangementRecette> Changements, int Inchangees, int SansValidation, List<string> Erreurs);

    private static string SansAccents(string s)
    {
        var d = s.Normalize(NormalizationForm.FormD);
        return new string(d.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).ToUpperInvariant();
    }

    /// <summary>« Validé » ou « Refusé » (casse et accents indifférents) ; null pour toute autre valeur.</summary>
    public static ResultatControle? LireValidation(string valeur) => SansAccents(valeur.Trim()) switch
    {
        "VALIDE" => ResultatControle.Conforme,
        "REFUSE" => ResultatControle.Echec,
        _ => null,
    };

    /// <summary>
    /// Analyse les validations d'un fichier exporté : il doit venir de cette MEP (<paramref name="mepFichier"/>), d'une recette
    /// inchangée depuis l'export (<paramref name="versionFichier"/>) et chaque validation doit être exploitable (valeur connue,
    /// validé par renseigné, date lisible, résultat constaté pour un refus, pas de contradiction avec un résultat déjà saisi).
    /// Les lignes sans validation sont ignorées ; le fichier ne fait jamais refaire une étape.
    /// </summary>
    public static AnalyseValidation AnalyserValidation(Mep mep, Guid? mepFichier, string versionFichier, IReadOnlyList<LigneValidation> lignes)
    {
        var erreurs = new List<string>();
        var changements = new List<ChangementRecette>();
        var (inchangees, sansValidation) = (0, 0);
        if (mepFichier != mep.Id)
            return new([], 0, 0, ["Ce fichier est l'export de la recette d'une autre MEP."]);
        if (!string.Equals(versionFichier, VersionRecette(mep), StringComparison.Ordinal))
            erreurs.Add("La recette a été modifiée dans l'application depuis l'export de ce fichier : exportez-la de nouveau et reportez-y les validations.");

        var vues = new HashSet<Guid>();
        foreach (var l in lignes)
        {
            var fiche = mep.FichesRecette.FirstOrDefault(f => f.Id == l.FicheId);
            var etape = fiche?.Etapes.FirstOrDefault(t => t.Id == l.EtapeId);
            if (fiche is null || etape is null)
            {
                // Déjà couvert par le changement de version de la recette.
                if (erreurs.Count == 0) erreurs.Add($"{l.Lieu} : étape inconnue dans la recette.");
                continue;
            }
            if (!vues.Add(etape.Id))
            {
                erreurs.Add($"{l.Lieu} : étape présente deux fois dans le fichier.");
                continue;
            }
            if (l.Validation.Length == 0)
            {
                if (l.Constate.Length > 0 || l.ValidePar.Length > 0 || l.Date is not null || !l.DateLisible) sansValidation++;
                continue;
            }
            var anomalies = new List<string>();
            var resultat = LireValidation(l.Validation);
            if (resultat is null) anomalies.Add($"validation « {l.Validation} » inconnue ({LibelleValide} ou {LibelleRefuse} attendu)");
            if (l.ValidePar.Length == 0) anomalies.Add("indiquer qui a validé (colonne « Validé par »)");
            if (!l.DateLisible) anomalies.Add("date illisible (jour/mois/année attendu)");
            if (resultat == ResultatControle.Echec && l.Constate.Length == 0) anomalies.Add("décrire le résultat constaté du refus");
            if (anomalies.Count > 0)
            {
                erreurs.Add($"{l.Lieu} : {string.Join(" ; ", anomalies)}.");
                continue;
            }
            var observation = l.Constate.Length > 0 ? l.Constate : ObservationValideeSansCommentaire;
            if (etape.Resultat is { } actuel)
            {
                if (actuel == resultat && string.Equals(etape.Observation.Trim(), observation, StringComparison.Ordinal)) inchangees++;
                else
                    erreurs.Add($"{l.Lieu} : déjà « {(actuel == ResultatControle.Conforme ? "Conforme" : "Échec")} » dans l'application ; "
                        + "le pilote doit la faire refaire avant d'importer une autre validation.");
                continue;
            }
            changements.Add(new ChangementRecette(fiche, etape, resultat!.Value, observation, l.ValidePar, l.Date));
        }
        return new(changements, inchangees, sansValidation, erreurs);
    }

    /// <summary>
    /// Reporte les validations analysées (sans anomalie) : mêmes droits et même phase que la saisie dans l'application.
    /// Le compte qui importe est enregistré comme auteur ; le nom et la date saisis dans le fichier sont conservés.
    /// </summary>
    public static void AppliquerValidation(Mep mep, AnalyseValidation analyse, Guid utilisateurId, bool estAdmin, DateTimeOffset maintenant)
    {
        ExigerSaisieRecette(mep, utilisateurId, estAdmin);
        if (analyse.Erreurs.Count > 0) throw new RegleMetierException("Fichier refusé : " + string.Join(" ", analyse.Erreurs));
        foreach (var c in analyse.Changements)
        {
            c.Etape.Resultat = c.Resultat;
            c.Etape.Observation = c.Observation;
            c.Etape.ParId = utilisateurId;
            c.Etape.Le = maintenant;
            c.Etape.ValideePar = c.ValidePar;
            c.Etape.ValideeLe = c.Date;
        }
        mep.FichesRecette = [.. mep.FichesRecette];
    }
}
