import { Ban, Play, Siren } from 'lucide-react';
import { Link } from 'react-router-dom';
import { useMep } from '../mepContext';
import { Alerte } from '../ui/Alerte';
import { EmptyState } from '../ui/EmptyState';
import { BoutonGoNoGo } from './ActionsPilote';
import { ExecutionVue } from './ExecutionVue';

export default function ExecutionTab() {
  const { mep } = useMep();
  if (mep.phase === 'Annulee' && !mep.premierGo) {
    return (
      <div className="mx-auto max-w-3xl px-6 py-10">
        <EmptyState
          icone={Ban}
          titre="MEP annulée avant le Go"
          description="Aucune étape n’a été exécutée. Dupliquez la MEP pour la reprogrammer."
        />
      </div>
    );
  }
  if (mep.phase === 'Preparation') {
    return (
      <div className="mx-auto max-w-3xl px-6 py-10">
        <EmptyState
          icone={Play}
          titre="L’exécution n’a pas commencé"
          description={
            mep.erreursPlan.length > 0
              ? `Le plan comporte ${mep.erreursPlan.length} point(s) à corriger avant de pouvoir passer en exécution (voir « Vérifier le plan »).`
              : 'Le plan est complet. Le pilote peut prendre la décision Go / no-go.'
          }
          action={mep.droits.estPilote ? <BoutonGoNoGo /> : undefined}
        />
      </div>
    );
  }
  return (
    <div className="mx-auto w-full max-w-[1700px] px-6 py-6">
      {mep.phase === 'RetourArriere' && (
        <Alerte ton="danger" icone={Siren} className="mb-4" titre="Le déploiement est arrêté">
          Le retour arrière est en cours.{' '}
          <Link to="?onglet=plan&plan=retour" className="font-semibold underline">
            Ouvrir l’onglet Retour arrière
          </Link>
        </Alerte>
      )}
      <ExecutionVue plan="Deploiement" />
    </div>
  );
}
