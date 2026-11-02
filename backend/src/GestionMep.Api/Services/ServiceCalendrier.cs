using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Api.Endpoints;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Services;

/// <summary>
/// Calendrier des MEP, périodes de gel et abonnement iCalendar (#129), partagés par l'API et la ligne de commande.
/// <para>
/// Heures : tout est enregistré et renvoyé en UTC. Le créneau d'une MEP se calcule dans son fuseau (<see cref="Mep.FuseauHoraire"/>),
/// une période de gel se répète dans le sien (celui de l'instance par défaut, <see cref="Fuseaux.ParDefaut"/>) ; les jours
/// <c>du</c> et <c>au</c> d'une plage se lisent dans le fuseau demandé (celui de l'instance par défaut). L'export iCalendar donne
/// les heures en UTC (l'agenda les affiche dans le fuseau de son utilisateur) et les journées entières en dates locales.
/// </para>
/// </summary>
public sealed class ServiceCalendrier(MepDbContext db, TimeProvider temps)
{
    /// <summary>Plage la plus longue d'une lecture du calendrier ou d'un export.</summary>
    public const int JoursMax = 366;
    /// <summary>Taille d'une page du calendrier par défaut, et au plus.</summary>
    public const int LimiteParDefaut = 100, LimiteMax = 500;
    /// <summary>Abonnement : du jour - 90 jours au jour + 365 jours, dans le fuseau de l'instance.</summary>
    public const int JoursAvantAbonnement = 90, JoursApresAbonnement = 365;

    public DateTimeOffset Maintenant => temps.GetUtcNow();

    /// <summary>Plage lue : jours inclus dans le fuseau, et ses bornes en instants [Debut, Fin).</summary>
    public sealed record Plage(DateOnly Du, DateOnly Au, string Fuseau, DateTimeOffset Debut, DateTimeOffset Fin);

    private static DateOnly? Jour(string? texte, string nom) =>
        string.IsNullOrWhiteSpace(texte) ? null
            : DateOnly.TryParseExact(texte.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d
            : throw ErreurHttp.Saisie($"Date « {texte} » invalide pour « {nom} » : format AAAA-MM-JJ attendu.");

    /// <summary>Fuseau saisi (IANA ou Windows), ou celui de l'instance ; 400 s'il est inconnu.</summary>
    public static string LireFuseau(string? saisie) =>
        string.IsNullOrWhiteSpace(saisie) ? Fuseaux.ParDefaut
            : Fuseaux.Normaliser(saisie) ?? throw ErreurHttp.Saisie($"Fuseau horaire inconnu : « {saisie} ». Indiquer un identifiant IANA, par exemple {Fuseaux.ParDefaut}.");

    /// <summary>
    /// Plage demandée : <c>du</c> et <c>au</c> inclus (par défaut, le mois en cours dans le fuseau), <see cref="JoursMax"/> jours au plus.
    /// </summary>
    public Plage LirePlage(string? du, string? au, string? fuseau)
    {
        var tz = LireFuseau(fuseau);
        var aujourdhui = Fuseaux.Jour(Maintenant, tz);
        var debut = Jour(du, "du") ?? new DateOnly(aujourdhui.Year, aujourdhui.Month, 1);
        var fin = Jour(au, "au") ?? (Jour(du, "du") is { } d ? d.AddMonths(1).AddDays(-1) : debut.AddMonths(1).AddDays(-1));
        if (fin < debut) throw ErreurHttp.Saisie("Période invalide : « du » est après « au ».");
        if (fin.DayNumber - debut.DayNumber + 1 > JoursMax) throw ErreurHttp.Saisie($"Période trop longue : {JoursMax} jours au plus.");
        return new Plage(debut, fin, tz, Fuseaux.Instant(debut, null, tz).ToUniversalTime(), Fuseaux.Instant(fin.AddDays(1), null, tz).ToUniversalTime());
    }

    /// <summary>Plage de l'abonnement : de 90 jours avant à 365 jours après aujourd'hui (fuseau de l'instance).</summary>
    public Plage PlageAbonnement()
    {
        var aujourdhui = Fuseaux.Jour(Maintenant, Fuseaux.ParDefaut);
        var du = aujourdhui.AddDays(-JoursAvantAbonnement);
        var au = aujourdhui.AddDays(JoursApresAbonnement);
        return new Plage(du, au, Fuseaux.ParDefaut, Fuseaux.Instant(du, null, Fuseaux.ParDefaut).ToUniversalTime(),
            Fuseaux.Instant(au.AddDays(1), null, Fuseaux.ParDefaut).ToUniversalTime());
    }

    /// <summary>Filtres de la liste des MEP repris par le calendrier (sans la période, remplacée par la plage).</summary>
    public static FiltreMeps Filtre(ParametresCalendrier p) =>
        FiltreMeps.Lire(new ParametresFiltreMeps(p.Recherche, p.Systeme, p.Environnement, p.Equipe, p.Personne, p.Role, p.Etat, Type: p.Type));

    // ---------- Conversions ----------

    public static OccurrenceGelDto Dto(OccurrenceGel o, StatutDerogation? derogation = null) =>
        new(o.Periode.Id, o.Periode.Nom, o.Periode.Motif, o.Debut, o.Fin, o.Periode.FuseauHoraire, o.Periode.SystemeCatalogueIds.Count == 0, derogation);

    public static PeriodeGelDto Dto(PeriodeGel p, Noms noms) =>
        new(p.Id, p.Nom, p.Debut, p.Fin, p.Motif, [.. p.SystemeCatalogueIds],
            [.. p.SystemeCatalogueIds.Select(id => noms.Systemes.GetValueOrDefault(id) ?? id.ToString())], [.. p.Environnements],
            p.ProductionSeulement, p.Recurrence, p.FinRecurrence, p.FuseauHoraire, [.. p.ApprobateurIds], [.. p.EquipeApprobatriceIds],
            Decideurs(p, noms), p.Version, p.CreeeLe, p.ModifieeLe);

    /// <summary>Noms affichés des systèmes du catalogue, des comptes (leur personne) et des équipes.</summary>
    public sealed record Noms(IReadOnlyDictionary<Guid, string> Systemes, IReadOnlyDictionary<Guid, string> Comptes, IReadOnlyDictionary<Guid, string> Equipes);

    /// <summary>Qui décide des dérogations à la période, en clair : « Claire Durand, équipe Comptabilité », « les administrateurs ».</summary>
    public static string Decideurs(PeriodeGel? p, Noms noms)
    {
        if (p is null || (p.ApprobateurIds.Count == 0 && p.EquipeApprobatriceIds.Count == 0)) return "les administrateurs";
        return string.Join(", ", p.ApprobateurIds.Select(id => noms.Comptes.GetValueOrDefault(id) ?? "compte supprimé").Distinct()
            .Concat(p.EquipeApprobatriceIds.Select(id => $"équipe {noms.Equipes.GetValueOrDefault(id) ?? "supprimée"}")));
    }

    private static Systeme VersSysteme(SystemeResumeDto s) => new()
    {
        CatalogueId = s.CatalogueId, Nom = s.Nom,
        Environnements = [.. s.Environnements.Select(e => new EnvironnementSysteme { Nom = e.Nom, Production = e.Production })],
    };

    private async Task<Noms> LireNoms() => new(
        await db.SystemesCatalogue.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Nom),
        (await Personnes.Charger(db, avecEquipes: false)).NomsAffiches(),
        await db.Equipes.AsNoTracking().ToDictionaryAsync(e => e.Id, e => e.Nom));

    public async Task<List<PeriodeGel>> Periodes() => await db.PeriodesGel.AsNoTracking().OrderBy(p => p.Debut).ThenBy(p => p.Nom).ToListAsync();

    // ---------- Conflits ----------

    private sealed record Candidat(Guid Id, string Outil, string VersionCible, EtatMep Etat, ElementCalendrier Element, Dictionary<string, string> Noms);

    /// <summary>
    /// Conflits de chaque cible avec les autres MEP (hors modèles et MEP annulées) : une requête lit les MEP dont le créneau
    /// touche celui des cibles et qui partagent un de leurs systèmes du catalogue (ou ont un système saisi à la main).
    /// </summary>
    public async Task<Dictionary<Guid, List<ConflitDto>>> Conflits(IReadOnlyCollection<ElementCalendrier> cibles)
    {
        var resultat = cibles.ToDictionary(c => c.MepId, _ => new List<ConflitDto>());
        var avecSystemes = cibles.Where(c => c.Systemes.Count > 0).ToList();
        if (avecSystemes.Count == 0) return resultat;
        var maintenant = Maintenant;
        var debut = avecSystemes.Min(c => c.Debut);
        var fin = avecSystemes.Max(c => Regles.FinEffective(c.Debut, c.Fin, c.EnCours, maintenant)).AddSeconds(1);
        var catalogue = avecSystemes.SelectMany(c => c.Systemes).Where(k => Guid.TryParse(k, out _)).Select(Guid.Parse).Distinct().ToList();
        var libres = avecSystemes.Any(c => c.Systemes.Any(k => k.StartsWith("libre:", StringComparison.Ordinal)));
        var lancees = new[] { Phase.Execution, Phase.Recette, Phase.RetourArriere };
        var lignes = await db.Meps.AsNoTracking()
            .Where(m => !m.EstModele && m.Phase != Phase.Annulee && m.Calendrier.Debut != null && m.Calendrier.Debut < fin
                && (m.Calendrier.Debut >= debut || m.Calendrier.Fin > debut || (lancees.Contains(m.Phase) && maintenant > debut))
                && m.Systemes.Any(s => s.CatalogueId == null ? libres : catalogue.Contains(s.CatalogueId.Value)))
            .Select(m => new
            {
                m.Id, m.Outil, m.VersionCible, m.Phase, m.Issue, m.EchecsEtapes, m.EchecsRecette, Reportee = m.Report != null,
                Debut = m.Calendrier.Debut!.Value, m.Calendrier.Fin, Systemes = m.Systemes.Select(s => new { s.CatalogueId, s.Nom }).ToList(),
            })
            .ToListAsync();
        var candidats = lignes.Select(l =>
        {
            var noms = new Dictionary<string, string>();
            foreach (var s in l.Systemes) noms.TryAdd(Regles.CleSysteme(new Systeme { CatalogueId = s.CatalogueId, Nom = s.Nom }), s.Nom);
            return new Candidat(l.Id, l.Outil, l.VersionCible, Regles.EtatDe(l.Phase, l.Issue, l.EchecsEtapes + l.EchecsRecette, l.Reportee),
                new ElementCalendrier(l.Id, l.Debut, l.Fin, Regles.EstLancee(l.Phase), noms.Keys), noms);
        }).ToDictionary(c => c.Id);
        // Les cibles priment (créneau tout juste recalculé) ; une cible absente des candidats (annulée) n'a pas de conflit.
        var elements = candidats.Values.Select(c => cibles.FirstOrDefault(x => x.MepId == c.Id) ?? c.Element).ToList();
        var conflits = Regles.Conflits(elements, maintenant);
        foreach (var cible in cibles)
        {
            if (!candidats.ContainsKey(cible.MepId) || !conflits.TryGetValue(cible.MepId, out var liste)) continue;
            resultat[cible.MepId] = [.. liste.Select(x =>
            {
                var autre = candidats[x.AutreMepId];
                return new ConflitDto(autre.Id, autre.Outil, autre.VersionCible, autre.Etat, autre.Element.Debut, autre.Element.Fin,
                    [.. x.Systemes.Select(k => autre.Noms.GetValueOrDefault(k) ?? k)]);
            }).OrderBy(c => c.Debut)];
        }
        return resultat;
    }

    // ---------- Liste des MEP ----------

    private static readonly Phase[] PhasesSignalees = [Phase.Preparation, Phase.Execution, Phase.Recette, Phase.RetourArriere];

    /// <summary>
    /// Gels et conflits des MEP d'une page de la liste (cartes et tableau), pour celles qui en ont : MEP en préparation ou lancées
    /// seulement (une MEP clôturée ou annulée n'a plus rien à signaler). Un nombre fixe de requêtes, quelle que soit la page.
    /// </summary>
    public async Task<List<SignalementCalendrierDto>> Signalements(IReadOnlyCollection<MepResumeDto> meps)
    {
        var parId = meps.Where(m => !m.EstModele && PhasesSignalees.Contains(m.Phase)).ToDictionary(m => m.Id);
        if (parId.Count == 0) return [];
        var ids = parId.Keys.ToList();
        var fenetres = await db.Meps.AsNoTracking().Where(m => ids.Contains(m.Id) && m.Calendrier.Debut != null)
            .Select(m => new { m.Id, Debut = m.Calendrier.Debut!.Value, m.Calendrier.Fin, m.DerogationsGel }).ToListAsync();
        if (fenetres.Count == 0) return [];
        var maintenant = Maintenant;
        var periodes = await Periodes();
        var elements = fenetres.Select(f =>
        {
            var r = parId[f.Id];
            var enCours = Regles.EstLancee(r.Phase);
            var systemes = r.Systemes.Select(VersSysteme).ToList();
            var gels = Regles.GelsDuCreneau(f.Debut, Regles.FinEffective(f.Debut, f.Fin, enCours, maintenant), systemes, periodes)
                .Select(o => Dto(o, f.DerogationsGel.LastOrDefault(d => d.PeriodeGelId == o.Periode.Id && d.Occurrence == o.Debut)?.Statut)).ToList();
            return (f.Id, Gels: gels, Cible: new ElementCalendrier(f.Id, f.Debut, f.Fin, enCours, [.. systemes.Select(Regles.CleSysteme).Distinct()]));
        }).ToList();
        var conflits = await Conflits([.. elements.Select(e => e.Cible)]);
        return [.. elements.Select(e => new SignalementCalendrierDto(e.Id, e.Gels, conflits.GetValueOrDefault(e.Id) ?? []))
            .Where(s => s.Gels.Count > 0 || s.Conflits.Count > 0)];
    }

    // ---------- Détail d'une MEP ----------

    /// <summary>
    /// Place de chaque MEP dans le calendrier, vue par <paramref name="utilisateurId"/> : créneau, gels touchés, gels qui
    /// bloqueraient un Go maintenant, conflits et dérogations (un nombre fixe de requêtes, quel que soit le nombre de MEP).
    /// </summary>
    public async Task<Dictionary<Guid, CalendrierMepDto>> PourMeps(IReadOnlyList<Mep> meps, Guid? utilisateurId, AnnuairePersonnes annuaire)
    {
        var resultat = new Dictionary<Guid, CalendrierMepDto>();
        if (meps.Count == 0) return resultat;
        var maintenant = Maintenant;
        var periodes = await Periodes();
        var fenetres = meps.ToDictionary(m => m.Id, Regles.FenetreDe);
        var cibles = meps.Where(m => !m.EstModele && m.Phase != Phase.Annulee && fenetres[m.Id].Debut is not null)
            .Select(m => new ElementCalendrier(m.Id, fenetres[m.Id].Debut!.Value, fenetres[m.Id].Fin, Regles.EstLancee(m.Phase),
                [.. m.Systemes.Select(Regles.CleSysteme).Distinct()]))
            .ToList();
        var conflits = await Conflits(cibles);
        // Décideurs des dérogations : approbateurs de chaque période (comptes, équipes), les administrateurs sinon.
        var noms = new Noms(new Dictionary<Guid, string>(), annuaire.NomsAffiches(),
            await db.Equipes.AsNoTracking().ToDictionaryAsync(e => e.Id, e => e.Nom));
        var enAttente = utilisateurId is not null && meps.Any(m => m.DerogationsGel.Any(d => d.Statut == StatutDerogation.Demandee));
        var annuaireEquipes = enAttente ? await Personnes.Charger(db) : annuaire;
        var estAdmin = enAttente && await db.Utilisateurs.AsNoTracking().AnyAsync(u => u.Id == utilisateurId && u.EstAdmin);
        var parPeriode = periodes.ToDictionary(p => p.Id);
        OccurrenceGelDto DtoMep(OccurrenceGel o, StatutDerogation? statut) => Dto(o, statut) with { Decideurs = Decideurs(o.Periode, noms) };
        foreach (var m in meps)
        {
            var f = fenetres[m.Id];
            StatutDerogation? Derogation(OccurrenceGel o) => Regles.DerogationPour(m, o.Periode.Id, o.Debut)?.Statut;
            var gels = Regles.GelsDeLaMep(m, periodes, maintenant).Select(o => DtoMep(o, Derogation(o))).ToList();
            var auGo = m.Phase == Phase.Preparation && !m.EstModele
                ? Regles.GelsAuGo(m, periodes, maintenant).Where(o => !Regles.DerogationAccordee(m, o)).Select(o => DtoMep(o, Derogation(o))).ToList()
                : [];
            bool PeutDecider(DerogationGel d) => utilisateurId is { } u && d.Statut == StatutDerogation.Demandee && m.Phase == Phase.Preparation
                && Regles.PeutDeciderDerogation(m, u, estAdmin, parPeriode.GetValueOrDefault(d.PeriodeGelId), d, annuaireEquipes) is null;
            var derogations = m.DerogationsGel.Select(d => new DerogationGelDto(d.Id, d.PeriodeGelId, d.NomPeriode, d.Occurrence, d.OccurrenceFin,
                d.Motif, d.Statut, annuaire.NomPersonne(d.DemandeeParId), d.DemandeeLe, d.DecideeParId is { } p ? annuaire.NomPersonne(p) : null,
                d.DecideeLe, d.Commentaire, PeutDecider(d))).ToList();
            resultat[m.Id] = new CalendrierMepDto(f.Debut, f.Fin, f.JourneeEntiere, EstPrevue(m.Phase, m.DemarrageAutoriseLe),
                Regles.EstLancee(m.Phase), gels, auGo, conflits.GetValueOrDefault(m.Id) ?? [], derogations);
        }
        return resultat;
    }

    /// <summary>MEP pas encore déroulée (préparation, annulée, ou sans Go) : son créneau est le créneau prévu.</summary>
    private static bool EstPrevue(Phase phase, DateTimeOffset? go) => phase is Phase.Preparation or Phase.Annulee || go is null;

    // ---------- Calendrier ----------

    /// <summary>MEP du calendrier, avec ce que l'export iCalendar lit en plus (version, fuseau).</summary>
    public sealed record Ligne(ElementCalendrierDto Element, int Version);

    /// <summary>
    /// MEP (hors modèles) dont le créneau touche la plage et qui correspondent aux filtres, par début puis identifiant. Les
    /// critères simples et la plage sont lus en SQL (créneau indexé) ; la règle exacte des filtres s'applique ensuite aux résumés,
    /// comme la liste des MEP.
    /// </summary>
    public async Task<(List<Ligne> Lignes, List<OccurrenceGel> Gels)> Lire(Plage plage, FiltreMeps filtre)
    {
        var maintenant = Maintenant;
        var annuaire = await Personnes.Charger(db);
        var lancees = new[] { Phase.Execution, Phase.Recette, Phase.RetourArriere };
        var (debut, fin) = (plage.Debut, plage.Fin);
        var q = filtre.Prefiltrer(db.Meps.AsNoTracking().Where(m => !m.EstModele && m.Calendrier.Debut != null && m.Calendrier.Debut < fin
            && (m.Calendrier.Debut >= debut || m.Calendrier.Fin > debut || (lancees.Contains(m.Phase) && maintenant > debut))), annuaire);
        var fenetres = (await q.Select(m => new
        {
            m.Id, Debut = m.Calendrier.Debut!.Value, m.Calendrier.Fin, m.Calendrier.JourneeEntiere, m.FuseauHoraire, m.Version, m.DerogationsGel,
        }).ToListAsync()).ToDictionary(x => x.Id);
        var resumes = (await MepsEndpoints.Resumes(db, q, annuaire)).Where(filtre.Correspond).Where(r => fenetres.ContainsKey(r.Id)).ToList();
        var periodes = await Periodes();
        var gelsPlage = periodes.SelectMany(p => Regles.Occurrences(p, debut, fin)).OrderBy(o => o.Debut).ThenBy(o => o.Periode.Nom).ToList();
        var elements = resumes.Select(r =>
        {
            var f = fenetres[r.Id];
            var enCours = Regles.EstLancee(r.Phase);
            var systemes = r.Systemes.Select(VersSysteme).ToList();
            var cles = systemes.Select(Regles.CleSysteme).Distinct().ToList();
            var gels = Regles.GelsDuCreneau(f.Debut, Regles.FinEffective(f.Debut, f.Fin, enCours, maintenant), systemes, periodes)
                .Select(o => Dto(o, f.DerogationsGel.LastOrDefault(d => d.PeriodeGelId == o.Periode.Id && d.Occurrence == o.Debut)?.Statut)).ToList();
            return (Resume: r, Fenetre: f, EnCours: enCours, Gels: gels,
                Cible: new ElementCalendrier(r.Id, f.Debut, f.Fin, enCours, cles));
        }).ToList();
        var conflits = await Conflits([.. elements.Where(e => e.Resume.Phase != Phase.Annulee).Select(e => e.Cible)]);
        var lignes = elements.Select(e =>
        {
            var r = e.Resume;
            return new Ligne(new ElementCalendrierDto(r.Id, r.Outil, r.VersionCible, r.Objet, r.Type, r.Phase, r.Etat, e.Fenetre.Debut, e.Fenetre.Fin,
                e.Fenetre.JourneeEntiere, EstPrevue(r.Phase, r.Go), e.EnCours, e.Fenetre.FuseauHoraire, r.Pilote, r.ToucheProduction, r.Systemes,
                r.EquipeIds, e.Gels, conflits.GetValueOrDefault(r.Id) ?? []), e.Fenetre.Version);
        }).OrderBy(l => l.Element.Debut).ThenBy(l => l.Element.Id).ToList();
        return (lignes, gelsPlage);
    }

    /// <summary>Curseur de page : début et identifiant de la dernière MEP de la page.</summary>
    private static string Curseur(ElementCalendrierDto e) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{e.Debut.UtcTicks}|{e.Id:N}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static (long Ticks, Guid Id) LireCurseur(string texte)
    {
        try
        {
            var b64 = texte.Trim().Replace('-', '+').Replace('_', '/');
            var parties = Encoding.UTF8.GetString(Convert.FromBase64String(b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '='))).Split('|');
            return (long.Parse(parties[0], CultureInfo.InvariantCulture), Guid.ParseExact(parties[1], "N"));
        }
        catch (Exception e) when (e is FormatException or IndexOutOfRangeException or ArgumentException or OverflowException)
        {
            throw ErreurHttp.Saisie("Curseur « apres » invalide : reprendre la valeur « suivant » de la page précédente.");
        }
    }

    /// <summary>Page du calendrier : au plus <paramref name="limite"/> MEP après le curseur <paramref name="apres"/>.</summary>
    public async Task<PageCalendrierDto> Page(Plage plage, FiltreMeps filtre, int? limite, string? apres)
    {
        var n = limite ?? LimiteParDefaut;
        if (n is < 1 or > LimiteMax) throw ErreurHttp.Saisie($"« limite » doit être comprise entre 1 et {LimiteMax}.");
        var curseur = apres is null ? ((long, Guid)?)null : LireCurseur(apres);
        var (lignes, gels) = await Lire(plage, filtre);
        var suite = curseur is { } c
            ? lignes.Where(l => l.Element.Debut.UtcTicks > c.Item1 || (l.Element.Debut.UtcTicks == c.Item1 && l.Element.Id.CompareTo(c.Item2) > 0)).ToList()
            : lignes;
        var page = suite.Take(n).Select(l => l.Element).ToList();
        return new PageCalendrierDto(page, [.. gels.Select(o => Dto(o))], lignes.Count, suite.Count > n ? Curseur(page[^1]) : null,
            plage.Du, plage.Au, plage.Fuseau);
    }

    // ---------- iCalendar ----------

    /// <summary>Adresse publique du serveur (réglage url-publique), sinon celle de la requête ; null hors requête et sans réglage.</summary>
    public static string? AdresseBase(IConfiguration config, HttpRequest? requete) =>
        config["Application:UrlPublique"] is { Length: > 0 } u ? u.TrimEnd('/')
            : requete is null ? null : $"{requete.Scheme}://{requete.Host}{requete.PathBase}";

    /// <summary>
    /// Calendrier iCalendar des MEP de la plage (et des périodes de gel, sauf <paramref name="avecGels"/> faux). UID stable par
    /// MEP (<c>mep-{id}@gestion-mep</c>) et par occurrence de gel ; SEQUENCE : version de la MEP.
    /// </summary>
    public async Task<string> Ics(Plage plage, FiltreMeps filtre, bool avecGels, string? adresseBase, string nom = "MEP")
    {
        var maintenant = Maintenant;
        var (lignes, gels) = await Lire(plage, filtre);
        var evenements = new List<EvenementIcs>();
        foreach (var (e, version) in lignes)
        {
            var titre = $"MEP {e.Outil} {e.VersionCible}".Trim();
            if (e.ToucheProduction) titre = "[Production] " + titre;
            if (e.Phase == Phase.Annulee) titre += " (annulée)";
            var description = new List<string> { $"État : {FiltreMeps.Libelle(e.Etat)}" };
            if (e.Objet.Trim().Length > 0) description.Add($"Objet : {e.Objet.Trim()}");
            if (e.Pilote is { Length: > 0 } pilote) description.Add($"Pilote : {pilote}");
            if (e.Systemes.Count > 0)
                description.Add("Systèmes : " + string.Join(", ", e.Systemes.Select(s =>
                    s.Environnements.Count == 0 ? s.Nom : $"{s.Nom} ({string.Join(", ", s.Environnements.Select(x => x.Nom))})")));
            foreach (var g in e.Gels)
                description.Add($"Période de gel « {g.Nom} »" + (g.Derogation == StatutDerogation.Accordee ? " (dérogation accordée)" : ""));
            foreach (var c in e.Conflits) description.Add($"Conflit : MEP {c.Outil} {c.VersionCible} sur {string.Join(", ", c.Systemes)}".Replace("  ", " "));
            var url = adresseBase is null ? null : $"{adresseBase}/meps/{e.Id}";
            if (url is not null) description.Add(url);
            var fin = e.Fin ?? (e.EnCours ? Regles.FinEffective(e.Debut, null, true, maintenant) : null);
            evenements.Add(new EvenementIcs($"mep-{e.Id}@gestion-mep", titre, e.Debut, fin, e.JourneeEntiere,
                e.JourneeEntiere ? Fuseaux.Jour(e.Debut, e.FuseauHoraire) : null,
                e.JourneeEntiere && e.Fin is { } jf ? Fuseaux.Jour(jf, e.FuseauHoraire) : null,
                string.Join("\n", description), url,
                e.Phase switch { Phase.Annulee => StatutIcs.Annule, Phase.Preparation => StatutIcs.Provisoire, _ => StatutIcs.Confirme },
                e.ToucheProduction ? [FiltreMeps.Libelle(e.Etat), "Production"] : [FiltreMeps.Libelle(e.Etat)], version));
        }
        if (avecGels)
            foreach (var o in gels)
            {
                var portee = o.Periode.SystemeCatalogueIds.Count == 0 ? "Tous les systèmes" : $"{o.Periode.SystemeCatalogueIds.Count} système(s)";
                if (o.Periode.Environnements.Count > 0) portee += " ; environnements " + string.Join(", ", o.Periode.Environnements);
                if (o.Periode.ProductionSeulement) portee += " ; production seulement";
                evenements.Add(new EvenementIcs($"gel-{o.Periode.Id:N}-{o.Debut.UtcTicks}@gestion-mep", $"Gel : {o.Periode.Nom}", o.Debut, o.Fin,
                    Description: string.Join("\n", new[] { o.Periode.Motif.Trim(), portee }.Where(x => x.Length > 0)),
                    Categories: ["Période de gel"], Sequence: o.Periode.Version, Transparent: true));
            }
        return ICalendar.Calendrier(nom, Fuseaux.ParDefaut, evenements, maintenant);
    }

    // ---------- Périodes de gel (administration) ----------

    public async Task<List<PeriodeGelDto>> ListerPeriodes()
    {
        var noms = await LireNoms();
        return [.. (await Periodes()).Select(p => Dto(p, noms))];
    }

    private async Task<PeriodeGel> ChargerPeriode(Guid id) =>
        await db.PeriodesGel.FirstOrDefaultAsync(p => p.Id == id) ?? throw ErreurHttp.NonTrouve("Période de gel introuvable.");

    /// <summary>Période désignée par son identifiant (8 premiers caractères suffisent) ou son nom exact (sans casse) ; 404 sinon.</summary>
    public async Task<PeriodeGel> TrouverPeriode(string texte)
    {
        var toutes = await db.PeriodesGel.AsNoTracking().ToListAsync();
        var t = texte.Trim();
        var trouvees = Guid.TryParse(t, out var id) ? toutes.Where(p => p.Id == id).ToList()
            : toutes.Where(p => string.Equals(p.Nom, t, StringComparison.OrdinalIgnoreCase)).ToList() is { Count: > 0 } parNom ? parNom
            : t.Length >= 4 ? toutes.Where(p => p.Id.ToString().StartsWith(t.ToLowerInvariant(), StringComparison.Ordinal)).ToList() : [];
        return trouvees.Count switch
        {
            0 => throw ErreurHttp.NonTrouve($"Période de gel « {texte} » introuvable."),
            1 => trouvees[0],
            _ => throw ErreurHttp.Saisie($"Plusieurs périodes de gel correspondent à « {texte} » : donner l'identifiant complet."),
        };
    }

    /// <summary>Applique la saisie (champs absents : valeurs par défaut) et renvoie les anomalies ; les systèmes doivent exister.</summary>
    private async Task Appliquer(PeriodeGel p, PeriodeGelSaisie s)
    {
        if (s.Debut is null || s.Fin is null) throw ErreurHttp.Saisie("Le début et la fin de la période de gel sont obligatoires.");
        p.Nom = ServiceMep.Texte(s.Nom);
        p.Debut = s.Debut.Value.ToUniversalTime();
        p.Fin = s.Fin.Value.ToUniversalTime();
        p.Motif = ServiceMep.Texte(s.Motif);
        p.SystemeCatalogueIds = [.. (s.SystemeCatalogueIds ?? []).Distinct()];
        p.ApprobateurIds = [.. (s.ApprobateurIds ?? []).Distinct()];
        p.EquipeApprobatriceIds = [.. (s.EquipeApprobatriceIds ?? []).Distinct()];
        p.Environnements = [.. (s.Environnements ?? []).Select(e => e.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)];
        p.ProductionSeulement = s.ProductionSeulement ?? false;
        p.Recurrence = s.Recurrence ?? RecurrenceGel.Aucune;
        p.FinRecurrence = s.FinRecurrence;
        p.FuseauHoraire = Fuseaux.Normaliser(s.FuseauHoraire) ?? (string.IsNullOrWhiteSpace(s.FuseauHoraire) ? Fuseaux.ParDefaut : s.FuseauHoraire.Trim());
        var erreurs = Regles.ErreursPeriodeGel(p);
        if (p.SystemeCatalogueIds.Count > 0)
        {
            var connus = await db.SystemesCatalogue.AsNoTracking().CountAsync(x => p.SystemeCatalogueIds.Contains(x.Id));
            if (connus != p.SystemeCatalogueIds.Count) erreurs.Add("Système du catalogue inconnu.");
        }
        if (p.ApprobateurIds.Count > 0)
        {
            var actifs = await db.Utilisateurs.AsNoTracking().CountAsync(u => p.ApprobateurIds.Contains(u.Id) && u.Actif);
            if (actifs != p.ApprobateurIds.Count) erreurs.Add("Approbateur inconnu ou compte désactivé.");
        }
        if (p.EquipeApprobatriceIds.Count > 0)
        {
            var connues = await db.Equipes.AsNoTracking().CountAsync(e => p.EquipeApprobatriceIds.Contains(e.Id));
            if (connues != p.EquipeApprobatriceIds.Count) erreurs.Add("Équipe approbatrice inconnue.");
        }
        // Nom unique, sans casse ni accents : la ligne de commande désigne une période par son nom.
        if (p.Nom.Length > 0)
        {
            var nom = Regles.NormaliserNom(p.Nom);
            var autres = await db.PeriodesGel.AsNoTracking().Where(x => x.Id != p.Id).Select(x => x.Nom).ToListAsync();
            if (autres.Any(x => Regles.NormaliserNom(x) == nom)) erreurs.Add($"Une période de gel s'appelle déjà « {p.Nom} ».");
        }
        if (erreurs.Count > 0) throw ErreurHttp.Saisie(erreurs);
    }

    private static string Decrire(PeriodeGel p, Noms noms)
    {
        var o = Regles.DecrireOccurrence(new OccurrenceGel(p, p.Debut, p.Fin));
        if (p.Recurrence != RecurrenceGel.Aucune) o += $", répétée ({p.Recurrence}{(p.FinRecurrence is { } f ? $" jusqu'au {f:dd/MM/yyyy}" : "")})";
        o += p.SystemeCatalogueIds.Count == 0 ? ", tous les systèmes" : ", systèmes " + string.Join(", ", p.SystemeCatalogueIds.Select(i => noms.Systemes.GetValueOrDefault(i) ?? i.ToString()));
        if (p.Environnements.Count > 0) o += ", environnements " + string.Join(", ", p.Environnements);
        if (p.ProductionSeulement) o += ", production seulement";
        return o + ", dérogations décidées par " + Decideurs(p, noms);
    }

    /// <summary>Crée une période de gel (journal d'administration « gel.cree »). <paramref name="acteur"/> null : ligne de commande.</summary>
    public async Task<PeriodeGelDto> Creer(Acteur? acteur, PeriodeGelSaisie s)
    {
        var p = new PeriodeGel { CreeeLe = Maintenant, ModifieeLe = Maintenant };
        await Appliquer(p, s);
        db.PeriodesGel.Add(p);
        var noms = await LireNoms();
        JournalAdmin.Ecrire(db, acteur, "gel.cree", $"Période de gel créée : {Decrire(p, noms)}" + (p.Motif.Length > 0 ? $". Motif : {p.Motif}" : ""));
        await db.SaveChangesAsync();
        return Dto(p, noms);
    }

    /// <summary>Remplace une période de gel (version obligatoire, 409 si périmée) ; ancienne et nouvelle valeur au journal.</summary>
    public async Task<PeriodeGelDto> Modifier(Acteur? acteur, Guid id, PeriodeGelSaisie s)
    {
        var p = await ChargerPeriode(id);
        if (s.Version is null) throw ErreurHttp.Saisie("La version de la période de gel est obligatoire.");
        if (s.Version != p.Version)
            throw ErreurHttp.Conflit("La période de gel a été modifiée par quelqu'un d'autre depuis votre dernière lecture. Rechargez-la puis recommencez.");
        var noms = await LireNoms();
        var avant = Decrire(p, noms);
        await Appliquer(p, s);
        var apres = Decrire(p, noms);
        p.Version++;
        p.ModifieeLe = Maintenant;
        JournalAdmin.Ecrire(db, acteur, "gel.modifie", avant == apres ? $"Période de gel modifiée (motif) : {apres}" : $"Période de gel modifiée : {avant} → {apres}");
        await db.SaveChangesAsync();
        return Dto(p, noms);
    }

    /// <summary>Supprime une période de gel (les dérogations déjà inscrites dans les MEP restent dans leur historique).</summary>
    public async Task Supprimer(Acteur? acteur, Guid id)
    {
        var p = await ChargerPeriode(id);
        db.PeriodesGel.Remove(p);
        JournalAdmin.Ecrire(db, acteur, "gel.supprime", $"Période de gel supprimée : {Decrire(p, await LireNoms())}");
        await db.SaveChangesAsync();
    }

    // ---------- Abonnement ----------

    /// <summary>Empreinte SHA-256 (hexadécimal minuscule) d'un secret d'abonnement.</summary>
    public static string Empreinte(string secret) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    /// <summary>Secret d'abonnement : 32 octets aléatoires en base64url (43 caractères).</summary>
    private static string NouveauSecret() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Forme d'un secret d'abonnement (avant toute lecture en base).</summary>
    public static bool SecretBienForme(string secret) => secret.Length == 43 && secret.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    public const string CheminAbonnement = "/api/calendrier/abonnement/";

    public async Task<AbonnementCalendrierDto> Abonnement(Guid compte) =>
        await db.AbonnementsCalendrier.AsNoTracking().FirstOrDefaultAsync(a => a.UtilisateurId == compte) is { } a
            ? new AbonnementCalendrierDto(true, a.CreeLe, a.DernierAcces, null)
            : new AbonnementCalendrierDto(false, null, null, null);

    /// <summary>
    /// Crée ou régénère l'adresse d'abonnement du compte : un nouveau secret remplace l'ancien (l'ancienne adresse cesse de
    /// répondre). L'adresse n'est donnée qu'ici. Journal d'administration : « calendrier.abonnement ».
    /// </summary>
    public async Task<AbonnementCalendrierDto> Regenerer(Acteur acteur, string? adresseBase)
    {
        var secret = NouveauSecret();
        var a = await db.AbonnementsCalendrier.FirstOrDefaultAsync(x => x.UtilisateurId == acteur.Id);
        var regenere = a is not null;
        if (a is null)
        {
            a = new AbonnementCalendrier { UtilisateurId = acteur.Id };
            db.AbonnementsCalendrier.Add(a);
        }
        a.Empreinte = Empreinte(secret);
        a.CreeLe = Maintenant;
        a.DernierAcces = null;
        JournalAdmin.Ecrire(db, acteur, "calendrier.abonnement", regenere ? "Lien d'abonnement au calendrier régénéré (l'ancien ne répond plus)" : "Lien d'abonnement au calendrier créé");
        await db.SaveChangesAsync();
        var chemin = $"{CheminAbonnement}{secret}.ics";
        return new AbonnementCalendrierDto(true, a.CreeLe, null, adresseBase is null ? chemin : adresseBase + chemin);
    }

    /// <summary>Révoque l'abonnement du compte (sans effet s'il n'y en a pas).</summary>
    public async Task Revoquer(Acteur acteur)
    {
        var a = await db.AbonnementsCalendrier.FirstOrDefaultAsync(x => x.UtilisateurId == acteur.Id);
        if (a is null) return;
        db.AbonnementsCalendrier.Remove(a);
        JournalAdmin.Ecrire(db, acteur, "calendrier.abonnement", "Lien d'abonnement au calendrier révoqué");
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Compte d'un secret d'abonnement, s'il désigne un abonnement d'un compte actif (null sinon) ; note le dernier accès (au
    /// plus une écriture toutes les 5 minutes).
    /// </summary>
    public async Task<Guid?> CompteAbonne(string secret)
    {
        if (!SecretBienForme(secret)) return null;
        var empreinte = Empreinte(secret);
        var a = await db.AbonnementsCalendrier.FirstOrDefaultAsync(x => x.Empreinte == empreinte);
        if (a is null || !await db.Utilisateurs.AsNoTracking().AnyAsync(u => u.Id == a.UtilisateurId && u.Actif)) return null;
        if (a.DernierAcces is not { } d || Maintenant - d > TimeSpan.FromMinutes(5))
        {
            a.DernierAcces = Maintenant;
            await db.SaveChangesAsync();
        }
        return a.UtilisateurId;
    }
}
