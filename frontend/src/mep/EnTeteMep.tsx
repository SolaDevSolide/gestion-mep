import { ArrowRight, CalendarDays, ChevronLeft, Copy, LayoutTemplate, Mail, Server } from 'lucide-react';
import { useEffect, useRef, useState, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { abregerEnvironnement } from '../environnements';
import { etapesDuPlan, nomParticipant, planActif, segmentsProgression } from '../domaine';
import { formatDateHeure } from '../format';
import { metaPhase } from '../statuts';
import type { Mep } from '../types';
import { Avatar } from '../ui/Avatar';
import { Button } from '../ui/Button';
import { Dialog } from '../ui/Dialog';
import { TextesACopierContenu } from '../textes/TextesACopier';
import { cn } from '../ui/cn';
import { nommerSegment, ProgressBar } from '../ui/ProgressBar';
import { TON } from '../ui/tons';
import { Tooltip } from '../ui/Tooltip';
import { Badge, StatusPill } from '../ui/StatusPill';
import {
  BoutonCloturer,
  BoutonDeclencherRepli,
  BoutonGoNoGo,
  BoutonPasserRecette,
  BoutonRevenirPreparation,
} from './ActionsPilote';
import { BoutonCloturerEchec, BoutonReprendrePreparation, estApresGoEnCours, MenuPlusActions } from './CycleDeVie';
import { DuplicationDialog } from './DuplicationDialog';
import { NomSystemeMep } from './SystemeCliquable';
import { CompteARebours } from './CompteARebours';
import { PlanCheck } from './PlanCheck';
import { PresentsMep } from '../presence';
import { BadgeSimplifiee, estSimplifiee } from './typeMep';

/** En-tête collant : identité, phase, personnes présentes, compte à rebours, progression, actions du pilote, onglets. */
export function EnTeteMep({ mep, onglets }: { mep: Mep; onglets: ReactNode }) {
  const meta = metaPhase(mep.phase, mep.etat, mep.report?.dateVisee);
  const pilote = nomParticipant(mep, mep.piloteId);
  const plan = planActif(mep.phase) ?? 'Deploiement';
  const etapes = etapesDuPlan(mep, plan);
  const termine = etapes.filter((e) => e.etat === 'Reussie').length;
  const pilotes = mep.droits.estPilote;
  const [duplication, setDuplication] = useState(false);
  const [textes, setTextes] = useState(false);
  const ref = useRef<HTMLElement>(null);
  useEffect(() => {
    const el = ref.current;
    if (!el || typeof ResizeObserver === 'undefined') return;
    const maj = () => document.documentElement.style.setProperty('--entete-h', `${el.offsetHeight}px`);
    maj();
    const ro = new ResizeObserver(maj);
    ro.observe(el);
    return () => {
      ro.disconnect();
      document.documentElement.style.removeProperty('--entete-h');
    };
  }, []);

  return (
    <header
      ref={ref}
      data-entete-mep
      className="z-30 border-b border-line bg-surface/95 backdrop-blur-md [@media(min-height:760px)]:sticky [@media(min-height:760px)]:top-[var(--bandeau-demo,0px)]"
    >
      <div className="px-6 pt-3">
        <div className="flex flex-wrap items-center justify-between gap-x-6 gap-y-2">
          <div className="min-w-0">
            <div className="flex flex-wrap items-center gap-x-3 gap-y-1.5">
              <Tooltip content="Retour à la liste des MEP" cote="bottom">
                <Link
                  to="/"
                  aria-label="Retour à la liste des mises en production"
                  className="-ml-1.5 flex size-8 items-center justify-center rounded-lg text-fg-subtle transition-colors duration-150 hover:bg-surface-2 hover:text-fg"
                >
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
              {!mep.estModele && (
                <StatusPill ton={meta.ton} icone={meta.Icone} anime={meta.anime} taille="lg">
                  {meta.libelle}
                </StatusPill>
              )}
              {mep.estModele && (
                <Badge ton="later" className="text-xs">
                  <LayoutTemplate className="size-3.5" aria-hidden />
                  Modèle
                </Badge>
              )}
              {estSimplifiee(mep) && <BadgeSimplifiee />}
            </div>
            <p className="mt-1 flex flex-wrap items-center gap-x-4 gap-y-0.5 pl-8 text-ui text-fg-muted">
              {mep.objet && <span className="max-w-md truncate">{mep.objet}</span>}
              {!mep.estModele && (
                <span className="inline-flex items-center gap-1.5">
                  <CalendarDays className="size-3.5 text-fg-subtle" aria-hidden />
                  Démarrée le {mep.demarrageAutorise ? formatDateHeure(mep.demarrageAutorise.le) : '—'}
                  {mep.cloture && ` · Clôturée le ${formatDateHeure(mep.cloture.le)}`}
                  {mep.annulation && ` · Annulée le ${formatDateHeure(mep.annulation.le)}`}
                </span>
              )}
              <EnvironnementsMep mep={mep} />
              {pilote && !mep.estModele && (
                <span className="inline-flex items-center gap-1.5">
                  <Avatar nom={pilote} taille="xs" />
                  Pilote : {pilote}
                  {mep.pilotageSecours && estApresGoEnCours(mep) && (
                    <Badge ton="warn" className="text-2xs">
                      secours
                    </Badge>
                  )}
                </span>
              )}
            </p>
          </div>

          <div className="flex flex-wrap items-center gap-2">
            <PresentsMep />
            {(mep.phase === 'Execution' || mep.phase === 'Recette') &&
              mep.retourArriereActive &&
              mep.echeanceRepli !== null &&
              mep.nonRetourFranchi === null && (
                <CompteARebours
                  echeance={mep.echeanceRepli}
                  fuseau={mep.fuseauHoraire}
                  recette={mep.phase === 'Recette'}
                />
              )}
            {(mep.phase === 'Preparation' || (mep.phase === 'Execution' && mep.erreursPlan.length > 0)) && (
              <PlanCheck mep={mep} />
            )}
            {/* À toutes les phases : le rappel et le compte rendu servent pendant et après l'exécution. */}
            <Button taille="sm" icone={<Mail className="size-4" aria-hidden />} onClick={() => setTextes(true)}>
              Textes à copier
            </Button>
            <Button
              taille="sm"
              variante={mep.estModele ? 'primaire' : undefined}
              icone={<Copy className="size-4" aria-hidden />}
              onClick={() => setDuplication(true)}
            >
              {mep.estModele ? 'Créer une MEP depuis ce modèle' : 'Dupliquer'}
            </Button>
            {pilotes && mep.phase === 'Preparation' && !mep.estModele && (
              <>
                {mep.report && <BoutonReprendrePreparation />}
                <BoutonGoNoGo />
              </>
            )}
            {pilotes && mep.phase === 'Execution' && (
              <>
                <BoutonRevenirPreparation />
                {mep.retourArriereActive && <BoutonDeclencherRepli />}
                <BoutonPasserRecette />
              </>
            )}
            {pilotes && mep.phase === 'Recette' && (
              <>
                {mep.retourArriereActive && <BoutonDeclencherRepli />}
                <BoutonCloturer />
              </>
            )}
            {pilotes && mep.phase === 'RetourArriere' && (
              <>
                {etapes.some((e) => e.etat === 'Echec') && <BoutonCloturerEchec />}
                <BoutonCloturer />
              </>
            )}
            <MenuPlusActions />
          </div>
        </div>

        {mep.phase !== 'Preparation' && mep.phase !== 'Annulee' && (
          <div className="mt-2.5 flex flex-wrap items-center gap-x-4 gap-y-1">
            <ProgressBar className="min-w-48 flex-1" hauteur="h-1.5" segments={segmentsProgression(etapes)} />
            <p className="flex shrink-0 items-center gap-3 text-xs text-fg-subtle tnum">
              <span className="font-medium text-fg-muted">
                {termine}/{etapes.length} {plan === 'RetourArriere' ? 'étapes de retour arrière' : 'étapes'}
              </span>
              {segmentsProgression(etapes)
                .filter((x) => x.valeur > 0 && x.ton !== 'wait')
                .map((x) => (
                  <span key={x.libelle} className="inline-flex items-center gap-1">
                    <span className={cn('size-2 rounded-full', TON[x.ton].bgSolide)} aria-hidden />
                    {x.valeur} {nommerSegment(x)}
                  </span>
                ))}
            </p>
          </div>
        )}
      </div>
      <div className="mt-0.5 px-4">{onglets}</div>
      <Dialog
        open={textes}
        onOpenChange={setTextes}
        titre="Textes à copier"
        description="Choisissez un modèle : le texte se remplit avec les informations de la MEP. Copiez-le dans votre messagerie."
        taille="xl"
      >
        {textes && <TextesACopierContenu mep={mep} />}
      </Dialog>
      <DuplicationDialog
        open={duplication}
        onOpenChange={setDuplication}
        source={{ id: mep.id, outil: mep.outil, estModele: mep.estModele }}
      />
    </header>
  );
}

/**
 * Environnements touchés, système par système : « BDD Préprod, Prod · BATCH Prod » (production en orange). Le nom du
 * système ouvre son menu : historique des MEP, MEP sur ce système (#59).
 */
function EnvironnementsMep({ mep }: { mep: Mep }) {
  const systemes = mep.systemes.filter((s) => s.environnements.length > 0);
  if (systemes.length === 0) return null;
  return (
    <span className="inline-flex flex-wrap items-center gap-x-2 gap-y-0.5" aria-label="Environnements par système">
      <Server className="size-3.5 text-fg-subtle" aria-hidden />
      {systemes.map((s, i) => (
        <span key={s.id} className="inline-flex items-center gap-1">
          {i > 0 && (
            <span className="text-fg-subtle" aria-hidden>
              ·
            </span>
          )}
          <NomSystemeMep s={s} />
          <span>
            {s.environnements.map((e, j) => (
              <span key={e.nom}>
                {j > 0 && ', '}
                <span title={e.nom} className={cn(e.production && 'font-semibold text-warn-text')}>
                  {abregerEnvironnement(e.nom)}
                </span>
              </span>
            ))}
          </span>
        </span>
      ))}
    </span>
  );
}
