namespace GestionMep.Domain;

// Règles métier : modèles et duplication.
public static partial class Regles
{
    /// <summary>
    /// Copie profonde d'une MEP, en préparation, avec de nouveaux identifiants et toutes les références remappées
    /// (codes d'étapes, lignes et positions conservés). Ne sont pas copiés : échéance, date et heures de MEP, responsable des étapes (obsolète), relecture, go / no-go, bilan,
    /// résolutions exécutées, résultats (étapes, recettes, sauvegarde réalisée) et décisions d'approbation.
    /// </summary>
    public const string PrefixeModele = "Modèle : ";

    /// <summary>
    /// Outil de la copie : « X (copie) » ; une MEP créée depuis un modèle reprend l'outil du modèle sans le préfixe « Modèle : ».
    /// </summary>
    public static string NomCopie(Mep source, bool commeModele)
    {
        var outil = source.Outil.Trim();
        if (source.EstModele && !commeModele)
            return outil.StartsWith(PrefixeModele, StringComparison.OrdinalIgnoreCase) ? outil[PrefixeModele.Length..].Trim() : outil;
        return outil.EndsWith("(copie)", StringComparison.OrdinalIgnoreCase) ? outil : $"{outil} (copie)";
    }

    public static Mep Dupliquer(Mep source, string? environnement, bool commeModele, Guid utilisateurId, DateTimeOffset maintenant)
    {
        var mep = new Mep
        {
            Outil = NomCopie(source, commeModele), VersionActuelle = source.VersionActuelle, VersionCible = source.VersionCible,
            Objet = source.Objet, Impacts = source.Impacts,
            FuseauHoraire = source.FuseauHoraire, IndisponibiliteMinutes = source.IndisponibiliteMinutes,
            Environnement = string.IsNullOrWhiteSpace(environnement) ? source.Environnement : environnement.Trim(),
            EstModele = commeModele, BilanActive = source.BilanActive,
            CreeLe = maintenant, CreeParUtilisateurId = utilisateurId,
            PlanRepli = new PlanRepli
            {
                QuandDeclencher = source.PlanRepli.QuandDeclencher, DureeTesteeMinutes = source.PlanRepli.DureeTesteeMinutes,
                VersionARestaurer = source.PlanRepli.VersionARestaurer, DonneesDepuisSauvegarde = source.PlanRepli.DonneesDepuisSauvegarde,
                SiRepliEchoue = source.PlanRepli.SiRepliEchoue,
            },
        };

        var participants = new Dictionary<Guid, Guid>();
        foreach (var p in source.Participants)
        {
            var copie = new Participant { MepId = mep.Id, Nom = p.Nom, Role = p.Role, Contact = p.Contact, UtilisateurId = p.UtilisateurId };
            participants[p.Id] = copie.Id;
            mep.Participants.Add(copie);
        }
        Guid? P(Guid? id) => id is { } g && participants.TryGetValue(g, out var n) ? n : null;
        mep.PiloteId = P(source.PiloteId);
        mep.SuppleantIds = source.SuppleantIds.Select(id => P(id)).OfType<Guid>().ToList();

        var systemes = new Dictionary<Guid, Guid>();
        foreach (var s in source.Systemes)
        {
            var copie = new Systeme
            {
                MepId = mep.Id, Nom = s.Nom, Cible = s.Cible, Impact = s.Impact, CatalogueId = s.CatalogueId,
                Sauvegarde = new SauvegardeSysteme
                {
                    Statut = s.Sauvegarde.Statut, Moment = s.Sauvegarde.Moment, Contenu = s.Sauvegarde.Contenu, Justification = s.Sauvegarde.Justification,
                },
            };
            systemes[s.Id] = copie.Id;
            mep.Systemes.Add(copie);
        }
        Guid? S(Guid? id) => id is { } g && systemes.TryGetValue(g, out var n) ? n : null;
        mep.ResponsablesTechniques = source.ResponsablesTechniques
            .Where(r => P(r.ParticipantId) is not null)
            .Select(r => new ResponsableTechnique
            {
                ParticipantId = P(r.ParticipantId)!.Value, SystemeIds = r.SystemeIds.Select(id => S(id)).OfType<Guid>().ToList(),
                Perimetre = r.Perimetre, Disponibilite = r.Disponibilite, ContactJourJ = r.ContactJourJ,
            }).ToList();

        foreach (var l in source.Livrables)
            mep.Livrables.Add(new Livrable { MepId = mep.Id, Objet = l.Objet, Version = l.Version, Emplacement = l.Emplacement, SystemeId = S(l.SystemeId) });

        var s0 = source.Sauvegarde;
        mep.Sauvegarde = new Sauvegarde
        {
            Mode = s0.Mode, Structure = s0.Structure, Donnees = s0.Donnees, Configuration = s0.Configuration,
            ResponsableId = P(s0.ResponsableId), Procedure = s0.Procedure, Copies = s0.Copies,
        };

        var etapes = source.Etapes.ToDictionary(e => e.Id, _ => Guid.NewGuid());
        foreach (var e in source.EtapesTriees())
            mep.Etapes.Add(new Etape
            {
                Id = etapes[e.Id], MepId = mep.Id, Plan = e.Plan, Type = e.Type, PointDeNonRetour = e.PointDeNonRetour,
                Code = e.Code, Ligne = e.Ligne, Ordre = e.Ordre, Action = e.Action, SystemeIds = e.SystemeIds.Select(id => S(id)).OfType<Guid>().ToList(),
                DureeEstimeeMinutes = e.DureeEstimeeMinutes,
                Criticite = e.Criticite, Instructions = e.Instructions, ResultatAttendu = e.ResultatAttendu,
                Dependances = e.Dependances.Where(etapes.ContainsKey).Select(d => etapes[d]).ToList(),
                ResolutionIds = [.. e.ResolutionIds],
            });

        mep.FichesRecette = source.FichesRecette.OrderBy(f => f.Ordre).Select(f => new FicheRecette
        {
            Ordre = f.Ordre, SystemeId = S(f.SystemeId), Titre = f.Titre,
            Etapes = f.Etapes.OrderBy(t => t.Ordre).Select(t => new EtapeTest { Ordre = t.Ordre, Instructions = t.Instructions, ResultatAttendu = t.ResultatAttendu }).ToList(),
        }).ToList();
        mep.Approbations = source.Approbations.Select(a => new Approbation { UtilisateurId = a.UtilisateurId, Nom = a.Nom }).ToList();
        return mep;
    }
}
