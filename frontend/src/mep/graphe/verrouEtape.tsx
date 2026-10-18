import { Hand, Lock } from 'lucide-react';
import { createContext, useCallback, useContext, useEffect, useRef, useState } from 'react';
import { prenom } from '../../presence';
import { Button } from '../../ui/Button';
import type { PriseVerrou, Verrou } from '../../types';
import { apiVerrou } from './apiGraphe';

/** Renouvellement du verrou tant que le panneau est ouvert (le serveur l'oublie après 60 s sans signe de vie). */
export const RENOUVELLEMENT_MS = 20_000;

/** Verrous en cours de la MEP (flux temps réel) et identifiant de cet onglet (celui passé au flux). */
export interface ValeurVerrous {
  session: string;
  /** null tant que le flux n'a rien dit. */
  verrous: Verrou[] | null;
}

export const VerrousContext = createContext<ValeurVerrous>({ session: '', verrous: null });

/** Lecture tolérante de l'événement `verrous` du flux. */
export function lireVerrous(donnees: unknown): Verrou[] | null {
  if (!Array.isArray(donnees)) return null;
  return donnees.filter(
    (v): v is Verrou =>
      typeof v === 'object' &&
      v !== null &&
      typeof (v as Verrou).etapeId === 'string' &&
      typeof (v as Verrou).session === 'string',
  );
}

/**
 * État du verrou d'une étape ouverte :
 * - `libre` : pas encore pris (ou étape fermée) ;
 * - `moi` : pris par cet onglet, le panneau est modifiable ;
 * - `autre` : pris par quelqu'un d'autre à l'ouverture (« Alice est dessus ») ;
 * - `perdu` : quelqu'un a pris la main pendant qu'on éditait (« Bob a pris la main »).
 */
export type EtatVerrou = 'libre' | 'moi' | 'autre' | 'perdu';

export interface VerrouEtape {
  etat: EtatVerrou;
  detenteur: Verrou | null;
  prendreLaMain: () => void;
}

/** Nouvel état après une réponse du serveur à une prise (ou un renouvellement) du verrou. */
export function etatApresPrise(avant: EtatVerrou, r: PriseVerrou): EtatVerrou {
  if (r.pris) return 'moi';
  return avant === 'moi' || avant === 'perdu' ? 'perdu' : 'autre';
}

/** Nouvel état d'après la liste diffusée par le flux ; `reprendre` : le verrou est libre, on peut le reprendre. */
export function etatDepuisFlux(
  avant: EtatVerrou,
  verrou: Verrou | undefined,
  session: string,
): { etat: EtatVerrou; reprendre: boolean } {
  if (!verrou) return { etat: avant, reprendre: avant === 'autre' };
  if (verrou.session === session) return { etat: avant === 'libre' ? 'libre' : 'moi', reprendre: false };
  if (avant === 'moi' || avant === 'perdu') return { etat: 'perdu', reprendre: false };
  return { etat: avant === 'libre' ? 'libre' : 'autre', reprendre: false };
}

/**
 * Verrou léger d'édition d'une étape (question 11) : pris à l'ouverture du panneau, renouvelé tant qu'il reste ouvert,
 * relâché à la fermeture. Les prises de main arrivent par le flux temps réel (`event: verrous`).
 */
export function useVerrouEtape(mepId: string, etapeId: string | null, actif: boolean): VerrouEtape {
  const { session, verrous } = useContext(VerrousContext);
  const [detenteur, setDetenteur] = useState<Verrou | null>(null);
  const [etat, setEtat] = useState<EtatVerrou>('libre');
  const etatRef = useRef(etat);
  useEffect(() => {
    etatRef.current = etat;
  }, [etat]);

  const prendre = useCallback(
    async (prendreLaMain = false) => {
      if (!etapeId || !session) return;
      try {
        const r = await apiVerrou.prendre(mepId, etapeId, session, prendreLaMain);
        setDetenteur(r.verrou);
        setEtat((e) => etatApresPrise(prendreLaMain ? 'libre' : e, r));
      } catch {
        /* serveur injoignable : on réessaie au prochain renouvellement */
      }
    },
    [mepId, etapeId, session],
  );

  useEffect(() => {
    if (!etapeId || !actif || !session) return;
    void prendre();
    const t = window.setInterval(() => {
      // Pris par quelqu'un d'autre : on ne le lui reprend pas en silence ; « Prendre la main » le fait.
      if (etatRef.current === 'moi' || etatRef.current === 'libre') void prendre();
    }, RENOUVELLEMENT_MS);
    return () => {
      window.clearInterval(t);
      setEtat('libre');
      setDetenteur(null);
      if (etatRef.current === 'moi')
        apiVerrou.relacher(mepId, etapeId, session).catch(() => {
          /* expirera seul */
        });
      etatRef.current = 'libre';
    };
  }, [mepId, etapeId, actif, session, prendre]);

  // Flux temps réel : quelqu'un a pris la main, ou l'a relâchée (on la reprend si l'on attendait).
  useEffect(() => {
    if (!verrous || !etapeId || !actif) return;
    const v = verrous.find((x) => x.etapeId === etapeId);
    const { etat: suivant, reprendre } = etatDepuisFlux(etatRef.current, v, session);
    if (v && v.session !== session) setDetenteur(v);
    if (suivant !== etatRef.current) setEtat(suivant);
    if (reprendre) void prendre();
  }, [verrous, etapeId, actif, session, prendre]);

  const prendreLaMain = useCallback(() => void prendre(true), [prendre]);
  return { etat, detenteur, prendreLaMain };
}

/** Bandeau du panneau d'une étape verrouillée par quelqu'un d'autre. */
export function BandeauVerrou({ verrou }: { verrou: VerrouEtape }) {
  if (verrou.etat !== 'autre' && verrou.etat !== 'perdu') return null;
  const nom = verrou.detenteur ? prenom(verrou.detenteur.nom) : 'Quelqu’un';
  return (
    <div
      role="status"
      className="flex flex-wrap items-center gap-2 rounded-lg border border-warn-bd bg-warn-bg px-3 py-2 text-sm text-warn-fg"
    >
      <Lock className="size-4 shrink-0" aria-hidden />
      <span className="min-w-0 flex-1 font-medium">
        {verrou.etat === 'perdu'
          ? `${nom} a pris la main : lecture seule. Votre saisie non enregistrée reste affichée pour que vous la copiiez.`
          : `${nom} est dessus : lecture seule.`}
      </span>
      <Button taille="sm" icone={<Hand className="size-4" aria-hidden />} onClick={verrou.prendreLaMain}>
        Prendre la main
      </Button>
    </div>
  );
}

/** Clé d'une prise de main (qui l'a prise, à quel onglet) : une même prise n'est annoncée qu'une fois. */
const clePrise = (v: Verrou) => `${v.etapeId}|${v.session}|${v.reprisDeSession ?? ''}`;

/**
 * Prises de main apparues entre deux listes de verrous diffusées par le flux : verrous qui viennent d'être pris à
 * quelqu'un d'autre (`reprisDeNom`). La première liste reçue (`avant` null) ne compte pas : ce sont des prises anciennes.
 */
export function nouvellesPrisesDeMain(avant: Verrou[] | null, apres: Verrou[] | null): Verrou[] {
  if (!avant || !apres) return [];
  const connues = new Set(avant.filter((v) => v.reprisDeNom).map(clePrise));
  return apres.filter((v) => v.reprisDeNom && !connues.has(clePrise(v)));
}

/** « Claire a pris la main sur E0005 à Alice ». */
export function libellePriseDeMain(v: Verrou, code: string): string {
  return `${prenom(v.nom)} a pris la main sur ${code} à ${prenom(v.reprisDeNom ?? 'quelqu’un')}`;
}
