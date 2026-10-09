import { IdentiteSection } from '../components/preparation/IdentiteSection';
import { SauvegardeSection } from '../components/preparation/SauvegardeSection';
import { LivrablesSection, TableauxSection } from '../components/preparation/TableauxSection';
import { ValidationSection } from '../components/preparation/ValidationSection';
import { useMep } from '../mepContext';

export default function PreparationTab() {
  const { mep } = useMep();
  return (
    <div className="sections">
      {!mep.droits.peutModifierPlan && (
        <p className="alerte alerte-info">Le plan est figé : la MEP est en exécution. Revenez en préparation pour le modifier.</p>
      )}
      <IdentiteSection />
      <TableauxSection />
      <LivrablesSection />
      <SauvegardeSection />
      <ValidationSection />
    </div>
  );
}
