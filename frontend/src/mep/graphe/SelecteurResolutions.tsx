import { Plus, Search, Wrench, X } from 'lucide-react';
import { useId, useMemo, useState, type KeyboardEvent } from 'react';
import { classer, surligner, termes } from '../../recherche';
import type { ResolutionType, SystemeCatalogue } from '../../types';
import { cn } from '../../ui/cn';

interface Props {
  resolutions: ResolutionType[];
  catalogue: SystemeCatalogue[];
  /** Résolutions liées à l'étape. */
  liees: string[];
  onChange: (ids: string[]) => void;
  /** Systèmes du catalogue touchés par l'étape (présélectionnés dans les filtres, résultats en tête). */
  systemesEtape: string[];
  /** Création d'une fiche (titre proposé : le texte cherché). */
  onCreer: (titre: string) => void;
  disabled?: boolean;
}

function Surligne({ texte, ts }: { texte: string; ts: string[] }) {
  return <>{surligner(texte, ts).map((s, i) => (s.trouve ? <mark key={i} className="rounded-sm bg-warn-bg px-0.5 text-warn-fg">{s.texte}</mark> : <span key={i}>{s.texte}</span>))}</>;
}

const extrait = (s: string, n = 160) => { const t = s.replace(/```[^\n]*\n?/g, '').replace(/\s+/g, ' ').trim(); return t.length > n ? `${t.slice(0, n)}…` : t; };

/**
 * Sélecteur de résolutions types avec recherche : insensible à la casse et aux accents, tous les mots doivent correspondre
 * (titre, symptômes, diagnostic, système), filtres par système, classement par pertinence, navigation au clavier.
 */
export function SelecteurResolutions({ resolutions, catalogue, liees, onChange, systemesEtape, onCreer, disabled }: Props) {
  const id = useId();
  const [recherche, setRecherche] = useState('');
  const [filtre, setFiltre] = useState<string[]>(() => systemesEtape.filter((s) => resolutions.some((r) => r.systemeCatalogueId === s)));
  const [actif, setActif] = useState(0);
  const [survol, setSurvol] = useState<string | null>(null);
  const nomSysteme = useMemo(() => new Map(catalogue.map((c) => [c.id, c.nom])), [catalogue]);
  const ts = termes(recherche);

  // Puces de filtre : systèmes de l'étape d'abord, puis les autres systèmes qui ont des résolutions.
  const puces = useMemo(() => {
    const avec = new Set(resolutions.map((r) => r.systemeCatalogueId).filter((s): s is string => !!s));
    const autres = [...avec].filter((s) => !systemesEtape.includes(s)).sort((a, b) => (nomSysteme.get(a) ?? '').localeCompare(nomSysteme.get(b) ?? '', 'fr'));
    return [...systemesEtape.filter((s) => avec.has(s) || nomSysteme.has(s)), ...autres];
  }, [resolutions, systemesEtape, nomSysteme]);

  const resultats = useMemo(() => classer(
    resolutions.filter((r) => r.actif && !liees.includes(r.id) && (filtre.length === 0 || r.systemeCatalogueId === null || filtre.includes(r.systemeCatalogueId))),
    recherche,
    (r) => ({
      titre: r.titre,
      textes: [r.symptomes, r.diagnostic, r.systemeCatalogueId ? nomSysteme.get(r.systemeCatalogueId) ?? '' : 'tous les systèmes'],
      prioritaire: r.systemeCatalogueId !== null && systemesEtape.includes(r.systemeCatalogueId),
    }),
  ), [resolutions, liees, filtre, recherche, nomSysteme, systemesEtape]);

  // Correspondances masquées par le filtre de systèmes (proposées en un clic).
  const horsFiltre = useMemo(() => (filtre.length === 0 ? 0 : classer(
    resolutions.filter((r) => r.actif && !liees.includes(r.id) && r.systemeCatalogueId !== null && !filtre.includes(r.systemeCatalogueId)),
    recherche,
    (r) => ({ titre: r.titre, textes: [r.symptomes, r.diagnostic, r.systemeCatalogueId ? nomSysteme.get(r.systemeCatalogueId) ?? '' : ''] }),
  ).length), [resolutions, liees, filtre, recherche, nomSysteme]);

  const index = Math.min(actif, Math.max(0, resultats.length - 1));
  const lier = (r: ResolutionType) => { onChange([...liees, r.id]); setActif(0); };
  const basculerFiltre = (s: string) => { setFiltre(filtre.includes(s) ? filtre.filter((x) => x !== s) : [...filtre, s]); setActif(0); };

  function clavier(e: KeyboardEvent<HTMLInputElement>) {
    if (e.key === 'ArrowDown') { e.preventDefault(); setActif(Math.min(index + 1, resultats.length - 1)); }
    else if (e.key === 'ArrowUp') { e.preventDefault(); setActif(Math.max(index - 1, 0)); }
    else if (e.key === 'Enter') { e.preventDefault(); if (resultats[index]) lier(resultats[index]); }
    else if (e.key === 'Escape' && recherche) { e.preventDefault(); e.stopPropagation(); setRecherche(''); }
  }

  const detail = resultats.find((r) => r.id === survol) ?? resultats[index];

  return (
    <div className="grid gap-3">
      <div className="grid gap-1.5">
        <p className="text-[13px] font-medium">Liées à cette étape ({liees.length})</p>
        {liees.length === 0 ? <p className="text-[13px] text-fg-subtle">Aucune : l’opérateur n’aura pas de procédure proposée en cas d’échec.</p> : (
          <ul className="flex flex-wrap gap-1.5">
            {liees.map((rid) => {
              const r = resolutions.find((x) => x.id === rid);
              return (
                <li key={rid} className="inline-flex max-w-full items-center gap-1.5 rounded-full border border-accent bg-accent-soft py-0.5 pr-1 pl-2.5 text-[13px] font-medium text-accent-text">
                  <Wrench className="size-3.5 shrink-0" aria-hidden />
                  <span className="truncate">{r?.titre ?? 'Résolution inactive'}</span>
                  {!disabled && (
                    <button type="button" aria-label={`Retirer la résolution ${r?.titre ?? ''}`} onClick={() => onChange(liees.filter((x) => x !== rid))} className="rounded-full p-0.5 hover:bg-accent/15">
                      <X className="size-3.5" aria-hidden />
                    </button>
                  )}
                </li>
              );
            })}
          </ul>
        )}
      </div>

      {!disabled && (
        <div className="grid gap-2 rounded-lg border border-line bg-surface-2/40 p-3">
          <label htmlFor={`${id}-q`} className="text-[13px] font-medium">Ajouter une résolution</label>
          <div className="relative">
            <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-fg-subtle" aria-hidden />
            <input
              id={`${id}-q`}
              role="combobox"
              aria-expanded
              aria-controls={`${id}-liste`}
              aria-activedescendant={resultats[index] ? `${id}-${resultats[index].id}` : undefined}
              aria-autocomplete="list"
              value={recherche}
              onChange={(e) => { setRecherche(e.target.value); setActif(0); }}
              onKeyDown={clavier}
              placeholder="Rechercher : titre, symptôme, commande, système…"
              className="h-9 w-full rounded-lg border border-line-strong bg-surface pr-3 pl-9 text-sm shadow-sm focus:border-accent focus:ring-2 focus:ring-accent/30 focus:outline-none"
            />
          </div>
          {puces.length > 0 && (
            <div role="group" aria-label="Filtrer par système" className="flex flex-wrap gap-1.5">
              <button type="button" aria-pressed={filtre.length === 0} onClick={() => { setFiltre([]); setActif(0); }}
                className={cn('rounded-full border px-2.5 py-0.5 text-xs font-medium', filtre.length === 0 ? 'border-accent bg-accent text-accent-fg' : 'border-line-strong text-fg-muted hover:text-fg')}>
                Tous
              </button>
              {puces.map((s) => (
                <button key={s} type="button" aria-pressed={filtre.includes(s)} onClick={() => basculerFiltre(s)}
                  className={cn('rounded-full border px-2.5 py-0.5 text-xs font-medium', filtre.includes(s) ? 'border-accent bg-accent-soft text-accent-text' : 'border-line-strong text-fg-muted hover:text-fg')}>
                  {nomSysteme.get(s) ?? 'Système'}{systemesEtape.includes(s) && <span className="ml-1 opacity-70">· étape</span>}
                </button>
              ))}
            </div>
          )}
          <ul id={`${id}-liste`} role="listbox" aria-label="Résolutions trouvées" className="grid max-h-72 gap-1 overflow-y-auto">
            {resultats.map((r, i) => {
              const meme = r.systemeCatalogueId !== null && systemesEtape.includes(r.systemeCatalogueId);
              return (
                <li
                  key={r.id}
                  id={`${id}-${r.id}`}
                  role="option"
                  aria-selected={i === index}
                  onMouseEnter={() => { setSurvol(r.id); setActif(i); }}
                  onMouseLeave={() => setSurvol(null)}
                  onMouseDown={(e) => e.preventDefault()}
                  onClick={() => lier(r)}
                  className={cn('cursor-pointer rounded-md border px-2.5 py-1.5 text-[13px]', i === index ? 'border-accent bg-accent-soft' : 'border-transparent hover:bg-surface')}
                >
                  <span className="flex flex-wrap items-center gap-1.5">
                    <span className="font-medium"><Surligne texte={r.titre} ts={ts} /></span>
                    <span className="text-xs text-fg-subtle">{r.systemeCatalogueId ? nomSysteme.get(r.systemeCatalogueId) ?? 'Système' : 'Tous les systèmes'}</span>
                    {meme && <span className="rounded border border-accent px-1 text-[10.5px] font-semibold text-accent-text">même système</span>}
                  </span>
                </li>
              );
            })}
            {resultats.length === 0 && <li className="px-2.5 py-1.5 text-[13px] text-fg-subtle">Aucune résolution ne correspond{ts.length ? ` à « ${recherche.trim()} »` : ''}.</li>}
          </ul>
          {horsFiltre > 0 && (
            <button type="button" onClick={() => { setFiltre([]); setActif(0); }} className="justify-self-start text-xs font-medium text-accent-text hover:underline">
              {horsFiltre} autre{horsFiltre > 1 ? 's' : ''} résultat{horsFiltre > 1 ? 's' : ''} dans d’autres systèmes : tout afficher
            </button>
          )}
          {detail && detail.symptomes.trim() && (
            <p aria-live="polite" className="rounded-md bg-surface px-2.5 py-1.5 text-xs text-fg-muted">
              <span className="font-semibold text-fg">Symptômes : </span><Surligne texte={extrait(detail.symptomes)} ts={ts} />
            </p>
          )}
          <p className="text-[11px] text-fg-subtle">↑ ↓ pour parcourir, Entrée pour lier.</p>
          <div>
            <button type="button" onClick={() => onCreer(recherche.trim())} className="inline-flex items-center gap-1.5 text-[13px] font-semibold text-accent-text hover:underline">
              <Plus className="size-4" aria-hidden />Créer une nouvelle fiche de résolution{recherche.trim() ? ` « ${recherche.trim()} »` : ''}
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
