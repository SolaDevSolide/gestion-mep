import { etape, mep, resolution } from '../test/fabrique';
import type { Etape, Mep } from '../types';
import { droitsEtape, resolutionsDeLEtape } from './droitsEtape';

/** MEP de test : p1 participant relié par défaut, ni pilote ni administrateur. */
function m(p: Partial<Mep> = {}, droits: { participantId?: string | null; estPilote?: boolean } = {}): Mep {
  const base = mep(p);
  return {
    ...base,
    droits: {
      ...base.droits,
      participantId: droits.participantId === undefined ? 'p1' : droits.participantId,
      estPilote: droits.estPilote ?? false,
    },
  };
}

const droits = (
  e: Etape,
  opts: { p?: Partial<Mep>; participantId?: string | null; estPilote?: boolean; admin?: boolean } = {},
) => droitsEtape(m({ etapes: [e], ...opts.p }, opts), e, opts.admin ?? false);

describe('droitsEtape : démarrer', () => {
  it('un participant relié peut démarrer une étape disponible et à faire', () => {
    expect(droits(etape()).peutDemarrer).toBe(true);
  });

  it('sans participant relié, seuls le pilote et un administrateur peuvent démarrer', () => {
    expect(droits(etape(), { participantId: null }).peutDemarrer).toBe(false);
    expect(droits(etape(), { participantId: null, estPilote: true }).peutDemarrer).toBe(true);
    expect(droits(etape(), { participantId: null, admin: true }).peutDemarrer).toBe(true);
  });

  it('pas de démarrage si l’étape attend, est déjà démarrée ou attend l’accord de non-retour', () => {
    expect(droits(etape({}, 'Attend')).peutDemarrer).toBe(false);
    expect(droits(etape({ etat: 'EnCours', executantId: 'p1' })).peutDemarrer).toBe(false);
    const nonRetour = droits(etape({ pointDeNonRetour: true }));
    expect(nonRetour.nonRetourAttendu).toBe(true);
    expect(nonRetour.peutDemarrer).toBe(false);
    const autorise = droits(
      etape({ pointDeNonRetour: true, nonRetourAutorise: { par: 'Alice', le: '2026-10-09T21:00:00Z' } }),
    );
    expect(autorise.nonRetourAttendu).toBe(false);
    expect(autorise.peutDemarrer).toBe(true);
  });
});

describe('droitsEtape : terminer et agir sur une résolution', () => {
  const enCours = etape({ etat: 'EnCours', executantId: 'p1' });

  it('l’exécutant peut terminer et agir sur les résolutions', () => {
    const d = droits(enCours);
    expect(d.estExecutant).toBe(true);
    expect(d.peutTerminer).toBe(true);
    expect(d.peutAgirResolution).toBe(true);
  });

  it('le pilote peut terminer à la place de l’exécutant', () => {
    const d = droits(enCours, { participantId: 'p3', estPilote: true });
    expect(d.estExecutant).toBe(false);
    expect(d.peutTerminer).toBe(true);
    expect(d.peutAgirResolution).toBe(true);
  });

  it('un autre participant ne peut ni terminer ni agir sur les résolutions', () => {
    const d = droits(enCours, { participantId: 'p2' });
    expect(d.peutTerminer).toBe(false);
    expect(d.peutAgirResolution).toBe(false);
  });

  it('on ne termine qu’une étape en cours', () => {
    expect(droits(etape({ etat: 'Reussie', executantId: 'p1' })).peutTerminer).toBe(false);
  });
});

describe('droitsEtape : prise de main', () => {
  const enCours = etape({ etat: 'EnCours', executantId: 'p1' });

  it('un autre participant ou un administrateur peut prendre la main en exécution', () => {
    expect(droits(enCours, { participantId: 'p2' }).peutPrendreLaMain).toBe(true);
    expect(droits(enCours, { participantId: null, admin: true }).peutPrendreLaMain).toBe(true);
    expect(droits(etape({ etat: 'Echec', executantId: 'p1' }), { participantId: 'p2' }).peutPrendreLaMain).toBe(true);
  });

  it('ni l’exécutant, ni un compte non relié, ni sur une étape non démarrée', () => {
    expect(droits(enCours).peutPrendreLaMain).toBe(false);
    expect(droits(enCours, { participantId: null }).peutPrendreLaMain).toBe(false);
    expect(droits(etape({ executantId: null, etat: 'EnCours' }), { participantId: 'p2' }).peutPrendreLaMain).toBe(
      false,
    );
    expect(droits(etape({ etat: 'Reussie', executantId: 'p1' }), { participantId: 'p2' }).peutPrendreLaMain).toBe(
      false,
    );
  });

  it('seulement sur le plan qui agit dans la phase en cours', () => {
    expect(droits(enCours, { participantId: 'p2', p: { phase: 'Recette' } }).peutPrendreLaMain).toBe(false);
    const repli = etape({ plan: 'RetourArriere', etat: 'EnCours', executantId: 'p1' });
    expect(droits(repli, { participantId: 'p2' }).planAgissant).toBe(false);
    expect(droits(repli, { participantId: 'p2', p: { phase: 'RetourArriere' } }).peutPrendreLaMain).toBe(true);
    expect(droits(enCours, { participantId: 'p2', p: { phase: 'RetourArriere' } }).planAgissant).toBe(false);
  });
});

describe('droitsEtape : commentaire', () => {
  it('exécutant, pilote ou administrateur, en exécution, recette ou retour arrière', () => {
    const e = etape({ etat: 'EnCours', executantId: 'p1' });
    expect(droits(e).peutCommenter).toBe(true);
    expect(droits(e, { participantId: 'p3', estPilote: true }).peutCommenter).toBe(true);
    expect(droits(e, { participantId: null, admin: true }).peutCommenter).toBe(true);
    expect(droits(e, { participantId: 'p2' }).peutCommenter).toBe(false);
    expect(droits(e, { p: { phase: 'Recette' } }).peutCommenter).toBe(true);
    expect(droits(e, { p: { phase: 'RetourArriere' } }).peutCommenter).toBe(true);
    expect(droits(e, { p: { phase: 'Cloturee' } }).peutCommenter).toBe(false);
  });
});

describe('droitsEtape : résolutions', () => {
  it('étape en échec avec une résolution en cours', () => {
    const e = etape({ etat: 'Echec', executantId: 'p1' });
    const d = droits(e, { p: { resolutions: [resolution()] } });
    expect(d.enEchec).toBe(true);
    expect(d.resolutionReussie).toBe(false);
    expect(d.resolutionEchouee).toBe(false);
  });

  it('dernière résolution réussie : l’étape est de nouveau à faire', () => {
    const e = etape({ etat: 'AFaire', executantId: 'p1' });
    expect(droits(e, { p: { resolutions: [resolution({ statut: 'Reussie' })] } }).resolutionReussie).toBe(true);
  });

  it('dernière résolution échouée : l’étape reste en échec', () => {
    const e = etape({ etat: 'Echec', executantId: 'p1' });
    const resolutions = [
      resolution({ id: 'r1', statut: 'Reussie', demarreeLe: '2026-10-09T20:00:00Z' }),
      resolution({ id: 'r2', statut: 'Echouee', demarreeLe: '2026-10-09T21:00:00Z' }),
    ];
    const d = droits(e, { p: { resolutions } });
    expect(d.resolutionEchouee).toBe(true);
    expect(d.resolutionReussie).toBe(false);
  });
});

describe('resolutionsDeLEtape', () => {
  it('trie par démarrage et sépare la résolution en cours de l’historique', () => {
    const e = etape();
    const r = resolutionsDeLEtape(
      m({
        resolutions: [
          resolution({ id: 'r3', statut: 'EnCours', demarreeLe: '2026-10-09T22:00:00Z' }),
          resolution({ id: 'r1', statut: 'Echouee', demarreeLe: '2026-10-09T20:00:00Z' }),
          resolution({ id: 'autre', etapeId: 'e9', statut: 'Reussie' }),
        ],
      }),
      e,
    );
    expect(r.toutes.map((x) => x.id)).toEqual(['r1', 'r3']);
    expect(r.enCours?.id).toBe('r3');
    expect(r.derniere?.id).toBe('r3');
    expect(r.historique.map((x) => x.id)).toEqual(['r1']);
  });
});
