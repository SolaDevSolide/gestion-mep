import { useMep } from '../mepContext';
import { EtapesEditeur } from './EtapesEditeur';

/** Onglet Étapes : graphe du plan de déploiement (éditable en préparation, en lecture ensuite). */
export default function EtapesTab() {
  const { mep } = useMep();
  return (
    <div className="mx-auto w-full max-w-[1700px] px-6 py-6">
      <EtapesEditeur key={mep.phase} plan="Deploiement" />
    </div>
  );
}
