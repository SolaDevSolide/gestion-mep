using System.Text.Json;

namespace GestionMep.Domain;

// Règles métier : structure du plan. Graphe libre (#76) : seuls les liens comptent, la place des cartes est calculée par
// l'interface. Ligne et Ordre ne se saisissent plus : ils sont recalculés après chaque opération (profondeur dans le graphe).
public static partial class Regles
{
    /// <summary>
    /// Lignes d'un plan, de haut en bas : étapes regroupées par <see cref="Etape.Ligne"/> (profondeur dans le graphe), chacune triée
    /// par <see cref="Etape.Ordre"/>. Lecture seule (exports, ligne de commande).
    /// </summary>
    public static List<List<Etape>> Lignes(Mep mep, TypePlan plan) =>
        mep.EtapesDu(plan).GroupBy(e => e.Ligne).OrderBy(g => g.Key).Select(g => g.ToList()).ToList();

    /// <summary>Recalcule Ligne (0..n-1, lignes vides supprimées) et Ordre (0..k-1) depuis l'ordre des listes.</summary>
    private static void Renumeroter(IEnumerable<List<Etape>> lignes)
    {
        var n = 0;
        foreach (var ligne in lignes.Where(l => l.Count > 0))
        {
            for (var i = 0; i < ligne.Count; i++)
            {
                ligne[i].Ligne = n;
                ligne[i].Ordre = i;
            }
            n++;
        }
    }

    /// <summary>
    /// Ligne et ordre des étapes d'un plan : profondeur dans le graphe des dépendances effectives (0 sans préalable, sinon 1 + la
    /// plus grande ligne des préalables) ; dans une ligne, l'ancienne place (ligne, puis ordre, puis code) donne la position, pour
    /// qu'un ajout ailleurs ne réordonne pas le reste. Sert aussi aux étapes d'avant le jalon 4 (ligne -1).
    /// </summary>
    public static void CalculerLignes(IReadOnlyList<Etape> etapesDuPlan)
    {
        var parId = etapesDuPlan.ToDictionary(e => e.Id);
        var profondeur = new Dictionary<Guid, int>();
        var enCours = new HashSet<Guid>();
        int Profondeur(Etape e)
        {
            if (profondeur.TryGetValue(e.Id, out var p)) return p;
            if (!enCours.Add(e.Id)) return 0; // cycle (données invalides) : on coupe
            var prealables = e.Dependances.Where(d => d != e.Id && parId.ContainsKey(d)).Select(d => Profondeur(parId[d])).ToList();
            enCours.Remove(e.Id);
            return profondeur[e.Id] = prealables.Count == 0 ? 0 : prealables.Max() + 1;
        }
        foreach (var e in etapesDuPlan) Profondeur(e);
        Renumeroter(etapesDuPlan.GroupBy(e => profondeur[e.Id]).OrderBy(g => g.Key)
            .Select(g => g.OrderBy(e => e.Ligne).ThenBy(e => e.Ordre).ThenBy(e => e.Code, StringComparer.Ordinal).ToList()));
    }

    /// <summary>Équivalent de InitialiserAction : prochain code E0001… (ou R0001… pour le retour arrière) sans jamais réutiliser un numéro.</summary>
    public static string ProchainCode(IEnumerable<Etape> etapes, TypePlan plan = TypePlan.Deploiement)
    {
        var prefixe = plan == TypePlan.Deploiement ? 'E' : 'R';
        var max = 0;
        foreach (var e in etapes)
            if (e.Code.Length > 1 && e.Code[0] == prefixe && int.TryParse(e.Code[1..], out var n) && n > max) max = n;
        return $"{prefixe}{max + 1:0000}";
    }

    // ---------- Nœuds et liens ----------

    /// <summary>Extrémité d'un lien du graphe : une étape, ou un cercle de section (avec sa section).</summary>
    public sealed record Noeud(Guid Id, Etape? Etape, Section? Section, PortSection? Port)
    {
        public bool EstEntree => Port is { Sens: SensPort.Entree };
        public bool EstSortie => Port is { Sens: SensPort.Sortie };
        public override string ToString() => Etape is { } e ? e.Code : $"le cercle « {Port!.Titre} » de la section « {Section!.Titre} »";
    }

    /// <summary>Étape ou cercle de ce plan portant cet Id ; null s'il n'y en a pas.</summary>
    public static Noeud? TrouverNoeud(Mep mep, TypePlan plan, Guid id)
    {
        var e = mep.Etapes.FirstOrDefault(x => x.Id == id && x.Plan == plan);
        if (e is not null) return new(id, e, null, null);
        foreach (var s in mep.Sections.Where(s => s.Plan == plan))
            if (s.Ports.FirstOrDefault(p => p.Id == id) is { } port) return new(id, null, s, port);
        return null;
    }

    private static Noeud ExigerNoeud(Mep mep, TypePlan plan, Guid id) =>
        TrouverNoeud(mep, plan, id) ?? throw new SaisieInvalideException("Étape ou cercle introuvable dans ce plan.");

    private static Noeud NoeudDe(Etape e) => new(e.Id, e, null, null);

    /// <summary>Préalables directs (saisis) : les préalables effectifs moins ceux qui viennent des cercles.</summary>
    public static List<Guid> Directes(Etape e) => e.Dependances.Where(d => !e.DependancesDerivees.Contains(d)).ToList();

    /// <summary>
    /// Relie deux nœuds : <paramref name="vers"/> attend <paramref name="de"/>. Liens possibles : étape → étape (préalable direct),
    /// étape hors de la section ou cercle de sortie d'une autre section → cercle d'entrée, cercle d'entrée → étape de sa section,
    /// étape de la section → cercle de sortie, cercle de sortie → étape hors de sa section. Un lien déjà là ne change rien.
    /// </summary>
    private static void Lier(Noeud de, Noeud vers)
    {
        if (de.Id == vers.Id) throw new RegleMetierException("Une étape ne peut pas s'attendre elle-même.");
        switch (de, vers)
        {
            case ({ Etape: { } a }, { Etape: { } b }):
                // Préalable déjà là par un cercle : il devient aussi direct (il reste si le cercle est délié).
                if (b.DependancesDerivees.Contains(a.Id)) b.DependancesDerivees = b.DependancesDerivees.Where(d => d != a.Id).ToList();
                else if (!b.Dependances.Contains(a.Id)) b.Dependances = [.. b.Dependances, a.Id];
                break;
            case ({ Etape: { } a }, { EstEntree: true, Section: { } s, Port: { } p }):
                if (a.SectionId == s.Id)
                    throw new RegleMetierException($"{a.Code} fait partie de la section « {s.Titre} » : un cercle d'entrée reçoit ce qui vient de l'extérieur de la section.");
                Ajouter(p.Sources, a.Id);
                break;
            case ({ EstSortie: true, Section: { } s1, Port: { } q }, { EstEntree: true, Section: { } s2, Port: { } p }):
                if (s1.Id == s2.Id) throw new RegleMetierException("Un cercle de sortie ne peut pas alimenter un cercle d'entrée de la même section.");
                Ajouter(p.Sources, q.Id);
                break;
            case ({ EstEntree: true, Section: { } s, Port: { } p }, { Etape: { } b }):
                if (b.SectionId != s.Id)
                    throw new RegleMetierException($"{b.Code} ne fait pas partie de la section « {s.Titre} » : seules ses étapes attendent ses cercles d'entrée.");
                Ajouter(p.Etapes, b.Id);
                break;
            case ({ Etape: { } a }, { EstSortie: true, Section: { } s, Port: { } q }):
                if (a.SectionId != s.Id)
                    throw new RegleMetierException($"{a.Code} ne fait pas partie de la section « {s.Titre} » : un cercle de sortie n'attend que des étapes de sa section.");
                Ajouter(q.Etapes, a.Id);
                break;
            case ({ EstSortie: true, Section: { } s, Port: { } q }, { Etape: { } b }):
                if (b.SectionId == s.Id)
                    throw new RegleMetierException($"{b.Code} fait partie de la section « {s.Titre} » : une étape de la section n'attend pas ses propres cercles de sortie.");
                if (!b.PortsAttendus.Contains(q.Id)) b.PortsAttendus = [.. b.PortsAttendus, q.Id];
                break;
            default:
                throw new RegleMetierException("Lien impossible : un cercle se relie à une étape, ou un cercle de sortie au cercle d'entrée d'une autre section.");
        }
    }

    private static void Ajouter(List<Guid> liste, Guid id)
    {
        if (!liste.Contains(id)) liste.Add(id);
    }

    /// <summary>Le lien « <paramref name="vers"/> attend <paramref name="de"/> » existe (préalable direct, ou lien d'un cercle).</summary>
    private static bool ExisteLien(Noeud de, Noeud vers) => (de, vers) switch
    {
        ({ Etape: { } a }, { Etape: { } b }) => Directes(b).Contains(a.Id),
        (_, { EstEntree: true, Port: { } p }) => p.Sources.Contains(de.Id),
        ({ EstEntree: true, Port: { } p }, { Etape: { } b }) => p.Etapes.Contains(b.Id),
        ({ Etape: { } a }, { EstSortie: true, Port: { } q }) => q.Etapes.Contains(a.Id),
        ({ EstSortie: true, Port: { } q }, { Etape: { } b }) => b.PortsAttendus.Contains(q.Id),
        _ => false,
    };

    /// <summary>Retire le lien « <paramref name="vers"/> attend <paramref name="de"/> » (422 s'il n'existe pas).</summary>
    private static void Delier(Noeud de, Noeud vers)
    {
        if (de.Etape is { } a && vers.Etape is { } b && !Directes(b).Contains(a.Id) && b.Dependances.Contains(a.Id))
            throw new RegleMetierException($"{b.Code} attend {a.Code} par un cercle de section : délier le cercle.");
        if (!ExisteLien(de, vers)) throw new SaisieInvalideException($"Lien introuvable : {vers} n'attend pas {de}.");
        switch (de, vers)
        {
            case ({ Etape: { } x }, { Etape: { } y }):
                y.Dependances = y.Dependances.Where(d => d != x.Id).ToList();
                break;
            case (_, { EstEntree: true, Port: { } p }):
                p.Sources.Remove(de.Id);
                break;
            case ({ EstEntree: true, Port: { } p }, { Etape: { } y }):
                p.Etapes.Remove(y.Id);
                break;
            case ({ Etape: { } x }, { EstSortie: true, Port: { } q }):
                q.Etapes.Remove(x.Id);
                break;
            case ({ EstSortie: true, Port: { } q }, { Etape: { } y }):
                y.PortsAttendus = y.PortsAttendus.Where(id => id != q.Id).ToList();
                break;
        }
    }

    /// <summary>Ce que l'étape attend directement : étapes (préalables directs), cercles d'entrée de sa section, cercles de sortie attendus.</summary>
    private static List<Noeud> Entrants(Mep mep, Etape e)
    {
        var res = Directes(e).Select(d => TrouverNoeud(mep, e.Plan, d)).OfType<Noeud>().Where(n => n.Etape is not null).ToList();
        if (mep.Sections.FirstOrDefault(s => s.Id == e.SectionId) is { } section)
            res.AddRange(section.Ports.Where(p => p.Sens == SensPort.Entree && p.Etapes.Contains(e.Id)).OrderBy(p => p.Ordre)
                .Select(p => new Noeud(p.Id, null, section, p)));
        res.AddRange(e.PortsAttendus.Select(q => TrouverNoeud(mep, e.Plan, q)).OfType<Noeud>());
        return res;
    }

    /// <summary>Ce qui attend directement l'étape : étapes, cercles d'entrée dont elle est une source, cercles de sortie de sa section.</summary>
    private static List<Noeud> Sortants(Mep mep, Etape e)
    {
        var res = mep.EtapesDu(e.Plan).Where(x => x != e && Directes(x).Contains(e.Id)).Select(NoeudDe).ToList();
        foreach (var s in mep.SectionsDu(e.Plan))
            res.AddRange(s.Ports.OrderBy(p => p.Ordre)
                .Where(p => p.Sens == SensPort.Entree ? p.Sources.Contains(e.Id) : s.Id == e.SectionId && p.Etapes.Contains(e.Id))
                .Select(p => new Noeud(p.Id, null, s, p)));
        return res;
    }

    /// <summary>
    /// L'étape quitte sa place : tous ses liens sont retirés, et ce qui l'attendait attend désormais ce qu'elle attendait
    /// (quand ce lien est possible), pour que l'ordre du reste du plan ne change pas.
    /// </summary>
    private static void Detacher(Mep mep, Etape e)
    {
        var moi = NoeudDe(e);
        var entrants = Entrants(mep, e);
        var sortants = Sortants(mep, e);
        foreach (var i in entrants) Delier(i, moi);
        foreach (var o in sortants) Delier(moi, o);
        foreach (var o in sortants)
            foreach (var i in entrants.Where(i => i.Id != o.Id))
            {
                // Pas de lien redondant : une étape qui attend déjà l'autre par un autre chemin reste comme elle est.
                if (i.Etape is { } a && o.Etape is { } b && AttendDeja(mep, b, a, [e.Id])) continue;
                try { Lier(i, o); }
                catch (RegleMetierException) { /* lien impossible (ex. entrée et sortie d'une même section) : ignoré */ }
            }
    }

    /// <summary><paramref name="vers"/> attend déjà <paramref name="de"/>, directement ou non, sans passer par <paramref name="exclues"/>.</summary>
    private static bool AttendDeja(Mep mep, Etape vers, Etape de, HashSet<Guid> exclues)
    {
        var parId = mep.Etapes.Where(x => x.Plan == vers.Plan).ToDictionary(x => x.Id);
        var vues = new HashSet<Guid>();
        var pile = new Stack<Guid>(vers.Dependances);
        while (pile.Count > 0)
        {
            var id = pile.Pop();
            if (exclues.Contains(id) || !vues.Add(id)) continue;
            if (id == de.Id) return true;
            if (parId.TryGetValue(id, out var x)) foreach (var d in x.Dependances) pile.Push(d);
        }
        return false;
    }

    // ---------- Compilation des cercles ----------

    /// <summary>
    /// Recalcule les préalables effectifs du plan à partir des cercles : <c>Dependances = (Dependances − anciennes dérivées) ∪
    /// nouvelles dérivées</c>. Les dérivées d'une étape sont, pour chaque cercle d'entrée de sa section qui la contient, ses
    /// sources résolues (étape : elle-même ; cercle de sortie : ses étapes) et, pour chaque cercle de sortie qu'elle attend, ses
    /// étapes. Les références devenues invalides (étape ou cercle disparus, étape sortie de la section) sont retirées.
    /// Avec <paramref name="figer"/>, les préalables dérivés qui ne viennent plus d'aucun cercle deviennent directs au lieu de
    /// disparaître (suppression d'une section : les liens restent). Sans section, rien ne change.
    /// </summary>
    public static void CompilerSections(Mep mep, TypePlan plan, bool figer = false)
    {
        var etapes = mep.Etapes.Where(e => e.Plan == plan).ToList();
        var parId = etapes.ToDictionary(e => e.Id);
        var sections = mep.Sections.Where(s => s.Plan == plan).ToList();
        var sectionsParId = sections.ToDictionary(s => s.Id);
        foreach (var e in etapes.Where(e => e.SectionId is { } sid && !sectionsParId.ContainsKey(sid))) e.SectionId = null;
        var sorties = sections.SelectMany(s => s.Ports.Where(p => p.Sens == SensPort.Sortie).Select(p => (Section: s, Port: p)))
            .ToDictionary(x => x.Port.Id);

        foreach (var s in sections)
            foreach (var p in s.Ports)
            {
                p.Etapes = p.Etapes.Distinct().Where(id => parId.TryGetValue(id, out var e) && e.SectionId == s.Id).ToList();
                p.Sources = p.Sens == SensPort.Sortie ? [] : p.Sources.Distinct()
                    .Where(id => parId.TryGetValue(id, out var e) ? e.SectionId != s.Id : sorties.TryGetValue(id, out var q) && q.Section.Id != s.Id)
                    .ToList();
            }

        foreach (var e in etapes)
        {
            var attendus = e.PortsAttendus.Distinct().Where(id => sorties.TryGetValue(id, out var q) && q.Section.Id != e.SectionId).ToList();
            if (!attendus.SequenceEqual(e.PortsAttendus)) e.PortsAttendus = attendus;

            var derivees = new List<Guid>();
            if (e.SectionId is { } sid)
                foreach (var p in sectionsParId[sid].Ports.Where(p => p.Sens == SensPort.Entree && p.Etapes.Contains(e.Id)).OrderBy(p => p.Ordre))
                    foreach (var source in p.Sources)
                        if (sorties.TryGetValue(source, out var q)) derivees.AddRange(q.Port.Etapes);
                        else derivees.Add(source);
            foreach (var q in e.PortsAttendus) derivees.AddRange(sorties[q].Port.Etapes);
            derivees = derivees.Distinct().ToList();

            if (figer) e.DependancesDerivees = e.DependancesDerivees.Where(derivees.Contains).ToList();
            var directes = Directes(e);
            var nouvelles = derivees.Where(d => !directes.Contains(d)).ToList();
            var effectives = directes.Concat(nouvelles).ToList();
            if (!effectives.ToHashSet().SetEquals(e.Dependances)) e.Dependances = effectives;
            if (!nouvelles.ToHashSet().SetEquals(e.DependancesDerivees)) e.DependancesDerivees = nouvelles;
        }
    }

    // ---------- Opérations ----------

    private sealed record Etat(List<Guid> Dependances, List<Guid> Derivees, Guid? SectionId, List<Guid> PortsAttendus);

    private static readonly JsonSerializerOptions OptionsCopie = new();

    private static List<Section> Copier(List<Section> sections) =>
        JsonSerializer.Deserialize<List<Section>>(JsonSerializer.Serialize(sections, OptionsCopie), OptionsCopie)!;

    private static string Signature(Section s) => JsonSerializer.Serialize(s, OptionsCopie);

    /// <summary>
    /// Opération sur le graphe d'un plan (préparation) : <paramref name="action"/> change les liens, puis les cercles sont compilés
    /// et le graphe vérifié. Un graphe invalide (cycle, auto-lien, lien vers un autre plan) annule tout et lève l'erreur. Sinon, la
    /// révision des étapes dont les préalables effectifs ont changé (sauf <paramref name="sauf"/>) et celle des sections modifiées
    /// augmentent une fois, puis les lignes du plan sont recalculées.
    /// </summary>
    private static void Operer(Mep mep, TypePlan plan, Action action, Etape? sauf = null, bool figer = false, bool apresGo = false)
    {
        // Seule exception à la préparation : l'étape corrective ajoutée pendant l'exécution (AjouterEtapeCorrective).
        if (!apresGo) ExigerPreparation(mep);
        var etapes = mep.Etapes.ToList();
        var avant = etapes.ToDictionary(e => e, e => new Etat([.. e.Dependances], [.. e.DependancesDerivees], e.SectionId, [.. e.PortsAttendus]));
        // État des sections à remettre en place (mêmes objets) si l'opération est refusée.
        var sections = mep.Sections.ToList();
        var copies = Copier(mep.Sections).ToDictionary(s => s.Id);
        var ports = sections.ToDictionary(s => s.Id, s => s.Ports.ToList());
        var signatures = mep.Sections.ToDictionary(s => s.Id, Signature);
        try
        {
            action();
            CompilerSections(mep, plan, figer);
            var erreur = ErreurGraphe(mep, plan);
            if (erreur is not null)
                throw new RegleMetierException(erreur == "Cycle de dépendances"
                    ? "Opération refusée : elle créerait un cycle (une étape s'attendrait elle-même, directement ou par un cercle de section)."
                    : erreur);
        }
        catch
        {
            foreach (var e in mep.Etapes.Except(etapes).ToList()) mep.Etapes.Remove(e);
            foreach (var e in etapes.Except(mep.Etapes).ToList()) mep.Etapes.Add(e);
            foreach (var (e, etat) in avant)
            {
                if (!e.Dependances.SequenceEqual(etat.Dependances)) e.Dependances = etat.Dependances;
                if (!e.DependancesDerivees.SequenceEqual(etat.Derivees)) e.DependancesDerivees = etat.Derivees;
                e.SectionId = etat.SectionId;
                if (!e.PortsAttendus.SequenceEqual(etat.PortsAttendus)) e.PortsAttendus = etat.PortsAttendus;
            }
            mep.Sections.Clear();
            mep.Sections.AddRange(sections);
            foreach (var section in sections)
            {
                section.Ports.Clear();
                section.Ports.AddRange(ports[section.Id]);
                foreach (var port in section.Ports)
                {
                    var copie = copies[section.Id].Ports.Single(p => p.Id == port.Id);
                    (port.Etapes, port.Sources, port.Ordre) = (copie.Etapes, copie.Sources, copie.Ordre);
                }
            }
            throw;
        }
        // Une étape du bac qui reçoit un lien (fiche d'une autre étape, lien tracé) revient dans le graphe : au bac, jamais de lien.
        foreach (var e in mep.EtapesDu(plan).Where(e => e.APlacer && AUnLien(mep, e)).ToList()) e.APlacer = false;
        foreach (var (e, etat) in avant)
            if (e != sauf && mep.Etapes.Contains(e) && !e.Dependances.ToHashSet().SetEquals(etat.Dependances)) e.Revision++;
        foreach (var s in mep.Sections)
            if (signatures.TryGetValue(s.Id, out var signature) && signature != Signature(s)) s.Revision++;
        CalculerLignes(mep.EtapesDu(plan).ToList());
    }

    /// <summary>
    /// Place d'une étape dans le graphe (au plus une option) : <c>Apres</c>, elle attend ce nœud (étape ou cercle) sans toucher
    /// aux autres liens ; <c>EntreDe</c>/<c>EntreVers</c>, elle s'insère sur ce lien (<c>EntreVers</c> n'attend plus
    /// <c>EntreDe</c> directement : il attend l'étape, qui attend <c>EntreDe</c>) ; <c>ParalleleA</c>, mêmes préalables et mêmes
    /// suivants que cette étape ; rien : étape isolée.
    /// </summary>
    public record PlaceEtape(Guid? Apres = null, Guid? EntreDe = null, Guid? EntreVers = null, Guid? ParalleleA = null)
    {
        public bool Vide => Apres is null && EntreDe is null && EntreVers is null && ParalleleA is null;
    }

    private sealed record PlaceResolue(Noeud? Apres, Noeud? De, Noeud? Vers, Etape? Parallele);

    private static PlaceResolue Resoudre(Mep mep, TypePlan plan, PlaceEtape place)
    {
        var options = (place.Apres is null ? 0 : 1) + (place.EntreDe is null && place.EntreVers is null ? 0 : 1) + (place.ParalleleA is null ? 0 : 1);
        if (options > 1) throw new SaisieInvalideException("Choisir une seule place : après une carte, sur un lien, ou en parallèle d'une carte.");
        if ((place.EntreDe is null) != (place.EntreVers is null)) throw new SaisieInvalideException("Un lien se désigne par ses deux extrémités.");
        var parallele = place.ParalleleA is { } p
            ? ExigerNoeud(mep, plan, p).Etape ?? throw new SaisieInvalideException("En parallèle : choisir une étape, pas un cercle.")
            : null;
        return new(place.Apres is { } a ? ExigerNoeud(mep, plan, a) : null,
            place.EntreDe is { } d ? ExigerNoeud(mep, plan, d) : null,
            place.EntreVers is { } v ? ExigerNoeud(mep, plan, v) : null, parallele);
    }

    /// <summary>Section du côté où arrive un lien : une étape est dans sa section ; un cercle d'entrée a sa section en aval, un cercle de sortie en amont.</summary>
    private static Guid? Cote(Noeud n, bool commeOrigine) =>
        n.Etape is { } e ? e.SectionId : (n.EstEntree == commeOrigine ? n.Section!.Id : null);

    private static Guid? SectionDeduite(PlaceResolue place)
    {
        if (place.Apres is { } a) return Cote(a, true);
        if (place.De is { } de)
        {
            var amont = Cote(de, true);
            return amont == Cote(place.Vers!, false) ? amont : null;
        }
        return place.Parallele?.SectionId;
    }

    /// <summary>
    /// Indice de place avant le recalcul des lignes (<see cref="CalculerLignes"/> trie une ligne par ancienne place) : en
    /// parallèle, juste à droite de la carte ; sur un lien, à côté de la carte d'arrivée ; sinon en bout de ligne.
    /// </summary>
    private static void IndiceDePlace(Etape etape, PlaceResolue place)
    {
        (etape.Ligne, etape.Ordre) = place switch
        {
            { Parallele: { } c } => (c.Ligne, c.Ordre),
            { Vers.Etape: { } v } => (v.Ligne, v.Ordre),
            _ => (int.MaxValue, int.MaxValue),
        };
    }

    private static void Inserer(Mep mep, Etape etape, PlaceResolue place)
    {
        IndiceDePlace(etape, place);
        RangApres(mep, etape, place.Parallele ?? place.Vers?.Etape ?? place.Apres?.Etape ?? place.De?.Etape);
        var moi = NoeudDe(etape);
        if (place.Apres is { } a) Lier(a, moi);
        else if (place.De is { } de)
        {
            var vers = place.Vers!;
            if (!ExisteLien(de, vers)) throw new SaisieInvalideException($"Lien introuvable : {vers} n'attend pas {de}.");
            Delier(de, vers);
            Lier(de, moi);
            Lier(moi, vers);
        }
        else if (place.Parallele is { } c)
        {
            foreach (var i in Entrants(mep, c)) Lier(i, moi);
            foreach (var o in Sortants(mep, c)) Lier(moi, o);
        }
    }

    /// <summary>
    /// Création rapide dans une section : une étape sans préalable dans la section attend le premier cercle d'entrée, une étape
    /// que rien n'attend dans la section est attendue par le premier cercle de sortie.
    /// </summary>
    private static void RelierAuxCercles(Mep mep, Etape e)
    {
        if (mep.Sections.FirstOrDefault(s => s.Id == e.SectionId) is not { } s) return;
        var dansSection = mep.Etapes.Where(x => x.SectionId == s.Id && x != e).Select(x => x.Id).ToHashSet();
        var entrees = s.Ports.Where(p => p.Sens == SensPort.Entree).OrderBy(p => p.Ordre).ToList();
        var sortiesSection = s.Ports.Where(p => p.Sens == SensPort.Sortie).OrderBy(p => p.Ordre).ToList();
        var aPrealable = Directes(e).Any(dansSection.Contains) || entrees.Any(p => p.Etapes.Contains(e.Id));
        var aSuivant = mep.Etapes.Any(x => dansSection.Contains(x.Id) && Directes(x).Contains(e.Id)) || sortiesSection.Any(p => p.Etapes.Contains(e.Id));
        if (!aPrealable && entrees.Count > 0) entrees[0].Etapes.Add(e.Id);
        if (!aSuivant && sortiesSection.Count > 0) sortiesSection[0].Etapes.Add(e.Id);
    }

    private static Section ExigerSection(Mep mep, TypePlan plan, Guid id) =>
        mep.Sections.FirstOrDefault(s => s.Id == id && s.Plan == plan) ?? throw new SaisieInvalideException("Section introuvable dans ce plan.");

    /// <summary>
    /// Ajoute une nouvelle étape (pas encore dans <see cref="Mep.Etapes"/>) à sa place dans le graphe. Section : celle donnée,
    /// sinon celle de la place (lien ou carte à l'intérieur d'une section), sinon aucune. Dans une section, l'étape se relie
    /// d'elle-même à ses premiers cercles si rien d'autre ne la relie à la section (<see cref="RelierAuxCercles"/>).
    /// </summary>
    public static void AjouterEtape(Mep mep, Etape etape, PlaceEtape place, Guid? sectionId = null) =>
        Operer(mep, etape.Plan, () =>
        {
            var resolue = Resoudre(mep, etape.Plan, place);
            etape.SectionId = sectionId is { } sid ? ExigerSection(mep, etape.Plan, sid).Id : SectionDeduite(resolue);
            etape.Dependances = [];
            etape.DependancesDerivees = [];
            etape.PortsAttendus = [];
            mep.Etapes.Add(etape);
            Inserer(mep, etape, resolue);
            RelierAuxCercles(mep, etape);
        }, sauf: etape);

    /// <summary>
    /// Déplace une carte (préparation) : elle quitte sa place (<see cref="Detacher"/>), entre dans une section, en sort
    /// (<paramref name="sortirDeSection"/>) ou change de section, puis s'insère à sa nouvelle place. Sans section indiquée,
    /// elle prend celle de sa nouvelle place (ou garde la sienne sans nouvelle place). Lâchée sur l'un de ses propres liens,
    /// elle ne bouge pas.
    /// </summary>
    public static void DeplacerEtape(Mep mep, Etape etape, PlaceEtape place, Guid? sectionId = null, bool sortirDeSection = false) =>
        Operer(mep, etape.Plan, () =>
        {
            var resolue = Resoudre(mep, etape.Plan, place);
            if (place.Vide && sectionId is null && !sortirDeSection) throw new SaisieInvalideException("Indiquer où déplacer l'étape.");
            if (sectionId is not null && sortirDeSection) throw new SaisieInvalideException("Choisir : entrer dans une section ou en sortir.");
            if (resolue.Apres?.Id == etape.Id || resolue.Parallele == etape)
                throw new SaisieInvalideException("Choisir une autre carte que celle qu'on déplace.");
            if (resolue.De is { } de && resolue.Vers is { } vers)
            {
                if (de.Id == etape.Id || vers.Id == etape.Id) return; // lâchée sur son propre lien
                if (!ExisteLien(de, vers)) throw new SaisieInvalideException($"Lien introuvable : {vers} n'attend pas {de}.");
            }
            var section = sortirDeSection ? null
                : sectionId is { } sid ? ExigerSection(mep, etape.Plan, sid).Id
                : place.Vide ? etape.SectionId : SectionDeduite(resolue);
            var placeActuelle = (etape.Ligne, etape.Ordre, etape.Rang);
            Detacher(mep, etape);
            etape.SectionId = section;
            Inserer(mep, etape, resolue);
            if (place.Vide) (etape.Ligne, etape.Ordre, etape.Rang) = placeActuelle;
            RelierAuxCercles(mep, etape);
        });

    /// <summary>
    /// Supprime une étape (préparation) : ce qui l'attendait attend désormais ce qu'elle attendait (<see cref="Detacher"/>), et elle
    /// disparaît des cercles.
    /// </summary>
    public static void RetirerEtape(Mep mep, Etape etape) =>
        Operer(mep, etape.Plan, () =>
        {
            Detacher(mep, etape);
            mep.Etapes.Remove(etape);
        }, sauf: etape);

    /// <summary>
    /// Préalables directs d'une étape (fiche de l'étape). Les préalables qui viennent des cercles de section ne se saisissent pas
    /// ici : ils restent tant que le cercle est relié (on peut donc renvoyer les préalables effectifs). Refusé : un préalable d'un
    /// autre plan, l'étape elle-même, un cycle.
    /// </summary>
    public static void DefinirDependances(Mep mep, Etape etape, IEnumerable<Guid> dependances)
    {
        var nouvelles = dependances.Distinct().ToList();
        Operer(mep, etape.Plan, () =>
        {
            var autrePlan = mep.Etapes.FirstOrDefault(e => nouvelles.Contains(e.Id) && e.Plan != etape.Plan);
            if (autrePlan is not null) throw new SaisieInvalideException($"{autrePlan.Code} appartient à un autre plan.");
            etape.Dependances = [.. nouvelles.Where(d => !etape.DependancesDerivees.Contains(d)), .. etape.DependancesDerivees];
        }, sauf: etape);
    }

    /// <summary>Relie deux nœuds du plan (étapes ou cercles) : <paramref name="versId"/> attend <paramref name="deId"/>. Voir <see cref="Lier"/>.</summary>
    public static (Noeud De, Noeud Vers) Relier(Mep mep, TypePlan plan, Guid deId, Guid versId)
    {
        Noeud? de = null, vers = null;
        Operer(mep, plan, () =>
        {
            de = ExigerNoeud(mep, plan, deId);
            vers = ExigerNoeud(mep, plan, versId);
            Lier(de, vers);
        });
        return (de!, vers!);
    }

    /// <summary>Retire le lien « <paramref name="versId"/> attend <paramref name="deId"/> » (préalable direct ou lien d'un cercle).</summary>
    public static (Noeud De, Noeud Vers) Delier(Mep mep, TypePlan plan, Guid deId, Guid versId)
    {
        Noeud? de = null, vers = null;
        Operer(mep, plan, () =>
        {
            de = ExigerNoeud(mep, plan, deId);
            vers = ExigerNoeud(mep, plan, versId);
            Delier(de, vers);
        });
        return (de!, vers!);
    }

    // ---------- Bac des étapes à placer ----------

    /// <summary>L'étape a-t-elle un lien : un préalable, un suivant, ou une place dans un cercle de section ?</summary>
    public static bool AUnLien(Mep mep, Etape e) =>
        e.Dependances.Count > 0 || e.PortsAttendus.Count > 0
        || mep.EtapesDu(e.Plan).Any(x => x.Dependances.Contains(e.Id))
        || mep.SectionsDu(e.Plan).Any(s => s.Ports.Any(p => p.Etapes.Contains(e.Id) || p.Sources.Contains(e.Id)));

    /// <summary>
    /// Retire une étape du graphe (préparation) : elle quitte sa place comme pour un déplacement (<see cref="Detacher"/> : ce qui
    /// l'attendait attend désormais ce qu'elle attendait) et attend dans le bac « Étapes à placer », sans lien, dans sa section.
    /// Renvoie faux si elle y était déjà.
    /// </summary>
    public static bool RetirerDuPlan(Mep mep, Etape etape)
    {
        ExigerPreparation(mep);
        if (etape.APlacer) return false;
        Operer(mep, etape.Plan, () =>
        {
            Detacher(mep, etape);
            etape.APlacer = true;
        });
        return true;
    }

    /// <summary>
    /// Replace dans le graphe une étape du bac (préparation), à la place donnée (après une carte ou un cercle, sur un lien, en
    /// parallèle d'une carte, ou à part). Section : celle donnée (vue d'une section), sinon celle de la place, sinon la sienne
    /// pour une étape posée à part. Dans une section, elle se relie d'elle-même à ses premiers cercles (<see cref="RelierAuxCercles"/>).
    /// </summary>
    public static void PlacerEtape(Mep mep, Etape etape, PlaceEtape place, Guid? sectionId = null) =>
        Operer(mep, etape.Plan, () =>
        {
            if (!etape.APlacer) throw new RegleMetierException($"{etape.Code} est déjà dans le graphe : la déplacer plutôt.");
            var resolue = Resoudre(mep, etape.Plan, place);
            if (resolue.Apres?.Id == etape.Id || resolue.Parallele == etape || resolue.De?.Id == etape.Id || resolue.Vers?.Id == etape.Id)
                throw new RegleMetierException("Choisir une autre place que l'étape elle-même.");
            etape.SectionId = sectionId is { } sid ? ExigerSection(mep, etape.Plan, sid).Id
                : place.Vide ? etape.SectionId : SectionDeduite(resolue);
            etape.APlacer = false;
            Inserer(mep, etape, resolue);
            RelierAuxCercles(mep, etape);
        });

    /// <summary>
    /// Une section quitte le graphe (remise dans le bac) : les liens extérieurs de ses cercles sont retirés, et ce qui attendait ses
    /// cercles de sortie attend désormais ce qui arrivait à ses cercles d'entrée (quand ce lien est possible), comme pour une étape
    /// (<see cref="Detacher"/>). L'intérieur de la section (ses étapes et leurs liens aux cercles) ne change pas.
    /// </summary>
    private static void DetacherSection(Mep mep, Section section)
    {
        var interieur = mep.Etapes.Where(e => e.SectionId == section.Id).Select(e => e.Id).ToHashSet();
        var entrees = section.Ports.Where(p => p.Sens == SensPort.Entree).ToList();
        var sorties = section.Ports.Where(p => p.Sens == SensPort.Sortie).ToList();
        var amont = entrees.SelectMany(p => p.Sources).Distinct().Select(id => TrouverNoeud(mep, section.Plan, id)).OfType<Noeud>().ToList();
        var aval = new List<Noeud>();
        foreach (var q in sorties)
        {
            foreach (var e in mep.EtapesDu(section.Plan).Where(e => e.PortsAttendus.Contains(q.Id)))
            {
                e.PortsAttendus = e.PortsAttendus.Where(id => id != q.Id).ToList();
                aval.Add(NoeudDe(e));
            }
            foreach (var s in mep.SectionsDu(section.Plan).Where(s => s.Id != section.Id))
                foreach (var p in s.Ports.Where(p => p.Sens == SensPort.Entree && p.Sources.Remove(q.Id)))
                    aval.Add(new Noeud(p.Id, null, s, p));
        }
        foreach (var p in entrees) p.Sources.Clear();
        // Les préalables effectifs passent encore par l'intérieur de la section jusqu'à la compilation : il est exclu du parcours.
        foreach (var o in aval.DistinctBy(n => n.Id))
            foreach (var i in amont.Where(i => i.Id != o.Id))
            {
                if (i.Etape is { } a && o.Etape is { } b && AttendDeja(mep, b, a, interieur)) continue;
                try { Lier(i, o); }
                catch (RegleMetierException) { /* lien impossible : ignoré */ }
            }
    }

    /// <summary>Remise dans le bac d'une section placée : voir <see cref="DetacherSection"/>.</summary>
    private static void RetirerSectionDuPlan(Mep mep, Section section) =>
        Operer(mep, section.Plan, () =>
        {
            DetacherSection(mep, section);
            section.Placee = false;
        });
}
