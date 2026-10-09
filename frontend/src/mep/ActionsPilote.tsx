import { CheckCircle2, ClipboardList, Play, Siren } from 'lucide-react';
import { useState } from 'react';
import { api } from '../api';
import { useMep } from '../mepContext';
import { Alerte } from '../ui/Alerte';
import { Button, type TailleBouton } from '../ui/Button';
import { ConfirmDialog } from '../ui/Dialog';
import { Field, Textarea } from '../ui/Input';
import { Tooltip } from '../ui/Tooltip';
import { etapesDuPlan } from '../domaine';
import { OctagonAlert } from 'lucide-react';

export function BoutonPasserExecution({ taille = 'sm' }: { taille?: TailleBouton }) {
  const { mep, run } = useMep();
  const [ouvert, setOuvert] = useState(false);
  return (
    <>
      <Button variante="primaire" taille={taille} icone={<Play className="size-4" aria-hidden />} onClick={() => setOuvert(true)}>Passer en exécution</Button>
      <ConfirmDialog
        open={ouvert}
        onOpenChange={setOuvert}
        titre="Passer en exécution ?"
        description="Le plan est figé : on ne peut plus ajouter d’étapes ni modifier les instructions. Les opérateurs peuvent alors démarrer leurs étapes."
        libelleConfirmer="Passer en exécution"
        onConfirmer={() => run(() => api.changerPhase(mep.id, 'Execution'), 'MEP passée en exécution.')}
      >
        {mep.erreursPlan.length > 0 && (
          <Alerte ton="warn" icone={OctagonAlert} titre={`${mep.erreursPlan.length} point(s) à corriger dans le plan`}>
            Le serveur refusera le passage tant que le plan est incomplet.
          </Alerte>
        )}
      </ConfirmDialog>
    </>
  );
}

export function BoutonRevenirPreparation({ taille = 'sm' }: { taille?: TailleBouton }) {
  const { mep, run } = useMep();
  const [ouvert, setOuvert] = useState(false);
  return (
    <>
      <Button taille={taille} icone={<ClipboardList className="size-4" aria-hidden />} onClick={() => setOuvert(true)}>Revenir en préparation</Button>
      <ConfirmDialog
        open={ouvert}
        onOpenChange={setOuvert}
        titre="Revenir en préparation ?"
        description="Les états des étapes sont conservés. Cela n’est possible que si aucune étape n’est en cours."
        libelleConfirmer="Revenir en préparation"
        onConfirmer={() => run(() => api.changerPhase(mep.id, 'Preparation'), 'MEP revenue en préparation.')}
      />
    </>
  );
}

export function BoutonDeclencherRepli({ taille = 'sm' }: { taille?: TailleBouton }) {
  const { mep, run } = useMep();
  const [ouvert, setOuvert] = useState(false);
  const [motif, setMotif] = useState('');
  return (
    <>
      <Button variante="danger" taille={taille} icone={<Siren className="size-4" aria-hidden />} onClick={() => setOuvert(true)}>Déclencher le retour arrière</Button>
      <ConfirmDialog
        open={ouvert}
        onOpenChange={(o) => { setOuvert(o); if (!o) setMotif(''); }}
        titre="Déclencher le retour arrière"
        description="Le plan de déploiement s’arrête et les étapes de retour arrière deviennent disponibles. Cette décision est tracée au journal."
        libelleConfirmer="Déclencher le retour arrière"
        variante="danger"
        desactive={!motif.trim()}
        taille="md"
        onConfirmer={async () => {
          const ok = await run(() => api.declencherRetourArriere(mep.id, motif.trim()), 'Retour arrière déclenché.');
          if (ok) setMotif('');
          return ok;
        }}
      >
        <Field label="Motif (obligatoire)" hint="Expliquez pourquoi : cette information est visible de tous.">
          {(id) => <Textarea id={id} value={motif} onChange={(e) => setMotif(e.target.value)} rows={3} placeholder="ex. L’API ne répond plus après la migration, résolution impossible dans le temps imparti." autoFocus />}
        </Field>
      </ConfirmDialog>
    </>
  );
}

/** Clôture : « MEP réussie » en exécution, « Service rétabli » en retour arrière. */
export function BoutonCloturer({ taille = 'sm', libelle }: { taille?: TailleBouton; libelle?: string }) {
  const { mep, run } = useMep();
  const [ouvert, setOuvert] = useState(false);
  const repli = mep.phase === 'RetourArriere';
  const etapes = etapesDuPlan(mep, repli ? 'RetourArriere' : 'Deploiement');
  const restantes = etapes.filter((e) => e.etat !== 'Reussie').length;
  const texte = libelle ?? (repli ? 'Service rétabli — clôturer' : 'Clôturer : MEP réussie');
  const bouton = (
    <Button variante="succes" taille={taille} icone={<CheckCircle2 className="size-4" aria-hidden />} disabled={restantes > 0} onClick={() => setOuvert(true)}>{texte}</Button>
  );
  return (
    <>
      {restantes > 0 ? (
        <Tooltip content={`${restantes} étape(s) ${repli ? 'de retour arrière' : 'du déploiement'} pas encore réussie(s)`}>
          <span tabIndex={0}>{bouton}</span>
        </Tooltip>
      ) : bouton}
      <ConfirmDialog
        open={ouvert}
        onOpenChange={setOuvert}
        titre={repli ? 'Le service est-il rétabli ?' : 'Clôturer la MEP comme réussie ?'}
        description={repli
          ? 'Toutes les étapes de retour arrière sont réussies. La MEP sera clôturée comme « repliée ».'
          : 'Toutes les étapes du déploiement sont réussies. La MEP sera clôturée comme « réussie » et passera en lecture seule.'}
        libelleConfirmer={repli ? 'Service rétabli, clôturer' : 'Clôturer la MEP'}
        variante="succes"
        onConfirmer={() => run(() => api.cloturer(mep.id), 'MEP clôturée.')}
      >
      </ConfirmDialog>
    </>
  );
}
