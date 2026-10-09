using GestionMep.Domain;

namespace GestionMep.Api.Services;

/// <summary>Unique endroit où une entité <see cref="Mep"/> devient un <see cref="MepDto"/> (disponibilités, erreurs du plan, droits).</summary>
public static class MepMapping
{
    public static MepDto VersDto(Mep m, Guid? utilisateurId, IReadOnlyDictionary<Guid, string> nomsUtilisateurs, DateTimeOffset maintenant)
    {
        string? Nom(Guid? id) => id is { } g && nomsUtilisateurs.TryGetValue(g, out var n) ? n : null;
        ValidationDto? Validation(Guid? par, DateTimeOffset? le) =>
            par is null || le is null ? null : new ValidationDto(Nom(par) ?? "Inconnu", le.Value);

        var s = m.Sauvegarde;
        var participant = utilisateurId is null ? null : m.Participants.FirstOrDefault(p => p.UtilisateurId == utilisateurId);

        return new MepDto(
            m.Id, m.Version, m.Outil, m.VersionActuelle, m.VersionCible, m.Objet, m.Impacts,
            m.DateMep, m.HeureDebut, m.HeureFin, m.FuseauHoraire, m.IndisponibiliteMinutes,
            m.PiloteId, m.SuppleantId, m.Phase, m.EcheanceRepli,
            new SauvegardeDto(s.Structure, s.Donnees, s.Configuration, s.ResponsableId, s.RealiseeLe, s.Procedure, s.Copies,
                s.RestaurationTestee, s.DateTestRestauration, s.PreuveTestRestauration),
            Validation(m.ProcedureRelueParId, m.ProcedureRelueLe),
            Validation(m.DemarrageAutoriseParId, m.DemarrageAutoriseLe),
            m.Participants.Select(p => new ParticipantDto(p.Id, p.Nom, p.Role, p.Contact, p.UtilisateurId, Nom(p.UtilisateurId))).ToList(),
            m.Systemes.Select(x => new SystemeDto(x.Id, x.Nom, x.Cible, x.Impact)).ToList(),
            m.Livrables.Select(l => new LivrableDto(l.Id, l.Objet, l.Version, l.Emplacement, l.SystemeId)).ToList(),
            m.Etapes.OrderBy(e => e.Ordre).Select(e =>
            {
                var d = Regles.Disponibilite(e, m.Etapes, m.EcheanceRepli, maintenant);
                return new EtapeDto(e.Id, e.Code, e.Ordre, e.Action, e.SystemeId, e.ResponsableId, e.Criticite, e.Etat, e.Instructions,
                    e.ResultatAttendu, e.Preuve, e.DebutReel, e.FinReelle, e.Revision, [.. e.Dependances], new DisponibiliteDto(d.Statut, d.Libelle));
            }).ToList(),
            Regles.ErreursPlan(m, maintenant),
            new DroitsDto(utilisateurId is { } u && Regles.EstPilote(m, u), m.Phase == Phase.Preparation, participant?.Id));
    }
}
