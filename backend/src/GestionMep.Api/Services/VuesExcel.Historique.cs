using GestionMep.Domain;
using GestionMep.Tableur;

namespace GestionMep.Api.Services;

public static partial class VuesExcel
{
    // ---------- Historique d'un système (#59) ----------

    /// <summary>
    /// Historique des MEP d'un système du catalogue, comme l'onglet « Historique des MEP » de sa fiche : synthèse, une ligne
    /// par MEP (la plus récente d'abord), puis une ligne par échec d'étape survenu sur ce système.
    /// </summary>
    public static byte[] Historique(HistoriqueSystemeDto h, DateTimeOffset maintenant, PaletteExcel? palette = null)
    {
        var titre = $"Historique des MEP · {h.Nom}";
        using var classeur = ExcelCommun.Nouveau(titre, maintenant, null, palette);
        var r = h.Resume;
        var contexte = $"{r.Total} MEP · {r.DouzeMois} sur les 12 derniers mois · modèles exclus";
        var derniere = h.Meps.FirstOrDefault(m => m.Id == r.DerniereMepId);
        Fiche(classeur, "Synthèse", titre, contexte,
        [
            ("Système", h.Nom),
            ("MEP au total", r.Total), ("Sur les 12 derniers mois", r.DouzeMois), ("En cours", r.EnCours), ("Clôturées", r.Cloturees),
            ("Réussies", r.Reussies), ("Réussies avec incidents", r.AvecIncidents), ("Après retour arrière", r.Repliees),
            ("Échecs d'étape sur ce système", r.Echecs), ("Dont repris avec succès", r.EchecsRepris),
            ("Dernière MEP", derniere is null ? "" : $"{ExcelCommun.NomMep(derniere.Outil, derniere.VersionCible)} · {derniere.Jour:dd/MM/yyyy} · {FiltreMeps.Libelle(derniere.Etat)}"),
        ]);
        Tableau(classeur, "MEP", titre, contexte,
            [Col("Jour", 12, TypeColonne.Date), Col("Outil", 24), Col("Version cible", 12) with { Facultative = true }, Col("Objet", 40),
                Col("Type", 11, TypeColonne.Court), Col("État", 20, TypeColonne.Court) with { Tons = ExcelCommun.TonsEtatMep },
                Col("Go", 16, TypeColonne.DateHeure) with { Facultative = true }, Col("Clôturée le", 16, TypeColonne.DateHeure) with { Facultative = true },
                Col("Pilote", 18) with { Facultative = true }, Col($"Environnements de {h.Nom}", 26) with { Facultative = true },
                Col("Autres systèmes", 30) with { Facultative = true }, Col($"Échecs sur {h.Nom}", 10, TypeColonne.Entier), ColIdentifiant()],
            h.Meps.Select(m => new object?[]
            {
                m.Jour, m.Outil, m.Type == TypeMep.Simplifiee ? "" : m.VersionCible, m.Objet, Libelle(m.Type), FiltreMeps.Libelle(m.Etat),
                Quand(m.Go, null), Quand(m.ClotureeLe, null), m.Pilote ?? "", string.Join(", ", m.Environnements.Select(e => e.Nom)),
                SystemesEtEnvironnements(m.AutresSystemes), m.Echecs.Count, m.Id.ToString(),
            }),
            $"Aucune MEP n'a encore touché {h.Nom}.", colonnesFigees: 2);
        var echecs = h.Meps.SelectMany(m => m.Echecs.Select(e => (Mep: m, Echec: e))).ToList();
        Tableau(classeur, "Échecs", $"Échecs d'étape · {h.Nom}", $"{echecs.Count} échec{(echecs.Count > 1 ? "s" : "")} · une étape reprise garde son échec",
            [Col("MEP", 30), Col("Jour", 12, TypeColonne.Date), Col("Plan", 14, TypeColonne.Court), Col("Étape", 8, TypeColonne.Code), Col("Action", 36),
                Col("Échec le", 16, TypeColonne.DateHeure), Col("Motif", 40), Col("Résolution", 30) with { Facultative = true },
                Col("Statut de la résolution", 14, TypeColonne.Court) with { Tons = ExcelCommun.TonsResolution, Facultative = true },
                Col("Reprise réussie", 11, TypeColonne.Court), ColIdentifiant()],
            echecs.Select(x => new object?[]
            {
                ExcelCommun.NomMep(x.Mep.Outil, x.Mep.VersionCible), x.Mep.Jour, x.Echec.Plan == TypePlan.RetourArriere ? "Retour arrière" : "Déploiement",
                x.Echec.Code, x.Echec.Action, Quand(x.Echec.EchecLe, null), x.Echec.Motif, x.Echec.Resolution ?? "",
                x.Echec.StatutResolution is { } st ? Libelle(st) : "", OuiNon(x.Echec.Repris), x.Mep.Id.ToString(),
            }),
            $"Aucun échec d'étape sur {h.Nom}.", colonnesFigees: 1);
        return classeur.Enregistrer();
    }
}
