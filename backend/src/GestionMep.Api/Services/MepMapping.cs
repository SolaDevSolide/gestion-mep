using GestionMep.Domain;

namespace GestionMep.Api.Services;

/// <summary>Unique endroit où une entité <see cref="Mep"/> devient un <see cref="MepDto"/> (disponibilités, erreurs du plan, droits).</summary>
public static class MepMapping
{
    /// <summary>Compte vu par l'API ; <paramref name="nomPersonne"/> et <paramref name="equipeIds"/> viennent de l'annuaire (voir <see cref="Personnes"/>).</summary>
    public static UtilisateurDto VersDto(Utilisateur u, string nomPersonne, List<Guid> equipeIds) =>
        new(u.Id, u.Login, u.NomAffiche, u.EstAdmin, u.Role, u.Contact, u.Expertises.Select(x => new ExpertiseDto(x.SystemeCatalogueId, x.Detail)).ToList(),
            u.Actif, u.DoitChangerMotDePasse, u.ComptePrincipalId, nomPersonne, equipeIds);

    public static CompteAnnuaireDto VersAnnuaire(UtilisateurDto u) =>
        new(u.Id, u.Login, u.NomAffiche, u.Role, u.Contact, u.Expertises, u.Actif, u.ComptePrincipalId, u.NomPersonne, u.EquipeIds);

    public static PieceJointeDto VersDto(PieceJointe p, IReadOnlyDictionary<Guid, string> nomsUtilisateurs) =>
        new(p.Id, p.MepId, p.EtapeId, p.ResolutionTypeId, p.ExecutionResolutionId, p.NomFichier, p.TypeContenu, p.Taille, p.Sha256,
            p.DeposeParId, p.DeposeParId is { } d && nomsUtilisateurs.TryGetValue(d, out var n) ? n : null, p.DeposeLe);

    public static SectionDto VersDto(Section s, string? auteur) =>
        new(s.Id, s.Plan, s.Titre, s.Description, [.. s.SystemeIds], s.AuteurId, auteur, s.CreeLe, s.Placee, s.Revision,
            s.Ports.OrderBy(p => p.Sens).ThenBy(p => p.Ordre)
                .Select(p => new PortSectionDto(p.Id, p.Sens, p.Titre, p.Description, p.Ordre, [.. p.Etapes], [.. p.Sources])).ToList(), s.Rang);

    public static ObjetImpacteDto VersDto(ObjetImpacte o) =>
        new(o.Id, o.SystemeId, o.ObjetCatalogueId, o.Categorie, o.Type, o.Nom, o.Libelle, o.Precision, o.Version, o.Repris);

    /// <summary>Suivi des tests unitaires (route <c>/meps/{id}/tests-unitaires</c> et <c>gestion-mep meps exporter</c>).</summary>
    public static TestsUnitairesDto TestsUnitaires(Mep m)
    {
        var tests = Regles.TestsUnitaires(m).Select(e => new TestUnitaireDto(e.Id, e.Code, e.Plan, e.Action, e.Etat, Regles.TestFait(e),
            e.ResultatAttendu, e.ResultatConstate, m.Participants.FirstOrDefault(p => p.Id == e.ExecutantId)?.Nom, e.FinReelle)).ToList();
        var faits = tests.Count(t => t.Fait);
        return new TestsUnitairesDto(faits, tests.Count - faits, Regles.EtapesSuivies(m).Count(e => e.Fin == FinEtape.Verification), tests);
    }

    public static CommunicationDto Communication(Communication c, IReadOnlyList<PieceJointe> pieces) =>
        new(c.ReferenceDemande, c.NiveauRisque, c.Benefices, c.UtilisateursImpactes, c.ArretApplicatif, c.ActionUtilisateurs, c.HeureLimite,
            c.DateAnnoncee, c.HeureDebutAnnoncee, c.HeureFinAnnoncee, c.ValideeParUtilisateurs, c.ValideePar, c.ValideeLe,
            c.PreuveValidationId is { } p && pieces.Any(x => x.Id == p) ? p : null);

    /// <summary>
    /// <paramref name="contexte"/> : seuil d'approbations, annuaire (comptes reliés) et résolutions liées, pour les anomalies du
    /// plan et la séparation des tâches (voir <see cref="ServiceMep.Contexte"/>).
    /// </summary>
    public static MepDto VersDto(Mep m, Guid? utilisateurId, IReadOnlyDictionary<Guid, string> nomsUtilisateurs, DateTimeOffset maintenant,
        ContexteVerification contexte, IReadOnlyList<PieceJointe> pieces, bool estAdmin = false)
    {
        string? Nom(Guid? id) => id is { } g && nomsUtilisateurs.TryGetValue(g, out var n) ? n : null;
        ValidationDto? Validation(Guid? par, DateTimeOffset? le) =>
            par is null || le is null ? null : new ValidationDto(Nom(par) ?? "Inconnu", le.Value);

        var s = m.Sauvegarde;
        var pilote = utilisateurId is { } moi && Regles.EstPilote(m, moi);
        string? NomParticipant(Guid? id) => m.Participants.FirstOrDefault(p => p.Id == id)?.Nom;
        var participant = utilisateurId is null ? null : m.Participants.FirstOrDefault(p => p.UtilisateurId == utilisateurId);

        return new MepDto(
            m.Id, m.Version, m.Outil, m.VersionActuelle, m.VersionCible, m.Objet, m.Impacts,
            m.DateMep, m.HeureDebut, m.HeureFin, m.FuseauHoraire, m.IndisponibiliteMinutes,
            m.PiloteId, Regles.Suppleants(m), m.Phase, m.EcheanceRepli,
            m.Issue,
            m.RetourArriereDeclencheLe is { } declenche ? new RetourArriereDto(declenche, Nom(m.RetourArriereDeclenchePar), m.RetourArriereMotif) : null,
            m.ClotureeLe is { } cloture ? new ClotureDto(cloture, Nom(m.ClotureeParId), m.EtatService, m.ReferenceIncident) : null,
            new PlanRepliDto(m.PlanRepli.QuandDeclencher, m.PlanRepli.DureeTesteeMinutes, m.PlanRepli.VersionARestaurer,
                m.PlanRepli.DonneesDepuisSauvegarde, m.PlanRepli.SiRepliEchoue),
            new SauvegardeDto(s.Mode, s.Structure, s.Donnees, s.Configuration, s.ResponsableId, s.RealiseeLe, s.Procedure, s.Copies,
                s.RestaurationTestee, s.DateTestRestauration, s.PreuveTestRestauration),
            Validation(m.ProcedureRelueParId, m.ProcedureRelueLe),
            Validation(m.DemarrageAutoriseParId, m.DemarrageAutoriseLe),
            m.Participants.Select(p => new ParticipantDto(p.Id, p.Nom, p.Role, p.Contact, p.UtilisateurId, Nom(p.UtilisateurId))).ToList(),
            m.Systemes.Select(x => new SystemeDto(x.Id, x.Nom, x.Cible, x.Impact, x.CatalogueId,
                new SauvegardeSystemeDto(x.Sauvegarde.Statut, x.Sauvegarde.Moment, x.Sauvegarde.Contenu, x.Sauvegarde.Justification,
                    x.Sauvegarde.Emplacement, x.Sauvegarde.MethodeRecuperation),
                x.Environnements.Select(e => new EnvironnementSystemeDto(e.Nom, e.Production)).ToList())).ToList(),
            Regles.ObjetsTries(m).Select(VersDto).ToList(),
            m.EtapesTriees().Select(e =>
            {
                var d = Regles.Disponibilite(m, e, maintenant);
                return new EtapeDto(e.Id, e.Plan, e.Code, e.Ligne, e.Ordre, e.Action, [.. e.SystemeIds], e.ResponsableId, e.ExecutantId, e.Criticite,
                    e.DureeEstimeeMinutes, e.Etat,
                    e.Instructions, e.ResultatAttendu, e.Preuve, e.Commentaire, e.DebutReel, e.FinReelle, e.Revision, [.. e.Dependances], [.. e.ResolutionIds],
                    new DisponibiliteDto(d.Statut, d.Libelle), e.Type, e.PointDeNonRetour, Validation(e.NonRetourAutoriseParId, e.NonRetourAutoriseLe),
                    e.Fin, e.ResultatConstate, e.SauvegardeEmplacement, e.SauvegardeMethodeRecuperation, e.ModeleEtapeId,
                    e.Environnements.Select(x => new EnvironnementEtapeDto(x.SystemeId, x.Nom)).ToList(), e.SectionId,
                    [.. e.DependancesDerivees], [.. e.PortsAttendus], e.Rang, e.APlacer, e.AjouteeApresGo);
            }).ToList(),
            m.Resolutions.OrderBy(r => r.DemarreeLe).Select(r => new ExecutionResolutionDto(
                r.Id, r.EtapeId, r.ResolutionTypeId, r.Titre, r.Symptomes, r.Diagnostic,
                r.Corrections.OrderBy(c => c.Ordre).Select(c => new CorrectionExecuteeDto(
                    c.Ordre, c.Instruction, c.ResultatAttendu, c.Resultat, c.Observation, Nom(c.ParId), c.Le)).ToList(),
                r.VerificationInstruction, r.VerificationAttendu, r.VerificationResultat, r.VerificationObservation, r.Statut,
                Nom(r.DemarreeParId) ?? "Inconnu", r.DemarreeLe, r.TermineeLe)).ToList(),
            Regles.ErreursPlan(m, maintenant, contexte),
            new DroitsDto(pilote, m.Phase == Phase.Preparation, participant?.Id,
                utilisateurId is { } a && m.Approbations.Any(x => x.UtilisateurId == a),
                utilisateurId is { } rt && Regles.EstResponsableTechnique(m, rt),
                m.Phase == Phase.Preparation && utilisateurId is { } ed && Regles.PeutEditerContenu(m, ed, estAdmin),
                estAdmin,
                (pilote || estAdmin) && m.Phase == Phase.Preparation && !m.EstModele,
                pilote && m.Phase == Phase.Preparation && !m.EstModele,
                estAdmin && !Regles.ADejaEuUnGo(m),
                (pilote || estAdmin) && Regles.EstApresGoEnCours(m),
                m.Phase == Phase.Preparation
                    ? utilisateurId is { } ap && Regles.PeutEditerContenu(m, ap, estAdmin)
                    : (pilote || estAdmin) && Regles.EstApresGoEnCours(m),
                !m.EstModele && m.RetourArriereActive && (m.Phase == Phase.Preparation
                    ? utilisateurId is { } ec && Regles.PeutEditerContenu(m, ec, estAdmin)
                    : (pilote || estAdmin) && m.Phase is Phase.Execution or Phase.Recette)),
            m.EstModele, contexte.ApprobationsMinimum,
            m.Approbations.Select(a => new ApprobationDto(a.Id, a.UtilisateurId, Nom(a.UtilisateurId) ?? a.Nom, a.Decision, a.Commentaire, a.Le,
                Regles.EmpechementApprobation(m, a.UtilisateurId, contexte.Annuaire))).ToList(),
            m.GoNoGo.Select(c => new CritereValideDto(c.Libelle, Nom(c.ParId), c.Le)).ToList(),
            m.FichesRecette.OrderBy(f => f.Ordre).Select(f => new FicheRecetteDto(f.Id, f.Ordre, f.SystemeId, f.Titre,
                f.Etapes.OrderBy(t => t.Ordre).Select(t => new EtapeTestDto(t.Id, t.Ordre, t.Instructions, t.ResultatAttendu,
                    t.Resultat, t.Observation, Nom(t.ParId), t.Le, t.ValideePar, t.ValideeLe)).ToList())).ToList(),
            m.BilanActive,
            m.Bilan is { } b ? new BilanDto(b.CeQuiAMarche, b.Incidents, b.Actions, Nom(b.RedigeParId), b.Le) : null,
            Regles.EtapeNonRetourFranchie(m)?.Code,
            pieces.OrderBy(p => p.DeposeLe).Select(p => VersDto(p, nomsUtilisateurs)).ToList(),
            m.ResponsablesTechniques.Select(r => new ResponsableTechniqueDto(r.ParticipantId, [.. r.SystemeIds], r.Perimetre, r.Disponibilite, r.ContactJourJ)).ToList(),
            Regles.Avertissements(m),
            m.RetourArriereActive,
            Communication(m.Communication, pieces),
            m.Type,
            m.Sections.OrderBy(x => x.Plan).ThenBy(x => x.CreeLe).ThenBy(x => x.Titre, StringComparer.Ordinal).Select(x => VersDto(x, Nom(x.AuteurId))).ToList(),
            Regles.EtatDeLaMep(m), m.EchecsEtapes,
            m.Annulation is { } an ? new AnnulationDto(an.Motif, an.EtatProduction, Nom(an.ParId), an.Le) : null,
            m.Report is { } r ? new ReportDto(r.Motif, r.DateVisee, Nom(r.ParId), r.Le) : null,
            m.PremierGoLe,
            m.PilotageSecours is { } ps
                ? new PilotageSecoursDto(Nom(ps.ParId), ps.Le, ps.Motif, NomParticipant(ps.AncienPiloteId), NomParticipant(ps.NouveauPiloteId))
                : null,
            m.EchecsRecette,
            m.EcheanceSignaleeLe,
            m.ResolutionsFigees.Select(f => new ResolutionFigeeDto(f.ResolutionTypeId, f.Version, f.Titre, f.Symptomes, f.Diagnostic, [.. f.SystemeCatalogueIds],
                f.Corrections.OrderBy(c => c.Ordre).Select(c => new CorrectionDto(c.Ordre, c.Instruction, c.ResultatAttendu)).ToList(),
                f.VerificationInstruction, f.VerificationAttendu)).ToList(),
            m.ResolutionsFigeesLe);
    }
}
