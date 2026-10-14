import { Check, Eye } from 'lucide-react';
import { useState } from 'react';
import { useMep } from '../mepContext';
import { libelleEditeurs, useEditeurs, ZonePresence } from '../presence';
import { Avatar } from '../ui/Avatar';
import { Alerte } from '../ui/Alerte';
import { cn } from '../ui/cn';
import { completude, type IdSection } from './completude';
import { IdentiteSection } from './preparation/IdentiteSection';
import { ResponsablesTechniquesSection } from './preparation/ResponsablesTechniquesSection';
import { PersonnesSection, SystemesSection } from './preparation/ListesSections';
import { CommunicationSection } from './preparation/CommunicationSection';
import { ObjetsImpactesSection } from './preparation/ObjetsImpactesSection';
import { PiecesSection } from './preparation/PiecesSection';
import { SauvegardeSection, ValidationSection } from './preparation/SauvegardeValidation';

const SECTIONS: { id: IdSection; numero: number; titre: string }[] = [
  { id: 'intervention', numero: 1, titre: 'Intervention' },
  { id: 'personnes', numero: 2, titre: 'Personnes' },
  { id: 'systemes', numero: 3, titre: 'Systèmes' },
  { id: 'techniques', numero: 4, titre: 'Responsables techniques' },
  { id: 'objets', numero: 5, titre: 'Objets impactés' },
  { id: 'sauvegarde', numero: 6, titre: 'Sauvegarde' },
  { id: 'validation', numero: 7, titre: 'Validation' },
  { id: 'communication', numero: 8, titre: 'Communication' },
  { id: 'pieces', numero: 9, titre: 'Pièces jointes' },
];

/** Dans le sommaire : avatars des autres personnes qui travaillent sur la section. */
function EditeursSection({ id }: { id: IdSection }) {
  const editeurs = useEditeurs(`preparation:${id}`);
  if (editeurs.length === 0) return null;
  return (
    <span className="flex shrink-0 -space-x-1" role="img" aria-label={libelleEditeurs(editeurs)}>
      {editeurs.slice(0, 3).map((p) => (
        <Avatar
          key={p.utilisateurId}
          nom={p.nom}
          taille="xs"
          className="ring-2 ring-surface"
          titre={`${p.nom} modifie`}
        />
      ))}
    </span>
  );
}

export default function PreparationTab() {
  const { mep } = useMep();
  const [section, setSection] = useState<IdSection>('intervention');
  const ok = completude(mep);
  const nbOk = SECTIONS.filter((s) => ok[s.id]).length;

  return (
    <div className="mx-auto grid w-full max-w-6xl gap-6 px-6 py-6 lg:grid-cols-[15rem_minmax(0,1fr)]">
      <nav
        aria-label="Sections de la préparation"
        className="lg:self-start lg:[@media(min-height:760px)]:sticky lg:[@media(min-height:760px)]:top-[calc(var(--entete-h,12rem)+var(--bandeau-demo,0px)+1rem)]"
      >
        <p className="mb-2 hidden px-2 text-xs text-fg-subtle lg:block tnum">
          {nbOk}/{SECTIONS.length} sections complètes
        </p>
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
                  <span
                    className={cn(
                      'flex size-6 shrink-0 items-center justify-center rounded-md font-mono text-xs font-bold',
                      actif ? 'bg-accent text-accent-fg' : 'bg-surface-2 text-fg-muted',
                    )}
                  >
                    {s.numero}
                  </span>
                  <span className="min-w-0 flex-1 whitespace-nowrap leading-tight lg:whitespace-normal">{s.titre}</span>
                  <EditeursSection id={s.id} />
                  {ok[s.id] ? (
                    <span className="flex size-5 shrink-0 items-center justify-center rounded-full bg-ok-solid text-white">
                      <Check className="size-3" strokeWidth={3} aria-hidden />
                      <span className="sr-only">Complète</span>
                    </span>
                  ) : (
                    <span
                      className="size-2.5 rounded-full border-2 border-warn-solid"
                      role="img"
                      aria-label="À compléter"
                    />
                  )}
                </button>
              </li>
            );
          })}
        </ul>
      </nav>

      <div className="min-w-0 space-y-4">
        {mep.droits.peutModifierPlan && !mep.droits.peutEditerContenu && (
          <Alerte ton="wait" icone={Eye}>
            Lecture seule : seuls les responsables techniques, le pilote, ses suppléants ou un administrateur peuvent
            modifier cette MEP.
          </Alerte>
        )}
        {!mep.droits.peutModifierPlan && (
          <Alerte ton="wait" icone={Eye}>
            Lecture seule : la MEP n’est plus en préparation.
            {mep.droits.estPilote &&
              (mep.retourArriereActive
                ? ' Le pilote peut encore modifier la sauvegarde, la communication et l’échéance de retour arrière.'
                : ' Le pilote peut encore modifier la sauvegarde et la communication.')}
          </Alerte>
        )}
        {/* Toutes les sections restent montées : un brouillon non enregistré survit au changement de section.
            Chacune signale aux autres qui y travaille (focus ou saisie en cours). */}
        <ZonePresence cle="preparation:intervention" className={section !== 'intervention' ? 'hidden' : undefined}>
          <IdentiteSection />
        </ZonePresence>
        <ZonePresence cle="preparation:personnes" className={section !== 'personnes' ? 'hidden' : undefined}>
          <PersonnesSection />
        </ZonePresence>
        <ZonePresence cle="preparation:systemes" className={section !== 'systemes' ? 'hidden' : undefined}>
          <SystemesSection />
        </ZonePresence>
        <ZonePresence cle="preparation:techniques" className={section !== 'techniques' ? 'hidden' : undefined}>
          <ResponsablesTechniquesSection />
        </ZonePresence>
        <ZonePresence cle="preparation:objets" className={section !== 'objets' ? 'hidden' : undefined}>
          <ObjetsImpactesSection />
        </ZonePresence>
        <ZonePresence cle="preparation:sauvegarde" className={section !== 'sauvegarde' ? 'hidden' : undefined}>
          <SauvegardeSection />
        </ZonePresence>
        <ZonePresence cle="preparation:validation" className={section !== 'validation' ? 'hidden' : undefined}>
          <ValidationSection />
        </ZonePresence>
        <ZonePresence cle="preparation:communication" className={section !== 'communication' ? 'hidden' : undefined}>
          <CommunicationSection />
        </ZonePresence>
        <ZonePresence cle="preparation:pieces" className={section !== 'pieces' ? 'hidden' : undefined}>
          <PiecesSection />
        </ZonePresence>
      </div>
    </div>
  );
}
