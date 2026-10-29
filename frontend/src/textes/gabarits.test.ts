import { describe, expect, it } from 'vitest';
import vecteurs from '../vecteurs-gabarits.json';
import {
  type CatalogueGabarit,
  decouper,
  encoderValeurUrl,
  index,
  mettreEnFormeJson,
  position,
  rendreJson,
  rendreTexte,
  rendreUrl,
  suggestion,
  validerJson,
  variablesGabarit,
} from './gabarits';

const catalogue: CatalogueGabarit = new Map(Object.entries(vecteurs.variables));

describe('rendu des gabarits (mêmes cas que le serveur)', () => {
  it.each(vecteurs.cas.map((c) => [c.nom, c] as const))('%s', (_, cas) => {
    const valeurs = new Map(
      Object.entries(cas.valeurs as Record<string, string | string[]>).map(([k, v]) => [
        k,
        Array.isArray(v) ? { liste: v } : { texte: v },
      ]),
    );
    const rendre = cas.format === 'json' ? rendreJson : cas.format === 'url' ? rendreUrl : rendreTexte;
    expect(rendre(cas.gabarit, valeurs, catalogue)).toEqual(cas.attendu);
  });
});

describe('outils de l’éditeur', () => {
  it('encode une valeur comme Uri.EscapeDataString', () => {
    expect(encoderValeurUrl("a b&c!*'()~._-é")).toBe('a%20b%26c%21%2A%27%28%29~._-%C3%A9');
  });

  it('positions et index se répondent', () => {
    const t = 'ab\ncd\nef';
    expect(position(t, 4)).toEqual({ ligne: 2, colonne: 2 });
    expect(index(t, 2, 2)).toBe(4);
    expect(index(t, 9, 1)).toBe(t.length);
  });

  it('liste les variables sans doublon', () => {
    expect(variablesGabarit('{{a}} {{ b }} {{a}}')).toEqual(['a', 'b']);
  });

  it('valide le JSON standard', () => {
    expect(validerJson('{"a": [1, true, null, "x"]}')).toBeNull();
    expect(validerJson('{"a": 1,}')?.message).toBe("Virgule en trop avant « } » dans l'objet du message.");
  });

  it('découpe un gabarit JSON sans rien perdre', () => {
    const g = '{"titre": "{{essai}}MEP {{pilot}}", "n": -1, "ok": true, "s": {{etape_systemes}}}';
    const morceaux = decouper(g, 'Json', catalogue);
    expect(morceaux.map((m) => m.texte).join('')).toBe(g);
    expect(morceaux).toContainEqual({ type: 'cle', texte: '"titre"' });
    expect(morceaux).toContainEqual({ type: 'variable', texte: '{{essai}}' });
    expect(morceaux).toContainEqual({ type: 'inconnue', texte: '{{pilot}}' });
    expect(morceaux).toContainEqual({ type: 'nombre', texte: '-1' });
    expect(morceaux).toContainEqual({ type: 'mot', texte: 'true' });
    expect(morceaux).toContainEqual({ type: 'variable', texte: '{{etape_systemes}}' });
  });

  it('découpe un texte : seules les variables ressortent', () => {
    const morceaux = decouper('Bonjour {{pilote}} "x"', 'Texte', catalogue);
    expect(morceaux).toEqual([
      { type: 'texte', texte: 'Bonjour ' },
      { type: 'variable', texte: '{{pilote}}' },
      { type: 'texte', texte: ' "x"' },
    ]);
  });

  it('remet en forme un JSON valide, refuse un JSON invalide', () => {
    expect(mettreEnFormeJson('{"a":"{{pilote}}, x","b":[1,2],"c":{},"s":{{etape_systemes}}}', catalogue)).toBe(
      '{\n  "a": "{{pilote}}, x",\n  "b": [\n    1,\n    2\n  ],\n  "c": {},\n  "s": {{etape_systemes}}\n}\n',
    );
    expect(mettreEnFormeJson('{"a": }', catalogue)).toBeNull();
  });

  it('propose la variable la plus proche', () => {
    const cles = ['lien_mep', 'pilote', 'phase', 'etape'];
    expect(suggestion('lien_mpe', cles)).toBe('lien_mep');
    expect(suggestion('pilot', cles)).toBe('pilote');
    expect(suggestion('zzzzzz', cles)).toBeNull();
  });
});
