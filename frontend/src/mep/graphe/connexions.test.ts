import { etape } from '../../test/fabrique';
import type { Etape } from '../../types';
import { deplacementPour, interpreterConnexion } from './connexions';
import type { LienModele } from './modeleGraphe';
import type { Section } from './sections';

const e = (id: string, dependances: string[] = [], sectionId: string | null = null) =>
  ({
    ...etape({ id, code: id.toUpperCase(), dependances }),
    sectionId,
    portsAttendus: [],
    dependancesDerivees: [],
  }) as Etape;

const section = (id: string, entree: string, sortie: string): Section => ({
  id,
  plan: 'Deploiement',
  titre: id,
  description: '',
  systemeIds: [],
  auteurId: null,
  auteur: null,
  creeLe: '2026-10-15T08:00:00Z',
  placee: true,
  revision: 1,
  ports: [
    { id: entree, sens: 'Entree', titre: 'Début', description: '', ordre: 0, etapes: [], sources: [] },
    { id: sortie, sens: 'Sortie', titre: 'Fin', description: '', ordre: 0, etapes: [], sources: [] },
  ],
});

const etapes = [e('a'), e('b', ['a']), e('x', [], 's1'), e('y', [], 's2')];
const sections = [section('s1', 'in1', 'out1'), section('s2', 'in2', 'out2')];
const lien = (source: string, sourceHandle: string, target: string, targetHandle: string) =>
  interpreterConnexion({ source, sourceHandle, target, targetHandle }, etapes, sections);

describe('tracé d’un lien entre deux poignées', () => {
  it('carte → carte : lien direct, sauf boucle ou doublon', () => {
    expect(lien('b', 'bas', 'x', 'haut')).toEqual({ type: 'lier', de: 'b', vers: 'x' });
    expect(lien('b', 'bas', 'a', 'haut')).toMatchObject({ type: 'refus', raison: expect.stringMatching(/boucle/) });
    expect(lien('a', 'bas', 'b', 'haut')).toMatchObject({ type: 'refus', raison: 'Ce lien existe déjà.' });
  });

  it('cercles d’une section repliée (poignées) ou dépliée (nœuds) : les six liens possibles', () => {
    // Étape hors section → entrée ; sortie → étape hors section ; sortie → entrée d'une autre section.
    expect(lien('a', 'bas', 's:s1', 'in1')).toEqual({ type: 'lier', de: 'a', vers: 'in1' });
    expect(lien('s:s1', 'out1', 'b', 'haut')).toEqual({ type: 'lier', de: 'out1', vers: 'b' });
    expect(lien('s:s1', 'out1', 's:s2', 'in2')).toEqual({ type: 'lier', de: 'out1', vers: 'in2' });
    // Section dépliée : entrée → étape de la section ; étape de la section → sortie.
    expect(lien('c:in1', 'bas', 'x', 'haut')).toEqual({ type: 'lier', de: 'in1', vers: 'x' });
    expect(lien('x', 'bas', 'c:out1', 'haut')).toEqual({ type: 'lier', de: 'x', vers: 'out1' });
    expect(lien('c:out2', 'bas', 'a', 'haut')).toEqual({ type: 'lier', de: 'out2', vers: 'a' });
  });

  it('refuse les liens qui n’ont pas de sens', () => {
    expect(lien('x', 'bas', 's:s1', 'in1')).toMatchObject({ type: 'refus' });
    expect(lien('c:in1', 'bas', 'a', 'haut')).toMatchObject({ type: 'refus' });
    expect(lien('s:s1', 'out1', 's:s1', 'in1')).toMatchObject({ type: 'refus' });
  });
});

describe('dépôt d’une carte glissée', () => {
  const l = (de: string, vers: string): LienModele => ({
    id: `${de}>${vers}`,
    source: de,
    sourcePoignee: 'bas',
    cible: vers,
    ciblePoignee: 'haut',
    logique: { de, vers },
  });
  it('sur une carte : après elle ; sur un lien : insertion ; sur soi-même ou ses propres liens : rien', () => {
    expect(deplacementPour('a', { type: 'carte', id: 'x' })).toEqual({ apres: 'x' });
    expect(deplacementPour('x', { type: 'lien', lien: l('a', 'b') })).toEqual({ surLien: { de: 'a', vers: 'b' } });
    expect(deplacementPour('a', { type: 'carte', id: 'a' })).toBeNull();
    expect(deplacementPour('a', { type: 'lien', lien: l('a', 'b') })).toBeNull();
  });
});
