import {
  calculerCompteARebours,
  compte,
  depuisInputDateTimeFuseau,
  formatDateHeureFuseau,
  fuseauValide,
  versInputDateTimeFuseau,
  familleEvenement,
  formatDate,
  formatDuree,
  formatMinutes,
  initiales,
  LIBELLE_CRITICITE,
  LIBELLE_DISPONIBILITE,
  LIBELLE_ETAT,
  LIBELLE_FIN,
  LIBELLE_PHASE,
  ressembleCommande,
  tonDisponibilite,
} from './format';

describe('énumérations', () => {
  it('affiche les libellés français', () => {
    expect(LIBELLE_ETAT.AFaire).toBe('À faire');
    expect(LIBELLE_ETAT.Reussie).toBe('Réussie');
    expect(LIBELLE_ETAT.Echec).toBe('Échec');
    expect(LIBELLE_CRITICITE.Moderee).toBe('Modérée');
    expect(LIBELLE_CRITICITE.Elevee).toBe('Élevée');
    expect(LIBELLE_PHASE.Execution).toBe('Exécution');
    expect(LIBELLE_FIN.TestUnitaire).toBe('Test unitaire');
    expect(LIBELLE_FIN.Verification).toBe('Simple vérification');
  });
  it('accorde un mot au nombre', () => {
    expect(compte(0, 'non fait')).toBe('0 non fait');
    expect(compte(1, 'fait')).toBe('1 fait');
    expect(compte(2, 'fait')).toBe('2 faits');
    expect(compte(3, 'simple vérification', 'simples vérifications')).toBe('3 simples vérifications');
  });
  it('associe une couleur à la disponibilité', () => {
    expect(tonDisponibilite('Disponible')).toBe('ok');
    expect(tonDisponibilite('Attend')).toBe('wait');
    expect(tonDisponibilite('EnCours')).toBe('run');
    expect(tonDisponibilite('RetourArriere')).toBe('rollback');
    expect(tonDisponibilite('ArretResolution')).toBe('danger');
    expect(tonDisponibilite('ResolutionEnCours')).toBe('warn');
    expect(tonDisponibilite('SiRetourArriere')).toBe('later');
    expect(tonDisponibilite('Terminee')).toBe('done');
  });
  it('traduit les énumérations du jalon 2', () => {
    expect(LIBELLE_PHASE.RetourArriere).toBe('Retour arrière');
    expect(LIBELLE_PHASE.Cloturee).toBe('Clôturée');
    expect(LIBELLE_DISPONIBILITE.SiRetourArriere).toBe('Si retour arrière');
    expect(LIBELLE_DISPONIBILITE.ResolutionEnCours).toBe('Résolution en cours');
  });
});

describe('compte à rebours', () => {
  const t0 = Date.parse('2026-10-09T20:00:00Z');
  it('normal au-delà d’une heure', () => {
    const c = calculerCompteARebours('2026-10-09T23:30:15Z', t0);
    expect(c.niveau).toBe('normal');
    expect(c.texte).toBe('03 h 30 min 15 s');
  });
  it('orange sous une heure', () => {
    const c = calculerCompteARebours('2026-10-09T20:59:59Z', t0);
    expect(c.niveau).toBe('attention');
  });
  it('rouge une fois dépassée', () => {
    const c = calculerCompteARebours('2026-10-09T19:59:59Z', t0);
    expect(c.niveau).toBe('depasse');
    expect(c.texte).toBe('RETOUR ARRIÈRE OBLIGATOIRE');
  });
  it('en recette, une échéance dépassée est seulement signalée (#94)', () => {
    const c = calculerCompteARebours('2026-10-09T19:59:59Z', t0, true);
    expect(c.niveau).toBe('depasse');
    expect(c.texte).toBe('ÉCHÉANCE DÉPASSÉE');
  });
  it('sans échéance', () => {
    expect(calculerCompteARebours(null, t0).niveau).toBe('aucun');
  });
  it('affiche les jours', () => {
    expect(formatDuree(90_000_000)).toBe('1 j 01 h 00 min 00 s');
  });
});

describe('dates et commandes', () => {
  it('formate une date sans décalage de fuseau', () => {
    expect(formatDate('2026-10-09')).toBe('09/10/2026');
    expect(formatDate(null)).toBe('—');
  });
  it('détecte des commandes', () => {
    expect(ressembleCommande('$ systemctl restart api')).toBe(true);
    expect(ressembleCommande('docker compose up -d')).toBe(true);
    expect(ressembleCommande('Ouvrir la console puis cliquer sur Valider')).toBe(false);
  });
});

describe('utilitaires', () => {
  it('calcule les initiales', () => {
    expect(initiales('Alice Martin')).toBe('AM');
    expect(initiales('bob')).toBe('BO');
    expect(initiales('')).toBe('?');
  });
  it('extrait la famille d’un type d’événement', () => {
    expect(familleEvenement('etape.prise-en-main')).toBe('etape');
    expect(familleEvenement('repli.declenche')).toBe('repli');
  });
  it('formate des minutes', () => {
    expect(formatMinutes(30)).toBe('30 min');
    expect(formatMinutes(90)).toBe('1 h 30');
    expect(formatMinutes(120)).toBe('2 h');
    expect(formatMinutes(null)).toBe('—');
  });
});

describe('heures dans le fuseau de la MEP (#94)', () => {
  it('affiche et saisit à l’heure du fuseau, changements d’heure compris', () => {
    // 23/10 : heure d'été à Paris (UTC+2) ; 26/10 : heure d'hiver (UTC+1).
    expect(formatDateHeureFuseau('2026-10-23T21:00:00Z', 'Europe/Paris')).toBe('23/10/2026 23:00');
    expect(formatDateHeureFuseau('2026-10-26T01:00:00Z', 'Europe/Paris')).toBe('26/10/2026 02:00');
    expect(versInputDateTimeFuseau('2026-10-23T21:00:00Z', 'Europe/Paris')).toBe('2026-10-23T23:00');
    expect(depuisInputDateTimeFuseau('2026-10-23T23:00', 'Europe/Paris')).toBe('2026-10-23T21:00:00.000Z');
    expect(depuisInputDateTimeFuseau('2026-10-26T02:00', 'Europe/Paris')).toBe('2026-10-26T01:00:00.000Z');
    expect(depuisInputDateTimeFuseau('2026-10-23T23:00', 'America/New_York')).toBe('2026-10-24T03:00:00.000Z');
  });
  it('fuseau inconnu ou vide : UTC, comme le serveur', () => {
    expect(fuseauValide('Zone/Inconnue')).toBe('UTC');
    expect(fuseauValide('')).toBe('UTC');
    expect(formatDateHeureFuseau('2026-10-23T21:00:00Z', 'Zone/Inconnue')).toBe('23/10/2026 21:00');
    expect(depuisInputDateTimeFuseau('', 'Europe/Paris')).toBeNull();
    expect(formatDateHeureFuseau(null, 'Europe/Paris')).toBe('—');
  });
});
