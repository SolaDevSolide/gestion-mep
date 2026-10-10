import { Maximize2, Minimize2, X } from 'lucide-react';
import { createContext, useCallback, useContext, useRef, useState, type CSSProperties, type KeyboardEvent, type PointerEvent, type ReactNode } from 'react';
import { BoutonIcone } from '../../ui/Button';
import { Card } from '../../ui/Card';
import { cn } from '../../ui/cn';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '../../ui/Tabs';

const CLE = 'mep.panneau.largeur';
export const LARGEUR_DEFAUT = 480;
const MIN = 360;
const max = () => Math.max(MIN, Math.min(960, Math.round((typeof window === 'undefined' ? 1600 : window.innerWidth) * 0.7)));
const borner = (l: number) => Math.round(Math.min(max(), Math.max(MIN, l)));

function lire(): number {
  try {
    const v = Number(window.localStorage.getItem(CLE));
    return Number.isFinite(v) && v >= MIN ? v : LARGEUR_DEFAUT;
  } catch {
    return LARGEUR_DEFAUT;
  }
}
function ecrire(l: number) {
  try { window.localStorage.setItem(CLE, String(l)); } catch { /* stockage indisponible */ }
}

interface ContexteLateral { large: boolean; basculerLarge: () => void }
const Contexte = createContext<ContexteLateral>({ large: false, basculerLarge: () => undefined });
export const usePanneauLarge = () => useContext(Contexte);

/**
 * Panneau de droite du graphe : largeur réglable (poignée, mémorisée), mode « large » (≈ 65 % de l'écran).
 * Renvoie le style de la grille (graphe | panneau) et le conteneur du panneau.
 */
export function usePanneauLateral() {
  const [largeur, setLargeur] = useState(lire);
  const [large, setLarge] = useState(false);
  const basculerLarge = useCallback(() => setLarge((x) => !x), []);
  const styleGrille = { '--col-panneau': large ? 'min(65vw, 1400px)' : `${largeur}px` } as CSSProperties;
  const classesGrille = 'grid items-start gap-4 xl:grid-cols-[minmax(0,1fr)_var(--col-panneau)]';
  const changerLargeur = (l: number) => { const b = borner(l); setLargeur(b); ecrire(b); };
  return { largeur, large, basculerLarge, changerLargeur, styleGrille, classesGrille };
}

/** Conteneur collant du panneau, avec la poignée de redimensionnement sur son bord gauche. */
export function ConteneurPanneau({ etiquette, largeur, large, basculerLarge, changerLargeur, children }: {
  etiquette: string; largeur: number; large: boolean; basculerLarge: () => void; changerLargeur: (l: number) => void; children: ReactNode;
}) {
  const depart = useRef<{ x: number; l: number } | null>(null);
  const [actif, setActif] = useState(false);

  function appuyer(e: PointerEvent<HTMLDivElement>) {
    if (large) return;
    e.preventDefault();
    e.currentTarget.setPointerCapture?.(e.pointerId);
    depart.current = { x: e.clientX, l: largeur };
    setActif(true);
  }
  function bouger(e: PointerEvent<HTMLDivElement>) {
    const d = depart.current;
    if (d) changerLargeur(d.l + (d.x - e.clientX));
  }
  function lacher() { depart.current = null; setActif(false); }
  function clavier(e: KeyboardEvent<HTMLDivElement>) {
    if (e.key === 'ArrowLeft') { e.preventDefault(); changerLargeur(largeur + 32); }
    else if (e.key === 'ArrowRight') { e.preventDefault(); changerLargeur(largeur - 32); }
  }

  return (
    <Contexte.Provider value={{ large, basculerLarge }}>
      <aside aria-label={etiquette} className="relative min-w-0 xl:sticky xl:top-[calc(var(--entete-h,12rem)+1rem)]">
        {!large && (
          <div
            role="separator"
            aria-orientation="vertical"
            aria-label="Largeur du panneau (flèches gauche et droite)"
            aria-valuenow={largeur}
            aria-valuemin={MIN}
            aria-valuemax={max()}
            tabIndex={0}
            onPointerDown={appuyer}
            onPointerMove={bouger}
            onPointerUp={lacher}
            onPointerCancel={lacher}
            onKeyDown={clavier}
            title="Glisser pour élargir ou réduire le panneau"
            className="group absolute top-0 bottom-0 -left-3 z-10 hidden w-3 cursor-col-resize touch-none items-center justify-center xl:flex"
          >
            <span className={cn('h-14 w-1 rounded-full bg-line-strong transition-colors group-hover:bg-accent group-focus-visible:bg-accent', actif && 'bg-accent')} />
          </div>
        )}
        {children}
      </aside>
    </Contexte.Provider>
  );
}

/** Bouton Agrandir / Réduire du panneau (visible sur grand écran). */
export function BoutonAgrandir() {
  const { large, basculerLarge } = usePanneauLarge();
  return (
    <BoutonIcone label={large ? 'Réduire le panneau' : 'Agrandir le panneau'} taille="sm" className="hidden xl:inline-flex" onClick={basculerLarge} aria-pressed={large}>
      {large ? <Minimize2 className="size-4" aria-hidden /> : <Maximize2 className="size-4" aria-hidden />}
    </BoutonIcone>
  );
}

export interface OngletPanneau {
  valeur: string;
  libelle: string;
  compteur?: number;
  /** Pastille : l'onglet contient une anomalie à corriger. */
  alerte?: string | false;
  contenu: ReactNode;
}

/**
 * Structure commune des panneaux d'étape : en-tête fixe, onglets (avec compteurs et pastilles), contenu qui défile,
 * pied fixe.
 */
export function StructurePanneau({ entete, onglets, onglet, onOnglet, pied, onFermer }: {
  entete: ReactNode;
  onglets: OngletPanneau[];
  onglet: string;
  onOnglet: (o: string) => void;
  pied?: ReactNode;
  onFermer?: () => void;
}) {
  const courant = onglets.some((o) => o.valeur === onglet) ? onglet : onglets[0].valeur;
  return (
    <Card className="flex min-h-0 flex-col overflow-hidden xl:max-h-[calc(100vh-var(--entete-h,12rem)-2rem)]">
      <Tabs value={courant} onValueChange={onOnglet} className="flex min-h-0 flex-1 flex-col">
        <div className="shrink-0 border-b border-line">
          <div className="flex items-start gap-2 px-5 pt-4 pb-3">
            <div className="min-w-0 flex-1">{entete}</div>
            <div className="flex shrink-0 items-center gap-0.5">
              <BoutonAgrandir />
              {onFermer && <BoutonIcone label="Fermer le panneau" taille="sm" onClick={onFermer}><X className="size-4" aria-hidden /></BoutonIcone>}
            </div>
          </div>
          <TabsList aria-label="Sections de l’étape" className="px-3">
            {onglets.map((o) => (
              <TabsTrigger key={o.valeur} value={o.valeur} className="h-9 px-2.5 text-[13px]">
                {o.libelle}
                {o.compteur !== undefined && <span className="rounded-full bg-surface-2 px-1.5 text-[11px] font-semibold text-fg-muted tnum">{o.compteur}</span>}
                {o.alerte && <span className="size-2 rounded-full bg-warn-solid" role="img" aria-label={o.alerte} title={o.alerte} />}
              </TabsTrigger>
            ))}
          </TabsList>
        </div>
        <div className="min-h-0 flex-1 overflow-y-auto">
          {onglets.map((o) => (
            <TabsContent key={o.valeur} value={o.valeur} className="grid gap-6 px-5 py-5 outline-none">{o.contenu}</TabsContent>
          ))}
        </div>
        {pied && <div className="shrink-0 border-t border-line bg-surface px-5 py-3">{pied}</div>}
      </Tabs>
    </Card>
  );
}

/** Section d'un onglet : titre, description facultative, contenu. */
export function Section({ titre, description, children, action }: { titre: string; description?: ReactNode; children: ReactNode; action?: ReactNode }) {
  return (
    <section className="grid gap-3">
      <div className="flex items-end justify-between gap-3">
        <div>
          <h3 className="text-sm font-semibold">{titre}</h3>
          {description && <p className="mt-0.5 text-xs text-fg-subtle">{description}</p>}
        </div>
        {action}
      </div>
      {children}
    </section>
  );
}
