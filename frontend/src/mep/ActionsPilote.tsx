import { CheckCircle2, ClipboardCheck, ClipboardList, OctagonAlert, Play, Siren } from 'lucide-react';
import { useEffect, useState } from 'react';
import { api } from '../api';
import { useMep } from '../mepContext';
import { Alerte } from '../ui/Alerte';
import { Button, type TailleBouton } from '../ui/Button';
import { ConfirmDialog, Dialog } from '../ui/Dialog';
import { Field, Textarea } from '../ui/Input';
import { Tooltip } from '../ui/Tooltip';
import { deploiementReussi, etapesDuPlan } from '../domaine';
import type { CritereGoNoGo } from '../types';
import { ClotureDialog } from './ClotureDialog';
import { GoNoGoDialog } from './GoNoGoDialog';

/** Décision Go / no-go (remplace l'ancien « Passer en exécution »). */
export function BoutonGoNoGo({ taille = 'sm' }: { taille?: TailleBouton }) {
  const { mep, run } = useMep();
  const [ouvert, setOuvert] = useState(false);
  const [criteres, setCriteres] = useState<CritereGoNoGo[] | null>(null);
  const [erreur, setErreur] = useState('');

  useEffect(() => {
    if (!ouvert) return;
    let annule = false;
    setErreur('');
    api.criteresGoNoGo()
      .then((l) => { if (!annule) setCriteres(l.filter((c) => c.actif).sort((a, b) => a.ordre - b.ordre)); })
      .catch((e: unknown) => { if (!annule) { setCriteres(null); setErreur(e instanceof Error ? e.message : 'Critères indisponibles.'); } });
    return () => { annule = true; };
  }, [ouvert]);

  const modele = mep.estModele;
  const bouton = (
    <Button variante="primaire" taille={taille} icone={<Play className="size-4" aria-hidden />} disabled={modele} onClick={() => setOuvert(true)}>Go / no-go</Button>
  );
  return (
    <>
      {modele ? <Tooltip content="Un modèle ne passe pas en exécution : dupliquez-le pour créer une MEP."><span tabIndex={0}>{bouton}</span></Tooltip> : bouton}
      {ouvert && erreur && !criteres && (
        <Dialog open onOpenChange={setOuvert} titre="Go / no-go" taille="sm" pied={<Button onClick={() => setOuvert(false)}>Fermer</Button>}>
          <Alerte ton="danger" icone={OctagonAlert} role="alert">{erreur}</Alerte>
        </Dialog>
      )}
      <GoNoGoDialog
        open={ouvert && criteres !== null}
        onOpenChange={setOuvert}
        criteres={criteres ?? []}
        erreursPlan={mep.erreursPlan}
        onGo={(ids) => run(() => api.go(mep.id, ids), 'Go : la MEP est en exécution.')}
        onNoGo={(motif) => run(() => api.noGo(mep.id, motif), 'No-go enregistré : la MEP reste en préparation.')}
      />
    </>
  );
}

/** Passage en recette, quand tout le déploiement est réussi. */
export function BoutonPasserRecette({ taille = 'sm' }: { taille?: TailleBouton }) {
  const { mep, run } = useMep();
  const [ouvert, setOuvert] = useState(false);
  const pret = deploiementReussi(mep);
  const restantes = etapesDuPlan(mep, 'Deploiement').filter((e) => e.etat !== 'Reussie').length;
  const bouton = (
    <Button variante="primaire" taille={taille} icone={<ClipboardCheck className="size-4" aria-hidden />} disabled={!pret} onClick={() => setOuvert(true)}>Passer en recette</Button>
  );
  return (
    <>
      {pret ? bouton : (
        <Tooltip content={`${restantes} étape(s) du déploiement pas encore réussie(s)`}><span tabIndex={0}>{bouton}</span></Tooltip>
      )}
      <ConfirmDialog
        open={ouvert}
        onOpenChange={setOuvert}
        titre="Passer en recette ?"
        description={`Le déploiement est terminé. Les ${mep.testsRecette.length} test(s) de recette peuvent maintenant être réalisés. Le retour arrière reste possible${mep.nonRetourFranchi ? ', sauf point de non-retour franchi' : ''}.`}
        libelleConfirmer="Passer en recette"
        onConfirmer={() => run(() => api.passerEnRecette(mep.id), 'MEP passée en recette.')}
      />
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
  const bloque = mep.nonRetourFranchi !== null;
  const bouton = (
    <Button variante="danger" taille={taille} icone={<Siren className="size-4" aria-hidden />} disabled={bloque} onClick={() => setOuvert(true)}>Déclencher le retour arrière</Button>
  );
  return (
    <>
      {bloque ? (
        <Tooltip content={`Point de non-retour franchi (${mep.nonRetourFranchi}) : corriger en avant.`}><span tabIndex={0}>{bouton}</span></Tooltip>
      ) : bouton}
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

/** Clôture : « MEP réussie » depuis la recette (tous les tests conformes), « Service rétabli » en retour arrière. */
export function BoutonCloturer({ taille = 'sm', libelle }: { taille?: TailleBouton; libelle?: string }) {
  const { mep, run } = useMep();
  const [ouvert, setOuvert] = useState(false);
  const repli = mep.phase === 'RetourArriere';
  const etapes = etapesDuPlan(mep, repli ? 'RetourArriere' : 'Deploiement');
  const restantes = etapes.filter((e) => e.etat !== 'Reussie').length;
  const nonConformes = repli ? 0 : mep.testsRecette.filter((t) => t.resultat !== 'Conforme').length;
  const raison = restantes > 0
    ? `${restantes} étape(s) ${repli ? 'de retour arrière' : 'du déploiement'} pas encore réussie(s)`
    : nonConformes > 0 ? `${nonConformes} test(s) de recette pas encore conforme(s)` : null;
  const texte = libelle ?? (repli ? 'Service rétabli — clôturer' : 'Clôturer : MEP réussie');
  const bouton = (
    <Button variante="succes" taille={taille} icone={<CheckCircle2 className="size-4" aria-hidden />} disabled={raison !== null} onClick={() => setOuvert(true)}>{texte}</Button>
  );
  return (
    <>
      {raison ? <Tooltip content={raison}><span tabIndex={0}>{bouton}</span></Tooltip> : bouton}
      <ClotureDialog
        open={ouvert}
        onOpenChange={setOuvert}
        repli={repli}
        bilanActive={mep.bilanActive}
        onConfirmer={(bilan) => run(() => api.cloturer(mep.id, mep.bilanActive || bilan.ceQuiAMarche || bilan.incidents || bilan.actions ? bilan : undefined), 'MEP clôturée.')}
      />
    </>
  );
}
