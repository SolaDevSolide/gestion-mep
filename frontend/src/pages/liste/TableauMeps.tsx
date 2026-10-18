// Résultats de la recherche avancée (#59) en tableau (écran large) ou en cartes compactes (téléphone) : état, jour,
// systèmes et environnements, pilote ou rôle de la personne filtrée, échecs survenus, rapport de fin.
import { ArrowDown, ArrowRight, FileText, TriangleAlert } from 'lucide-react';
import { Link, useNavigate } from 'react-router-dom';
import { formatDate, formatHeureLocale } from '../../format';
import { PuceSystemeMep } from '../../mep/SystemeCliquable';
import { BadgeSimplifiee, estSimplifiee } from '../../mep/typeMep';
import { LIBELLE_ROLE, retient, rolesDeLaPersonne, systemesTries, type FiltreMeps } from '../../rechercheMeps';
import { metaEtatMep } from '../../statuts';
import type { MepResume } from '../../types';
import { Avatar } from '../../ui/Avatar';
import { cn } from '../../ui/cn';
import { EnChargement, Squelette } from '../../ui/Squelette';
import { StatusPill } from '../../ui/StatusPill';

export function PastilleEtatMep({ m, court }: { m: Pick<MepResume, 'etat'>; court?: boolean }) {
  const meta = metaEtatMep(m.etat);
  return (
    <StatusPill ton={meta.ton} icone={meta.Icone} taille="sm" anime={m.etat === 'Execution'}>
      {court ? meta.court : meta.libelle}
    </StatusPill>
  );
}

function Jour({ m }: { m: MepResume }) {
  return (
    <>
      <span className={cn('tnum', !m.go && 'text-fg-muted')}>{formatDate(m.jour)}</span>
      <span className="block text-xs text-fg-subtle tnum">
        {m.go ? `Go ${formatHeureLocale(m.go)}` : 'créée, pas de Go'}
      </span>
    </>
  );
}

function Systemes({ m, f }: { m: MepResume; f: FiltreMeps }) {
  if (m.systemes.length === 0) return <span className="text-xs text-fg-subtle">Aucun système</span>;
  return (
    <span className="flex max-w-80 flex-wrap gap-1">
      {systemesTries(f, m.systemes).map((s, i) => (
        <PuceSystemeMep key={`${s.nom}-${i}`} s={s} compacte={f.systemes.length > 0 && !retient(f, s)} />
      ))}
    </span>
  );
}

function Echecs({ m }: { m: MepResume }) {
  if (m.echecsSurvenus === 0) return <span className="text-fg-subtle">—</span>;
  const surSystemes = m.systemes.filter((s) => s.echecs > 0).map((s) => s.nom);
  return (
    <span
      className="inline-flex items-center gap-1 font-semibold text-danger-solid tnum"
      title={`${m.echecsSurvenus} échec${m.echecsSurvenus > 1 ? 's' : ''} d’étape${surSystemes.length ? ` (${surSystemes.join(', ')})` : ''}`}
    >
      <TriangleAlert className="size-3.5" aria-hidden />
      {m.echecsSurvenus}
    </span>
  );
}

function Roles({ m, f }: { m: MepResume; f: FiltreMeps }) {
  const roles = rolesDeLaPersonne(f, m);
  if (!roles) return <span className="text-fg-subtle">—</span>;
  return (
    <span className="text-[13px]">{roles.length ? roles.map((r) => LIBELLE_ROLE[r]).join(', ') : 'Participant'}</span>
  );
}

const th = 'px-3 py-2.5 text-left text-[11px] font-semibold tracking-wider whitespace-nowrap text-fg-subtle uppercase';

export function TableauMeps({ meps, f, nomPersonne }: { meps: MepResume[]; f: FiltreMeps; nomPersonne?: string }) {
  const navigate = useNavigate();
  const parPersonne = Boolean(f.personne);
  return (
    <>
      <div className="mt-4 hidden overflow-hidden rounded-xl border border-line bg-surface shadow-sm md:block">
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <caption className="sr-only">Mises en production trouvées, les plus récentes d’abord</caption>
            <thead className="border-b border-line bg-surface-2/60">
              <tr>
                <th scope="col" className={th}>
                  MEP
                </th>
                <th scope="col" className={th}>
                  État
                </th>
                <th scope="col" className={th} aria-sort="descending">
                  <span className="inline-flex items-center gap-1 text-fg">
                    Jour <ArrowDown className="size-3" aria-hidden />
                  </span>
                </th>
                <th scope="col" className={th}>
                  Systèmes · environnements
                </th>
                <th scope="col" className={th}>
                  {parPersonne ? `Rôle de ${nomPersonne ?? 'la personne'}` : 'Pilote'}
                </th>
                <th scope="col" className={cn(th, 'text-right')}>
                  Échecs
                </th>
                <th scope="col" className={th}>
                  <span className="sr-only">Rapport de fin</span>
                </th>
              </tr>
            </thead>
            <tbody className="divide-y divide-line">
              {meps.map((m) => (
                <tr
                  key={m.id}
                  className="cursor-pointer transition-colors duration-150 hover:bg-surface-2/50"
                  onClick={() => void navigate(`/meps/${m.id}`)}
                >
                  <td className="max-w-72 px-3 py-2.5">
                    <span className="flex items-center gap-1.5 font-semibold">
                      <Link
                        to={`/meps/${m.id}`}
                        className="truncate hover:text-accent-text hover:underline"
                        onClick={(e) => e.stopPropagation()}
                      >
                        {m.outil || 'Sans nom'}
                      </Link>
                      {!estSimplifiee(m) && m.versionCible.trim() && (
                        <>
                          <ArrowRight className="size-3.5 shrink-0 text-fg-subtle" aria-hidden />
                          <span className="font-mono text-[13px]">{m.versionCible}</span>
                        </>
                      )}
                      {estSimplifiee(m) && <BadgeSimplifiee className="text-[10px]" />}
                    </span>
                    {m.objet && <span className="block truncate text-xs text-fg-subtle">{m.objet}</span>}
                  </td>
                  <td className="px-3 py-2.5">
                    <PastilleEtatMep m={m} court />
                  </td>
                  <td className="px-3 py-2.5 whitespace-nowrap">
                    <Jour m={m} />
                  </td>
                  <td className="px-3 py-2.5">
                    <Systemes m={m} f={f} />
                  </td>
                  <td className={cn('px-3 py-2.5', parPersonne ? 'max-w-48 min-w-32' : 'whitespace-nowrap')}>
                    {parPersonne ? (
                      <Roles m={m} f={f} />
                    ) : m.pilote ? (
                      <span className="inline-flex items-center gap-2">
                        <Avatar nom={m.pilote} taille="sm" />
                        {m.pilote}
                      </span>
                    ) : (
                      <span className="text-fg-subtle">Pas de pilote</span>
                    )}
                  </td>
                  <td className="px-3 py-2.5 text-right">
                    <Echecs m={m} />
                  </td>
                  <td className="px-3 py-2.5 text-right">
                    {m.phase === 'Cloturee' && (
                      <Link
                        to={`/meps/${m.id}/rapport`}
                        onClick={(e) => e.stopPropagation()}
                        title="Rapport de fin"
                        className="inline-flex size-7 items-center justify-center rounded-md text-fg-subtle hover:bg-surface-2 hover:text-fg"
                        aria-label={`Rapport de fin de ${m.outil}`}
                      >
                        <FileText className="size-4" aria-hidden />
                      </Link>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
      {/* Téléphone : une carte compacte par MEP. */}
      <ul className="mt-4 space-y-2 md:hidden">
        {meps.map((m) => (
          <li key={m.id} className="relative rounded-xl border border-line bg-surface p-3 shadow-sm">
            <div className="flex items-start justify-between gap-2">
              <Link
                to={`/meps/${m.id}`}
                className="min-w-0 font-semibold after:absolute after:inset-0 after:rounded-xl"
              >
                {m.outil || 'Sans nom'}
                {!estSimplifiee(m) && m.versionCible.trim() && (
                  <span className="font-mono text-[13px] text-fg-muted"> → {m.versionCible}</span>
                )}
              </Link>
              <PastilleEtatMep m={m} court />
            </div>
            <div className="mt-1 flex flex-wrap items-center gap-x-2 text-xs text-fg-subtle">
              <span className="tnum">{formatDate(m.jour)}</span>·
              {parPersonne ? <Roles m={m} f={f} /> : <span>{m.pilote ?? 'Pas de pilote'}</span>}
              {m.echecsSurvenus > 0 && (
                <span className="inline-flex items-center gap-1 font-semibold text-danger-solid">
                  · <TriangleAlert className="size-3" aria-hidden /> {m.echecsSurvenus} échec
                  {m.echecsSurvenus > 1 ? 's' : ''}
                </span>
              )}
            </div>
            <div className="relative z-10 mt-2 w-fit">
              <Systemes m={m} f={f} />
            </div>
          </li>
        ))}
      </ul>
    </>
  );
}

export function SqueletteTableauMeps() {
  return (
    <EnChargement
      libelle="Chargement des mises en production…"
      className="mt-4 overflow-hidden rounded-xl border border-line bg-surface"
    >
      <div className="flex gap-6 border-b border-line bg-surface-2/60 px-3 py-3">
        {['w-24', 'w-14', 'w-12', 'w-40', 'w-16'].map((w) => (
          <Squelette key={w} className={cn('h-3', w)} />
        ))}
      </div>
      {Array.from({ length: 6 }, (_, i) => (
        <div key={i} className="flex items-center gap-6 border-b border-line px-3 py-3 last:border-0">
          <div className="w-48 shrink-0 space-y-1.5 sm:w-64">
            <Squelette className="h-4 w-40" />
            <Squelette className="h-3 w-48" />
          </div>
          <Squelette className="h-5 w-24 shrink-0 rounded-full" />
          <Squelette className="hidden h-4 w-20 sm:block" />
          <div className="hidden flex-1 gap-1 md:flex">
            <Squelette className="h-5 w-16" />
            <Squelette className="h-5 w-14" />
          </div>
          <Squelette className="hidden size-6 rounded-full md:block" />
        </div>
      ))}
    </EnChargement>
  );
}
