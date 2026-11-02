import type { JetonApi } from '../types';
import {
  aRecreer,
  comptesTechniques,
  echeance,
  exempleCurl,
  joursJusquA,
  loginCompteTechnique,
  quandUtilise,
  trier,
  visible,
} from './jetons';

const MAINTENANT = new Date('2026-11-02T10:00:00');
const jour = (n: number) => new Date(MAINTENANT.getTime() + n * 86_400_000).toISOString();

function jeton(p: Partial<JetonApi> = {}): JetonApi {
  return {
    id: 'j1',
    nom: 'Rapport hebdo',
    type: 'Personnel',
    etat: 'Actif',
    prefixe: 'gmep_r8Fq2x0A',
    portees: ['meps:lecture'],
    creeLe: jour(-20),
    creePar: 'Alice',
    expireLe: jour(70),
    revoqueLe: null,
    revoquePar: null,
    derniereUtilisationLe: null,
    derniereAdresseIp: null,
    compteId: 'u-alice',
    compteLogin: 'alice',
    compteNom: 'Alice',
    requetesParMinute: null,
    ...p,
  };
}

describe('Jetons d’accès : règles d’affichage', () => {
  it('rappelle l’expiration 7 jours avant pour un jeton personnel, 30 jours avant pour un jeton de service', () => {
    expect(echeance(jeton({ expireLe: jour(70) }), MAINTENANT)).toEqual({ etat: 'normal', jours: 70 });
    expect(echeance(jeton({ expireLe: jour(6) }), MAINTENANT)).toEqual({ etat: 'bientot', jours: 6 });
    expect(echeance(jeton({ expireLe: jour(28) }), MAINTENANT).etat).toBe('normal');
    expect(echeance(jeton({ type: 'Service', expireLe: jour(28) }), MAINTENANT)).toEqual({
      etat: 'bientot',
      jours: 28,
    });
    expect(echeance(jeton({ etat: 'Expire', expireLe: jour(-1) }), MAINTENANT).etat).toBe('expire');
    expect(echeance(jeton({ etat: 'Revoque', revoqueLe: jour(-2) }), MAINTENANT).etat).toBe('revoque');
  });

  it('propose « Recréer » à l’approche de l’expiration ou après, jamais après une révocation', () => {
    expect(aRecreer(jeton({ expireLe: jour(70) }), MAINTENANT)).toBe(false);
    expect(aRecreer(jeton({ expireLe: jour(3) }), MAINTENANT)).toBe(true);
    expect(aRecreer(jeton({ etat: 'Expire', expireLe: jour(-3) }), MAINTENANT)).toBe(true);
    expect(aRecreer(jeton({ etat: 'Revoque', revoqueLe: jour(-1), expireLe: jour(3) }), MAINTENANT)).toBe(false);
  });

  it('garde les révoqués et expirés 30 jours dans les listes, actifs en tête', () => {
    expect(visible(jeton(), MAINTENANT)).toBe(true);
    expect(visible(jeton({ etat: 'Expire', expireLe: jour(-29) }), MAINTENANT)).toBe(true);
    expect(visible(jeton({ etat: 'Expire', expireLe: jour(-31) }), MAINTENANT)).toBe(false);
    expect(visible(jeton({ etat: 'Revoque', revoqueLe: jour(-40), expireLe: jour(10) }), MAINTENANT)).toBe(false);
    const liste = [jeton({ id: 'a', etat: 'Revoque', revoqueLe: jour(-1) }), jeton({ id: 'b' })];
    expect(trier(liste).map((j) => j.id)).toEqual(['b', 'a']);
  });

  it('dit quand le jeton a servi', () => {
    expect(quandUtilise(null, MAINTENANT)).toBe('jamais');
    expect(quandUtilise(new Date(MAINTENANT.getTime() - 4 * 60_000).toISOString(), MAINTENANT)).toBe('il y a 4 min');
    expect(quandUtilise(new Date(MAINTENANT.getTime() - 2 * 3_600_000).toISOString(), MAINTENANT)).toBe('il y a 2 h');
    expect(quandUtilise(new Date('2026-11-01T09:00:00').toISOString(), MAINTENANT)).toBe('hier');
    expect(quandUtilise(new Date('2026-10-17T09:00:00').toISOString(), MAINTENANT)).toBe('17/10/2026');
  });

  it('calcule la durée jusqu’à une date choisie, un an au plus', () => {
    expect(joursJusquA('2026-11-03', MAINTENANT)).toBe(1);
    expect(joursJusquA('2027-01-31', MAINTENANT)).toBe(90);
    expect(joursJusquA('2026-11-02', MAINTENANT)).toBeNull();
    expect(joursJusquA('2028-01-01', MAINTENANT)).toBeNull();
    expect(joursJusquA('', MAINTENANT)).toBeNull();
  });

  it('prévoit le login du compte technique comme le serveur', () => {
    expect(loginCompteTechnique('  Chaîne CI : GitLab ! ')).toBe('svc-chaine-ci-gitlab');
    expect(loginCompteTechnique('« »')).toBe('svc-jeton');
    expect(loginCompteTechnique('a'.repeat(80))).toHaveLength(44);
  });

  it('montre un exemple d’appel adapté aux portées', () => {
    expect(exempleCurl('https://mep.x', ['meps:lecture'])).toContain('https://mep.x/api/meps?');
    expect(exempleCurl('https://mep.x', ['catalogue:lecture'])).toContain('/api/catalogue/systemes');
    expect(exempleCurl('https://mep.x', ['execution'])).toContain('/api/auth/moi');
    expect(exempleCurl('https://mep.x', ['execution'])).toContain('Authorization: Bearer $GMEP_JETON');
  });

  it('liste une fois chaque compte technique des jetons de service', () => {
    const comptes = comptesTechniques([
      jeton({ id: '1', type: 'Service', compteId: 'c1', compteLogin: 'svc-ci', compteNom: 'CI' }),
      jeton({ id: '2', type: 'Service', compteId: 'c1', compteLogin: 'svc-ci', compteNom: 'CI' }),
      jeton({ id: '3' }),
    ]);
    expect(comptes).toEqual([{ id: 'c1', login: 'svc-ci', nom: 'CI' }]);
  });
});
