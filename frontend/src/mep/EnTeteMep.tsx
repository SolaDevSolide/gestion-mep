import { ArrowRight, CalendarDays, ChevronLeft, Copy, LayoutTemplate } from 'lucide-react';
import { useEffect, useRef, useState, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { etapesDuPlan, nomParticipant, planActif, segmentsProgression } from '../domaine';
import { formatDate, formatHeure } from '../format';
import { metaPhase } from '../statuts';
import type { Mep } from '../types';
import { Avatar } from '../ui/Avatar';
import { Button } from '../ui/Button';
import { cn } from '../ui/cn';
import { nommerSegment, ProgressBar } from '../ui/ProgressBar';
import { TON } from '../ui/tons';
import { Tooltip } from '../ui/Tooltip';
import { Badge, StatusPill } from '../ui/StatusPill';
import { BoutonCloturer, BoutonDeclencherRepli, BoutonGoNoGo, BoutonPasserRecette, BoutonRevenirPreparation } from './ActionsPilote';
import { DuplicationDialog } from './DuplicationDialog';
import { CompteARebours } from './CompteARebours';
import { PlanCheck } from './PlanCheck';

/** En-tête collant : identité, phase, compte à rebours, progression, actions du pilote, onglets. */
export function EnTeteMep({ mep, onglets }: { mep: Mep; onglets: ReactNode }) {
  const meta = metaPhase(mep.phase, mep.issue);
  const pilote = nomParticipant(mep, mep.piloteId);
  const plan = planActif(mep.phase) ?? 'Deploiement';
  const etapes = etapesDuPlan(mep, plan);
  const termine = etapes.filter((e) => e.etat === 'Reussie').length;
  const pilotes = mep.droits.estPilote;
  const [duplication, setDuplication] = useState(false);
  const ref = useRef<HTMLElement>(null);
  useEffect(() => {
    const el = ref.current;
    if (!el || typeof ResizeObserver === 'undefined') return;
    const maj = () => document.documentElement.style.setProperty('--entete-h', `${el.offsetHeight}px`);
    maj();
    const ro = new ResizeObserver(maj);
    ro.observe(el);
    return () => { ro.disconnect(); document.documentElement.style.removeProperty('--entete-h'); };
  }, []);

  return (
    <header ref={ref} className="z-30 border-b border-line bg-surface/95 backdrop-blur-md [@media(min-height:760px)]:sticky [@media(min-height:760px)]:top-0">
      <div className="px-6 pt-3">
        <div className="flex flex-wrap items-center justify-between gap-x-6 gap-y-2">
          <div className="min-w-0">
            <div className="flex flex-wrap items-center gap-x-3 gap-y-1.5">
              <Tooltip content="Retour à la liste des MEP" cote="bottom">
                <Link to="/" aria-label="Retour à la liste des mises en production" className="-ml-1.5 flex size-8 items-center justify-center rounded-lg text-fg-subtle transition-colors duration-150 hover:bg-surface-2 hover:text-fg">
                  <ChevronLeft className="size-5" aria-hidden />
                </Link>
              </Tooltip>
              <h1 className="flex min-w-0 items-center gap-2 text-xl font-semibold tracking-tight">
                <span className="truncate">{mep.outil || 'MEP sans nom'}</span>
                {mep.versionCible.trim() && (
                  <>
                    <ArrowRight className="size-[18px] shrink-0 text-fg-subtle" aria-hidden />
                    <span className="font-mono">{mep.versionCible}</span>
                  </>
                )}
              </h1>
              <StatusPill ton={meta.ton} icone={meta.Icone} anime={meta.anime} taille="lg">{meta.libelle}</StatusPill>
              {mep.estModele && <Badge ton="later" className="text-xs"><LayoutTemplate className="size-3.5" aria-hidden />Modèle</Badge>}
              {mep.environnement && <Badge ton={mep.environnement.toLowerCase().startsWith('prod') ? 'warn' : undefined} className="text-xs">{mep.environnement}</Badge>}
            </div>
            <p className="mt-1 flex flex-wrap items-center gap-x-4 gap-y-0.5 pl-8 text-[13px] text-fg-muted">
              {mep.objet && <span className="max-w-md truncate">{mep.objet}</span>}
              <span className="inline-flex items-center gap-1.5"><CalendarDays className="size-3.5 text-fg-subtle" aria-hidden />{formatDate(mep.dateMep)}{mep.heureDebut && ` · ${formatHeure(mep.heureDebut)}`}</span>
              {pilote && <span className="inline-flex items-center gap-1.5"><Avatar nom={pilote} taille="xs" />Pilote : {pilote}</span>}
            </p>
          </div>

          <div className="flex flex-wrap items-center gap-2">
            {(mep.phase === 'Execution' || mep.phase === 'Recette') && mep.nonRetourFranchi === null && <CompteARebours echeance={mep.echeanceRepli} />}
            {(mep.phase === 'Preparation' || (mep.phase === 'Execution' && mep.erreursPlan.length > 0)) && <PlanCheck mep={mep} />}
            <Button taille="sm" icone={<Copy className="size-4" aria-hidden />} onClick={() => setDuplication(true)}>Dupliquer</Button>
            {pilotes && mep.phase === 'Preparation' && <BoutonGoNoGo />}
            {pilotes && mep.phase === 'Execution' && (
              <>
                <BoutonRevenirPreparation />
                <BoutonDeclencherRepli />
                <BoutonPasserRecette />
              </>
            )}
            {pilotes && mep.phase === 'Recette' && (
              <>
                <BoutonDeclencherRepli />
                <BoutonCloturer />
              </>
            )}
            {pilotes && mep.phase === 'RetourArriere' && <BoutonCloturer />}
          </div>
        </div>

        {mep.phase !== 'Preparation' && (
          <div className="mt-2.5 flex flex-wrap items-center gap-x-4 gap-y-1">
            <ProgressBar className="min-w-48 flex-1" hauteur="h-1.5" segments={segmentsProgression(etapes)} />
            <p className="flex shrink-0 items-center gap-3 text-xs text-fg-subtle tnum">
              <span className="font-medium text-fg-muted">{termine}/{etapes.length} {plan === 'RetourArriere' ? 'étapes de repli' : 'étapes'}</span>
              {segmentsProgression(etapes).filter((x) => x.valeur > 0 && x.ton !== 'wait').map((x) => (
                <span key={x.libelle} className="inline-flex items-center gap-1"><span className={cn('size-2 rounded-full', TON[x.ton].bgSolide)} aria-hidden />{x.valeur} {nommerSegment(x)}</span>
              ))}
            </p>
          </div>
        )}
      </div>
      <div className="mt-0.5 px-4">{onglets}</div>
      <DuplicationDialog open={duplication} onOpenChange={setDuplication} source={{ id: mep.id, outil: mep.outil, estModele: mep.estModele, environnement: mep.environnement }} />
    </header>
  );
}
