import { Squelette } from '../../ui/Squelette';
import { CARTE } from './rangement';

/** Une carte du squelette, à la taille d'une carte d'étape. */
function Carte() {
  return (
    <div
      className="grid content-start gap-2 rounded-lg border border-line bg-surface p-2.5"
      style={{ width: CARTE.largeur, height: CARTE.hauteur }}
    >
      <Squelette className="h-3 w-2/5" />
      <Squelette className="h-3.5 w-11/12" />
      <Squelette className="h-3.5 w-3/5" />
    </div>
  );
}
const Trait = () => <div aria-hidden className="h-8 w-px bg-line-strong" />;

/**
 * Place du graphe tant qu'il n'est pas rangé (premier rangement, autre vue) : quelques cartes reliées, au lieu de montrer
 * des cartes à une place provisoire. Annoncé aux lecteurs d'écran ; n'apparaît qu'après un court délai.
 */
export function SqueletteGraphe() {
  return (
    <div
      role="status"
      aria-busy="true"
      aria-label="Rangement du graphe…"
      className="squelette-zone pointer-events-none absolute inset-0 z-10 flex flex-col items-center overflow-hidden pt-10"
    >
      <Carte />
      <Trait />
      <div className="flex gap-10">
        <Carte />
        <Carte />
      </div>
      <Trait />
      <Carte />
    </div>
  );
}
