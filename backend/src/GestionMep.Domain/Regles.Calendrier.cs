using System.Globalization;
using System.Text;

namespace GestionMep.Domain;

// Règles métier : calendrier des MEP, périodes de gel, dérogations et conflits (#129).
public static partial class Regles
{
    /// <summary>Version de la règle de <see cref="FenetreDe"/> : l'augmenter fait recalculer toutes les MEP au démarrage.</summary>
    public const int FormatCalendrier = 1;

    /// <summary>Durée la plus longue d'une occurrence, par répétition : une occurrence ne chevauche jamais la suivante.</summary>
    private static TimeSpan? DureeMaximale(RecurrenceGel r) => r switch
    {
        RecurrenceGel.Hebdomadaire => TimeSpan.FromDays(7),
        RecurrenceGel.Mensuelle or RecurrenceGel.FinDeMois => TimeSpan.FromDays(28),
        RecurrenceGel.Annuelle => TimeSpan.FromDays(365),
        _ => null,
    };

    /// <summary>MEP lancée (exécution, recette ou retour arrière) : son créneau s'étend au moins jusqu'à maintenant.</summary>
    public static bool EstLancee(Phase p) => p is Phase.Execution or Phase.Recette or Phase.RetourArriere;

    // ---------- Créneau d'une MEP ----------

    /// <summary>
    /// Créneau prévu : date visée d'un report en cours, sinon date annoncée (Communication), sinon ancienne date de la MEP ; heure
    /// de début annoncée dans le fuseau de la MEP. Sans heure de début, la journée locale entière. Fin : heure de fin annoncée
    /// (le lendemain si elle précède le début), sinon début + indisponibilité prévue, sinon inconnue.
    /// </summary>
    public static (DateTimeOffset? Debut, DateTimeOffset? Fin, bool JourneeEntiere) CreneauPrevu(Mep m)
    {
        var c = m.Communication;
        DateOnly? jour = m.Report?.DateVisee ?? c.DateAnnoncee ?? m.DateMep;
        if (jour is not { } j) return (null, null, false);
        var heureDebut = c.DateAnnoncee is not null || m.Report?.DateVisee is not null ? c.HeureDebutAnnoncee : m.HeureDebut;
        if (heureDebut is null) return (Fuseaux.Instant(j, null, m.FuseauHoraire), Fuseaux.Instant(j.AddDays(1), null, m.FuseauHoraire), true);
        var (debut, fin) = SynchroLiaison.Creneau(m);
        return (debut, fin, false);
    }

    /// <summary>
    /// Créneau de la MEP dans le calendrier. En préparation : le créneau prévu. Lancée : du Go à Go + durée prévue (inconnue sans
    /// durée prévue), prolongé jusqu'à maintenant tant qu'elle n'est pas clôturée (<see cref="FinEffective"/>). Clôturée : du Go à
    /// la clôture. Annulée : le créneau prévu, sinon du Go à l'annulation. Sans Go, une MEP lancée ou clôturée (données
    /// anciennes) garde son créneau prévu. Un modèle n'a pas de créneau.
    /// </summary>
    public static FenetreCalendrier FenetreDe(Mep m)
    {
        var f = new FenetreCalendrier { Format = FormatCalendrier };
        if (m.EstModele) return f;
        var (prevu, finPrevue, journee) = CreneauPrevu(m);
        void Prevu()
        {
            f.Debut = prevu;
            f.Fin = finPrevue;
            f.JourneeEntiere = journee;
        }
        switch (m.Phase)
        {
            case Phase.Preparation:
                Prevu();
                break;
            case Phase.Execution or Phase.Recette or Phase.RetourArriere when m.DemarrageAutoriseLe is { } go:
                f.Debut = go;
                f.Fin = !journee && prevu is { } d && finPrevue is { } fp ? go + (fp - d) : null;
                break;
            case Phase.Cloturee when m.DemarrageAutoriseLe is { } go:
                f.Debut = go;
                f.Fin = m.ClotureeLe is { } c && c >= go ? c : null;
                break;
            case Phase.Annulee when prevu is null && m.DemarrageAutoriseLe is { } go:
                f.Debut = go;
                f.Fin = m.Annulation?.Le is { } a && a >= go ? a : null;
                break;
            default:
                Prevu();
                break;
        }
        // Enregistré et renvoyé en UTC (le fuseau ne sert qu'au calcul).
        f.Debut = f.Debut?.ToUniversalTime();
        f.Fin = f.Fin?.ToUniversalTime();
        return f;
    }

    /// <summary>Recalcule le créneau enregistré de la MEP (appelé à chaque enregistrement) ; ne touche que ce qui change.</summary>
    public static void MettreAJourCalendrier(Mep m)
    {
        var f = FenetreDe(m);
        TomberDerogations(m, f);
        var c = m.Calendrier ??= new FenetreCalendrier();
        if (c.Debut != f.Debut) c.Debut = f.Debut;
        if (c.Fin != f.Fin) c.Fin = f.Fin;
        if (c.JourneeEntiere != f.JourneeEntiere) c.JourneeEntiere = f.JourneeEntiere;
        if (c.Format != f.Format) c.Format = f.Format;
    }

    /// <summary>
    /// Une dérogation demandée ou accordée vaut pour le créneau prévu au moment de la demande : si la date de la MEP change
    /// pendant la préparation, elle tombe (<see cref="StatutDerogation.Caduque"/>) et se redemande.
    /// </summary>
    public static void TomberDerogations(Mep m, FenetreCalendrier f)
    {
        if (m.Phase != Phase.Preparation) return;
        foreach (var d in m.DerogationsGel)
            if (d.Statut is StatutDerogation.Demandee or StatutDerogation.Accordee && d.CreneauDebut is not null
                && (d.CreneauDebut != f.Debut || d.CreneauFin != f.Fin))
                d.Statut = StatutDerogation.Caduque;
    }

    /// <summary>
    /// Fin à retenir pour les chevauchements : la fin enregistrée, sinon le début (instant) ; une MEP lancée s'étend au moins
    /// jusqu'à <paramref name="maintenant"/>.
    /// </summary>
    public static DateTimeOffset FinEffective(DateTimeOffset debut, DateTimeOffset? fin, bool enCours, DateTimeOffset maintenant)
    {
        var f = fin is { } x && x > debut ? x : debut;
        return enCours && maintenant > f ? maintenant : f;
    }

    /// <summary>
    /// Deux créneaux [début, fin) se chevauchent ; un créneau sans durée (début = fin) est un instant, qui touche un créneau
    /// s'il y tombe, ou un autre instant au même moment.
    /// </summary>
    public static bool Chevauchent(DateTimeOffset aDebut, DateTimeOffset aFin, DateTimeOffset bDebut, DateTimeOffset bFin)
    {
        if (aDebut == bDebut) return true;
        var aInstant = aFin <= aDebut;
        var bInstant = bFin <= bDebut;
        if (aInstant) return bDebut <= aDebut && aDebut < bFin;
        if (bInstant) return aDebut <= bDebut && bDebut < aFin;
        return aDebut < bFin && bDebut < aFin;
    }

    // ---------- Périodes de gel ----------

    /// <summary>Anomalies de saisie d'une période de gel (vide : valide).</summary>
    public static List<string> ErreursPeriodeGel(PeriodeGel p)
    {
        var erreurs = new List<string>();
        if (p.Nom.Trim().Length == 0) erreurs.Add("Le nom de la période de gel est obligatoire.");
        else if (p.Nom.Trim().Length > 200) erreurs.Add("Le nom de la période de gel dépasse 200 caractères.");
        if (p.Motif.Length > 2000) erreurs.Add("Le motif dépasse 2 000 caractères.");
        if (p.Fin <= p.Debut) erreurs.Add("La fin de la période de gel doit être après son début.");
        if (!Enum.IsDefined(p.Recurrence)) erreurs.Add("Répétition inconnue : Aucune, Hebdomadaire, Mensuelle, FinDeMois ou Annuelle.");
        else if (DureeMaximale(p.Recurrence) is { } max && p.Fin - p.Debut > max)
            erreurs.Add($"Une période répétée ne peut pas durer plus de {max.TotalDays} jours : elle chevaucherait la suivante.");
        if (Fuseaux.Normaliser(p.FuseauHoraire) is null) erreurs.Add($"Fuseau horaire inconnu : « {p.FuseauHoraire} ».");
        if (p.FinRecurrence is { } fr)
        {
            if (p.Recurrence == RecurrenceGel.Aucune) erreurs.Add("Une date de fin de répétition n'a de sens que pour une période répétée.");
            else if (fr < Fuseaux.Jour(p.Debut, p.FuseauHoraire)) erreurs.Add("La fin de la répétition précède le début de la période.");
        }
        if (p.Environnements.Any(e => e.Trim().Length == 0 || e.Trim().Length > 100)) erreurs.Add("Un environnement est vide ou dépasse 100 caractères.");
        return erreurs;
    }

    private static DateTime DecalerOccurrence(DateTime debutLocal, RecurrenceGel r, int n)
    {
        switch (r)
        {
            case RecurrenceGel.Hebdomadaire: return debutLocal.AddDays(7 * n);
            case RecurrenceGel.Mensuelle: return debutLocal.AddMonths(n);
            case RecurrenceGel.Annuelle: return debutLocal.AddYears(n);
            case RecurrenceGel.FinDeMois:
                var ecart = DateTime.DaysInMonth(debutLocal.Year, debutLocal.Month) - debutLocal.Day;
                var mois = new DateTime(debutLocal.Year, debutLocal.Month, 1, 0, 0, 0, DateTimeKind.Unspecified).AddMonths(n);
                var jour = Math.Max(1, DateTime.DaysInMonth(mois.Year, mois.Month) - ecart);
                return mois.AddDays(jour - 1) + debutLocal.TimeOfDay;
            default: return debutLocal;
        }
    }

    /// <summary>Rang de la première occurrence à examiner pour une plage qui commence à <paramref name="du"/> (une de marge).</summary>
    private static int PremierRang(PeriodeGel p, DateTime debutLocal, DateTimeOffset du)
    {
        if (du <= p.Debut) return 0;
        var local = Fuseaux.Local(du, p.FuseauHoraire);
        var n = p.Recurrence switch
        {
            RecurrenceGel.Hebdomadaire => (int)Math.Floor((local - debutLocal).TotalDays / 7),
            RecurrenceGel.Mensuelle or RecurrenceGel.FinDeMois => (local.Year - debutLocal.Year) * 12 + local.Month - debutLocal.Month,
            RecurrenceGel.Annuelle => local.Year - debutLocal.Year,
            _ => 0,
        };
        return Math.Max(0, n - 1);
    }

    /// <summary>
    /// Occurrences de la période qui touchent [<paramref name="du"/>, <paramref name="au"/>), dans l'ordre. Une période répétée se
    /// répète à la même heure locale dans son fuseau (même durée locale), jusqu'à <see cref="PeriodeGel.FinRecurrence"/>.
    /// </summary>
    public static IEnumerable<OccurrenceGel> Occurrences(PeriodeGel p, DateTimeOffset du, DateTimeOffset au)
    {
        if (p.Fin <= p.Debut || au <= du) yield break;
        if (p.Recurrence == RecurrenceGel.Aucune)
        {
            if (p.Debut < au && p.Fin > du) yield return new OccurrenceGel(p, p.Debut, p.Fin);
            yield break;
        }
        var debutLocal = Fuseaux.Local(p.Debut, p.FuseauHoraire);
        var duree = Fuseaux.Local(p.Fin, p.FuseauHoraire) - debutLocal;
        var premier = PremierRang(p, debutLocal, du);
        // Garde-fou : une plage de calendrier reste bornée (366 jours), quelques centaines d'occurrences au plus.
        for (var n = premier; n < premier + 2000; n++)
        {
            var local = DecalerOccurrence(debutLocal, p.Recurrence, n);
            if (p.FinRecurrence is { } fr && DateOnly.FromDateTime(local) > fr) yield break;
            var finLocale = local + duree;
            var debut = Fuseaux.Instant(DateOnly.FromDateTime(local), TimeOnly.FromDateTime(local), p.FuseauHoraire);
            var fin = Fuseaux.Instant(DateOnly.FromDateTime(finLocale), TimeOnly.FromDateTime(finLocale), p.FuseauHoraire);
            if (debut >= au) yield break;
            if (fin > du && fin > debut) yield return new OccurrenceGel(p, debut.ToUniversalTime(), fin.ToUniversalTime());
        }
    }

    /// <summary>Texte sans accents ni casse (mêmes règles que les filtres de la liste des MEP).</summary>
    public static string NormaliserNom(string? s)
    {
        var d = (s ?? "").Trim().Replace("œ", "oe").Replace("Œ", "oe").Replace("æ", "ae").Replace("Æ", "ae").Normalize(NormalizationForm.FormD);
        return new string(d.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant();
    }

    /// <summary>
    /// La période concerne une MEP qui touche ces systèmes : gel global, ou l'un de ses systèmes du catalogue est touché ; puis,
    /// si la période le précise, l'un de ces systèmes touche un des environnements nommés (sans casse ni accents) et, avec
    /// <see cref="PeriodeGel.ProductionSeulement"/>, un environnement de production. Une MEP sans système n'est concernée que par
    /// un gel global sans condition d'environnement.
    /// </summary>
    public static bool Concerne(PeriodeGel p, IEnumerable<Systeme> systemes)
    {
        var cibles = (p.SystemeCatalogueIds.Count == 0
            ? systemes
            : systemes.Where(s => s.CatalogueId is { } id && p.SystemeCatalogueIds.Contains(id))).ToList();
        if (p.SystemeCatalogueIds.Count > 0 && cibles.Count == 0) return false;
        if (p.Environnements.Count == 0 && !p.ProductionSeulement) return true;
        var envs = p.Environnements.Select(NormaliserNom).ToHashSet();
        return cibles.Any(s => s.Environnements.Any(e => (envs.Count == 0 || envs.Contains(NormaliserNom(e.Nom))) && (!p.ProductionSeulement || e.Production)));
    }

    /// <summary>Occurrences de gel qui touchent le créneau [début, fin effective] et concernent ces systèmes, par début.</summary>
    public static List<OccurrenceGel> GelsDuCreneau(DateTimeOffset debut, DateTimeOffset finEffective, IReadOnlyCollection<Systeme> systemes,
        IEnumerable<PeriodeGel> periodes)
    {
        // Plage d'examen élargie d'une seconde : un créneau sans durée doit trouver l'occurrence où il tombe.
        var au = finEffective > debut ? finEffective : debut.AddSeconds(1);
        return periodes.Where(p => Concerne(p, systemes))
            .SelectMany(p => Occurrences(p, debut, au))
            .Where(o => Chevauchent(debut, finEffective, o.Debut, o.Fin))
            .OrderBy(o => o.Debut).ThenBy(o => o.Periode.Nom, StringComparer.Ordinal).ToList();
    }

    /// <summary>Occurrences de gel qui touchent le créneau de la MEP (vide pour une MEP sans date) : signalées en préparation.</summary>
    public static List<OccurrenceGel> GelsDeLaMep(Mep m, IEnumerable<PeriodeGel> periodes, DateTimeOffset maintenant)
    {
        var f = FenetreDe(m);
        if (f.Debut is not { } debut) return [];
        return GelsDuCreneau(debut, FinEffective(debut, f.Fin, EstLancee(m.Phase), maintenant), m.Systemes, periodes);
    }

    /// <summary>
    /// Occurrences de gel que toucherait un Go donné maintenant : de maintenant à maintenant + durée prévue de la MEP (un instant
    /// sans durée prévue).
    /// </summary>
    public static List<OccurrenceGel> GelsAuGo(Mep m, IEnumerable<PeriodeGel> periodes, DateTimeOffset maintenant)
    {
        var (debut, fin, journee) = CreneauPrevu(m);
        var duree = !journee && debut is { } d && fin is { } f && f > d ? f - d : TimeSpan.Zero;
        return GelsDuCreneau(maintenant, maintenant + duree, m.Systemes, periodes);
    }

    /// <summary>Occurrences pour lesquelles une dérogation peut être demandée : celles du créneau prévu et celles d'un Go maintenant.</summary>
    public static List<OccurrenceGel> GelsConcernes(Mep m, IReadOnlyCollection<PeriodeGel> periodes, DateTimeOffset maintenant) =>
        [.. GelsDeLaMep(m, periodes, maintenant).Concat(GelsAuGo(m, periodes, maintenant))
            .DistinctBy(o => (o.Periode.Id, o.Debut)).OrderBy(o => o.Debut)];

    /// <summary>Dernière demande de dérogation pour cette occurrence (null : aucune).</summary>
    public static DerogationGel? DerogationPour(Mep m, Guid periodeId, DateTimeOffset occurrence) =>
        m.DerogationsGel.LastOrDefault(d => d.PeriodeGelId == periodeId && d.Occurrence == occurrence);

    /// <summary>Une dérogation accordée couvre cette occurrence.</summary>
    public static bool DerogationAccordee(Mep m, OccurrenceGel o) =>
        m.DerogationsGel.Any(d => d.Statut == StatutDerogation.Accordee && d.PeriodeGelId == o.Periode.Id && d.Occurrence == o.Debut);

    /// <summary>« « Clôture comptable » du 28/11/2026 00h00 au 03/12/2026 00h00 (Europe/Paris) ».</summary>
    public static string DecrireOccurrence(OccurrenceGel o)
    {
        string Local(DateTimeOffset t)
        {
            var l = Fuseaux.Local(t, o.Periode.FuseauHoraire);
            return $"{l.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)} {VariablesTexte.Heure(TimeOnly.FromDateTime(l))}";
        }
        return $"« {o.Periode.Nom} » du {Local(o.Debut)} au {Local(o.Fin)} ({o.Periode.FuseauHoraire})";
    }

    /// <summary>
    /// Go pendant une période de gel : refusé (<see cref="GoBloqueParGelException"/>) tant qu'une occurrence touchée par le Go
    /// n'a pas de dérogation accordée.
    /// </summary>
    public static void ExigerHorsGel(Mep m, IReadOnlyCollection<PeriodeGel> periodes, DateTimeOffset maintenant)
    {
        var bloquants = GelsAuGo(m, periodes, maintenant).Where(o => !DerogationAccordee(m, o)).ToList();
        if (bloquants.Count == 0) return;
        throw new GoBloqueParGelException(
            $"Go impossible pendant la période de gel {string.Join(", ", bloquants.Select(DecrireOccurrence))} : demander une dérogation, "
            + "avec son motif, et la faire accorder par un approbateur de la MEP.", bloquants);
    }

    // ---------- Dérogations ----------

    /// <summary>
    /// Demande de dérogation à une occurrence de gel : pilote ou suppléant, en préparation, motif obligatoire ; refusée si une
    /// demande pour cette occurrence attend déjà une décision ou a été accordée (une demande refusée ou retirée se refait).
    /// </summary>
    public static DerogationGel DemanderDerogation(Mep m, Guid compteId, OccurrenceGel o, string motif, DateTimeOffset maintenant)
    {
        if (!EstPilote(m, compteId)) throw new DroitInsuffisantException("Seul le pilote ou un suppléant peut demander une dérogation au gel.");
        if (m.EstModele) throw new RegleMetierException("Un modèle ne reçoit pas de dérogation : il ne passe jamais en exécution.");
        if (m.Phase != Phase.Preparation) throw new RegleMetierException("Une dérogation au gel se demande en préparation, avant le Go.");
        if (string.IsNullOrWhiteSpace(motif)) throw new RegleMetierException("Indiquer le motif de la dérogation au gel.");
        switch (DerogationPour(m, o.Periode.Id, o.Debut)?.Statut)
        {
            case StatutDerogation.Demandee:
                throw new RegleMetierException($"Une dérogation à « {o.Periode.Nom} » attend déjà la décision d'un approbateur.");
            case StatutDerogation.Accordee:
                throw new RegleMetierException($"La dérogation à « {o.Periode.Nom} » est déjà accordée.");
        }
        var d = new DerogationGel
        {
            PeriodeGelId = o.Periode.Id, NomPeriode = o.Periode.Nom, Occurrence = o.Debut, OccurrenceFin = o.Fin, Motif = motif.Trim(),
            DemandeeParId = compteId, DemandeeLe = maintenant,
        };
        var creneau = FenetreDe(m);
        (d.CreneauDebut, d.CreneauFin) = (creneau.Debut, creneau.Fin);
        m.DerogationsGel.Add(d);
        return d;
    }

    /// <summary>
    /// Approbateur d'une période de gel : un compte de la liste de la période (comptes reliés compris) ou membre d'une de ses
    /// équipes ; sans liste (ou période supprimée depuis la demande), un administrateur.
    /// </summary>
    public static bool EstApprobateurGel(PeriodeGel? p, Guid compteId, bool estAdmin, AnnuairePersonnes? annuaire = null)
    {
        if (p is null || (p.ApprobateurIds.Count == 0 && p.EquipeApprobatriceIds.Count == 0)) return estAdmin;
        var comptes = annuaire?.ComptesDeLaPersonne(compteId).Select(c => c.Id).ToHashSet() is { Count: > 0 } liste ? liste : [compteId];
        if (comptes.Overlaps(p.ApprobateurIds)) return true;
        var equipes = annuaire?.EquipesDeLaPersonne(compteId) ?? new HashSet<Guid>();
        return p.EquipeApprobatriceIds.Any(equipes.Contains);
    }

    /// <summary>
    /// Décision sur une dérogation : par un approbateur de la période (<see cref="EstApprobateurGel"/>) que la séparation des
    /// tâches n'écarte pas (<see cref="EmpechementApprobation"/> : ni pilote, ni suppléant, ni responsable technique de la MEP),
    /// jamais par la personne qui l'a demandée (comptes reliés compris) ; en préparation, demande en attente ; un refus exige un
    /// commentaire. <paramref name="annuaire"/> : avec les équipes, pour les approbateurs par équipe.
    /// </summary>
    public static void DeciderDerogation(Mep m, Guid compteId, bool estAdmin, PeriodeGel? periode, DerogationGel d, bool accorder,
        string commentaire, DateTimeOffset maintenant, AnnuairePersonnes? annuaire = null)
    {
        if (PeutDeciderDerogation(m, compteId, estAdmin, periode, d, annuaire) is { } refus) throw new DroitInsuffisantException(refus);
        if (m.Phase != Phase.Preparation) throw new RegleMetierException("Une dérogation au gel se décide en préparation, avant le Go.");
        if (d.Statut != StatutDerogation.Demandee) throw new RegleMetierException("Cette dérogation n'attend plus de décision.");
        if (!accorder && string.IsNullOrWhiteSpace(commentaire)) throw new RegleMetierException("Expliquer le refus dans le commentaire.");
        d.Statut = accorder ? StatutDerogation.Accordee : StatutDerogation.Refusee;
        d.DecideeParId = compteId;
        d.DecideeLe = maintenant;
        d.Commentaire = commentaire.Trim();
    }

    /// <summary>Raison pour laquelle ce compte ne peut pas décider de la dérogation (null : il le peut, à l'état et à la phase près).</summary>
    public static string? PeutDeciderDerogation(Mep m, Guid compteId, bool estAdmin, PeriodeGel? periode, DerogationGel d,
        AnnuairePersonnes? annuaire = null)
    {
        Guid Personne(Guid c) => annuaire?.Personne(c) ?? c;
        if (!EstApprobateurGel(periode, compteId, estAdmin, annuaire))
            return periode is null || (periode.ApprobateurIds.Count == 0 && periode.EquipeApprobatriceIds.Count == 0)
                ? "Seul un administrateur peut accorder ou refuser une dérogation à cette période de gel."
                : $"Seul un approbateur de la période de gel « {periode.Nom} » peut accorder ou refuser cette dérogation.";
        if (Personne(d.DemandeeParId) == Personne(compteId)) return "Une dérogation ne s'accorde jamais par la personne qui l'a demandée.";
        if (EmpechementApprobation(m, compteId, annuaire) is { } role) return $"Vous ne pouvez pas décider de cette dérogation ({role}).";
        return null;
    }

    /// <summary>Retrait d'une dérogation demandée ou accordée, par le pilote ou un suppléant, en préparation.</summary>
    public static void RetirerDerogation(Mep m, Guid compteId, DerogationGel d)
    {
        if (!EstPilote(m, compteId)) throw new DroitInsuffisantException("Seul le pilote ou un suppléant peut retirer une dérogation au gel.");
        if (m.Phase != Phase.Preparation) throw new RegleMetierException("Une dérogation au gel se retire en préparation, avant le Go.");
        if (d.Statut is not (StatutDerogation.Demandee or StatutDerogation.Accordee)) throw new RegleMetierException("Cette dérogation est déjà close.");
        d.Statut = StatutDerogation.Retiree;
    }

    // ---------- Conflits ----------

    /// <summary>Clé d'un système pour les conflits : identifiant du catalogue, ou « libre: » et le nom normalisé (comme les filtres).</summary>
    public static string CleSysteme(Systeme s) => s.CatalogueId is { } id ? id.ToString() : "libre:" + NormaliserNom(s.Nom);

    /// <summary>
    /// Conflits : deux MEP qui touchent un même système (<see cref="CleSysteme"/>) sur des créneaux qui se chevauchent
    /// (<see cref="Chevauchent"/>, une MEP lancée s'étendant jusqu'à maintenant). Simple signalement, jamais bloquant. Les MEP
    /// annulées et les modèles ne doivent pas figurer dans <paramref name="elements"/>.
    /// </summary>
    public static Dictionary<Guid, List<Conflit>> Conflits(IReadOnlyCollection<ElementCalendrier> elements, DateTimeOffset maintenant)
    {
        var resultat = elements.ToDictionary(e => e.MepId, _ => new List<Conflit>());
        var tries = elements.Select(e => (E: e, Fin: FinEffective(e.Debut, e.Fin, e.EnCours, maintenant))).OrderBy(x => x.E.Debut).ToList();
        for (var i = 0; i < tries.Count; i++)
        {
            var (a, aFin) = tries[i];
            for (var j = i + 1; j < tries.Count; j++)
            {
                var (b, bFin) = tries[j];
                if (b.Debut > aFin) break;
                if (!Chevauchent(a.Debut, aFin, b.Debut, bFin)) continue;
                var communs = a.Systemes.Intersect(b.Systemes).Order(StringComparer.Ordinal).ToList();
                if (communs.Count == 0) continue;
                resultat[a.MepId].Add(new Conflit(b.MepId, communs));
                resultat[b.MepId].Add(new Conflit(a.MepId, communs));
            }
        }
        return resultat;
    }

    // ---------- Modèles de texte ----------

    /// <summary>
    /// Valeur de <c>{{gel}}</c> : occurrences qui touchent le créneau de la MEP, avec l'état de la dérogation ; « aucune » hors
    /// gel ; null (information manquante) pour une MEP sans date. <paramref name="nom"/> : nom affiché d'un compte.
    /// </summary>
    public static string? TexteGel(Mep m, IReadOnlyCollection<PeriodeGel> periodes, Func<Guid, string?> nom, DateTimeOffset maintenant)
    {
        if (FenetreDe(m).Debut is null) return null;
        var gels = GelsDeLaMep(m, periodes, maintenant);
        if (gels.Count == 0) return "aucune";
        return string.Join(" ; ", gels.Select(o =>
        {
            var d = DerogationPour(m, o.Periode.Id, o.Debut);
            var etat = d?.Statut switch
            {
                StatutDerogation.Accordee => d.DecideeParId is { } par && nom(par) is { } n ? $"dérogation accordée par {n}" : "dérogation accordée",
                StatutDerogation.Demandee => "dérogation demandée, en attente d'accord",
                _ => "sans dérogation",
            };
            return $"{DecrireOccurrence(o)} : {etat}";
        }));
    }
}
