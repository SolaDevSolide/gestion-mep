import { act, renderHook } from '@testing-library/react';
import { brouillonsEnAttente, copierBrouillons, prendreRetour, reprendreBrouillon, retenirRetour } from './brouillons';
import { useDraft, useReportDirty } from './hooks';

describe('Saisies copiées avant une reconnexion Microsoft', () => {
  afterEach(() => {
    window.sessionStorage.clear();
    vi.restoreAllMocks();
  });

  it('retient la page à rouvrir une seule fois, jamais une autre origine ni l’écran de connexion', () => {
    retenirRetour('/meps/m1?vue=graphe');
    expect(prendreRetour()).toBe('/meps/m1?vue=graphe');
    expect(prendreRetour()).toBeNull();
    retenirRetour('//exemple.org');
    expect(prendreRetour()).toBeNull();
    retenirRetour('/connexion');
    expect(prendreRetour()).toBeNull();
  });

  it('rend chaque saisie à son formulaire, sur la même page seulement', () => {
    copierBrouillons('/meps/m1', [
      { libelle: 'Intervention', valeur: { draft: 1, base: 0 } },
      { libelle: 'Communication', valeur: { draft: 2, base: 0 } },
    ]);
    expect(reprendreBrouillon('/meps/m2', 'Intervention')).toBeUndefined();
    expect(reprendreBrouillon('/meps/m1', 'Intervention')).toEqual({ draft: 1, base: 0 });
    expect(reprendreBrouillon('/meps/m1', 'Intervention')).toBeUndefined();
    expect(brouillonsEnAttente()).toBe(true);
    expect(reprendreBrouillon('/meps/m1', 'Communication')).toEqual({ draft: 2, base: 0 });
    expect(brouillonsEnAttente()).toBe(false);
  });

  it('oublie une copie trop ancienne', () => {
    copierBrouillons('/meps/m1', [{ libelle: 'Intervention', valeur: { draft: 1, base: 0 } }]);
    const maintenant = Date.now();
    vi.spyOn(Date, 'now').mockReturnValue(maintenant + 2 * 60 * 60 * 1000);
    expect(reprendreBrouillon('/meps/m1', 'Intervention')).toBeUndefined();
    expect(brouillonsEnAttente()).toBe(false);
  });

  it('un formulaire monté au retour reprend sa saisie copiée', () => {
    window.history.replaceState(null, '', '/meps/m1');
    const avant = renderHook(() => {
      const d = useDraft({ titre: 'Bascule' });
      useReportDirty(d.dirty, 'Intervention', d);
      return d;
    });
    act(() => avant.result.current.update({ titre: 'Bascule du soir' }));
    copierBrouillons('/meps/m1', [{ libelle: 'Intervention', valeur: avant.result.current.lireBrouillon() }]);
    avant.unmount();

    const apres = renderHook(() => {
      const d = useDraft({ titre: 'Bascule' });
      useReportDirty(d.dirty, 'Intervention', d);
      return d;
    });
    expect(apres.result.current.value).toEqual({ titre: 'Bascule du soir' });
    expect(apres.result.current.dirty).toBe(true);
    expect(apres.result.current.conflit).toBe(false);
    window.history.replaceState(null, '', '/');
  });
});
