import { api } from '../api';
import { useMep } from '../mepContext';
import { BoutonExportExcel } from '../ui/BoutonExportExcel';
import { BoutonEtapeCorrective } from './CycleDeVie';
import { EtapesEditeur } from './EtapesEditeur';

/**
 * Onglet Étapes : graphe du plan de déploiement (éditable en préparation, en lecture ensuite). En exécution, le pilote et
 * les suppléants peuvent ajouter une étape corrective à la suite du plan (#93).
 */
export default function EtapesTab() {
  const { mep } = useMep();
  return (
    <div className="mx-auto w-full max-w-[1700px] px-6 py-6">
      <EtapesEditeur
        key={mep.phase}
        plan="Deploiement"
        actions={
          <>
            {mep.phase === 'Execution' && mep.droits.estPilote && <BoutonEtapeCorrective />}
            <BoutonExportExcel
              url={api.urlExportEtapes(mep.id)}
              title="Une ligne par étape, dans l’ordre du graphe : définition, exécution et résultat."
            />
          </>
        }
      />
    </div>
  );
}
