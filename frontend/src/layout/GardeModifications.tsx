import { TriangleAlert } from 'lucide-react';
import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { useBlocker, type Location } from 'react-router-dom';
import { DirtyContext, type DirtyRegistry } from '../hooks';
import { Button } from '../ui/Button';
import { Dialog } from '../ui/Dialog';

/** Paramètres d'adresse qui changent de vue (onglet d'une page, sous-onglet du plan d'une MEP). */
const PARAMETRES_DE_VUE = ['onglet', 'plan'];

/**
 * Vrai si passer de `avant` à `apres` démonte la vue courante : autre page (autre MEP, autre entrée du menu) ou autre
 * onglet ou sous-onglet. Les autres paramètres (étape ouverte…) restent libres.
 */
export function changeDeVue(
  avant: Pick<Location, 'pathname' | 'search'>,
  apres: Pick<Location, 'pathname' | 'search'>,
) {
  if (avant.pathname !== apres.pathname) return true;
  const a = new URLSearchParams(avant.search);
  const b = new URLSearchParams(apres.search);
  return PARAMETRES_DE_VUE.some((p) => a.get(p) !== b.get(p));
}

/**
 * Garde des modifications non enregistrées : les formulaires la renseignent par `useReportDirty`. Quitter la vue
 * (lien, menu, onglet, sous-onglet) ouvre « Quitter sans enregistrer ? » avec la liste de ce qui serait perdu ;
 * fermer ou recharger l'onglet du navigateur déclenche l'avertissement standard du navigateur.
 * Nécessite un routeur de données (`createBrowserRouter`).
 */
export function GardeModifications({ children }: { children: ReactNode }) {
  const sales = useRef(new Map<string, { libelle?: string; lire?: () => unknown }>());
  const registre = useMemo<DirtyRegistry>(
    () => ({
      set: (id, dirty, libelle, lire) => {
        if (dirty) sales.current.set(id, { libelle, lire });
        else sales.current.delete(id);
      },
      libelles: () => [...new Set([...sales.current.values()].map((e) => e.libelle ?? 'Une saisie en cours'))],
      brouillons: () =>
        [...sales.current.values()].flatMap((e) =>
          e.libelle && e.lire ? [{ libelle: e.libelle, valeur: e.lire() }] : [],
        ),
      abandonner: () => sales.current.clear(),
    }),
    [],
  );

  // Ce qui serait perdu, relevé au moment où la navigation est retenue.
  const [perdus, setPerdus] = useState<string[]>([]);
  const blocker = useBlocker(({ currentLocation, nextLocation }) => {
    // Session expirée : le retour à l'écran de connexion n'est pas retenu (rien ne pourrait plus être enregistré).
    if (nextLocation.pathname === '/connexion') return false;
    if (sales.current.size === 0 || !changeDeVue(currentLocation, nextLocation)) return false;
    setPerdus(registre.libelles().sort((a, b) => a.localeCompare(b, 'fr')));
    return true;
  });

  useEffect(() => {
    const avertir = (e: BeforeUnloadEvent) => {
      if (sales.current.size === 0) return;
      e.preventDefault();
      // Anciens navigateurs : une valeur de retour non vide déclenche aussi l'avertissement.
      e.returnValue = '';
    };
    window.addEventListener('beforeunload', avertir);
    return () => window.removeEventListener('beforeunload', avertir);
  }, []);

  const bloque = blocker.state === 'blocked';

  return (
    <DirtyContext.Provider value={registre}>
      {children}
      <Dialog
        open={bloque}
        onOpenChange={(o) => {
          if (!o && blocker.state === 'blocked') blocker.reset();
        }}
        taille="sm"
        titre={
          <span className="flex items-center gap-2">
            <TriangleAlert className="size-5 text-warn-solid" aria-hidden />
            Quitter sans enregistrer ?
          </span>
        }
        description="Ces modifications ne sont pas enregistrées. Elles seront perdues si vous quittez cette vue."
        pied={
          <>
            <Button variante="danger" onClick={() => blocker.proceed?.()}>
              Quitter sans enregistrer
            </Button>
            <Button variante="primaire" autoFocus onClick={() => blocker.reset?.()}>
              Rester
            </Button>
          </>
        }
      >
        <ul aria-label="Modifications non enregistrées" className="list-disc space-y-1 pl-5 text-sm">
          {perdus.map((l) => (
            <li key={l}>{l}</li>
          ))}
        </ul>
      </Dialog>
    </DirtyContext.Provider>
  );
}
