import { useCallback, useEffect, useRef, useState } from 'react';
import { api } from '../api';
import type { Evenement } from '../types';

/** Événements lus par page (première page, puis « Afficher plus »). */
export const PAGE_JOURNAL = 100;
/** Nouveaux événements lus d'un coup après une modification ; s'il y en a autant, la première page est relue. */
const MAX_NOUVEAUX = 500;

/**
 * Lecture par morceaux d'un journal gardé ailleurs (données partagées, `useJournalMep`) : sans `precedent`, la première
 * page de la famille `type` ; sinon seulement les événements plus récents que le dernier lu, ajoutés en tête (la
 * première page est relue s'il y en a trop d'un coup).
 */
export async function lireJournal(mepId: string, type: string, precedent: Evenement[] | null): Promise<Evenement[]> {
  const premiere = () => api.journal(mepId, { type, limite: PAGE_JOURNAL });
  if (precedent === null) return premiere();
  const dernier = precedent[0]?.id ?? 0;
  const nouveaux = await api.journal(mepId, { type, apres: dernier, limite: MAX_NOUVEAUX }, true);
  if (nouveaux.length >= MAX_NOUVEAUX) return premiere();
  const plusRecents = nouveaux.filter((n) => n.id > dernier);
  return plusRecents.length > 0 ? [...plusRecents, ...precedent] : precedent;
}

export interface FiltreJournalLu {
  /** Famille d'événements (`familleEvenement`), « tous » ou vide : toutes. */
  type?: string;
  recherche?: string;
}

export interface JournalLu {
  /** Événements lus, du plus récent au plus ancien ; null pendant le premier chargement de la MEP. */
  evenements: Evenement[] | null;
  /** false tant que les événements affichés sont ceux de filtres précédents (les nouveaux arrivent). */
  aJour: boolean;
  erreur: string;
  /** Erreur reçue telle quelle (null sans erreur), pour `ErreurChargement`. */
  echec: unknown;
  /** Relit la première page (« Réessayer ») ; `rechargement` : relecture en cours. */
  recharger: () => void;
  rechargement: boolean;
  /** Des événements plus anciens restent à lire. */
  suite: boolean;
  chargementSuite: boolean;
  afficherPlus: () => void;
}

interface Etat {
  mepId: string;
  cle: string;
  evenements: Evenement[];
  suite: boolean;
}

/**
 * Journal d'une MEP lu par morceaux (#96) : la première page, puis, à chaque changement de `signal` (version de la MEP),
 * seulement les événements plus récents que le dernier lu ; « Afficher plus » lit la page plus ancienne. Les filtres sont
 * appliqués par le serveur. Les lectures passent l'une après l'autre : une réponse ne peut pas en écraser une plus récente.
 */
export function useJournal(
  mepId: string,
  signal: unknown,
  filtre: FiltreJournalLu = {},
  limite = PAGE_JOURNAL,
): JournalLu {
  const type = filtre.type === 'tous' ? '' : (filtre.type ?? '');
  const recherche = filtre.recherche?.trim() ?? '';
  const cle = `${mepId}\n${type}\n${recherche}\n${limite}`;
  const [etat, setEtat] = useState<Etat | null>(null);
  const [erreur, setErreur] = useState<{ cle: string; echec: unknown } | null>(null);
  const [chargementSuite, setChargementSuite] = useState(false);
  const [rechargement, setRechargement] = useState(false);

  const cleRef = useRef(cle);
  const etatRef = useRef<Etat | null>(null);
  const fileRef = useRef<Promise<void>>(Promise.resolve());
  const signalRef = useRef(signal);

  useEffect(() => {
    cleRef.current = cle;
  }, [cle]);

  const ecrire = useCallback((e: Etat) => {
    etatRef.current = e;
    setEtat(e);
  }, []);

  /** Ajoute une lecture à la file : elle part quand la précédente est finie. */
  const enFile = useCallback((lecture: () => Promise<void>) => {
    fileRef.current = fileRef.current.then(lecture).catch(() => undefined);
  }, []);

  const premierePage = useCallback(
    async (pour: string) => {
      try {
        const page = await api.journal(mepId, { type, recherche, limite });
        if (cleRef.current !== pour) return;
        ecrire({ mepId, cle: pour, evenements: page, suite: page.length === limite });
        setErreur(null);
      } catch (e) {
        if (cleRef.current === pour)
          setErreur({ cle: pour, echec: e ?? new Error('Chargement du journal impossible.') });
      }
    },
    [mepId, type, recherche, limite, ecrire],
  );

  // Première page, à l'ouverture et à chaque changement de filtres.
  useEffect(() => {
    enFile(() => premierePage(cle));
  }, [cle, enFile, premierePage]);

  // Nouvelle version : seulement les événements plus récents que le dernier lu.
  useEffect(() => {
    if (Object.is(signalRef.current, signal)) return;
    signalRef.current = signal;
    const pour = cle;
    enFile(async () => {
      const e = etatRef.current;
      if (!e || e.cle !== pour || cleRef.current !== pour) return;
      const dernier = e.evenements[0]?.id ?? 0;
      const nouveaux = await api.journal(mepId, { type, recherche, apres: dernier, limite: MAX_NOUVEAUX }, true);
      if (cleRef.current !== pour || etatRef.current !== e) return;
      if (nouveaux.length >= MAX_NOUVEAUX) return premierePage(pour);
      const plusRecents = nouveaux.filter((n) => n.id > dernier);
      if (plusRecents.length > 0) ecrire({ ...e, evenements: [...plusRecents, ...e.evenements] });
    });
  }, [signal, cle, mepId, type, recherche, enFile, premierePage, ecrire]);

  const afficherPlus = useCallback(() => {
    const pour = cleRef.current;
    setChargementSuite(true);
    enFile(async () => {
      try {
        const e = etatRef.current;
        const plusAncien = e?.evenements.at(-1)?.id;
        if (!e || !e.suite || e.cle !== pour || plusAncien === undefined) return;
        const page = await api.journal(mepId, { type, recherche, avant: plusAncien, limite });
        if (cleRef.current !== pour || etatRef.current !== e) return;
        ecrire({
          ...e,
          evenements: [...e.evenements, ...page.filter((x) => x.id < plusAncien)],
          suite: page.length === limite,
        });
      } catch (err) {
        if (cleRef.current === pour)
          setErreur({ cle: pour, echec: err ?? new Error('Chargement du journal impossible.') });
      } finally {
        setChargementSuite(false);
      }
    });
  }, [mepId, type, recherche, limite, enFile, ecrire]);

  const recharger = useCallback(() => {
    const pour = cleRef.current;
    setRechargement(true);
    enFile(() => premierePage(pour).finally(() => setRechargement(false)));
  }, [enFile, premierePage]);

  const courant = etat && etat.mepId === mepId ? etat : null;
  const echec = erreur?.cle === cle ? erreur.echec : null;
  return {
    evenements: courant?.evenements ?? null,
    aJour: courant?.cle === cle,
    erreur: echec === null ? '' : echec instanceof Error ? echec.message : 'Chargement du journal impossible.',
    echec,
    recharger,
    rechargement,
    suite: courant?.cle === cle && courant.suite,
    chargementSuite,
    afficherPlus,
  };
}
