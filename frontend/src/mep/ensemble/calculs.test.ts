import { etape, mep } from '../../test/fabrique';
import type { Etape, Presence } from '../../types';
import {
  causesBlocage,
  chiffres,
  estMarquant,
  etatSection,
  etatVue,
  jourRelatif,
  ouSeTrouve,
  planSuivi,
  tempsRestant,
  trierSuivi,
} from './calculs';

const GO = Date.parse('2026-10-16T13:00:00Z');
const min = (n: number) => new Date(GO + n * 60_000).toISOString();

/**
 * A (20 min, réussie) puis, en parallèle, B (30 min, en cours depuis 20) et C (15 min, en échec) ; D (10 min) après C ;
 * E (10 min) attend B et D. Prévu : 20 + max(30, 15 + 10) + 10 = 60 min. Même plan que ReglesSuiviTests (serveur).
 */
function plan(): Etape[] {
  return [
    etape({ id: 'a', code: 'E0001', dureeEstimeeMinutes: 20, etat: 'Reussie', debutReel: min(0), finReelle: min(20) }),
    etape({ id: 'b', code: 'E0002', dureeEstimeeMinutes: 30, etat: 'EnCours', debutReel: min(20), dependances: ['a'] }),
    etape({ id: 'c', code: 'E0003', dureeEstimeeMinutes: 15, etat: 'Echec', debutReel: min(20), dependances: ['a'] }),
    etape({ id: 'd', code: 'E0004', dureeEstimeeMinutes: 10, dependances: ['c'] }),
    etape({ id: 'e', code: 'E0005', dureeEstimeeMinutes: 10, dependances: ['b', 'd'] }),
  ];
}

const lancee = (etapes: Etape[]) =>
  mep({ phase: 'Execution', etapes, demarrageAutorise: { par: 'Alice', le: min(0) } });

describe('Vue d’ensemble : calculs (#61)', () => {
  it('une étape qui attend une étape en échec, même de loin, est bloquée', () => {
    const etapes = plan();
    const bloquees = causesBlocage(etapes);
    expect([...bloquees.keys()].sort()).toEqual(['d', 'e']);
    expect(bloquees.get('e')?.map((x) => x.code)).toEqual(['E0003']);
    expect(etatVue(etapes[3], bloquees)).toBe('bloquee');
    expect(etatVue(etapes[2], bloquees)).toBe('echec');
    const enResolution = { ...etapes[2], disponibilite: { statut: 'ResolutionEnCours' as const, libelle: '' } };
    expect(etatVue(enResolution, bloquees)).toBe('resolution');
    // Section : l'échec l'emporte, puis l'avancement ; une section qui n'attend que des étapes bloquées est bloquée.
    expect(etatSection(etapes.slice(0, 3), bloquees)).toBe('echec');
    expect(etatSection(etapes.slice(0, 2), bloquees)).toBe('en-cours');
    expect(etatSection(etapes.slice(3), bloquees)).toBe('bloquee');
    expect(etatSection([etapes[0]], bloquees)).toBe('faite');
    expect(etatSection([], bloquees)).toBe('a-venir');
  });

  it('la fin estimée suit le plus long chemin des durées restantes (+10 min par échec, +5 par dépassement)', () => {
    const etapes = plan();
    const maintenant = GO + 60 * 60_000;
    const c = chiffres(lancee(etapes), etapes, maintenant);
    expect(c.prevuMin).toBe(60);
    expect(c.finPrevue).toBe(GO + 60 * 60_000);
    // B dépasse (5 min), C en échec (15 + 10) puis D (10), puis E (10) : max(5, 35) + 10 = 45 min.
    expect(tempsRestant(etapes[1], maintenant)).toBe(5);
    expect(tempsRestant(etapes[2], maintenant)).toBe(25);
    expect(c.finEstimee).toBe(maintenant + 45 * 60_000);
    expect(c.retardMin).toBe(45);
    expect(c.passeMin).toBe(60);
    expect([c.faites, c.total, c.pourcentage, c.echecs, c.bloquees, c.enCours]).toEqual([1, 5, 20, 1, 2, 1]);
    // B dans les temps (10 min sur 30) : il lui reste 20 minutes.
    expect(tempsRestant(etapes[1], GO + 30 * 60_000)).toBe(20);
  });

  it('pas de fin estimée avant le Go ni sans durées ; un plan terminé a sa fin réelle', () => {
    const etapes = plan();
    expect(chiffres(mep({ phase: 'Preparation', etapes }), etapes, GO).finEstimee).toBeNull();
    const sansDurees = plan();
    for (const e of sansDurees) e.dureeEstimeeMinutes = null;
    const s = chiffres(lancee(sansDurees), sansDurees, GO + 30 * 60_000);
    expect(s.sansDuree).toBe(true);
    expect([s.finEstimee, s.finPrevue, s.retardMin]).toEqual([null, null, 0]);
    const finies = plan();
    finies.forEach((e, i) => {
      e.etat = 'Reussie';
      e.finReelle = min(50 + i * 5);
    });
    const f = chiffres(lancee(finies), finies, GO + 300 * 60_000);
    expect(f.terminee).toBe(true);
    expect(f.finEstimee).toBe(GO + 70 * 60_000);
    expect(f.retardMin).toBe(10);
    expect(f.passeMin).toBe(70);
  });

  it('le retour arrière se suit depuis son déclenchement', () => {
    const r1 = etape({ id: 'r1', code: 'R0001', plan: 'RetourArriere', dureeEstimeeMinutes: 15 });
    const m = mep({
      phase: 'RetourArriere',
      etapes: [...plan(), r1],
      demarrageAutorise: { par: 'Alice', le: min(0) },
      retourArriere: { le: min(90), par: 'Alice', motif: 'Échec' },
    });
    expect(planSuivi(m)).toBe('RetourArriere');
    expect(planSuivi({ phase: 'Cloturee', issue: 'Repliee', retourArriere: null })).toBe('RetourArriere');
    expect(planSuivi({ phase: 'Cloturee', issue: 'Reussie', retourArriere: null })).toBe('Deploiement');
    // Échec après un retour arrière : on suit le retour arrière ; échec depuis la recette : le déploiement.
    const repli = { motif: 'x', par: 'Alice', le: '2026-10-19T20:00:00Z' };
    expect(planSuivi({ phase: 'Cloturee', issue: 'Echec', retourArriere: repli })).toBe('RetourArriere');
    expect(planSuivi({ phase: 'Cloturee', issue: 'Echec', retourArriere: null })).toBe('Deploiement');
    const c = chiffres(m, [r1], GO + 95 * 60_000);
    expect(c.depart).toBe(GO + 90 * 60_000);
    expect(c.finPrevue).toBe(GO + 105 * 60_000);
    expect(c.finEstimee).toBe(GO + 110 * 60_000);
  });

  it('le tableau de bord met les retours arrière, puis les échecs, puis les autres (plus ancien Go d’abord)', () => {
    const go = (le: string) => ({ par: 'Alice', le });
    const calme = mep({ id: 'calme', outil: 'Calme', phase: 'Execution', demarrageAutorise: go(min(0)) });
    const tardive = mep({ id: 'tardive', outil: 'Tardive', phase: 'Execution', demarrageAutorise: go(min(30)) });
    const echec = mep({
      id: 'echec',
      outil: 'Échec',
      phase: 'Execution',
      demarrageAutorise: go(min(60)),
      etapes: [etape({ etat: 'Echec' })],
    });
    const recette = mep({ id: 'recette', outil: 'Recette', phase: 'Recette', demarrageAutorise: go(min(-60)) });
    const repli = mep({
      id: 'repli',
      outil: 'Repli',
      phase: 'RetourArriere',
      demarrageAutorise: go(min(-30)),
      retourArriere: { le: min(90), par: null, motif: '' },
    });
    expect(trierSuivi([tardive, calme, echec, recette, repli]).map((m) => m.id)).toEqual([
      'repli',
      'echec',
      'recette',
      'calme',
      'tardive',
    ]);
    // Un échec du déploiement ne compte plus une fois le retour arrière lancé : c'est son plan qui est suivi.
    expect(trierSuivi([{ ...repli, etapes: [etape({ etat: 'Echec' })] }, echec])[0].id).toBe('repli');
  });

  it('dit où se trouve chaque personne présente, d’après sa présence localisée', () => {
    const m = mep({
      etapes: [etape({ id: 'e9', code: 'E0009' })],
      sections: [
        {
          id: 's1',
          plan: 'Deploiement',
          titre: 'Sauvegardes',
          description: '',
          systemeIds: [],
          auteurId: null,
          auteur: null,
          creeLe: min(0),
          placee: true,
          revision: 1,
          ports: [],
          rang: 0,
        },
      ],
    });
    const p = (elements: string[]): Pick<Presence, 'elements'> => ({ elements });
    expect(ouSeTrouve(p(['ici:etape:e9']), m)).toBe('sur E0009');
    expect(ouSeTrouve(p(['ici:section:s1', 'etape:e9']), m)).toBe('sur E0009');
    expect(ouSeTrouve(p(['ici:section:s1']), m)).toBe('dans la section Sauvegardes');
    expect(ouSeTrouve(p(['preparation:systemes']), m)).toBe('dans la préparation');
    expect(ouSeTrouve(p(['recette:f1']), m)).toBe('dans la recette');
    expect(ouSeTrouve(p(['etape:inconnue']), m)).toBeNull();
    expect(ouSeTrouve(p([]), m)).toBeNull();
  });

  it('garde du journal les événements marquants et situe les MEP prévues', () => {
    expect(
      ['gonogo.go', 'etape.echec', 'resolution.demarree', 'repli.declenche', 'mep.cloturee'].every((t) =>
        estMarquant({ type: t }),
      ),
    ).toBe(true);
    expect(
      ['etape.modifiee', 'etape.commentaire', 'mep.exportee', 'phase.execution'].some((t) => estMarquant({ type: t })),
    ).toBe(false);
    const midi = new Date(2026, 9, 16, 12, 0).getTime();
    expect(jourRelatif('2026-10-16', midi)).toBe('Aujourd’hui');
    expect(jourRelatif('2026-10-17', midi)).toBe('Demain');
    expect(jourRelatif('2026-10-20', midi)).toBe('20/10/2026');
  });
});
