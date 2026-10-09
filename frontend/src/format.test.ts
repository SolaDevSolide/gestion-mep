import { calculerCompteARebours, familleEvenement, formatDate, formatDuree, formatMinutes, initiales, LIBELLE_CRITICITE, LIBELLE_DISPONIBILITE, LIBELLE_ETAT, LIBELLE_ISSUE, LIBELLE_PHASE, ressembleCommande, tonDisponibilite } from './format';

describe('énumérations', () => {
  it('affiche les libellés français', () => {
    expect(LIBELLE_ETAT.AFaire).toBe('À faire');
    expect(LIBELLE_ETAT.Reussie).toBe('Réussie');
    expect(LIBELLE_ETAT.Echec).toBe('Échec');
    expect(LIBELLE_CRITICITE.Moderee).toBe('Modérée');
    expect(LIBELLE_CRITICITE.Elevee).toBe('Élevée');
    expect(LIBELLE_PHASE.Execution).toBe('Exécution');
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
    expect(LIBELLE_ISSUE.Repliee).toBe('Clôturée : repliée');
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
