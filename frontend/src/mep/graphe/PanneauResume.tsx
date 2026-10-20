import { CircleHelp, Ellipsis, FileSpreadsheet } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { telecharger } from '../../telechargement';
import type { Etape } from '../../types';
import { BoutonIcone } from '../../ui/Button';
import { Menu, MenuContent, MenuItem, MenuTrigger } from '../../ui/Menu';
import type { Section } from './sections';
import { Popover, PopoverAnchor, PopoverContent, PopoverTrigger } from '../../ui/Popover';

/** Intertitre de section (petites capitales). */
export function TitrePanneau({ children, id }: { children: ReactNode; id?: string }) {
  return (
    <h3 id={id} className="text-2xs font-semibold tracking-[0.06em] text-fg-subtle uppercase">
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

/** Résumé du plan : étapes, sections, durée cumulée (sauf <paramref name="sansDuree"/> : MEP simplifiée). */
export function ResumePlan({
  etapes,
  sections,
  sansDuree = false,
}: {
  etapes: Etape[];
  sections?: Section[];
  sansDuree?: boolean;
}) {
  const nb = sections?.length ?? 0;
  return (
    <p className="text-ui text-fg-subtle tnum">
      {etapes.length} étape{etapes.length > 1 ? 's' : ''}
      {nb > 0 && (
        <>
          {' '}
          · {nb} section{nb > 1 ? 's' : ''}
        </>
      )}
      {!sansDuree && etapes.length > 0 && <> · {dureeCumulee(etapes)}</>}
    </p>
  );
}

/** Conseils d'utilisation du graphe (aide « ? » et menu « ⋯ » de la vue compacte). */
export function ListeAide({ edition }: { edition: boolean }) {
  return (
    <ul className="grid list-disc gap-1 pl-4">
      {edition && (
        <li>
          « + » sous une carte : nouvelle étape qui l’attend ; à droite : en parallèle (mêmes préalables) ; sur un lien
          (au survol) : insérée entre les deux cartes.
        </li>
      )}
      {edition && (
        <li>
          Glisser une carte sur un lien pour l’y insérer, ou sur une autre carte pour la mettre après elle : ses liens
          changent, le rangement suit.
        </li>
      )}
      {edition && (
        <li>
          Ordre dans une rangée : glisser une carte ou une section à gauche ou à droite dans sa rangée (barre bleue),
          hors des liens et des cartes. La hauteur reste donnée par les liens ; l’ordre choisi vaut pour tout le monde.
        </li>
      )}
      {edition && <li>Tirer le rond du bas d’une carte vers celle qui doit l’attendre (ou vers un cercle).</li>}
      {edition && <li>Cliquer un lien pour le sélectionner, Suppr pour le retirer.</li>}
      <li>Cliquer une section pour la déplier ; le crayon ouvre sa vue.</li>
      <li>Survoler un lien ou ouvrir une carte met ses liens en avant ; un petit saut marque un croisement.</li>
      <li>Le bouton en haut à droite du plan l’affiche en plein écran (Échap pour revenir).</li>
      <li>Glisser le fond ou la molette pour se déplacer, Ctrl + molette pour zoomer ; « Aller à… » par code.</li>
      <li>Tab pour aller de carte en carte, Entrée pour ouvrir, Échap pour fermer.</li>
    </ul>
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
      <PopoverContent align="end" className="w-80 p-3 text-ui">
        <ListeAide edition={edition} />
      </PopoverContent>
    </Popover>
  );
}

/**
 * Menu « ⋯ » de la barre d'outils du graphe en vue compacte : « Exporter en Excel » et l'aide, qui quittent la ligne du
 * résumé pour laisser la hauteur au graphe.
 */
export function MenuPlusGraphe({
  edition,
  exporter,
}: {
  edition: boolean;
  exporter?: { url: string; title?: string };
}) {
  const [aide, setAide] = useState(false);
  return (
    <Popover open={aide} onOpenChange={setAide}>
      <Menu modal={false}>
        <PopoverAnchor asChild>
          <MenuTrigger asChild>
            <BoutonIcone label="Plus d’actions du graphe" taille="sm">
              <Ellipsis className="size-4" aria-hidden />
            </BoutonIcone>
          </MenuTrigger>
        </PopoverAnchor>
        <MenuContent
          align="end"
          // L'aide s'ouvre à la place du menu : le focus ne revient pas au bouton entre les deux.
          onCloseAutoFocus={(e) => {
            if (aide) e.preventDefault();
          }}
        >
          {exporter && (
            <MenuItem onSelect={() => telecharger(exporter.url)} title={exporter.title}>
              <FileSpreadsheet className="size-4 text-fg-subtle" aria-hidden />
              Exporter en Excel
            </MenuItem>
          )}
          <MenuItem onSelect={() => setAide(true)}>
            <CircleHelp className="size-4 text-fg-subtle" aria-hidden />
            Aide du graphe
          </MenuItem>
        </MenuContent>
      </Menu>
      <PopoverContent
        align="end"
        aria-label="Aide du graphe"
        className="max-h-[var(--radix-popover-content-available-height)] w-80 overflow-y-auto p-3 text-ui"
      >
        <ListeAide edition={edition} />
      </PopoverContent>
    </Popover>
  );
}
