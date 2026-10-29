using System.Threading.Channels;
using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Api.Endpoints;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Services;

/// <summary>Réveil du service d'envoi des appels (<c>EnvoiAppelsApi</c>) : capacité 1, les réveils en trop sont ignorés.</summary>
public sealed class SignalAppels
{
    private readonly Channel<bool> _signal = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public void Reveiller() => _signal.Writer.TryWrite(true);

    /// <summary>Attend un réveil, au plus <paramref name="max"/>.</summary>
    public async Task Attendre(TimeSpan max, CancellationToken ct)
    {
        using var delai = CancellationTokenSource.CreateLinkedTokenSource(ct);
        delai.CancelAfter(max);
        try
        {
            await _signal.Reader.WaitToReadAsync(delai.Token);
            _signal.Reader.TryRead(out _);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Passage régulier.
        }
    }
}

/// <summary>
/// Connexions aux API externes, côté application : valeurs des variables d'un événement, filtres, création des appels
/// (file d'envoi écrite dans la transaction de l'événement), lecture des connexions et de leurs appels.
/// </summary>
public sealed partial class ServiceConnexions(MepDbContext db, IConfiguration config, TimeProvider temps, SecretsConnexion secrets, SignalAppels signal)
{
    private List<ConnexionApi>? _actives;
    private AnnuairePersonnes? _annuaire;
    private List<Equipe>? _equipes;

    /// <summary>Des appels ont été mis en file : réveiller le service d'envoi une fois la transaction validée.</summary>
    public bool AReveiller { get; private set; }

    public DateTimeOffset Maintenant => temps.GetUtcNow();

    public static string NomMep(Mep m) => ExcelCommun.NomMep(m.Outil, m.VersionCible);

    /// <summary>Adresse de la MEP : elle exige le réglage url-publique (un envoi automatique n'a pas de requête d'où la déduire).</summary>
    public string? LienMep(Guid id) => config["Application:UrlPublique"] is { Length: > 0 } u ? $"{u.TrimEnd('/')}/meps/{id}" : null;

    public void Reveiller()
    {
        AReveiller = false;
        signal.Reveiller();
    }

    // ---------- Événements ----------

    /// <summary>Clé de déclencheur d'une ligne du journal (un test de recette n'en a une que s'il est en échec) ; null si aucune.</summary>
    public static string? CleJournal(string type, string detail) =>
        type == "recette.test"
            ? detail.Split(" : ")[0].EndsWith(" en échec", StringComparison.Ordinal) ? DeclencheursConnexion.RecetteNonConforme : null
            : DeclencheursConnexion.DeEvenement(type)?.Cle;

    /// <summary>Motif de l'événement : no-go, report, annulation, retour arrière, passage de pilotage.</summary>
    public static string Motif(Mep mep, string cle, string detail) => cle switch
    {
        "gonogo.nogo" => detail.StartsWith("No-go : ", StringComparison.Ordinal) ? detail["No-go : ".Length..] : detail,
        "mep.reportee" => mep.Report?.Motif ?? "",
        "mep.annulee" => mep.Annulation?.Motif ?? "",
        "repli.declenche" => mep.RetourArriereMotif,
        "pilotage" => detail.IndexOf("Motif : ", StringComparison.Ordinal) is var i and >= 0
            ? detail[(i + "Motif : ".Length)..].Split(". ")[0].TrimEnd('.') : "",
        _ => "",
    };

    /// <summary>Résumé court pour le journal de la MEP : « E0005 en échec », « Go donné ».</summary>
    public static string Resume(Mep mep, string cle, Guid? etapeId)
    {
        var etape = etapeId is { } id ? mep.Etapes.FirstOrDefault(e => e.Id == id) : null;
        if (etape is not null)
            return cle switch
            {
                "etape.demarree" => $"{etape.Code} démarrée",
                "etape.reussie" => $"{etape.Code} réussie",
                "etape.echec" => $"{etape.Code} en échec",
                "etape.non-retour-autorise" => $"point de non-retour {etape.Code} autorisé",
                _ => $"{DeclencheursConnexion.Libelle(cle)} ({etape.Code})",
            };
        return DeclencheursConnexion.Libelle(cle);
    }

    public static ContexteEvenement Contexte(Mep mep, EvenementJournal e, string cle) =>
        new(cle, e.Detail, e.Auteur, e.Quand, e.EtapeId, Motif(mep, cle, e.Detail));

    private async Task<AnnuairePersonnes> Annuaire() => _annuaire ??= await Personnes.Charger(db);

    private async Task<List<Equipe>> Equipes() =>
        _equipes ??= (await db.Equipes.AsNoTracking().ToListAsync()).OrderBy(e => e.Nom, EquipesEndpoints.OrdreDesNoms).ToList();

    /// <summary>Valeurs de toutes les variables d'un message pour la MEP et l'événement (équipe : celle du pilote, comme les textes).</summary>
    public async Task<Dictionary<string, ValeurVariable>> Valeurs(Mep mep, ContexteEvenement ev, Guid acteurId)
    {
        var equipes = await Equipes();
        var equipeId = CommunicationEndpoints.EquipeProposee(mep, equipes, await Annuaire(), acteurId);
        var pieces = await db.PiecesJointes.AsNoTracking().Where(p => p.MepId == mep.Id).ToDictionaryAsync(p => p.Id, p => p.NomFichier);
        var texte = VariablesTexte.Valeurs(mep, equipes.FirstOrDefault(e => e.Id == equipeId), LienMep(mep.Id), pieces, Maintenant, Regles.Incidents(mep));
        return VariablesEvenement.Valeurs(mep, ev, texte);
    }

    /// <summary>
    /// Filtres d'une connexion (liste vide : tout) : équipe (une personne de la MEP en est membre), système du catalogue,
    /// environnement (sans casse ni accents), niveau de risque, type de MEP, criticité de l'étape (événements d'étape).
    /// </summary>
    public async Task<bool> Correspond(ConnexionApi c, Mep mep, string cle, Guid? etapeId)
    {
        var f = c.Filtres ?? new FiltresConnexion();
        if (f.TypesMep.Count > 0 && !f.TypesMep.Contains(mep.Type)) return false;
        if (f.NiveauxRisque.Count > 0 && !f.NiveauxRisque.Contains(mep.Communication.NiveauRisque)) return false;
        if (f.SystemeCatalogueIds.Count > 0 && !mep.Systemes.Any(s => s.CatalogueId is { } id && f.SystemeCatalogueIds.Contains(id))) return false;
        if (f.Environnements.Count > 0)
        {
            var envs = f.Environnements.Select(e => VuesExcel.Normaliser(e.Trim())).ToHashSet();
            if (!mep.Systemes.Any(s => s.Environnements.Any(e => envs.Contains(VuesExcel.Normaliser(e.Nom.Trim()))))) return false;
        }
        if (f.CriticitesEtape.Count > 0 && DeclencheursConnexion.EstEvenementEtape(cle))
        {
            var etape = etapeId is { } id ? mep.Etapes.FirstOrDefault(e => e.Id == id) : null;
            if (etape?.Criticite is not { } criticite || !f.CriticitesEtape.Contains(criticite)) return false;
        }
        if (f.EquipeIds.Count > 0)
        {
            var annuaire = await Annuaire();
            if (!mep.Participants.Any(p => p.UtilisateurId is { } u && annuaire.EquipesDeLaPersonne(u).Overlaps(f.EquipeIds))) return false;
        }
        return true;
    }

    /// <summary>
    /// Nouvel appel d'une connexion pour un événement de la MEP : message rendu une fois pour toutes (sans secret), en file
    /// (<see cref="EtatAppel.EnAttente"/>). Un message qui ne se remplit pas (variable supprimée…) donne un appel en échec.
    /// </summary>
    public async Task<AppelApi> NouvelAppel(ConnexionApi c, Mep mep, ContexteEvenement ev, OrigineAppel origine, Acteur? demandeur)
    {
        var appel = new AppelApi
        {
            ConnexionId = c.Id, MepId = mep.Id, MepNom = NomMep(mep), TypeEvenement = ev.Cle, LibelleEvenement = ev.Libelle,
            Resume = Resume(mep, ev.Cle, ev.EtapeId), Origine = origine, DemandeParId = demandeur?.Id, DemandePar = demandeur?.Nom ?? "",
            TentativesMax = origine == OrigineAppel.Essai ? 1 : c.Tentatives, DelaiSecondes = c.DelaiSecondes, CreeLe = Maintenant,
            ProchaineTentative = Maintenant,
        };
        var valeurs = await Valeurs(mep, ev with { AppelId = appel.Id }, demandeur?.Id ?? Guid.Empty);
        Remplir(appel, c, RequeteConnexion.Rendre(c, valeurs));
        return appel;
    }

    /// <summary>Recopie la requête rendue dans l'appel ; s'il ne peut pas partir, l'appel est aussitôt en échec.</summary>
    public void Remplir(AppelApi appel, ConnexionApi c, RequeteRendue r)
    {
        appel.Methode = r.Methode;
        appel.Url = r.Url;
        appel.UrlSecrete = r.UrlSecrete;
        appel.TypeContenu = r.TypeContenu;
        appel.Corps = r.Corps;
        appel.Entetes = r.Entetes;
        if (r.Valide) return;
        var raison = r.Erreurs.FirstOrDefault() is { } e
            ? $"{e.Message} ({LibelleChamp(e.Champ)}, ligne {e.Ligne}, colonne {e.Colonne})"
            : $"variable inconnue {{{{{r.Inconnues[0]}}}}}";
        appel.Etat = EtatAppel.Echec;
        appel.ProchaineTentative = null;
        appel.TermineLe = Maintenant;
        appel.Tentatives.Add(new TentativeAppel
        {
            Debut = Maintenant, TypeErreur = "message",
            Erreur = $"Le message de la connexion « {c.Nom} » ne se remplit pas : {raison}. Corrigez la connexion, puis renvoyez.",
        });
    }

    public static string LibelleChamp(string champ) =>
        champ == "adresse" ? "adresse" : champ == "corps" ? "message" : champ.StartsWith("entete:", StringComparison.Ordinal) ? $"en-tête {champ[7..]}" : champ;

    private async Task<List<ConnexionApi>> Actives() =>
        _actives ??= await db.ConnexionsApi.AsNoTracking().Where(c => c.Active && c.Declencheurs.Count > 0).ToListAsync();

    /// <summary>
    /// Met en file les appels des connexions actives dont un déclencheur et les filtres correspondent aux événements que
    /// la MEP vient d'écrire au journal (même transaction : rien ne part si l'écriture est annulée). Jamais pour un modèle.
    /// </summary>
    public async Task Planifier(Mep mep, IReadOnlyList<(EvenementJournal Evenement, string? Declencheur)> evenements)
    {
        if (mep.EstModele || evenements.Count == 0) return;
        var cles = evenements.Select(e => e.Declencheur ?? CleJournal(e.Evenement.Type, e.Evenement.Detail)).ToList();
        if (cles.All(c => c is null)) return;
        var actives = await Actives();
        if (actives.Count == 0) return;
        for (var i = 0; i < evenements.Count; i++)
        {
            if (cles[i] is not { } cle) continue;
            var journal = evenements[i].Evenement;
            foreach (var c in actives.Where(c => c.Declencheurs.Contains(cle)))
            {
                if (!await Correspond(c, mep, cle, journal.EtapeId)) continue;
                var appel = await NouvelAppel(c, mep, Contexte(mep, journal, cle), OrigineAppel.Automatique, null);
                appel.Evenement = journal;
                db.AppelsApi.Add(appel);
                AReveiller = true;
            }
        }
    }

    // ---------- Lecture ----------

    public static AppelResumeDto Resume(AppelApi a, string connexion)
    {
        var derniere = a.Tentatives.LastOrDefault();
        return new AppelResumeDto(a.Id, a.ConnexionId, connexion, a.MepId, a.MepNom, a.TypeEvenement, a.LibelleEvenement, a.Resume, a.Origine,
            a.DemandePar, a.Etat, a.DernierCode, a.Tentatives.Count(t => t.TypeErreur != "message"), a.TentativesMax, derniere?.DureeMs ?? 0,
            a.CreeLe, a.TermineLe, a.Etat == EtatAppel.EnAttente ? a.ProchaineTentative : null, derniere?.Erreur ?? "");
    }

    public static TentativeDto Dto(TentativeAppel t) =>
        new(t.Debut, t.DureeMs, t.Code, t.Libelle, t.TypeErreur, t.Erreur, t.ExtraitReponse, t.EntetesReponse);

    /// <summary>Dernier appel (hors essais), dernier appel terminé (hors essais) et dernier essai de chaque connexion.</summary>
    private async Task<(Dictionary<Guid, AppelApi> Derniers, Dictionary<Guid, AppelApi> Termines, Dictionary<Guid, AppelApi> Essais)> Derniers(Guid? connexionId = null)
    {
        var q = db.AppelsApi.AsNoTracking().Where(a => connexionId == null || a.ConnexionId == connexionId);
        var derniers = await q.Where(a => a.Origine != OrigineAppel.Essai).GroupBy(a => a.ConnexionId)
            .Select(g => g.OrderByDescending(a => a.CreeLe).First()).ToListAsync();
        var termines = await q.Where(a => a.Origine != OrigineAppel.Essai && (a.Etat == EtatAppel.Reussi || a.Etat == EtatAppel.Echec))
            .GroupBy(a => a.ConnexionId).Select(g => g.OrderByDescending(a => a.CreeLe).First()).ToListAsync();
        var essais = await q.Where(a => a.Origine == OrigineAppel.Essai).GroupBy(a => a.ConnexionId)
            .Select(g => g.OrderByDescending(a => a.CreeLe).First()).ToListAsync();
        return (derniers.ToDictionary(a => a.ConnexionId), termines.ToDictionary(a => a.ConnexionId), essais.ToDictionary(a => a.ConnexionId));
    }

    private static EtatConnexion Etat(ConnexionApi c, AppelApi? dernier, AppelApi? termine) =>
        !c.Active ? EtatConnexion.Desactivee
        : termine?.Etat == EtatAppel.Echec ? EtatConnexion.EnErreur
        : dernier is null ? EtatConnexion.JamaisAppelee
        : EtatConnexion.Active;

    public async Task<List<ConnexionApiResumeDto>> Lister()
    {
        var connexions = await db.ConnexionsApi.AsNoTracking().OrderBy(c => c.Nom).ToListAsync();
        var (derniers, termines, essais) = await Derniers();
        return connexions.Select(c =>
        {
            var dernier = derniers.GetValueOrDefault(c.Id);
            return new ConnexionApiResumeDto(c.Id, c.Nom, c.Description, c.Active, c.Modele, c.Methode,
                RequeteConnexion.UrlAffichee(c.Url, c.UrlSecrete is not null), [.. c.Declencheurs], c.EnvoiManuel, c.LibelleEnvoiManuel,
                Etat(c, dernier, termines.GetValueOrDefault(c.Id)), dernier is null ? null : Resume(dernier, c.Nom),
                essais.GetValueOrDefault(c.Id) is { } e ? Resume(e, c.Nom) : null);
        }).ToList();
    }

    public async Task<ConnexionApi> Charger(Guid id) =>
        await db.ConnexionsApi.FirstOrDefaultAsync(c => c.Id == id) ?? throw ErreurHttp.NonTrouve("Cette connexion n'existe plus ou a été supprimée.");

    public async Task<ConnexionApiDto> Dto(ConnexionApi c)
    {
        var (derniers, termines, _) = await Derniers(c.Id);
        var dernier = derniers.GetValueOrDefault(c.Id);
        return new ConnexionApiDto(c.Id, c.Nom, c.Description, c.Active, c.Version, c.Modele, c.Methode, c.Url, c.UrlSecrete is not null,
            secrets.Etat(c.UrlSecrete), c.Authentification, c.Utilisateur, c.NomEntete, c.TokenUrl, c.ClientId, c.Scope, secrets.Etat(c.Secret),
            c.Entetes.Select(e => new EnteteConnexionDto(e.Nom, e.Secret ? "" : e.Valeur, e.Secret, e.Secret ? secrets.Etat(e.ValeurSecrete) : null)).ToList(),
            c.SecretSignature is not null, secrets.Etat(c.SecretSignature), c.Format, c.Corps, c.Champs, c.DelaiSecondes, c.Tentatives,
            [.. c.Declencheurs], c.Filtres ?? new FiltresConnexion(), c.EnvoiManuel, c.LibelleEnvoiManuel, c.CreeeLe, c.ModifieeLe, c.ModifieePar,
            Etat(c, dernier, termines.GetValueOrDefault(c.Id)), dernier is null ? null : Resume(dernier, c.Nom));
    }

    /// <summary>Commande curl équivalente à l'appel, secrets remplacés par &lt;secret&gt;.</summary>
    public static string Curl(ConnexionApi? c, AppelApi a, IEnumerable<EnteteAppelDto> entetes)
    {
        static string Q(string s) => "'" + s.Replace("'", "'\\''") + "'";
        var url = a.UrlSecrete ? $"{RequeteConnexion.Origine(a.Url)}/<secret>" : a.Url;
        var lignes = new List<string> { $"curl -X {a.Methode.ToString().ToUpperInvariant()} {Q(url)}" };
        foreach (var e in entetes)
            lignes.Add($"  -H {Q($"{e.Nom}: {e.Valeur.Replace(RequeteConnexion.Masque, "<secret>")}")}");
        if (a.TypeContenu.Length > 0) lignes.Add($"  --data-binary {Q(a.Corps)}");
        return string.Join(" \\\n", lignes);
    }
}
