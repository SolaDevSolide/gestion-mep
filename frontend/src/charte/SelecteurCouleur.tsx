import { Check, Pipette, TriangleAlert } from 'lucide-react';
import {
  useEffect,
  useRef,
  useState,
  type CSSProperties,
  type KeyboardEvent,
  type PointerEvent,
  type ReactNode,
} from 'react';
import { cn } from '../ui/cn';
import { Popover, PopoverContent, PopoverTrigger } from '../ui/Popover';
import {
  BLANC,
  contraste,
  formatRapport,
  hexVersRvb,
  normaliserHex,
  rvbVersHex,
  rvbVersTsl,
  rvbVersTsv,
  texteSur,
  tslVersRvb,
  tsvVersRvb,
  type Tsv,
} from './couleurs';

/** Couleurs proposées : soutenues, lisibles en texte blanc. */
export const SUGGESTIONS = [
  '#4f46e5',
  '#0f766e',
  '#0e7490',
  '#1d4ed8',
  '#7c3aed',
  '#c026d3',
  '#be123c',
  '#b45309',
  '#4d7c0f',
  '#1b2a41',
  '#334155',
  '#57534e',
];

// Couleurs récentes : commodité propre à ce navigateur (rien ne dépend d'elles).
const CLE_RECENTES = 'mep.charte.recentes';
function lireRecentes(): string[] {
  try {
    const l: unknown = JSON.parse(window.localStorage.getItem(CLE_RECENTES) ?? '[]');
    return Array.isArray(l) ? l.filter((c): c is string => typeof c === 'string' && normaliserHex(c) === c) : [];
  } catch {
    return [];
  }
}
function ajouterRecente(c: string) {
  try {
    const l = [c, ...lireRecentes().filter((x) => x !== c)].slice(0, 8);
    window.localStorage.setItem(CLE_RECENTES, JSON.stringify(l));
  } catch {
    /* stockage indisponible : pas de couleurs récentes */
  }
}

function Pastille({
  couleur,
  actif,
  onClick,
  titre,
}: {
  couleur: string;
  actif?: boolean;
  onClick: () => void;
  titre?: string;
}) {
  const libelle = titre ?? couleur.toUpperCase();
  return (
    <button
      type="button"
      onClick={onClick}
      title={libelle}
      aria-label={libelle}
      aria-pressed={actif}
      className={cn(
        'relative grid size-6 place-content-center rounded-md border border-black/10 shadow-sm transition-transform hover:scale-110 focus-visible:scale-110',
        actif && 'ring-2 ring-accent ring-offset-2 ring-offset-surface',
      )}
      style={{ background: couleur }}
    >
      {actif && <Check className="size-3.5" style={{ color: texteSur(couleur) }} aria-hidden />}
    </button>
  );
}

/** Zone à glisser (souris, doigt) et réglable au clavier (flèches ; Maj : pas de 10 %). */
function Glisser({
  className,
  style,
  onPosition,
  onClavier,
  children,
  label,
  pourcent,
  valeur,
}: {
  className: string;
  style?: CSSProperties;
  onPosition: (x: number, y: number) => void;
  onClavier: (dx: number, dy: number) => void;
  children: ReactNode;
  label: string;
  /** Position en pour cent (0-100) et sa lecture à voix haute. */
  pourcent: number;
  valeur: string;
}) {
  const ref = useRef<HTMLDivElement>(null);
  const suivre = (e: PointerEvent) => {
    const r = ref.current?.getBoundingClientRect();
    if (!r || r.width === 0) return;
    onPosition(
      Math.min(1, Math.max(0, (e.clientX - r.left) / r.width)),
      r.height ? Math.min(1, Math.max(0, (e.clientY - r.top) / r.height)) : 0,
    );
  };
  const clavier = (e: KeyboardEvent) => {
    const pas = e.shiftKey ? 0.1 : 0.01;
    const d: Record<string, [number, number]> = {
      ArrowLeft: [-pas, 0],
      ArrowRight: [pas, 0],
      ArrowUp: [0, -pas],
      ArrowDown: [0, pas],
    };
    const m = d[e.key];
    if (!m) return;
    e.preventDefault();
    onClavier(m[0], m[1]);
  };
  return (
    <div
      ref={ref}
      role="slider"
      aria-label={label}
      aria-valuemin={0}
      aria-valuemax={100}
      aria-valuenow={Math.round(pourcent)}
      aria-valuetext={valeur}
      tabIndex={0}
      className={cn(
        'relative cursor-crosshair touch-none outline-none select-none focus-visible:ring-2 focus-visible:ring-accent focus-visible:ring-offset-2 focus-visible:ring-offset-surface',
        className,
      )}
      style={style}
      onKeyDown={clavier}
      onPointerDown={(e) => {
        (e.target as HTMLElement).setPointerCapture?.(e.pointerId);
        suivre(e);
      }}
      onPointerMove={(e) => {
        if (e.buttons) suivre(e);
      }}
    >
      {children}
    </div>
  );
}

function Contraste({ texte, fond, libelle }: { texte: string; fond: string; libelle: string }) {
  const r = contraste(texte, fond);
  const ok = r >= 4.5;
  const moyen = r >= 3;
  return (
    <div className="flex min-w-0 flex-1 items-center gap-2 rounded-lg border border-line px-2 py-1.5">
      <span
        className="grid h-6 w-8 shrink-0 place-content-center rounded border border-black/10 text-[12px] font-semibold"
        style={{ background: fond, color: texte }}
        aria-hidden
      >
        Aa
      </span>
      <div className="min-w-0 leading-tight">
        <p className="truncate text-[11px] text-fg-subtle">{libelle}</p>
        <p className="tnum flex items-center gap-1 text-[12px] font-semibold">
          {formatRapport(r)}
          <span
            className={cn(
              'rounded px-1 text-[10px] font-bold',
              ok ? 'bg-ok-bg text-ok-fg' : moyen ? 'bg-warn-bg text-warn-fg' : 'bg-danger-bg text-danger-fg',
            )}
          >
            {ok ? 'AA' : moyen ? 'AA grand' : 'Échec'}
          </span>
        </p>
      </div>
    </div>
  );
}

const FORMATS = { hex: 'HEX', rvb: 'RVB', tsl: 'TSL' } as const;
type Format = keyof typeof FORMATS;

/** Trois champs numériques (RVB 0-255, TSL 0-360 / 0-100 / 0-100). */
function Composantes({
  format,
  valeur,
  onChange,
}: {
  format: 'rvb' | 'tsl';
  valeur: string;
  onChange: (hex: string) => void;
}) {
  const rvb = hexVersRvb(valeur);
  const tsl = rvbVersTsl(rvb);
  const nombres =
    format === 'rvb' ? [rvb.r, rvb.v, rvb.b] : [Math.round(tsl.t), Math.round(tsl.s * 100), Math.round(tsl.l * 100)];
  const libelles = format === 'rvb' ? ['Rouge', 'Vert', 'Bleu'] : ['Teinte', 'Saturation', 'Luminosité'];
  const maximums = format === 'rvb' ? [255, 255, 255] : [360, 100, 100];
  const changer = (i: number, n: number) => {
    const v = [...nombres];
    v[i] = Math.min(maximums[i], Math.max(0, n));
    onChange(
      format === 'rvb'
        ? rvbVersHex({ r: v[0], v: v[1], b: v[2] })
        : rvbVersHex(tslVersRvb({ t: v[0] % 360, s: v[1] / 100, l: v[2] / 100 })),
    );
  };
  return (
    <div className="grid min-w-0 flex-1 grid-cols-3 gap-1">
      {nombres.map((n, i) => (
        <input
          key={libelles[i]}
          type="number"
          aria-label={libelles[i]}
          title={libelles[i]}
          min={0}
          max={maximums[i]}
          value={n}
          onChange={(e) => {
            const x = Number(e.target.value);
            if (e.target.value !== '' && Number.isFinite(x)) changer(i, Math.round(x));
          }}
          className="tnum h-9 w-full min-w-0 rounded-lg border border-line-strong bg-surface px-1 text-center font-mono text-[12px] outline-none focus:ring-2 focus:ring-accent/40"
        />
      ))}
    </div>
  );
}

export interface PropsPanneau {
  valeur: string;
  /** Couleur enregistrée (moitié « avant » de l'échantillon). */
  initiale?: string;
  onChange: (hex: string) => void;
  titre: string;
  /** Couleurs de la charte, proposées en premier. */
  charte?: { libelle: string; couleur: string }[];
  /** Fond sur lequel la couleur sert de texte (contrôle affiché) : blanc, ou la surface du thème sombre. */
  fondTexte?: string;
}

const AUCUNE_COULEUR: NonNullable<PropsPanneau['charte']> = [];

/** Sélecteur de couleur : zone saturation / luminosité, teinte, saisie HEX, RVB ou TSL, pipette, contrastes, suggestions. */
export function PanneauCouleur({
  valeur,
  initiale,
  onChange,
  titre,
  charte = AUCUNE_COULEUR,
  fondTexte = BLANC,
}: PropsPanneau) {
  const [tsv, setTsv] = useState<Tsv>(() => rvbVersTsv(hexVersRvb(valeur)));
  const [saisie, setSaisie] = useState(valeur);
  const [format, setFormat] = useState<Format>('hex');
  const [recentes] = useState(lireRecentes);
  // Couleur changée de l'extérieur (correction, import) : la teinte d'un gris ou du noir est gardée.
  useEffect(() => {
    if (rvbVersHex(tsvVersRvb(tsv)) !== valeur) {
      const n = rvbVersTsv(hexVersRvb(valeur));
      setTsv((t) => (n.s === 0 || n.v === 0 ? { ...n, t: t.t } : n));
    }
    if (normaliserHex(saisie) !== valeur) setSaisie(valeur);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [valeur]);
  const emettre = (n: Tsv) => {
    const borne = {
      t: Math.min(359.9, Math.max(0, n.t)),
      s: Math.min(1, Math.max(0, n.s)),
      v: Math.min(1, Math.max(0, n.v)),
    };
    setTsv(borne);
    const hex = rvbVersHex(tsvVersRvb(borne));
    setSaisie(hex);
    onChange(hex);
  };
  const appliquer = (hex: string) => {
    setSaisie(hex);
    setTsv(rvbVersTsv(hexVersRvb(hex)));
    onChange(hex);
  };
  const choisir = (hex: string) => {
    ajouterRecente(hex);
    appliquer(hex);
  };
  const pur = rvbVersHex(tsvVersRvb({ t: tsv.t, s: 1, v: 1 }));
  const saisieValide = normaliserHex(saisie) !== null;
  const pipette = typeof window !== 'undefined' && 'EyeDropper' in window;
  const texte = texteSur(valeur);

  return (
    <div className="w-[300px] space-y-3 p-3">
      <div className="flex items-center justify-between gap-2">
        <p className="text-[13px] font-semibold">{titre}</p>
        <div
          role="group"
          aria-label="Format de saisie"
          className="inline-flex rounded-md border border-line bg-surface-2 p-0.5 text-[11px] font-medium"
        >
          {(Object.keys(FORMATS) as Format[]).map((f) => (
            <button
              key={f}
              type="button"
              aria-pressed={format === f}
              onClick={() => setFormat(f)}
              className={cn(
                'rounded px-1.5 py-0.5',
                format === f ? 'bg-surface text-fg shadow-sm' : 'text-fg-subtle hover:text-fg',
              )}
            >
              {FORMATS[f]}
            </button>
          ))}
        </div>
      </div>
      <Glisser
        label="Saturation et luminosité"
        pourcent={tsv.s * 100}
        valeur={`saturation ${Math.round(tsv.s * 100)} %, luminosité ${Math.round(tsv.v * 100)} %`}
        className="h-40 overflow-hidden rounded-lg border border-black/10"
        style={{ background: `linear-gradient(to top, #000, transparent), linear-gradient(to right, #fff, ${pur})` }}
        onPosition={(x, y) => emettre({ ...tsv, s: x, v: 1 - y })}
        onClavier={(dx, dy) => emettre({ ...tsv, s: tsv.s + dx, v: tsv.v - dy })}
      >
        <span
          className="pointer-events-none absolute size-4 -translate-x-1/2 -translate-y-1/2 rounded-full border-2 border-white shadow-[0_0_0_1px_rgb(0_0_0/0.3),0_2px_4px_rgb(0_0_0/0.3)]"
          style={{ left: `${tsv.s * 100}%`, top: `${(1 - tsv.v) * 100}%`, background: valeur }}
        />
      </Glisser>
      <Glisser
        label="Teinte"
        pourcent={(tsv.t / 360) * 100}
        valeur={`teinte ${Math.round(tsv.t)}°`}
        className="h-3 rounded-full"
        style={{ background: 'linear-gradient(to right,#f00,#ff0 17%,#0f0 33%,#0ff 50%,#00f 67%,#f0f 83%,#f00)' }}
        onPosition={(x) => emettre({ ...tsv, t: x * 359.9 })}
        onClavier={(dx, dy) => emettre({ ...tsv, t: tsv.t + (dx - dy) * 360 })}
      >
        <span
          className="pointer-events-none absolute top-1/2 size-4 -translate-x-1/2 -translate-y-1/2 rounded-full border-2 border-white shadow-[0_0_0_1px_rgb(0_0_0/0.3),0_1px_3px_rgb(0_0_0/0.3)]"
          style={{ left: `${(tsv.t / 360) * 100}%`, background: pur }}
        />
      </Glisser>
      <div className="flex items-center gap-2">
        <div
          className="flex h-9 w-12 shrink-0 overflow-hidden rounded-lg border border-black/10"
          title={`Avant ${(initiale ?? valeur).toUpperCase()} / après ${valeur.toUpperCase()}`}
        >
          <span className="flex-1" style={{ background: initiale ?? valeur }} />
          <span className="flex-1" style={{ background: valeur }} />
        </div>
        {format === 'hex' ? (
          <label
            className={cn(
              'flex h-9 min-w-0 flex-1 items-center rounded-lg border bg-surface px-2.5 font-mono text-[13px] focus-within:ring-2 focus-within:ring-accent/40',
              saisieValide ? 'border-line-strong' : 'border-danger-bd',
            )}
          >
            <span className="text-fg-subtle">#</span>
            <input
              aria-label="Code hexadécimal"
              aria-invalid={!saisieValide}
              className="min-w-0 flex-1 bg-transparent pl-0.5 uppercase outline-none"
              value={saisie.replace(/^#/, '')}
              maxLength={7}
              spellCheck={false}
              onChange={(e) => {
                setSaisie(e.target.value);
                const n = normaliserHex(e.target.value);
                if (n) {
                  setTsv(rvbVersTsv(hexVersRvb(n)));
                  onChange(n);
                }
              }}
              onBlur={() => {
                if (saisieValide) ajouterRecente(valeur);
                else setSaisie(valeur);
              }}
            />
          </label>
        ) : (
          <Composantes format={format} valeur={valeur} onChange={appliquer} />
        )}
        <button
          type="button"
          disabled={!pipette}
          title={pipette ? 'Prendre une couleur à l’écran' : 'Pipette non disponible dans ce navigateur'}
          aria-label="Pipette"
          className="grid size-9 shrink-0 place-content-center rounded-lg border border-line-strong text-fg-muted hover:bg-surface-2 hover:text-fg disabled:opacity-40"
          onClick={() => {
            const E = (window as unknown as { EyeDropper: new () => { open: () => Promise<{ sRGBHex: string }> } })
              .EyeDropper;
            new E()
              .open()
              .then((r) => choisir(normaliserHex(r.sRGBHex) ?? valeur))
              .catch(() => undefined); // annulé (Échap)
          }}
        >
          <Pipette className="size-4" aria-hidden />
        </button>
      </div>
      {!saisieValide && (
        <p className="flex items-center gap-1 text-[11px] text-danger-fg" role="alert">
          <TriangleAlert className="size-3.5" aria-hidden /> 6 chiffres hexadécimaux attendus, par exemple 0F766E.
        </p>
      )}
      <div className="flex gap-2">
        <Contraste texte={texte} fond={valeur} libelle={texte === BLANC ? 'Texte blanc' : 'Texte foncé'} />
        <Contraste
          texte={valeur}
          fond={fondTexte}
          libelle={fondTexte === BLANC ? 'Sur fond blanc' : 'Sur fond sombre'}
        />
      </div>
      {charte.length > 0 && (
        <div>
          <p className="mb-1.5 text-[11px] font-semibold tracking-wide text-fg-subtle uppercase">Charte</p>
          <div className="flex flex-wrap gap-1.5">
            {charte.map((c) => (
              <Pastille
                key={c.libelle}
                couleur={c.couleur}
                titre={`${c.libelle} ${c.couleur.toUpperCase()}`}
                actif={c.couleur === valeur}
                onClick={() => choisir(c.couleur)}
              />
            ))}
          </div>
        </div>
      )}
      <div>
        <p className="mb-1.5 text-[11px] font-semibold tracking-wide text-fg-subtle uppercase">Suggestions</p>
        <div className="flex flex-wrap gap-1.5">
          {SUGGESTIONS.map((c) => (
            <Pastille key={c} couleur={c} actif={c === valeur} onClick={() => choisir(c)} />
          ))}
        </div>
      </div>
      {recentes.length > 0 && (
        <div>
          <p className="mb-1.5 text-[11px] font-semibold tracking-wide text-fg-subtle uppercase">Récentes</p>
          <div className="flex flex-wrap gap-1.5">
            {recentes.map((c) => (
              <Pastille key={c} couleur={c} actif={c === valeur} onClick={() => choisir(c)} />
            ))}
          </div>
        </div>
      )}
    </div>
  );
}

/** Déclencheur (pastille, lien) qui ouvre le sélecteur dans une bulle. */
export function SelecteurCouleur({
  ouvert,
  onOuvert,
  enfant,
  cote = 'right',
  ...panneau
}: PropsPanneau & {
  ouvert?: boolean;
  onOuvert?: (o: boolean) => void;
  enfant: ReactNode;
  cote?: 'right' | 'bottom';
}) {
  return (
    <Popover open={ouvert} onOpenChange={onOuvert}>
      <PopoverTrigger asChild>{enfant}</PopoverTrigger>
      <PopoverContent
        side={cote}
        align="start"
        sideOffset={12}
        collisionPadding={16}
        className="p-0"
        aria-label={panneau.titre}
        onOpenAutoFocus={(e) => e.preventDefault()}
      >
        <PanneauCouleur {...panneau} />
      </PopoverContent>
    </Popover>
  );
}
