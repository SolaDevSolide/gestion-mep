import { createContext, useContext, useEffect, useId, useMemo, useRef, useState, type ReactNode } from 'react';
import { DirtyContext, type DirtyRegistry } from './hooks';
import type { Presence } from './types';
import { Avatar, couleurPersonne, stylePersonne } from './ui/Avatar';
import { cn } from './ui/cn';

/**
 * Présence en temps réel sur une MEP : qui l'a ouverte et sur quel élément chacun travaille. Informatif : rien n'est
 * verrouillé. Les clés d'élément sont libres (`etape:{id}`, `preparation:{section}`, `recette:{idFiche}`,
 * `retour:fiche`) ; le serveur les relaie telles quelles par le flux de la MEP.
 */
export interface PresenceValeur {
  /** Les autres personnes qui ont la MEP ouverte (vous n'y figurez pas), par ordre d'arrivée. */
  autres: Presence[];
  /** Déclare (ou retire) un élément sur lequel cet onglet travaille ; `jeton` identifie le composant déclarant. */
  declarer: (jeton: string, cle: string | null) => void;
}

const PresenceContext = createContext<PresenceValeur>({ autres: [], declarer: () => undefined });
export const PresenceProvider = PresenceContext.Provider;

/** Prénom affiché dans « Bob modifie » : premier mot du nom. */
export const prenom = (nom: string) => nom.trim().split(/\s+/)[0] || nom;

/** Phrase de l'étiquette : « Bob modifie », « Bob et Claire modifient », « Bob, Claire et Dave modifient ». */
export function libelleEditeurs(editeurs: Presence[]): string {
  const prenoms = editeurs.map((p) => prenom(p.nom));
  if (prenoms.length <= 1) return `${prenoms[0] ?? ''} modifie`;
  return `${prenoms.slice(0, -1).join(', ')} et ${prenoms.at(-1)} modifient`;
}

/** Déclare `cle` tant que `actif` est vrai (et que le composant est monté). */
export function usePresenceElement(cle: string, actif: boolean) {
  const { declarer } = useContext(PresenceContext);
  const jeton = useId();
  useEffect(() => {
    declarer(jeton, actif ? cle : null);
    return () => declarer(jeton, null);
  }, [declarer, jeton, cle, actif]);
}

/** Les autres personnes qui travaillent sur `cle` (ou, avec `prefixe`, sur un élément qui commence par `cle`). */
export function useEditeurs(cle: string, prefixe = false): Presence[] {
  const { autres } = useContext(PresenceContext);
  return useMemo(
    () => autres.filter((p) => p.elements.some((e) => (prefixe ? e.startsWith(cle) : e === cle))),
    [autres, cle, prefixe],
  );
}

export function useAutresPresents(): Presence[] {
  return useContext(PresenceContext).autres;
}

/** Encadré coloré (couleur de la première personne) et étiquette « Prénom modifie » posés sur un élément. */
export function MarquePresence({
  editeurs,
  className,
  etiquette,
}: {
  editeurs: Presence[];
  className?: string;
  /** Place de l'étiquette (par défaut sur le bord haut, à gauche). */
  etiquette?: string;
}) {
  if (editeurs.length === 0) return null;
  const couleur = couleurPersonne(editeurs[0].nom);
  return (
    <>
      <span
        aria-hidden
        className={cn('pointer-events-none absolute -inset-1 z-[3] rounded-[inherit] border-2', className)}
        style={{ borderColor: couleur }}
      />
      <span
        role="status"
        className={cn(
          'pointer-events-none absolute -top-3 left-2 z-[4] inline-flex max-w-[calc(100%-1rem)] items-center gap-1 truncate rounded-full px-2 py-0.5 text-2xs leading-none font-semibold shadow-sm',
          etiquette,
        )}
        style={stylePersonne(editeurs[0].nom)}
      >
        {libelleEditeurs(editeurs)}
      </span>
    </>
  );
}

/** Élément d'une MEP sur lequel les autres voient qui travaille ; ne déclare rien lui-même. */
export function CadrePresence({ cle, className, children }: { cle: string; className?: string; children: ReactNode }) {
  const editeurs = useEditeurs(cle);
  return (
    <div className={cn('relative rounded-xl', className)} data-presence={cle}>
      {children}
      <MarquePresence editeurs={editeurs} />
    </div>
  );
}

/**
 * Zone de travail : déclare `cle` tant que le focus y est ou qu'un de ses formulaires a une saisie non enregistrée
 * (signalée par `useReportDirty`, relayée au registre parent), et affiche qui d'autre y travaille.
 */
export function ZonePresence({ cle, className, children }: { cle: string; className?: string; children: ReactNode }) {
  const parent = useContext(DirtyContext);
  const sales = useRef(new Set<string>());
  const [sale, setSale] = useState(false);
  const [focus, setFocus] = useState(false);
  const registre = useMemo<DirtyRegistry>(
    () => ({
      set: (id, dirty, libelle, lire) => {
        parent.set(id, dirty, libelle, lire);
        if (dirty) sales.current.add(id);
        else sales.current.delete(id);
        setSale(sales.current.size > 0);
      },
      libelles: () => parent.libelles(),
      brouillons: () => parent.brouillons(),
      abandonner: () => parent.abandonner(),
    }),
    [parent],
  );
  usePresenceElement(cle, focus || sale);
  return (
    <DirtyContext.Provider value={registre}>
      <div
        className={className}
        onFocus={() => setFocus(true)}
        onBlur={(e) => {
          if (!e.currentTarget.contains(e.relatedTarget)) setFocus(false);
        }}
      >
        <CadrePresence cle={cle}>{children}</CadrePresence>
      </div>
    </DirtyContext.Provider>
  );
}

/** En-tête de la MEP : « Sur cette MEP : » et les avatars des autres personnes qui l'ont ouverte. */
export function PresentsMep() {
  const autres = useAutresPresents();
  if (autres.length === 0) return null;
  const visibles = autres.slice(0, 5);
  return (
    <div className="flex items-center gap-2 text-xs text-fg-muted" aria-label="Personnes sur cette MEP" role="group">
      <span>Sur cette MEP :</span>
      <span className="flex -space-x-1.5">
        {visibles.map((p) => (
          <Avatar
            key={p.utilisateurId}
            nom={p.nom}
            taille="sm"
            className="ring-2 ring-surface"
            titre={p.elements.length > 0 ? `${p.nom} (modifie)` : p.nom}
          />
        ))}
        {autres.length > visibles.length && (
          <span className="inline-flex size-6 items-center justify-center rounded-full bg-surface-2 text-2xs font-semibold text-fg-muted ring-2 ring-surface">
            +{autres.length - visibles.length}
          </span>
        )}
      </span>
    </div>
  );
}

/** Identifiant de cet onglet pour le flux et la déclaration de présence. */
export function nouvelleSession(): string {
  try {
    return crypto.randomUUID().replace(/-/g, '');
  } catch {
    return Math.random().toString(36).slice(2) + Date.now().toString(36);
  }
}
