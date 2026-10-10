using GestionMep.Domain;

namespace GestionMep.Api.Services;

/// <summary>Unique endroit où une entité <see cref="Mep"/> devient un <see cref="MepDto"/> (disponibilités, erreurs du plan, droits).</summary>
public static class MepMapping
{
    public static UtilisateurDto VersDto(Utilisateur u) =>
        new(u.Id, u.Login, u.NomAffiche, u.EstAdmin, u.Role, u.Contact, u.Expertises.Select(x => new ExpertiseDto(x.SystemeCatalogueId, x.Detail)).ToList());

    public static PieceJointeDto VersDto(PieceJointe p, IReadOnlyDictionary<Guid, string> nomsUtilisateurs) =>
        new(p.Id, p.MepId, p.EtapeId, p.ResolutionTypeId, p.ExecutionResolutionId, p.NomFichier, p.TypeContenu, p.Taille, p.Sha256,
            p.DeposeParId, p.DeposeParId is { } d && nomsUtilisateurs.TryGetValue(d, out var n) ? n : null, p.DeposeLe);

    public static MepDto VersDto(Mep m, Guid? utilisateurId, IReadOnlyDictionary<Guid, string> nomsUtilisateurs, DateTimeOffset maintenant,
        int approbationsMinimum, IReadOnlyList<PieceJointe> pieces, bool estAdmin = false)
    {
        string? Nom(Guid? id) => id is { } g && nomsUtilisateurs.TryGetValue(g, out var n) ? n : null;
        ValidationDto? Validation(Guid? par, DateTimeOffset? le) =>
            par is null || le is null ? null : new ValidationDto(Nom(par) ?? "Inconnu", le.Value);

        var s = m.Sauvegarde;
        var participant = utilisateurId is null ? null : m.Participants.FirstOrDefault(p => p.UtilisateurId == utilisateurId);

        return new MepDto(
            m.Id, m.Version, m.Outil, m.VersionActuelle, m.VersionCible, m.Objet, m.Impacts,
            m.DateMep, m.HeureDebut, m.HeureFin, m.FuseauHoraire, m.IndisponibiliteMinutes,
            m.PiloteId, [.. m.SuppleantIds], m.Phase, m.EcheanceRepli,
            m.Issue,
            m.RetourArriereDeclencheLe is { } declenche ? new RetourArriereDto(declenche, Nom(m.RetourArriereDeclenchePar), m.RetourArriereMotif) : null,
            m.ClotureeLe is { } cloture ? new ClotureDto(cloture, Nom(m.ClotureeParId)) : null,
            new PlanRepliDto(m.PlanRepli.QuandDeclencher, m.PlanRepli.DureeTesteeMinutes, m.PlanRepli.VersionARestaurer,
                m.PlanRepli.DonneesDepuisSauvegarde, m.PlanRepli.SiRepliEchoue),
            new SauvegardeDto(s.Mode, s.Structure, s.Donnees, s.Configuration, s.ResponsableId, s.RealiseeLe, s.Procedure, s.Copies,
                s.RestaurationTestee, s.DateTestRestauration, s.PreuveTestRestauration),
            Validation(m.ProcedureRelueParId, m.ProcedureRelueLe),
            Validation(m.DemarrageAutoriseParId, m.DemarrageAutoriseLe),
            m.Participants.Select(p => new ParticipantDto(p.Id, p.Nom, p.Role, p.Contact, p.UtilisateurId, Nom(p.UtilisateurId))).ToList(),
            m.Systemes.Select(x => new SystemeDto(x.Id, x.Nom, x.Cible, x.Impact, x.CatalogueId,
                new SauvegardeSystemeDto(x.Sauvegarde.Statut, x.Sauvegarde.Moment, x.Sauvegarde.Contenu, x.Sauvegarde.Justification))).ToList(),
            m.Livrables.Select(l => new LivrableDto(l.Id, l.Objet, l.Version, l.Emplacement, l.SystemeId)).ToList(),
            m.EtapesTriees().Select(e =>
            {
                var d = Regles.Disponibilite(m, e, maintenant);
                return new EtapeDto(e.Id, e.Plan, e.Code, e.Ligne, e.Ordre, e.Action, [.. e.SystemeIds], e.ResponsableId, e.ExecutantId, e.Criticite,
                    e.DureeEstimeeMinutes, e.Etat,
                    e.Instructions, e.ResultatAttendu, e.Preuve, e.Commentaire, e.DebutReel, e.FinReelle, e.Revision, [.. e.Dependances], [.. e.ResolutionIds],
                    new DisponibiliteDto(d.Statut, d.Libelle), e.Type, e.PointDeNonRetour, Validation(e.NonRetourAutoriseParId, e.NonRetourAutoriseLe));
            }).ToList(),
            m.Resolutions.OrderBy(r => r.DemarreeLe).Select(r => new ExecutionResolutionDto(
                r.Id, r.EtapeId, r.ResolutionTypeId, r.Titre, r.Symptomes, r.Diagnostic,
                r.Corrections.OrderBy(c => c.Ordre).Select(c => new CorrectionExecuteeDto(
                    c.Ordre, c.Instruction, c.ResultatAttendu, c.Resultat, c.Observation, Nom(c.ParId), c.Le)).ToList(),
                r.VerificationInstruction, r.VerificationAttendu, r.VerificationResultat, r.VerificationObservation, r.Statut,
                Nom(r.DemarreeParId) ?? "Inconnu", r.DemarreeLe, r.TermineeLe)).ToList(),
            Regles.ErreursPlan(m, maintenant, approbationsMinimum),
            new DroitsDto(utilisateurId is { } u && Regles.EstPilote(m, u), m.Phase == Phase.Preparation, participant?.Id,
                utilisateurId is { } a && m.Approbations.Any(x => x.UtilisateurId == a),
                utilisateurId is { } rt && Regles.EstResponsableTechnique(m, rt),
                m.Phase == Phase.Preparation && utilisateurId is { } ed && Regles.PeutEditerContenu(m, ed, estAdmin)),
            m.Environnement, m.EstModele, approbationsMinimum,
            m.Approbations.Select(a => new ApprobationDto(a.Id, a.UtilisateurId, Nom(a.UtilisateurId) ?? a.Nom, a.Decision, a.Commentaire, a.Le)).ToList(),
            m.GoNoGo.Select(c => new CritereValideDto(c.Libelle, Nom(c.ParId), c.Le)).ToList(),
            m.FichesRecette.OrderBy(f => f.Ordre).Select(f => new FicheRecetteDto(f.Id, f.Ordre, f.SystemeId, f.Titre,
                f.Etapes.OrderBy(t => t.Ordre).Select(t => new EtapeTestDto(t.Id, t.Ordre, t.Instructions, t.ResultatAttendu,
                    t.Resultat, t.Observation, Nom(t.ParId), t.Le)).ToList())).ToList(),
            m.BilanActive,
            m.Bilan is { } b ? new BilanDto(b.CeQuiAMarche, b.Incidents, b.Actions, Nom(b.RedigeParId), b.Le) : null,
            Regles.EtapeNonRetourFranchie(m)?.Code,
            pieces.OrderBy(p => p.DeposeLe).Select(p => VersDto(p, nomsUtilisateurs)).ToList(),
            m.ResponsablesTechniques.Select(r => new ResponsableTechniqueDto(r.ParticipantId, [.. r.SystemeIds], r.Perimetre, r.Disponibilite, r.ContactJourJ)).ToList(),
            Regles.Avertissements(m));
    }
}
