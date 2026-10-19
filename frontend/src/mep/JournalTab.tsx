import {
  ArrowRightLeft,
  Ban,
  CalendarClock,
  CheckCircle2,
  ClipboardCheck,
  FilePlus2,
  Flag,
  HandMetal,
  History,
  Layers,
  LifeBuoy,
  ListChecks,
  Package,
  Paperclip,
  Play,
  Search,
  ShieldCheck,
  Siren,
  Trash2,
  Undo2,
  UserPlus,
  UserRound,
  Wrench,
  XCircle,
  NotebookText,
  type LucideIcon,
} from 'lucide-react';
import { useMemo, useState } from 'react';
import { api } from '../api';
import {
  familleEvenement,
  formatDate,
  formatHeureLocale,
  formatJourLong,
  LIBELLE_FAMILLE_EVENEMENT,
  type Ton,
} from '../format';
import { useAsync } from '../hooks';
import type { Evenement } from '../types';
import { Alerte } from '../ui/Alerte';
import { Avatar } from '../ui/Avatar';
import { BoutonExportExcel } from '../ui/BoutonExportExcel';
import { cn } from '../ui/cn';
import { EmptyState } from '../ui/EmptyState';
import { Input, Select } from '../ui/Input';
import { TON } from '../ui/tons';
import { Code2, OctagonAlert } from 'lucide-react';
import { EnChargement, SqueletteChronologie } from '../ui/Squelette';
import { TestsUnitairesEncadre } from './TestsUnitairesEncadre';

function iconeEvenement(type: string): { Icone: LucideIcon; ton: Ton } {
  switch (type) {
    case 'mep.creee':
      return { Icone: FilePlus2, ton: 'run' };
    case 'mep.cloturee':
      return { Icone: Flag, ton: 'done' };
    case 'mep.annulee':
      return { Icone: Ban, ton: 'later' };
    case 'mep.reportee':
      return { Icone: CalendarClock, ton: 'wait' };
    case 'mep.reprise':
      return { Icone: History, ton: 'wait' };
    case 'pilotage.secours':
      return { Icone: LifeBuoy, ton: 'warn' };
    case 'pilotage.passe':
      return { Icone: ArrowRightLeft, ton: 'wait' };
    case 'participant.ajoute':
      return { Icone: UserPlus, ton: 'wait' };
    case 'phase.execution':
      return { Icone: Play, ton: 'run' };
    case 'phase.preparation':
      return { Icone: ClipboardCheck, ton: 'wait' };
    case 'etape.reussie':
      return { Icone: CheckCircle2, ton: 'ok' };
    case 'etape.echec':
      return { Icone: XCircle, ton: 'danger' };
    case 'etape.demarree':
      return { Icone: Play, ton: 'run' };
    case 'etape.supprimee':
      return { Icone: Trash2, ton: 'wait' };
    case 'etape.reprise':
      return { Icone: Play, ton: 'warn' };
    case 'etape.prise-en-main':
      return { Icone: HandMetal, ton: 'warn' };
    case 'repli.declenche':
      return { Icone: Siren, ton: 'rollback' };
    case 'repli.modifie':
    case 'repli.active':
      return { Icone: Undo2, ton: 'wait' };
    case 'repli.desactive':
      return { Icone: Undo2, ton: 'warn' };
    case 'resolution.demarree':
    case 'resolution.correction':
      return { Icone: Wrench, ton: 'warn' };
    case 'resolution.reussie':
      return { Icone: CheckCircle2, ton: 'ok' };
    case 'resolution.echouee':
      return { Icone: XCircle, ton: 'danger' };
    case 'procedure.relue':
      return { Icone: ShieldCheck, ton: 'ok' };
    case 'approbation.approuvee':
      return { Icone: CheckCircle2, ton: 'ok' };
    case 'approbation.refusee':
      return { Icone: XCircle, ton: 'danger' };
    case 'approbations.reinitialisees':
      return { Icone: Undo2, ton: 'warn' };
    case 'gonogo.go':
      return { Icone: Play, ton: 'ok' };
    case 'gonogo.nogo':
      return { Icone: XCircle, ton: 'danger' };
    case 'etape.non-retour-autorise':
      return { Icone: ShieldCheck, ton: 'warn' };
    case 'phase.recette':
      return { Icone: ClipboardCheck, ton: 'warn' };
    case 'piece.ajoutee':
    case 'piece.supprimee':
      return { Icone: Paperclip, ton: 'wait' };
    default:
      break;
  }
  switch (familleEvenement(type)) {
    case 'etape':
      return { Icone: ListChecks, ton: 'wait' };
    case 'recette':
      return { Icone: ClipboardCheck, ton: 'warn' };
    case 'approbation':
      return { Icone: ShieldCheck, ton: 'wait' };
    case 'responsables-techniques':
      return { Icone: Code2, ton: 'wait' };
    case 'participant':
      return { Icone: UserRound, ton: 'wait' };
    case 'systeme':
      return { Icone: Layers, ton: 'wait' };
    case 'livrable':
      return { Icone: Package, ton: 'wait' };
    case 'repli':
      return { Icone: Undo2, ton: 'rollback' };
    case 'resolution':
      return { Icone: Wrench, ton: 'warn' };
    default:
      return { Icone: NotebookText, ton: 'wait' };
  }
}

function jour(iso: string): string {
  const d = new Date(iso);
  return `${d.getFullYear()}-${d.getMonth()}-${d.getDate()}`;
}

/** Chronologie verticale du journal d'audit, précédée du suivi des tests unitaires. */
export default function JournalTab({ mepId, tick }: { mepId: string; tick: number }) {
  const { donnees, erreur } = useAsync(() => api.journal(mepId), [mepId, tick]);
  const [type, setType] = useState('tous');
  const [recherche, setRecherche] = useState('');

  const familles = useMemo(() => [...new Set((donnees ?? []).map((e) => familleEvenement(e.type)))], [donnees]);
  const filtres = useMemo(() => {
    const q = recherche.trim().toLowerCase();
    return (donnees ?? []).filter(
      (e) =>
        (type === 'tous' || familleEvenement(e.type) === type) &&
        (!q ||
          e.detail.toLowerCase().includes(q) ||
          e.auteur.toLowerCase().includes(q) ||
          (e.auteurLogin ?? '').toLowerCase().includes(q)),
    );
  }, [donnees, type, recherche]);

  const groupes = useMemo(() => {
    const g: { cle: string; iso: string; items: Evenement[] }[] = [];
    for (const e of filtres) {
      const cle = jour(e.quand);
      const dernier = g[g.length - 1];
      if (dernier && dernier.cle === cle) dernier.items.push(e);
      else g.push({ cle, iso: e.quand, items: [e] });
    }
    return g;
  }, [filtres]);

  return (
    <div className="mx-auto w-full max-w-4xl px-6 py-6">
      <TestsUnitairesEncadre mepId={mepId} tick={tick} />
      <div className="flex flex-wrap items-center gap-3">
        <div className="relative min-w-60 flex-1">
          <Search
            className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-fg-subtle"
            aria-hidden
          />
          <Input
            aria-label="Rechercher dans le journal"
            placeholder="Rechercher dans le journal…"
            className="pl-9"
            value={recherche}
            onChange={(e) => setRecherche(e.target.value)}
          />
        </div>
        <div className="w-52">
          <Select aria-label="Filtrer par type d’événement" value={type} onChange={(e) => setType(e.target.value)}>
            <option value="tous">Tous les types</option>
            {familles.map((f) => (
              <option key={f} value={f}>
                {LIBELLE_FAMILLE_EVENEMENT[f] ?? f}
              </option>
            ))}
          </Select>
        </div>
        <BoutonExportExcel
          url={api.urlExportJournal(mepId, { type, recherche })}
          disabled={donnees === null}
          title="Les événements affichés (filtres compris), puis le suivi des tests unitaires."
        />
      </div>

      {erreur && (
        <Alerte ton="danger" icone={OctagonAlert} role="alert" className="mt-4">
          {erreur}
        </Alerte>
      )}

      {donnees === null && !erreur && (
        <EnChargement libelle="Chargement du journal…" className="mt-6">
          <SqueletteChronologie evenements={8} />
        </EnChargement>
      )}

      {donnees !== null && filtres.length === 0 && (
        <EmptyState
          icone={NotebookText}
          titre="Aucun événement"
          description={
            donnees.length === 0
              ? 'Le journal se remplira au fil des actions.'
              : 'Aucun événement ne correspond à ces critères.'
          }
        />
      )}

      <div className={cn('mt-6 space-y-8', donnees !== null && 'apparition')}>
        {groupes.map((g) => (
          <section key={g.cle} aria-label={formatJourLong(g.iso)}>
            <h2 className="mb-3 text-xs font-semibold tracking-wider text-fg-subtle uppercase">
              {formatJourLong(g.iso)}
            </h2>
            <ol className="relative space-y-0 border-l border-line pl-0">
              {g.items.map((e) => {
                const { Icone, ton } = iconeEvenement(e.type);
                return (
                  <li key={e.id} className="relative flex gap-4 py-2.5 pl-8">
                    <span
                      className={cn(
                        'absolute top-3 -left-[15px] flex size-[30px] items-center justify-center rounded-full border-2 border-bg',
                        TON[ton].fond,
                        TON[ton].solide,
                      )}
                    >
                      <Icone className="size-4" aria-hidden />
                    </span>
                    <div className="min-w-0 flex-1">
                      <p className="text-sm text-fg">{e.detail}</p>
                      <p className="mt-0.5 flex flex-wrap items-center gap-x-2 text-xs text-fg-subtle">
                        <Avatar nom={e.auteur} taille="xs" />
                        <span className="font-medium text-fg-muted">
                          {e.auteur}
                          {e.auteurLogin && <span className="font-normal text-fg-subtle"> ({e.auteurLogin})</span>}
                        </span>
                        <span aria-hidden>·</span>
                        <time
                          dateTime={e.quand}
                          title={`${formatDate(e.quand)} ${formatHeureLocale(e.quand)}`}
                          className="tnum"
                        >
                          {formatHeureLocale(e.quand)}
                        </time>
                        <span className="rounded border border-line px-1.5 py-px text-[10px] font-medium">
                          {LIBELLE_FAMILLE_EVENEMENT[familleEvenement(e.type)] ?? familleEvenement(e.type)}
                        </span>
                      </p>
                    </div>
                  </li>
                );
              })}
            </ol>
          </section>
        ))}
      </div>
    </div>
  );
}
