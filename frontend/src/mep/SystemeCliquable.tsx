// Système affiché dans une liste, un rapport, l'en-tête d'une MEP ou le suivi en direct (#59) : un clic ouvre son menu,
// « Historique des MEP », « MEP sur ce système » (liste filtrée) et « Fiche du système ». Un système saisi à la main (hors
// catalogue) n'a pas d'historique : son menu ne propose que la liste filtrée.
import { Filter, History, Layers, TriangleAlert } from 'lucide-react';
import type { ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import { couleurSysteme, styleSysteme } from '../couleursSysteme';
import { abregerEnvironnement } from '../environnements';
import { lienListeSysteme, PREFIXE_LIBRE } from '../rechercheMeps';
import { cn } from '../ui/cn';
import { Menu, MenuContent, MenuItem, MenuLabel, MenuSeparator, MenuTrigger } from '../ui/Menu';

export interface SystemeAffiche {
  catalogueId: string | null;
  nom: string;
  environnements?: { nom: string; production: boolean }[];
  /** Échecs d'étape survenus sur ce système (signalés par une alerte). */
  echecs?: number;
}

/** Adresse de l'historique des MEP d'un système du catalogue (onglet de sa fiche). */
export const lienHistoriqueSysteme = (id: string) => `/parametres/systemes/${id}?onglet=historique`;

function MenuDuSysteme({ s, children, className }: { s: SystemeAffiche; children: ReactNode; className?: string }) {
  const navigate = useNavigate();
  const filtre = s.catalogueId ?? `${PREFIXE_LIBRE}${s.nom}`;
  return (
    <Menu>
      <MenuTrigger
        className={cn(
          'cursor-pointer rounded-md outline-none focus-visible:ring-2 focus-visible:ring-accent',
          className,
        )}
        aria-label={`${s.nom} : historique et MEP de ce système`}
        // Dans une carte ou une ligne cliquable : le menu ne doit pas ouvrir la MEP.
        onClick={(e) => e.stopPropagation()}
        onKeyDown={(e) => e.stopPropagation()}
      >
        {children}
      </MenuTrigger>
      <MenuContent align="start" onClick={(e) => e.stopPropagation()}>
        <MenuLabel>
          {s.nom}
          {s.catalogueId === null && <span className="font-normal"> · hors catalogue</span>}
        </MenuLabel>
        {s.catalogueId !== null && (
          <MenuItem onSelect={() => void navigate(lienHistoriqueSysteme(s.catalogueId!))}>
            <History className="size-4 text-fg-subtle" aria-hidden />
            Historique des MEP
          </MenuItem>
        )}
        <MenuItem onSelect={() => void navigate(lienListeSysteme(filtre))}>
          <Filter className="size-4 text-fg-subtle" aria-hidden />
          MEP sur {s.nom}
        </MenuItem>
        {s.catalogueId !== null ? (
          <>
            <MenuSeparator />
            <MenuItem onSelect={() => void navigate(`/parametres/systemes/${s.catalogueId}`)}>
              <Layers className="size-4 text-fg-subtle" aria-hidden />
              Fiche du système
            </MenuItem>
          </>
        ) : (
          <p className="max-w-60 px-2.5 py-1.5 text-xs text-fg-subtle">
            Saisi à la main dans la MEP : pas de fiche ni d’historique.
          </p>
        )}
      </MenuContent>
    </Menu>
  );
}

/**
 * Puce d'un système : sa couleur, ses environnements abrégés (production en orange) et une alerte s'il a eu un échec
 * d'étape. `compacte` : le nom seul (systèmes non filtrés d'une ligne de résultats).
 */
export function PuceSystemeMep({
  s,
  compacte,
  className,
}: {
  s: SystemeAffiche;
  compacte?: boolean;
  className?: string;
}) {
  const echec = (s.echecs ?? 0) > 0;
  return (
    <MenuDuSysteme s={s}>
      <span
        className={cn(
          'inline-flex items-center gap-1 rounded-md border px-1.5 py-px text-[11px] leading-4 whitespace-nowrap transition-shadow duration-150 hover:shadow-sm',
          s.catalogueId === null && 'border-dashed',
          className,
        )}
        style={{ ...styleSysteme(s.nom), borderColor: 'color-mix(in srgb, currentColor 35%, transparent)' }}
      >
        <i className="size-1.5 shrink-0 rounded-full" style={{ background: couleurSysteme(s.nom) }} aria-hidden />
        <span className="font-semibold">{s.nom}</span>
        {!compacte &&
          s.environnements?.map((e) => (
            <span key={e.nom} title={e.nom} className={cn(e.production ? 'font-semibold text-warn-fg' : 'opacity-75')}>
              {abregerEnvironnement(e.nom)}
            </span>
          ))}
        {echec && (
          <TriangleAlert
            className="size-3 text-danger-solid"
            aria-label={`${s.echecs} échec${(s.echecs ?? 0) > 1 ? 's' : ''} d’étape sur ce système`}
          />
        )}
      </span>
    </MenuDuSysteme>
  );
}

/** Nom d'un système dans un texte (en-tête d'une MEP, rapport) : souligné au survol, même menu que la puce. */
export function NomSystemeMep({ s, className }: { s: SystemeAffiche; className?: string }) {
  return (
    <MenuDuSysteme s={s} className={cn('inline-flex items-center gap-1 hover:underline', className)}>
      <i className="size-2 shrink-0 rounded-full" style={{ background: couleurSysteme(s.nom) }} aria-hidden />
      <span className="font-semibold text-fg">{s.nom}</span>
    </MenuDuSysteme>
  );
}
