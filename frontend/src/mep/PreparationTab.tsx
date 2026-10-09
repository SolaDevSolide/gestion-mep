import { Check, Eye } from 'lucide-react';
import { useState } from 'react';
import { useMep } from '../mepContext';
import { Alerte } from '../ui/Alerte';
import { cn } from '../ui/cn';
import { completude, type IdSection } from './completude';
import { IdentiteSection } from './preparation/IdentiteSection';
import { LivrablesSection, PersonnesSection, SystemesSection } from './preparation/ListesSections';
import { SauvegardeSection, ValidationSection } from './preparation/SauvegardeValidation';

const SECTIONS: { id: IdSection; numero: number; titre: string }[] = [
  { id: 'intervention', numero: 1, titre: 'Intervention' },
  { id: 'personnes', numero: 2, titre: 'Personnes' },
  { id: 'systemes', numero: 3, titre: 'Systèmes' },
  { id: 'livrables', numero: 4, titre: 'Livrables' },
  { id: 'sauvegarde', numero: 5, titre: 'Sauvegarde' },
  { id: 'validation', numero: 6, titre: 'Validation' },
];

export default function PreparationTab() {
  const { mep } = useMep();
  const [section, setSection] = useState<IdSection>('intervention');
  const ok = completude(mep);
  const nbOk = SECTIONS.filter((s) => ok[s.id]).length;

  return (
    <div className="mx-auto grid w-full max-w-6xl gap-6 px-6 py-6 lg:grid-cols-[15rem_minmax(0,1fr)]">
      <nav aria-label="Sections de la préparation" className="lg:self-start lg:[@media(min-height:760px)]:sticky lg:[@media(min-height:760px)]:top-[calc(var(--entete-h,12rem)+1rem)]">
        <p className="mb-2 hidden px-2 text-xs text-fg-subtle lg:block tnum">{nbOk}/{SECTIONS.length} sections complètes</p>
        <ul className="flex gap-1 overflow-x-auto pb-1 lg:flex-col lg:overflow-visible lg:pb-0">
          {SECTIONS.map((s) => {
            const actif = section === s.id;
            return (
              <li key={s.id} className="shrink-0">
                <button
                  type="button"
                  aria-current={actif ? 'true' : undefined}
                  onClick={() => setSection(s.id)}
                  className={cn(
                    'flex w-full items-center gap-3 rounded-lg px-2.5 py-2 text-left text-sm font-medium transition-colors duration-150',
                    actif ? 'bg-accent-soft text-accent-text' : 'text-fg-muted hover:bg-surface-2 hover:text-fg',
                  )}
                >
                  <span className={cn('flex size-6 shrink-0 items-center justify-center rounded-md font-mono text-xs font-bold', actif ? 'bg-accent text-accent-fg' : 'bg-surface-2 text-fg-muted')}>{s.numero}</span>
                  <span className="flex-1 whitespace-nowrap">{s.titre}</span>
                  {ok[s.id]
                    ? <span className="flex size-5 items-center justify-center rounded-full bg-ok-solid text-white"><Check className="size-3" strokeWidth={3} aria-hidden /><span className="sr-only">Complète</span></span>
                    : <span className="size-2.5 rounded-full border-2 border-warn-solid" role="img" aria-label="À compléter" />}
                </button>
              </li>
            );
          })}
        </ul>
      </nav>

      <div className="min-w-0 space-y-4">
        {!mep.droits.peutModifierPlan && (
          <Alerte ton="wait" icone={Eye}>
            Lecture seule : la MEP n’est plus en préparation.
            {mep.droits.estPilote && ' Le pilote peut encore modifier la sauvegarde et l’échéance de retour arrière.'}
          </Alerte>
        )}
        {/* Toutes les sections restent montées : un brouillon non enregistré survit au changement de section. */}
        <div hidden={section !== 'intervention'}><IdentiteSection /></div>
        <div hidden={section !== 'personnes'}><PersonnesSection /></div>
        <div hidden={section !== 'systemes'}><SystemesSection /></div>
        <div hidden={section !== 'livrables'}><LivrablesSection /></div>
        <div hidden={section !== 'sauvegarde'}><SauvegardeSection /></div>
        <div hidden={section !== 'validation'}><ValidationSection /></div>
      </div>
    </div>
  );
}
