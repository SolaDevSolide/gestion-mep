import { CircleHelp } from 'lucide-react';
import type { ReactNode } from 'react';
import { construireLignes } from '../../graphe';
import type { Etape } from '../../types';
import { Popover, PopoverContent, PopoverTrigger } from '../../ui/Popover';

/** Intertitre de section (petites capitales). */
export function TitrePanneau({ children, id }: { children: ReactNode; id?: string }) {
  return (
    <h3 id={id} className="text-[11px] font-semibold tracking-[0.06em] text-fg-subtle uppercase">
      {children}
    </h3>
  );
}

/** Durée cumulée : les étapes sans durée sont ignorées et comptées à part (la durée est facultative). */
export function dureeCumulee(etapes: Pick<Etape, 'dureeEstimeeMinutes'>[]): string {
  const total = etapes.reduce((t, e) => t + (e.dureeEstimeeMinutes ?? 0), 0);
  const sans = etapes.filter((e) => e.dureeEstimeeMinutes === null).length;
  if (sans === etapes.length) return 'durée non estimée';
  return `${total} min${sans ? ` (${sans} étape${sans > 1 ? 's' : ''} sans durée)` : ''}`;
}

/** Résumé d'une ligne du plan : étapes, lignes, durée cumulée. */
export function ResumePlan({ etapes }: { etapes: Etape[] }) {
  const lignes = construireLignes(etapes).length;
  return (
    <p className="text-[13px] text-fg-subtle tnum">
      {etapes.length} étape{etapes.length > 1 ? 's' : ''} · {lignes} ligne{lignes > 1 ? 's' : ''}
      {etapes.length > 0 && <> · {dureeCumulee(etapes)}</>}
    </p>
  );
}

/** Aide du graphe, repliée derrière une icône « ? ». */
export function AideGraphe({ edition }: { edition: boolean }) {
  return (
    <Popover>
      <PopoverTrigger
        className="inline-flex size-7 items-center justify-center rounded-full text-fg-subtle hover:bg-surface-2 hover:text-fg"
        aria-label="Aide du graphe"
      >
        <CircleHelp className="size-4" aria-hidden />
      </PopoverTrigger>
      <PopoverContent align="end" className="w-80 p-3 text-[13px]">
        <ul className="grid list-disc gap-1 pl-4">
          {edition && <li>Glisser une fiche pour la déplacer (liens recréés avec les lignes voisines).</li>}
          {edition && <li>Au clavier : Alt + flèches sur une fiche pour la déplacer.</li>}
          {edition && <li>Tirer le rond du bas d’une fiche vers celle qui doit l’attendre : lien particulier.</li>}
          {edition && <li>Cliquer un lien pour le sélectionner, Suppr pour le retirer.</li>}
          <li>Glisser le fond pour se déplacer, Ctrl + molette pour zoomer.</li>
          <li>Tab pour aller de fiche en fiche, Entrée pour ouvrir, Échap pour fermer.</li>
        </ul>
      </PopoverContent>
    </Popover>
  );
}
