namespace GestionMep.Domain;

// Règles métier : sections du plan (#60). Une section est un repère : seuls ses cercles comptent, à travers les préalables
// effectifs qu'ils donnent aux étapes (CompilerSections).
public static partial class Regles
{
    public const string TitreEntreeParDefaut = "Début";
    public const string TitreSortieParDefaut = "Fin";

    private static string TitreSection(string titre)
    {
        var t = titre.Trim();
        return t.Length == 0 ? throw new RegleMetierException("Le titre de la section est obligatoire.") : t;
    }

    /// <summary>Systèmes de la MEP, sans doublon (les autres sont refusés).</summary>
    private static List<Guid> SystemesDeLaMep(Mep mep, IEnumerable<Guid> systemeIds)
    {
        var ids = systemeIds.Distinct().ToList();
        if (ids.Any(id => mep.Systemes.All(s => s.Id != id))) throw new RegleMetierException("Système inconnu dans cette MEP.");
        return ids;
    }

    /// <summary>
    /// Crée une section (préparation) avec un cercle d'entrée « Début » et un cercle de sortie « Fin ». <paramref name="placee"/> faux :
    /// elle attend dans le bac.
    /// </summary>
    public static Section CreerSection(Mep mep, TypePlan plan, string titre, string description, IEnumerable<Guid> systemeIds, Guid? auteurId,
        DateTimeOffset maintenant, bool placee)
    {
        ExigerPreparation(mep);
        var section = new Section
        {
            Plan = plan, Titre = TitreSection(titre), Description = description.Trim(), SystemeIds = SystemesDeLaMep(mep, systemeIds),
            AuteurId = auteurId, CreeLe = maintenant, Placee = placee,
            Ports =
            [
                new PortSection { Sens = SensPort.Entree, Titre = TitreEntreeParDefaut, Ordre = 0 },
                new PortSection { Sens = SensPort.Sortie, Titre = TitreSortieParDefaut, Ordre = 0 },
            ],
        };
        mep.Sections.Add(section);
        return section;
    }

    /// <summary>Titre, description et systèmes d'une section (préparation).</summary>
    public static void ModifierSection(Mep mep, Section section, string titre, string description, IEnumerable<Guid> systemeIds)
    {
        ExigerPreparation(mep);
        section.Titre = TitreSection(titre);
        section.Description = description.Trim();
        section.SystemeIds = SystemesDeLaMep(mep, systemeIds);
        section.Revision++;
    }

    /// <summary>Place une section dans le graphe, ou la remet dans le bac. Renvoie faux si rien ne change.</summary>
    public static bool PlacerSection(Mep mep, Section section, bool placee)
    {
        ExigerPreparation(mep);
        if (section.Placee == placee) return false;
        section.Placee = placee;
        section.Revision++;
        return true;
    }

    /// <summary>
    /// Supprime une section (préparation) : ses étapes restent dans le plan, hors section, avec leurs préalables effectifs (ceux
    /// qui venaient de ses cercles deviennent directs) ; ses cercles disparaissent, et avec eux les liens qui y menaient.
    /// </summary>
    public static void SupprimerSection(Mep mep, Section section) =>
        Operer(mep, section.Plan, () =>
        {
            mep.Sections.RemoveAll(s => s.Id == section.Id);
            foreach (var e in mep.Etapes.Where(e => e.SectionId == section.Id)) e.SectionId = null;
        }, figer: true);

    /// <summary>Ajoute un cercle (préparation) à droite de ceux du même sens ; sans titre : « Entrée 2 », « Sortie 3 »…</summary>
    public static PortSection AjouterPort(Mep mep, Section section, SensPort sens, string titre, string description)
    {
        ExigerPreparation(mep);
        var memeSens = section.Ports.Where(p => p.Sens == sens).ToList();
        var t = titre.Trim();
        var port = new PortSection
        {
            Sens = sens, Description = description.Trim(),
            Titre = t.Length > 0 ? t : $"{(sens == SensPort.Entree ? "Entrée" : "Sortie")} {memeSens.Count + 1}",
            Ordre = memeSens.Count == 0 ? 0 : memeSens.Max(p => p.Ordre) + 1,
        };
        section.Ports.Add(port);
        section.Revision++;
        return port;
    }

    /// <summary>Titre et description d'un cercle (préparation).</summary>
    public static void ModifierPort(Mep mep, Section section, PortSection port, string titre, string description)
    {
        ExigerPreparation(mep);
        var t = titre.Trim();
        if (t.Length == 0) throw new RegleMetierException("Le titre du cercle est obligatoire.");
        port.Titre = t;
        port.Description = description.Trim();
        section.Revision++;
    }

    /// <summary>
    /// Supprime un cercle (préparation) : les liens qui passaient par lui disparaissent, avec les préalables qu'il donnait aux
    /// étapes (un préalable aussi saisi directement reste).
    /// </summary>
    public static void SupprimerPort(Mep mep, Section section, PortSection port) =>
        Operer(mep, section.Plan, () =>
        {
            var s = mep.Sections.First(x => x.Id == section.Id);
            s.Ports.RemoveAll(p => p.Id == port.Id);
            var memeSens = s.Ports.Where(p => p.Sens == port.Sens).OrderBy(p => p.Ordre).ToList();
            for (var i = 0; i < memeSens.Count; i++) memeSens[i].Ordre = i;
        });

    /// <summary>Nouvel ordre des cercles d'un sens (tous, chacun une fois), de gauche à droite.</summary>
    public static void OrdonnerPorts(Mep mep, Section section, SensPort sens, IReadOnlyList<Guid> ordre)
    {
        ExigerPreparation(mep);
        var ports = section.Ports.Where(p => p.Sens == sens).ToList();
        if (ordre.Count != ports.Count || ordre.Distinct().Count() != ordre.Count || ports.Any(p => !ordre.Contains(p.Id)))
            throw new RegleMetierException("Donner tous les cercles de ce bord, chacun une fois.");
        foreach (var p in ports) p.Ordre = ordre.ToList().IndexOf(p.Id);
        section.Revision++;
    }

    /// <summary>Retire un système supprimé de la MEP des sections qui le citaient.</summary>
    public static void RetirerSystemeDesSections(Mep mep, Guid systemeId)
    {
        foreach (var s in mep.Sections.Where(s => s.SystemeIds.Contains(systemeId)))
        {
            s.SystemeIds = s.SystemeIds.Where(id => id != systemeId).ToList();
            s.Revision++;
        }
    }

    /// <summary>« Vérifier le plan » : une section restée dans le bac bloque le Go (plans vérifiés seulement).</summary>
    public static IEnumerable<string> ErreursSections(Mep mep)
    {
        foreach (var s in mep.Sections.Where(s => !s.Placee && (s.Plan == TypePlan.Deploiement || mep.RetourArriereActive)).OrderBy(s => s.Plan).ThenBy(s => s.CreeLe))
            yield return $"{(s.Plan == TypePlan.RetourArriere ? "Retour arrière : s" : "S")}ection « {s.Titre} » dans le bac : la placer dans le plan ou la supprimer.";
    }

    /// <summary>Avertissements des sections (non bloquants) : cercle sans étape, cercle d'entrée sans source.</summary>
    public static IEnumerable<string> AvertissementsSections(Mep mep)
    {
        foreach (var s in mep.Sections.Where(s => s.Plan == TypePlan.Deploiement || mep.RetourArriereActive).OrderBy(s => s.Plan).ThenBy(s => s.CreeLe))
            foreach (var p in s.Ports.OrderBy(p => p.Sens).ThenBy(p => p.Ordre))
            {
                var nom = $"Section « {s.Titre} », cercle {(p.Sens == SensPort.Entree ? "d'entrée" : "de sortie")} « {p.Titre} »";
                if (p.Etapes.Count == 0) yield return $"{nom} : aucune étape n'y est reliée.";
                if (p.Sens == SensPort.Entree && p.Sources.Count == 0) yield return $"{nom} : rien n'y arrive.";
            }
    }
}
