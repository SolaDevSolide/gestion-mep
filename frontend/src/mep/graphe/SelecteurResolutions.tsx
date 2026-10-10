import { Plus, Search, Wrench, X } from 'lucide-react';
import { useId, useMemo, useState, type KeyboardEvent } from 'react';
import { classer, concerne, memeSysteme, surligner, termes } from '../../recherche';
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
  /** Nombre de résultats affichés avant « voir plus ». */
  limite?: number;
}

function Surligne({ texte, ts }: { texte: string; ts: string[] }) {
  return <>{surligner(texte, ts).map((s, i) => (s.trouve ? <mark key={i} className="rounded-sm bg-warn-bg px-0.5 text-warn-fg">{s.texte}</mark> : <span key={i}>{s.texte}</span>))}</>;
}

const extrait = (s: string, n = 140) => { const t = s.replace(/```[^\n]*\n?/g, '').replace(/\s+/g, ' ').trim(); return t.length > n ? `${t.slice(0, n)}…` : t; };


/**
 * Sélecteur de résolutions types avec recherche : insensible à la casse et aux accents, tous les mots doivent correspondre
 * (titre, symptômes, diagnostic, systèmes), filtres par système, classement par pertinence, navigation au clavier.
 */
export function SelecteurResolutions({ resolutions, catalogue, liees, onChange, systemesEtape, onCreer, disabled, limite = 4 }: Props) {
  const id = useId();
  const [recherche, setRecherche] = useState('');
  const [filtre, setFiltre] = useState<string[]>(() => systemesEtape.filter((s) => resolutions.some((r) => r.systemeCatalogueIds.includes(s))));
  const [actif, setActif] = useState(0);
  const [survol, setSurvol] = useState<string | null>(null);
  const [tout, setTout] = useState(false);
  const [focus, setFocus] = useState(false);
  const nomSysteme = useMemo(() => new Map(catalogue.map((c) => [c.id, c.nom])), [catalogue]);
  const noms = (r: ResolutionType) => (r.systemeCatalogueIds.length ? r.systemeCatalogueIds.map((s) => nomSysteme.get(s) ?? 'Système') : ['Tous les systèmes']);
  const ts = termes(recherche);

  // Puces de filtre : systèmes de l'étape d'abord, puis les autres systèmes qui ont des résolutions.
  const puces = useMemo(() => {
    const avec = new Set(resolutions.flatMap((r) => r.systemeCatalogueIds));
    const autres = [...avec].filter((s) => !systemesEtape.includes(s)).sort((a, b) => (nomSysteme.get(a) ?? '').localeCompare(nomSysteme.get(b) ?? '', 'fr'));
    return [...systemesEtape.filter((s) => avec.has(s) || nomSysteme.has(s)), ...autres];
  }, [resolutions, systemesEtape, nomSysteme]);

  const versDocument = (r: ResolutionType) => ({
    titre: r.titre,
    textes: [r.symptomes, r.diagnostic, ...(r.systemeCatalogueIds.length ? r.systemeCatalogueIds.map((s) => nomSysteme.get(s) ?? '') : ['tous les systèmes'])],
    prioritaire: memeSysteme(r.systemeCatalogueIds, systemesEtape),
  });
  const candidates = resolutions.filter((r) => r.actif && !liees.includes(r.id));
  const resultats = classer(candidates.filter((r) => filtre.length === 0 || concerne(r.systemeCatalogueIds, filtre)), recherche, versDocument);
  // Correspondances masquées par le filtre de systèmes (proposées en un clic).
  const horsFiltre = filtre.length === 0 ? 0 : classer(candidates.filter((r) => !concerne(r.systemeCatalogueIds, filtre)), recherche, versDocument).length;
  const visibles = tout ? resultats : resultats.slice(0, limite);

  const index = Math.min(actif, Math.max(0, visibles.length - 1));
  const lier = (r: ResolutionType) => { onChange([...liees, r.id]); setActif(0); };
  const basculerFiltre = (s: string) => { setFiltre(filtre.includes(s) ? filtre.filter((x) => x !== s) : [...filtre, s]); setActif(0); };

  function clavier(e: KeyboardEvent<HTMLInputElement>) {
    if (e.key === 'ArrowDown') { e.preventDefault(); setActif(Math.min(index + 1, visibles.length - 1)); }
    else if (e.key === 'ArrowUp') { e.preventDefault(); setActif(Math.max(index - 1, 0)); }
    else if (e.key === 'Enter') { e.preventDefault(); if (visibles[index]) lier(visibles[index]); }
    else if (e.key === 'Escape' && recherche) { e.preventDefault(); e.stopPropagation(); setRecherche(''); }
  }

  // Aperçu des symptômes : au survol, ou pour l'option active pendant la saisie au clavier.
  const detail = visibles.find((r) => r.id === survol) ?? (focus ? visibles[index] : undefined);

  return (
    <div className="grid gap-2">
      {liees.length === 0 ? <p className="text-[13px] text-fg-subtle">Aucune résolution liée.</p> : (
        <ul className="flex flex-wrap gap-1.5" aria-label="Résolutions liées">
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

      {!disabled && (
        <>
          <div className="relative">
            <Search className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-fg-subtle" aria-hidden />
            <input
              id={`${id}-q`}
              role="combobox"
              aria-label="Ajouter une résolution"
              aria-expanded
              aria-controls={`${id}-liste`}
              aria-activedescendant={visibles[index] ? `${id}-${visibles[index].id}` : undefined}
              aria-autocomplete="list"
              value={recherche}
              onChange={(e) => { setRecherche(e.target.value); setActif(0); setTout(false); }}
              onKeyDown={clavier}
              onFocus={() => setFocus(true)}
              onBlur={() => setFocus(false)}
              placeholder="Rechercher une résolution…"
              title="↑ ↓ pour parcourir, Entrée pour lier"
              className="h-8 w-full rounded-lg border border-line-strong bg-surface pr-3 pl-8 text-sm shadow-sm focus:border-accent focus:ring-2 focus:ring-accent/30 focus:outline-none"
            />
          </div>
          {puces.length > 0 && (
            <div role="group" aria-label="Filtrer par système" className="flex flex-wrap gap-1">
              <button type="button" aria-pressed={filtre.length === 0} onClick={() => { setFiltre([]); setActif(0); }}
                className={cn('rounded-full border px-2 py-px text-xs font-medium', filtre.length === 0 ? 'border-accent bg-accent text-accent-fg' : 'border-line-strong text-fg-muted hover:text-fg')}>
                Tous
              </button>
              {puces.map((s) => (
                <button key={s} type="button" aria-pressed={filtre.includes(s)} onClick={() => basculerFiltre(s)}
                  className={cn('rounded-full border px-2 py-px text-xs font-medium', filtre.includes(s) ? 'border-accent bg-accent-soft text-accent-text' : 'border-line-strong text-fg-muted hover:text-fg')}>
                  {nomSysteme.get(s) ?? 'Système'}
                </button>
              ))}
            </div>
          )}
          <ul id={`${id}-liste`} role="listbox" aria-label="Résolutions trouvées" className="grid gap-0.5">
            {visibles.map((r, i) => {
              const meme = memeSysteme(r.systemeCatalogueIds, systemesEtape);
              return (
                // Le clavier est géré par le champ de recherche (aria-activedescendant), comme pour toute liste ARIA « listbox ».
                // eslint-disable-next-line jsx-a11y/click-events-have-key-events
                <li
                  key={r.id}
                  id={`${id}-${r.id}`}
                  role="option"
                  aria-selected={i === index}
                  onMouseEnter={() => { setSurvol(r.id); setActif(i); }}
                  onMouseLeave={() => setSurvol(null)}
                  onMouseDown={(e) => e.preventDefault()}
                  onClick={() => lier(r)}
                  className={cn('cursor-pointer rounded-md border px-2 py-1 text-[13px]', i === index ? 'border-accent bg-accent-soft' : 'border-transparent hover:bg-surface-2')}
                >
                  <span className="block font-medium"><Surligne texte={r.titre} ts={ts} /></span>
                  <span className="flex flex-wrap items-center gap-1 text-[11px] text-fg-subtle">
                    {noms(r).join(' · ')}
                    {meme && <span className="rounded border border-accent px-1 font-semibold text-accent-text">même système</span>}
                  </span>
                </li>
              );
            })}
            {resultats.length === 0 && <li className="px-2 py-1 text-[13px] text-fg-subtle">Aucun résultat.</li>}
          </ul>
          <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-xs">
            {resultats.length > limite && (
              <button type="button" onClick={() => setTout(!tout)} className="font-medium text-accent-text hover:underline">
                {tout ? 'Voir moins' : `Voir plus (${resultats.length - limite})`}
              </button>
            )}
            {horsFiltre > 0 && (
              <button type="button" onClick={() => { setFiltre([]); setActif(0); }} className="font-medium text-accent-text hover:underline">
                {horsFiltre} autre{horsFiltre > 1 ? 's' : ''} dans d’autres systèmes
              </button>
            )}
            <button type="button" onClick={() => onCreer(recherche.trim())} className="ml-auto inline-flex items-center gap-1 font-semibold text-accent-text hover:underline">
              <Plus className="size-3.5" aria-hidden />Nouvelle fiche de résolution{recherche.trim() ? ` « ${recherche.trim()} »` : ''}
            </button>
          </div>
          {detail && detail.symptomes.trim() && (
            <p aria-live="polite" className="rounded-md bg-surface-2/60 px-2 py-1 text-xs text-fg-muted">
              <Surligne texte={extrait(detail.symptomes)} ts={ts} />
            </p>
          )}
        </>
      )}
    </div>
  );
}
