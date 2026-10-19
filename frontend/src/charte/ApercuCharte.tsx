import { Activity, CircleCheck, CircleX, Copy, Layers, Play, Rocket, ShieldUser, Wrench } from 'lucide-react';
import { cn } from '../ui/cn';
import { StatusPill } from '../ui/StatusPill';
import { paletteExcel, styleApercu, type Charte, type Mode } from './charte';

const MENU = [
  { Icone: Rocket, libelle: 'Mises en production', actif: true },
  { Icone: Activity, libelle: 'Suivi en direct', actif: false },
  { Icone: Layers, libelle: 'Systèmes', actif: false },
  { Icone: Wrench, libelle: 'Résolutions', actif: false },
  { Icone: ShieldUser, libelle: 'Console', actif: false },
];

/**
 * Aperçu de l'application avec la charte, dans le thème demandé, indépendant du thème de la console : les jetons de
 * couleur de la charte sont posés en variables CSS sur l'aperçu seul.
 */
export function ApercuAppli({ charte, mode }: { charte: Charte; mode: Mode }) {
  return (
    <div
      style={styleApercu(charte, mode)}
      role="img"
      aria-label={`Aperçu de l’application, thème ${mode}`}
      className={cn(
        'flex h-[372px] overflow-hidden rounded-lg border border-line bg-bg text-xs text-fg',
        mode === 'sombre' && 'dark',
      )}
    >
      <div className="flex w-[132px] shrink-0 flex-col gap-0.5 border-r border-nav-line bg-nav-bg p-2 text-nav-fg">
        <div className="mb-2 flex items-center gap-1.5 px-1 py-1">
          <span className="grid size-5 place-content-center rounded-md bg-accent text-accent-fg">
            <Rocket className="size-3" aria-hidden />
          </span>
          <span className="text-xs font-semibold">Gestion MEP</span>
        </div>
        {MENU.map(({ Icone, libelle, actif }) => (
          <span
            key={libelle}
            className={cn(
              'flex h-6 items-center gap-1.5 rounded-md px-1.5 text-2xs font-medium',
              actif ? 'bg-nav-active text-nav-active-fg' : 'text-nav-muted',
            )}
          >
            <Icone className="size-3 shrink-0" aria-hidden />
            <span className="truncate">{libelle}</span>
          </span>
        ))}
      </div>
      <div className="min-w-0 flex-1 space-y-2.5 p-3">
        <div className="flex items-center gap-2">
          <p className="text-sm font-semibold tracking-tight">Paiement mobile → 4.2</p>
          <StatusPill ton="run" icone={Play} taille="sm">
            Exécution
          </StatusPill>
        </div>
        <div className="flex flex-wrap items-center gap-1.5">
          <span className="inline-flex h-6 items-center rounded-md bg-accent px-2 text-2xs font-medium text-accent-fg shadow-sm">
            Passer en recette
          </span>
          <span className="inline-flex h-6 items-center gap-1 rounded-md border border-line-strong bg-surface px-2 text-2xs font-medium shadow-sm">
            <Copy className="size-3" aria-hidden /> Dupliquer
          </span>
          <span className="text-2xs font-medium text-accent-text underline underline-offset-2">Voir le journal</span>
        </div>
        <div className="flex gap-3 border-b border-line text-2xs font-medium">
          <span className="pb-1 text-fg-subtle">Vue d’ensemble</span>
          <span className="border-b-2 border-accent pb-1 text-fg">Plan</span>
          <span className="pb-1 text-fg-subtle">Exécution</span>
        </div>
        <div className="relative rounded-lg border border-line bg-surface p-2">
          <div className="relative rounded-xl border-2 border-dashed border-tertiaire/60 bg-tertiaire-soft/40 px-2 pt-4 pb-2">
            <span className="absolute -top-2.5 left-2 flex items-center gap-1 rounded-full border-2 border-tertiaire/60 bg-surface px-2 text-2xs font-semibold">
              Sauvegardes <span className="font-normal text-fg-muted">2 ét.</span>
            </span>
            <span className="absolute -top-1.5 left-1/2 -translate-x-1/2 rounded-full bg-tertiaire-soft px-1.5 text-2xs font-semibold text-tertiaire-text ring-1 ring-tertiaire">
              Début
            </span>
            <div className="mt-1 grid grid-cols-2 gap-2">
              {(
                [
                  ['BDD', 'Sauvegarde des transactions', 'ok'],
                  ['API', 'Sauvegarde du service', 'danger'],
                ] as const
              ).map(([s, t, e]) => (
                <div key={s} className="rounded-md border border-line bg-surface p-1.5 shadow-sm">
                  <p className="text-2xs font-semibold text-fg-subtle">
                    <span className="mr-1 inline-block size-1.5 rounded-full bg-tertiaire" />
                    {s} · Prod
                  </p>
                  <p className="truncate text-2xs font-semibold">{t}</p>
                  <StatusPill ton={e} icone={e === 'ok' ? CircleCheck : CircleX} taille="sm" className="mt-1">
                    {e === 'ok' ? 'Réussie' : 'Échec'}
                  </StatusPill>
                </div>
              ))}
            </div>
          </div>
        </div>
        <div className="flex flex-wrap gap-1">
          <StatusPill ton="ok" taille="sm">
            Réussie
          </StatusPill>
          <StatusPill ton="run" taille="sm">
            En cours
          </StatusPill>
          <StatusPill ton="warn" taille="sm">
            Résolution
          </StatusPill>
          <StatusPill ton="danger" taille="sm">
            Échec
          </StatusPill>
          <StatusPill ton="wait" taille="sm">
            À venir
          </StatusPill>
        </div>
        <div className="flex items-center gap-2">
          <span className="flex h-7 flex-1 items-center rounded-md border border-accent bg-surface px-2 text-2xs text-fg-muted ring-2 ring-accent/30">
            Champ en cours de saisie
          </span>
          <span className="size-4 rounded-[4px] bg-accent text-accent-fg">
            <svg viewBox="0 0 16 16" className="size-4" aria-hidden>
              <path d="M4 8.5l2.5 2.5L12 5.5" stroke="currentColor" strokeWidth="2" fill="none" />
            </svg>
          </span>
        </div>
      </div>
    </div>
  );
}

const LIGNES: [string, string, string, string, 'succes' | 'enCours' | 'echec'][] = [
  ['E0001', 'Sauvegarde de la table des transactions', 'BDD', 'Alice', 'succes'],
  ['E0002', 'Sauvegarde du service paiement', 'API', 'Bob', 'succes'],
  ['E0004', 'Déploiement du service paiement 4.2', 'API', 'Bob', 'enCours'],
  ['E0005', 'Publication de l’écran de paiement', 'WEB', 'Claire', 'echec'],
];
const LIBELLE_ETAT = { succes: 'Réussie', enCours: 'En cours', echec: 'Échec' };

/**
 * Aperçu d'un classeur exporté, avec la palette que le serveur donnera aux exports (même calcul, thème clair) : titre en
 * secondaire, en-têtes de tableau et liens en primaire, intertitre sur le fond pâle de la tertiaire.
 */
export function ApercuExcel({ charte }: { charte: Charte }) {
  const p = paletteExcel(charte);
  const colonnes = ['A', 'B', 'C', 'D', 'E'];
  const cellule = 'border border-[#d4d4dc] px-1.5 py-1 truncate';
  const numero = 'border border-[#e0e0e0] bg-[#f3f3f3] text-center text-2xs text-[#666]';
  return (
    <div
      role="img"
      aria-label="Aperçu d’un classeur Excel exporté"
      className="h-[372px] overflow-hidden rounded-lg border border-line bg-white font-[Calibri,Carlito,sans-serif] text-2xs text-[#16161a]"
    >
      <div className="flex items-center gap-2 border-b border-[#d0d0d0] bg-[#f3f3f3] px-2 py-1 text-2xs text-[#555]">
        <span className="rounded border border-[#ccc] bg-white px-1.5">A1</span>
        <span className="italic">fx</span>
        <span>Paiement mobile 4.2 · Étapes</span>
      </div>
      <table className="w-full table-fixed border-collapse">
        <colgroup>
          <col className="w-6" />
          <col className="w-14" />
          <col />
          <col className="w-12" />
          <col className="w-14" />
          <col className="w-16" />
        </colgroup>
        <thead>
          <tr className="bg-[#f3f3f3] text-center text-2xs text-[#666]">
            <th className="border border-[#e0e0e0]" />
            {colonnes.map((c) => (
              <th key={c} className="border border-[#e0e0e0] font-normal">
                {c}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          <tr>
            <td className={numero}>1</td>
            <td colSpan={5} className="px-1.5 pt-1.5 text-base font-bold" style={{ color: p.texteTitre }}>
              Paiement mobile 4.2 · Étapes
            </td>
          </tr>
          <tr>
            <td className={numero}>2</td>
            <td colSpan={5} className="px-1.5 pb-1.5 text-[#6b6b78]">
              Exporté le 17/10/2026 à 12:40 · 14 étapes
            </td>
          </tr>
          <tr>
            <td className={numero}>3</td>
            {['Réf.', 'Étape', 'Système', 'Qui', 'État'].map((h) => (
              <td
                key={h}
                className={cn(cellule, 'font-bold')}
                style={{ background: p.fondEnTete, color: p.texteEnTete }}
              >
                {h}
              </td>
            ))}
          </tr>
          <tr>
            <td className={numero}>4</td>
            <td
              colSpan={5}
              className={cn(cellule, 'font-bold')}
              style={{ background: p.fondSection, color: p.texteSection }}
            >
              Section · Sauvegardes
            </td>
          </tr>
          {LIGNES.map((l, i) => (
            <tr key={l[0]}>
              <td className={numero}>{i + 5}</td>
              <td className={cellule}>
                <span className="underline" style={{ color: p.texteLien }}>
                  {l[0]}
                </span>
              </td>
              <td className={cellule}>{l[1]}</td>
              <td className={cellule}>{l[2]}</td>
              <td className={cellule}>{l[3]}</td>
              <td className={cellule} style={{ background: p[l[4]][0], color: p[l[4]][1] }}>
                {LIBELLE_ETAT[l[4]]}
              </td>
            </tr>
          ))}
          <tr>
            <td className={numero}>9</td>
            <td className={cellule}>E0006</td>
            <td className={cellule}>Bascule des paiements</td>
            <td className={cellule}>API</td>
            <td className={cn(cellule, 'bg-[#fff7d6]')} />
            <td className={cn(cellule, 'bg-[#fff7d6]')} />
          </tr>
          {[10, 11, 12].map((n) => (
            <tr key={n}>
              <td className={numero}>{n}</td>
              <td colSpan={5} className="h-5" />
            </tr>
          ))}
        </tbody>
      </table>
      <div className="mt-2 flex items-center gap-0.5 border-t border-[#d0d0d0] bg-[#f3f3f3] px-2 text-2xs">
        <span
          className="border-b-2 bg-white px-2 py-0.5 font-semibold"
          style={{ borderColor: p.primaire, color: p.texteTitre }}
        >
          Étapes
        </span>
        <span className="px-2 py-0.5 text-[#555]">Retour arrière</span>
        <span className="px-2 py-0.5 text-[#555]">Recette</span>
      </div>
    </div>
  );
}
