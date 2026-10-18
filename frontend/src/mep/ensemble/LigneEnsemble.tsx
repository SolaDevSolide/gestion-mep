import { nomParticipant, segmentsProgression } from '../../domaine';
import type { Ton } from '../../format';
import type { Etape } from '../../types';
import { Avatar } from '../../ui/Avatar';
import { cn } from '../../ui/cn';
import { ProgressBar } from '../../ui/ProgressBar';
import { TON } from '../../ui/tons';
import { useGraphe } from '../graphe/contexteGraphe';
import { avancementSection } from '../graphe/sections';
import { etatSection, LIBELLE_ETAT_VUE, TON_ETAT_VUE } from './calculs';

/** Bordure et fond d'une section selon son état, dans la vue d'ensemble (#61). */
const CADRE_ETAT: Partial<Record<Ton, string>> = {
  ok: 'border-ok-solid bg-ok-bg/50',
  run: 'border-run-solid bg-run-bg/40',
  danger: 'border-danger-solid bg-danger-bg/50',
  warn: 'border-warn-solid bg-warn-bg/50',
  wait: 'border-line-strong bg-surface',
};

export function cadreEnsemble(etapes: Etape[], bloquees: ReadonlyMap<string, Etape[]>): string {
  const etat = etatSection(etapes, bloquees);
  return cn(CADRE_ETAT[TON_ETAT_VUE[etat]], etat === 'bloquee' && 'border-dashed');
}

/** En-tête d'une section dans la vue d'ensemble : barre d'avancement, état et qui y travaille en ce moment. */
export function LigneEnsemble({ etapes, bloquees }: { etapes: Etape[]; bloquees: ReadonlyMap<string, Etape[]> }) {
  const g = useGraphe();
  const etat = etatSection(etapes, bloquees);
  const av = avancementSection(etapes);
  const actifs = etapes.filter((e) => e.etat === 'EnCours' || e.disponibilite.statut === 'ResolutionEnCours');
  return (
    <div className="grid gap-1">
      <div className="flex items-center gap-2">
        <ProgressBar className="flex-1" hauteur="h-1.5" segments={segmentsProgression(etapes)} />
        <span className="text-xs font-semibold text-fg tnum">
          {av.reussies}/{av.total}
        </span>
      </div>
      <div className="flex min-w-0 flex-wrap items-center gap-1.5 text-[11.5px]">
        <span className={cn('font-semibold', TON[TON_ETAT_VUE[etat]].texte)}>{LIBELLE_ETAT_VUE[etat]}</span>
        {actifs.map((e) => {
          const nom = nomParticipant(g.mep, e.executantId);
          return nom ? (
            <span key={e.id} className="inline-flex items-center gap-1 text-fg-muted">
              · <Avatar nom={nom} taille="xs" /> {nom}
            </span>
          ) : null;
        })}
      </div>
    </div>
  );
}
