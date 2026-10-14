import type { Mep, Participant, ParticipantSaisie, ResponsableTechnique, Systeme, Utilisateur } from '../../types';

/** Nom, rôle et contact d'une personne pré-remplis depuis le profil de son compte (modifiables ensuite). */
export function participantDepuisCompte(
  u: Pick<Utilisateur, 'id' | 'nomPersonne' | 'role' | 'contact'>,
): ParticipantSaisie {
  // Nom de la personne : celui du compte principal pour un compte relié.
  return { nom: u.nomPersonne, role: u.role ?? '', contact: u.contact ?? '', utilisateurId: u.id };
}

export interface SuggestionResponsable {
  utilisateur: Utilisateur;
  /** Participant déjà relié à ce compte dans la MEP (sinon il sera créé à l'ajout). */
  participant: Participant | null;
  /** Systèmes de la MEP couverts par une expertise du compte, avec le détail de l'expertise. */
  systemes: { systeme: Systeme; detail: string }[];
}

/**
 * Comptes actifs dont une expertise porte sur un système (du catalogue) de la MEP et qui ne sont pas déjà responsables
 * techniques.
 * Triés par nombre de systèmes couverts (décroissant), puis par nom.
 */
export function suggestionsResponsablesTechniques(
  mep: Pick<Mep, 'systemes' | 'participants' | 'responsablesTechniques'>,
  utilisateurs: Utilisateur[],
): SuggestionResponsable[] {
  const dejaResponsables = new Set(mep.responsablesTechniques.map((r) => r.participantId));
  const suggestions: SuggestionResponsable[] = [];
  for (const u of utilisateurs) {
    if (!u.actif) continue;
    const participant = mep.participants.find((p) => p.utilisateurId === u.id) ?? null;
    if (participant && dejaResponsables.has(participant.id)) continue;
    const systemes = mep.systemes.flatMap((s) => {
      if (!s.catalogueId) return [];
      const expertise = u.expertises.find((e) => e.systemeCatalogueId === s.catalogueId);
      return expertise ? [{ systeme: s, detail: expertise.detail }] : [];
    });
    if (systemes.length > 0) suggestions.push({ utilisateur: u, participant, systemes });
  }
  return suggestions.sort(
    (a, b) =>
      b.systemes.length - a.systemes.length || a.utilisateur.nomAffiche.localeCompare(b.utilisateur.nomAffiche, 'fr'),
  );
}

/** Responsable technique issu d'une suggestion : systèmes concernés, périmètre = détail(s) de l'expertise. */
export function responsableDepuisSuggestion(
  s: SuggestionResponsable,
  participantId: string,
  contact: string,
): ResponsableTechnique {
  const details = [...new Set(s.systemes.map((x) => x.detail.trim()).filter(Boolean))];
  return {
    participantId,
    systemeIds: s.systemes.map((x) => x.systeme.id),
    perimetre: details.join(' ; '),
    disponibilite: 'Joignable',
    contactJourJ: contact,
  };
}
