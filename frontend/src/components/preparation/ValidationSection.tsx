import { api } from '../../api';
import { formatDateHeure } from '../../format';
import { useMep } from '../../mepContext';
import { Case, Section } from '../Champ';

export function ValidationSection() {
  const { mep, run } = useMep();
  const peut = mep.droits.peutModifierPlan && mep.droits.participantId !== null;
  const relue = mep.procedureRelue !== null;
  return (
    <Section numero={5} titre="Validation avant intervention">
      <Case
        label="Procédure relue"
        checked={relue}
        disabled={!peut}
        onChange={(c) => void run(() => api.procedureRelue(mep.id, c), c ? 'Procédure marquée relue.' : 'Relecture retirée.')}
      />
      {mep.procedureRelue && (
        <p className="petit muted">Relue par {mep.procedureRelue.par} le {formatDateHeure(mep.procedureRelue.le)}.</p>
      )}
      {!peut && mep.droits.peutModifierPlan && <p className="petit muted">Seul un participant relié à un compte peut valider la relecture.</p>}
      <div className="cases">
        <Case label="Démarrage autorisé" checked={mep.demarrageAutorise !== null} disabled onChange={() => undefined} />
      </div>
      {mep.demarrageAutorise ? (
        <p className="petit muted">Autorisé par {mep.demarrageAutorise.par} le {formatDateHeure(mep.demarrageAutorise.le)}.</p>
      ) : (
        <p className="petit muted">Le démarrage est autorisé par le pilote lors du passage en exécution.</p>
      )}
    </Section>
  );
}
