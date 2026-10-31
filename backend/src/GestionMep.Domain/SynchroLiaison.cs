using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GestionMep.Domain;

/// <summary>Bilan d'une synchronisation du catalogue de Noosphere vers le catalogue des systèmes.</summary>
public sealed class BilanSynchro
{
    public List<string> Crees { get; } = [];
    public List<string> Rattaches { get; } = [];
    public List<string> MisAJour { get; } = [];
    public List<string> Desactives { get; } = [];
    public List<string> Reactives { get; } = [];
    public List<string> Avertissements { get; } = [];
    /// <summary>Applications mises de côté : un système local a un nom proche, l'administrateur choisit.</summary>
    public List<string> ARapprocher { get; } = [];
    /// <summary>Systèmes à ajouter au catalogue (créés par le suivi d'une application).</summary>
    public List<SystemeCatalogue> Nouveaux { get; } = [];

    public bool AChange => Crees.Count + Rattaches.Count + MisAJour.Count + Desactives.Count + Reactives.Count > 0;

    public string Resume()
    {
        var parties = new List<string>();
        void Ajouter(List<string> l, string libelle)
        {
            if (l.Count > 0) parties.Add($"{l.Count} {libelle}");
        }
        Ajouter(Crees, "créé(s)");
        Ajouter(Rattaches, "rattaché(s)");
        Ajouter(MisAJour, "mis à jour");
        Ajouter(Desactives, "désactivé(s)");
        Ajouter(Reactives, "réactivé(s)");
        Ajouter(ARapprocher, "à rapprocher");
        return parties.Count == 0 ? "aucun changement" : string.Join(", ", parties);
    }
}

/// <summary>
/// Règles de la liaison avec Noosphere, sans base ni réseau : application du catalogue aux systèmes suivis (création,
/// rattachement, mise à jour du nom et de la description, désactivation d'une application retirée ou disparue), et
/// événement de déploiement d'une MEP (correspondance des phases et de l'issue avec les valeurs du contrat).
/// </summary>
public static class SynchroLiaison
{
    /// <summary>Lien coupé, application plus suivie : le système redevient local (rien n'est supprimé).</summary>
    public static int RendreLocaux(Guid liaisonId, IEnumerable<SystemeCatalogue> systemes)
    {
        var n = 0;
        foreach (var s in systemes.Where(s => s.Source?.LiaisonId == liaisonId))
        {
            s.Source = null;
            s.Version++;
            n++;
        }
        return n;
    }

    /// <summary>Nom comparable : sans accents, minuscules, lettres et chiffres seulement.</summary>
    public static string NomNormalise(string nom)
    {
        var sb = new StringBuilder();
        foreach (var c in nom.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    /// <summary>Nom identique ou proche (sans accents ni ponctuation : égal, l'un contient l'autre, ou deux lettres d'écart).</summary>
    public static bool NomsProches(string a, string b)
    {
        var x = NomNormalise(a);
        var y = NomNormalise(b);
        if (x.Length == 0 || y.Length == 0) return false;
        if (x == y) return true;
        if (Math.Min(x.Length, y.Length) >= 3 && (x.Contains(y, StringComparison.Ordinal) || y.Contains(x, StringComparison.Ordinal))) return true;
        return Math.Max(x.Length, y.Length) >= 5 && Distance(x, y) <= 2;
    }

    private static int Distance(string a, string b)
    {
        var precedente = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var courante = new int[b.Length + 1];
            courante[0] = i;
            for (var j = 1; j <= b.Length; j++)
                courante[j] = Math.Min(Math.Min(courante[j - 1] + 1, precedente[j] + 1), precedente[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            precedente = courante;
        }
        return precedente[b.Length];
    }

    /// <summary>Systèmes locaux (non suivis) de nom identique ou proche, les identiques d'abord.</summary>
    public static List<SystemeCatalogue> Suggestions(ApplicationNoosphere app, IEnumerable<SystemeCatalogue> systemes) =>
        systemes.Where(s => s.Source is null && NomsProches(s.Nom, app.Nom))
            .OrderBy(s => NomNormalise(s.Nom) == NomNormalise(app.Nom) ? 0 : 1).ThenBy(s => s.Nom, StringComparer.OrdinalIgnoreCase).ToList();

    private static string? NomDomaine(CatalogueNoosphere c, string? id) => id is null ? null : c.Domaines?.FirstOrDefault(d => d.Id == id)?.Nom;

    private static string? NomEquipe(CatalogueNoosphere c, string? id) => id is null ? null : c.Equipes?.FirstOrDefault(d => d.Id == id)?.Nom;

    private static bool EstRetiree(ApplicationNoosphere a) => string.Equals(a.Cycle, "retired", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Suit une application : rattachée au système <paramref name="systemeId"/> (local), sinon à un système local de même
    /// nom (sauf <paramref name="creer"/> : choix « Créer un système » de la console), sinon un nouveau système est créé.
    /// Renvoie le système.
    /// </summary>
    public static SystemeCatalogue Suivre(Liaison l, CatalogueNoosphere c, ApplicationNoosphere app, List<SystemeCatalogue> systemes,
        Guid? systemeId, DateTimeOffset maintenant, BilanSynchro bilan, bool creer = false)
    {
        if (systemes.FirstOrDefault(s => s.Source?.LiaisonId == l.Id && s.Source.ApplicationId == app.Id) is { } deja) return deja;
        SystemeCatalogue? cible;
        if (systemeId is { } id)
        {
            cible = systemes.FirstOrDefault(s => s.Id == id) ?? throw new SaisieInvalideException("Système du catalogue introuvable.");
            if (cible.Source is not null)
                throw new RegleMetierException($"« {cible.Nom} » suit déjà une application de Noosphere : choisissez un autre système ou créez-en un.");
        }
        else cible = creer ? null : systemes.FirstOrDefault(s => s.Source is null && NomNormalise(s.Nom) == NomNormalise(app.Nom));
        if (cible is not null) bilan.Rattaches.Add(cible.Nom);
        else
        {
            cible = new SystemeCatalogue { Nom = NomLibre(app.Nom, systemes), Description = app.Description ?? "" };
            systemes.Add(cible);
            bilan.Nouveaux.Add(cible);
            bilan.Crees.Add(cible.Nom);
        }
        cible.Source = new SourceNoosphere { LiaisonId = l.Id, ApplicationId = app.Id };
        Mettre(c, app, cible, systemes, maintenant, bilan, nouveau: true);
        if (!l.ApplicationsConnues.Contains(app.Id)) l.ApplicationsConnues = [.. l.ApplicationsConnues, app.Id];
        if (l.ApplicationsARapprocher.Contains(app.Id)) l.ApplicationsARapprocher = [.. l.ApplicationsARapprocher.Where(x => x != app.Id)];
        return cible;
    }

    /// <summary>Nom du système à créer : celui de l'application, suivi de « (2) », « (3) »… s'il est déjà pris.</summary>
    private static string NomLibre(string nom, IEnumerable<SystemeCatalogue> systemes, SystemeCatalogue? sauf = null)
    {
        var pris = systemes.Where(s => s != sauf).Select(s => s.Nom.ToLowerInvariant()).ToHashSet();
        var base_ = string.IsNullOrWhiteSpace(nom) ? "Application" : nom.Trim();
        if (!pris.Contains(base_.ToLowerInvariant())) return base_;
        for (var i = 2; ; i++)
            if (!pris.Contains($"{base_} ({i})".ToLowerInvariant())) return $"{base_} ({i})";
    }

    /// <summary>Recopie ce que Noosphere gère (nom, description, domaine, équipe, cycle, adresse) et l'état actif.</summary>
    private static void Mettre(CatalogueNoosphere c, ApplicationNoosphere app, SystemeCatalogue s, List<SystemeCatalogue> systemes,
        DateTimeOffset maintenant, BilanSynchro bilan, bool nouveau = false)
    {
        var src = s.Source!;
        var change = false;
        var nom = (app.Nom ?? "").Trim();
        if (nom.Length > 0 && nom != s.Nom)
        {
            if (systemes.Any(x => x != s && x.Nom.Equals(nom, StringComparison.OrdinalIgnoreCase)))
                bilan.Avertissements.Add($"« {s.Nom} » garde son nom : « {nom} » est déjà pris par un autre système du catalogue.");
            else
            {
                s.Nom = nom;
                change = true;
            }
        }
        var description = app.Description ?? "";
        if (description != s.Description) { s.Description = description; change = true; }
        var domaine = NomDomaine(c, app.DomaineId) ?? "";
        var equipe = NomEquipe(c, app.EquipeId) ?? "";
        var cycle = (app.Cycle ?? "").ToLowerInvariant();
        var url = app.Url ?? "";
        if (src.Domaine != domaine || src.Equipe != equipe || src.Cycle != cycle || src.Url != url || src.Absente)
        {
            src.Domaine = domaine;
            src.Equipe = equipe;
            src.Cycle = cycle;
            src.Url = url;
            change = true;
        }
        var revenue = src.Absente;
        src.Absente = false;
        src.DerniereSynchro = maintenant;
        if (EstRetiree(app))
        {
            if (s.Actif)
            {
                s.Actif = false;
                src.DesactiveParLiaison = true;
                bilan.Desactives.Add(s.Nom);
                change = true;
            }
        }
        else if (!s.Actif && src.DesactiveParLiaison)
        {
            s.Actif = true;
            src.DesactiveParLiaison = false;
            bilan.Reactives.Add(s.Nom);
            change = true;
        }
        if (change || revenue)
        {
            s.Version++;
            if (!nouveau && !bilan.Desactives.Contains(s.Nom) && !bilan.Reactives.Contains(s.Nom)) bilan.MisAJour.Add(s.Nom);
        }
    }

    /// <summary>
    /// Applique un catalogue reçu : met à jour les systèmes suivis, désactive ceux dont l'application est retirée, supprimée
    /// ou devenue restreinte (absente du catalogue), et suit les nouvelles applications des domaines suivis si le suivi
    /// automatique est activé. Les systèmes ne sont jamais supprimés.
    /// </summary>
    public static BilanSynchro Appliquer(Liaison l, CatalogueNoosphere c, List<SystemeCatalogue> systemes, DateTimeOffset maintenant)
    {
        var bilan = new BilanSynchro();
        var apps = (c.Applications ?? []).Where(a => !string.IsNullOrWhiteSpace(a.Id)).GroupBy(a => a.Id).Select(g => g.First()).ToDictionary(a => a.Id);
        foreach (var s in systemes.Where(s => s.Source?.LiaisonId == l.Id).ToList())
        {
            if (apps.TryGetValue(s.Source!.ApplicationId, out var app)) Mettre(c, app, s, systemes, maintenant, bilan);
            else if (!s.Source.Absente)
            {
                s.Source.Absente = true;
                s.Source.DerniereSynchro = maintenant;
                if (s.Actif)
                {
                    s.Actif = false;
                    s.Source.DesactiveParLiaison = true;
                }
                s.Version++;
                bilan.Desactives.Add(s.Nom);
            }
        }
        var connues = l.ApplicationsConnues.ToHashSet();
        var enAttente = l.ApplicationsARapprocher.ToHashSet();
        foreach (var app in apps.Values.Where(a => !connues.Contains(a.Id) || enAttente.Contains(a.Id)).OrderBy(a => a.Id, StringComparer.Ordinal))
        {
            // Une application déjà en attente y reste même sans suivi automatique : l'administrateur l'a demandée par son domaine.
            if (!enAttente.Remove(app.Id) && !l.SuiviAutomatique) continue;
            if (app.DomaineId is not { } d || !l.DomainesSuivis.Contains(d) || EstRetiree(app)
                || systemes.Any(s => s.Source?.LiaisonId == l.Id && s.Source.ApplicationId == app.Id)) continue;
            if (Suggestions(app, systemes).Count > 0)
            {
                enAttente.Add(app.Id);
                bilan.ARapprocher.Add(app.Nom);
            }
            else Suivre(l, c, app, systemes, null, maintenant, bilan);
        }
        // Une application en attente disparue du catalogue n'attend plus rien.
        l.ApplicationsARapprocher = [.. enAttente.Where(apps.ContainsKey).Order(StringComparer.Ordinal)];
        l.ApplicationsConnues = [.. connues.Union(apps.Keys).Order(StringComparer.Ordinal)];
        return bilan;
    }

    /// <summary>
    /// Suit les applications d'un domaine que l'administrateur vient d'ajouter, sauf les retirées et celles qu'il a choisies une
    /// par une : une application au nom proche d'un système local attend « Rattacher » ou « Créer un système ».
    /// </summary>
    public static void SuivreDomaine(Liaison l, CatalogueNoosphere c, IEnumerable<ApplicationNoosphere> apps, List<SystemeCatalogue> systemes,
        DateTimeOffset maintenant, BilanSynchro bilan)
    {
        foreach (var app in apps.Where(a => !EstRetiree(a)))
        {
            if (systemes.Any(s => s.Source?.LiaisonId == l.Id && s.Source.ApplicationId == app.Id)) continue;
            if (Suggestions(app, systemes).Count > 0)
            {
                if (!l.ApplicationsARapprocher.Contains(app.Id)) l.ApplicationsARapprocher = [.. l.ApplicationsARapprocher, app.Id];
                bilan.ARapprocher.Add(app.Nom);
            }
            else Suivre(l, c, app, systemes, null, maintenant, bilan);
        }
    }

    // ---------- Événements de déploiement ----------

    /// <summary>Phase de gestion-mep → phase du contrat.</summary>
    public static string PhaseContrat(Phase p) => p switch
    {
        Phase.Preparation => "preparation",
        Phase.Execution => "execution",
        Phase.RetourArriere => "rollback",
        Phase.Recette => "acceptance",
        Phase.Cloturee => "closed",
        Phase.Annulee => "cancelled",
        _ => "preparation",
    };

    /// <summary>Issue d'une MEP clôturée → issue du contrat (null tant qu'elle n'est pas clôturée).</summary>
    public static string? IssueContrat(Mep m) => m.Phase != Phase.Cloturee ? null : Regles.EtatDe(m.Phase, m.Issue, Regles.Incidents(m)) switch
    {
        EtatMep.AvecIncidents => "success-with-incidents",
        EtatMep.Repliee => "rolled-back",
        EtatMep.Echec => "failed",
        _ => "success",
    };

    public static string DateContrat(DateTimeOffset d) => d.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>
    /// Créneau prévu : date annoncée (ou date visée d'un report en cours), heure de début annoncée (minuit sans heure), dans
    /// le fuseau de la MEP ; à défaut, les anciennes date et heures de la MEP. Fin : heure de fin annoncée (le lendemain si
    /// elle précède le début), sinon début + indisponibilité prévue.
    /// </summary>
    public static (DateTimeOffset? Debut, DateTimeOffset? Fin) Creneau(Mep m)
    {
        var c = m.Communication;
        DateOnly? jour = m.Report?.DateVisee ?? c.DateAnnoncee ?? m.DateMep;
        if (jour is not { } j) return (null, null);
        var (hDebut, hFin) = c.DateAnnoncee is not null || m.Report?.DateVisee is not null
            ? (c.HeureDebutAnnoncee, c.HeureFinAnnoncee)
            : (m.HeureDebut, m.HeureFin);
        var debut = Fuseaux.Instant(j, hDebut, m.FuseauHoraire);
        DateTimeOffset? fin = null;
        if (hFin is { } hf)
        {
            fin = Fuseaux.Instant(j, hf, m.FuseauHoraire);
            if (fin <= debut) fin = Fuseaux.Instant(j.AddDays(1), hf, m.FuseauHoraire);
        }
        else if (m.IndisponibiliteMinutes is > 0 and var minutes) fin = debut.AddMinutes(minutes);
        return (debut, fin);
    }

    public static string Titre(Mep m)
    {
        var t = $"{m.Outil} {m.VersionCible}".Trim();
        if (t.Length == 0) t = m.Objet.Trim();
        return t.Length == 0 ? "MEP" : t.Length > 200 ? t[..200] : t;
    }

    /// <summary>
    /// Événement de déploiement d'une MEP pour une liaison : <paramref name="applications"/> associe chaque système suivi du
    /// catalogue (identifiant) à son application Noosphere. Seuls les systèmes suivis et leurs environnements sont envoyés.
    /// </summary>
    public static EvenementDeploiement Evenement(Mep m, IReadOnlyDictionary<Guid, string> applications, string urlBase, DateTimeOffset maintenant)
    {
        var suivis = m.Systemes.Where(s => s.CatalogueId is { } id && applications.ContainsKey(id)).ToList();
        var (debut, fin) = Creneau(m);
        return new EvenementDeploiement(m.Id, Titre(m), PhaseContrat(m.Phase), IssueContrat(m), m.Phase == Phase.Preparation && m.Report is not null,
            debut is { } d ? DateContrat(d) : null, fin is { } f ? DateContrat(f) : null,
            $"{urlBase.TrimEnd('/')}/meps/{m.Id}",
            [.. suivis.Select(s => applications[s.CatalogueId!.Value]).Distinct().Order(StringComparer.Ordinal)],
            [.. suivis.SelectMany(s => s.Environnements).GroupBy(e => e.Nom, StringComparer.OrdinalIgnoreCase)
                .Select(g => new EnvironnementDeploiement(g.First().Nom, g.Any(e => e.Production))).OrderBy(e => e.Nom, StringComparer.OrdinalIgnoreCase)],
            DateContrat(maintenant));
    }

    /// <summary>MEP supprimée après un envoi : le dernier événement envoyé, annulé.</summary>
    public static EvenementDeploiement Supprimee(EvenementDeploiement precedent, DateTimeOffset maintenant) =>
        precedent with { Phase = "cancelled", Issue = null, Reportee = false, MisAJourLe = DateContrat(maintenant) };

    /// <summary>Empreinte du contenu (hors <c>updatedAt</c>) : un événement identique au dernier envoyé ne repart pas.</summary>
    public static string Empreinte(EvenementDeploiement e) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(e with { MisAJourLe = "" })))[..32];
}

/// <summary>
/// File des MEP modifiées à envoyer, avec anti-rebond : la première modification d'une MEP fixe l'envoi 30 s plus tard, et
/// les modifications suivantes s'y regroupent (au plus un envoi toutes les 30 s pour un même lot). Sans état en base : au
/// démarrage et toutes les 15 minutes, un rapprochement complet rattrape ce qui aurait été perdu.
/// </summary>
public sealed class FileDeploiements(TimeSpan? delai = null)
{
    public static readonly TimeSpan DelaiParDefaut = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _delai = delai ?? DelaiParDefaut;
    private readonly Dictionary<Guid, DateTimeOffset> _attente = [];
    private readonly object _verrou = new();

    public void Signaler(Guid mepId, DateTimeOffset maintenant)
    {
        lock (_verrou)
            if (!_attente.ContainsKey(mepId)) _attente[mepId] = maintenant + _delai;
    }

    /// <summary>Remet des MEP en file pour une date précise (nouvel essai).</summary>
    public void Replanifier(IEnumerable<Guid> meps, DateTimeOffset quand)
    {
        lock (_verrou)
            foreach (var m in meps)
                _attente[m] = _attente.TryGetValue(m, out var d) && d < quand ? d : quand;
    }

    /// <summary>MEP dont l'échéance est atteinte, retirées de la file.</summary>
    public List<Guid> Prendre(DateTimeOffset maintenant)
    {
        lock (_verrou)
        {
            var prets = _attente.Where(p => p.Value <= maintenant).Select(p => p.Key).ToList();
            foreach (var p in prets) _attente.Remove(p);
            return prets;
        }
    }

    public DateTimeOffset? ProchaineEcheance()
    {
        lock (_verrou) return _attente.Count == 0 ? null : _attente.Values.Min();
    }

    public int Count
    {
        get { lock (_verrou) return _attente.Count; }
    }
}
