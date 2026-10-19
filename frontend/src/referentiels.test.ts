import { act, renderHook, waitFor } from '@testing-library/react';
import { api } from './api';
import { oublierReferentiels, useReferentiel, useReferentiels } from './referentiels';
import type { ResolutionType, SystemeCatalogue } from './types';

vi.mock('./api', () => ({ api: { catalogueSystemes: vi.fn(), resolutions: vi.fn(), utilisateurs: vi.fn() } }));

const catalogue = [
  { id: 'c-api', nom: 'API', description: 'Service API', cible: '', actif: true },
] as SystemeCatalogue[];
const resolutions = [] as ResolutionType[];

describe('useReferentiels', () => {
  beforeEach(() => {
    oublierReferentiels();
    vi.mocked(api.catalogueSystemes).mockResolvedValue(catalogue);
    vi.mocked(api.resolutions).mockResolvedValue(resolutions);
  });

  it('n’est prêt qu’une fois le catalogue et les résolutions arrivés', async () => {
    const { result } = renderHook(() => useReferentiels());
    expect(result.current.pret).toBe(false);
    await waitFor(() => expect(result.current.pret).toBe(true));
    expect(result.current.catalogue).toEqual(catalogue);
  });

  it('garde les référentiels d’un onglet à l’autre : un onglet rouvert est prêt tout de suite', async () => {
    const premier = renderHook(() => useReferentiels());
    await waitFor(() => expect(premier.result.current.pret).toBe(true));
    premier.unmount();
    const second = renderHook(() => useReferentiels());
    expect(second.result.current.pret).toBe(true);
    expect(second.result.current.catalogue).toEqual(catalogue);
    await waitFor(() => expect(api.catalogueSystemes).toHaveBeenCalledTimes(2));
  });

  it('laisse afficher l’onglet si un référentiel ne se charge pas', async () => {
    vi.mocked(api.catalogueSystemes).mockRejectedValue(new Error('hors ligne'));
    const { result } = renderHook(() => useReferentiels());
    await waitFor(() => expect(result.current.pret).toBe(true));
    expect(result.current.catalogue).toEqual([]);
  });
});

describe('useReferentiel', () => {
  beforeEach(() => {
    oublierReferentiels();
    vi.mocked(api.utilisateurs).mockReset();
  });

  it('une seule requête pour tous les composants qui demandent la même liste', async () => {
    vi.mocked(api.utilisateurs).mockResolvedValue([]);
    const a = renderHook(() => useReferentiel('comptes'));
    const b = renderHook(() => useReferentiel('comptes'));
    await waitFor(() => expect(a.result.current.pret).toBe(true));
    expect(b.result.current.pret).toBe(true);
    expect(api.utilisateurs).toHaveBeenCalledTimes(1);
  });

  it('une panne reste une erreur, jamais une liste vide ; « Réessayer » relit la liste', async () => {
    const panne = new Error('hors ligne');
    vi.mocked(api.utilisateurs).mockRejectedValueOnce(panne).mockResolvedValueOnce([]);
    const { result } = renderHook(() => useReferentiel('comptes'));
    await waitFor(() => expect(result.current.pret).toBe(true));
    expect(result.current.erreur).toBe(panne);
    expect(result.current.donnees).toBeNull();
    act(() => result.current.recharger());
    await waitFor(() => expect(result.current.donnees).toEqual([]));
    expect(result.current.erreur).toBeNull();
  });

  it('inactif : aucune requête', () => {
    renderHook(() => useReferentiel('comptes', false));
    expect(api.utilisateurs).not.toHaveBeenCalled();
  });
});
