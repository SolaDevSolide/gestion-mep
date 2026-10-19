import { Maximize2, Minimize2, Plus, Search } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { PresentsMep } from '../../presence';
import type { Etape, TypePlan } from '../../types';
import { BoutonIcone, Button } from '../../ui/Button';
import { cn } from '../../ui/cn';

/** « + Étape » de la barre : création à part (menu de création) ou pose à part de la copie d'une étape modèle. */
export interface BoutonEtapeBarre {
  /** Une copie d'étape modèle attend d'être posée : le bouton la pose à part, sans lien. */
  placement: boolean;
  /** Le clic ouvre le menu « Étape vierge / Depuis un modèle ». */
  menu: boolean;
  onClick: (el: HTMLElement) => void;
}

/**
 * Barre d'outils du graphe libre, en haut de son cadre : étapes modèles (`enTete`) à gauche ; à droite, outils de l'écran
 * (`outils`), « + Étape », « Aller à… » (code d'une étape), menu « ⋯ » (`menu`) et plein écran.
 * Vue compacte (`compact`) : une ligne de 44 px, avec le résumé du plan (`resume`) après les étapes modèles.
 */
export function BarreOutilsGraphe({
  plan,
  etapes,
  enTete,
  outils,
  boutonEtape,
  plein,
  onPlein,
  onAller,
  compact = false,
  resume,
  menu,
}: {
  plan: TypePlan;
  /** Étapes proposées par « Aller à… ». */
  etapes: Etape[];
  enTete?: ReactNode;
  outils?: ReactNode;
  /** Absent hors édition. */
  boutonEtape?: BoutonEtapeBarre;
  plein: boolean;
  onPlein: (plein: boolean) => void;
  /** Va à l'étape de ce code ; vrai si elle est trouvée (le champ se vide). */
  onAller: (texte: string) => boolean;
  compact?: boolean;
  /** Résumé du plan (« 2 étapes · 35 min ») et messages, dans la barre en vue compacte. */
  resume?: ReactNode;
  /** Menu « ⋯ » (export, aide), avant le bouton plein écran. */
  menu?: ReactNode;
}) {
  const [recherche, setRecherche] = useState('');
  return (
    <div
      role="toolbar"
      aria-label="Outils du graphe"
      className={cn(
        '@container flex shrink-0 items-center gap-2 overflow-x-auto border-b border-line bg-surface-2/60 px-2',
        compact ? 'h-11' : 'min-h-12 py-1.5',
      )}
    >
      {/* Boutons des étapes modèles : posés dans la barre (ils se placent d'eux-mêmes en absolu ailleurs). */}
      {enTete && (
        <div className="shrink-0 [&>div]:!static [&>div]:!shadow-none [&_button]:whitespace-nowrap">{enTete}</div>
      )}
      {resume && (
        <div className="flex min-w-0 flex-1 items-center gap-3 overflow-hidden whitespace-nowrap [&>p]:min-w-0 [&>p]:truncate">
          {resume}
        </div>
      )}
      <div className="ml-auto flex shrink-0 items-center gap-2">
        {/* Plein écran : qui est sur la MEP reste visible (la page en dessous est masquée). */}
        {plein && <PresentsMep />}
        {outils}
        {boutonEtape && (
          <Button
            taille="sm"
            icone={<Plus className="size-4" aria-hidden />}
            aria-haspopup={boutonEtape.placement || !boutonEtape.menu ? undefined : 'menu'}
            onClick={(e) => boutonEtape.onClick(e.currentTarget)}
            title={boutonEtape.placement ? 'Poser la copie à part, sans lien' : 'Créer une étape à part, sans lien'}
          >
            {boutonEtape.placement ? 'Poser à part' : 'Étape'}
          </Button>
        )}
        <form
          role="search"
          className="relative"
          onSubmit={(e) => {
            e.preventDefault();
            if (onAller(recherche)) setRecherche('');
          }}
        >
          <Search
            className="pointer-events-none absolute top-1/2 left-2 size-3.5 -translate-y-1/2 text-fg-subtle"
            aria-hidden
          />
          <input
            type="search"
            aria-label="Aller à une étape (code)"
            placeholder="Aller à…"
            list={`codes-${plan}`}
            value={recherche}
            onChange={(e) => setRecherche(e.target.value)}
            className="h-8 w-36 rounded-lg border border-line bg-surface pr-2 pl-7 text-[13px] focus:border-accent focus:ring-2 focus:ring-accent/30 focus:outline-none @max-2xl:w-28"
          />
          <datalist id={`codes-${plan}`}>
            {etapes.map((e) => (
              <option key={e.id} value={e.code}>
                {e.action}
              </option>
            ))}
          </datalist>
        </form>
        {menu}
        <BoutonIcone
          label={plein ? 'Quitter le plein écran (Échap)' : 'Plan en plein écran'}
          taille="sm"
          aria-pressed={plein}
          onClick={() => onPlein(!plein)}
        >
          {plein ? <Minimize2 className="size-4" aria-hidden /> : <Maximize2 className="size-4" aria-hidden />}
        </BoutonIcone>
      </div>
    </div>
  );
}
