import { Check, CheckCircle2, CircleHelp, Lock, OctagonAlert, Play, RotateCcw, Wrench } from 'lucide-react';
import { Link } from 'react-router-dom';
import type { Etape, ExecutionResolution, Mep, ResolutionType } from '../../types';
import { Alerte } from '../../ui/Alerte';
import { Button } from '../../ui/Button';
import { Instructions, Markdown } from '../../ui/Instructions';
import type { DroitsEtape } from '../droitsEtape';
import type { ActionsEtape } from '../EtapePanneau';
import { Bloc } from '../graphe/EspaceEtape';
import { ResolutionDeroule } from '../ResolutionDeroule';

/** Instructions exactes de l'étape et résultat attendu. */
function InstructionsEtape({ etape }: { etape: Etape }) {
  return (
    <>
      <Bloc titre="Instructions exactes">
        <Instructions source={etape.instructions} vide="Aucune instruction renseignée." />
      </Bloc>
      {etape.resultatAttendu.trim() && (
        <section className="rounded-lg border border-ok-bd bg-ok-bg px-3.5 py-3 text-ok-fg">
          <h3 className="mb-1.5 flex items-center gap-1.5 text-[11px] font-semibold tracking-[0.06em] uppercase"><Check className="size-3.5" aria-hidden />Résultat attendu</h3>
          <Markdown source={etape.resultatAttendu} />
        </section>
      )}
    </>
  );
}

/** Résolutions types proposées pour une étape en échec (« Problème connu ? »). */
function ResolutionsProposees({ etape, liees, peutAgir, actions }: { etape: Etape; liees: ResolutionType[]; peutAgir: boolean; actions: ActionsEtape }) {
  return (
    <section>
      <h3 className="mb-2 flex items-center gap-1.5 text-sm font-semibold"><CircleHelp className="size-4" aria-hidden />Problème connu ?</h3>
      <ul className="grid gap-2">
        {liees.map((r) => (
          <li key={r.id} className="rounded-lg border border-warn-bd bg-warn-bg/40 p-3">
            <p className="flex items-center gap-2 font-semibold"><Wrench className="size-4 text-warn-solid" aria-hidden />{r.titre}</p>
            {r.symptomes.trim() && <Markdown className="mt-1 text-fg-muted" source={`**Symptômes** : ${r.symptomes}`} />}
            <div className="mt-2 flex flex-wrap items-center gap-3">
              <Button variante="primaire" taille="sm" disabled={!peutAgir} icone={<Wrench className="size-4" aria-hidden />} onClick={() => void actions.appliquerResolution(etape, r.id)}>Appliquer cette résolution</Button>
              {!peutAgir && <span className="flex items-center gap-1 text-xs text-fg-subtle"><Lock className="size-3.5" aria-hidden />Réservé à l’exécutant ou au pilote.</span>}
            </div>
          </li>
        ))}
      </ul>
    </section>
  );
}

interface Props {
  mep: Mep;
  etape: Etape;
  droits: DroitsEtape;
  /** Résolutions types liées à l'étape. */
  liees: ResolutionType[];
  /** Résolution en cours de déroulé, s'il y en a une. */
  enCours: ExecutionResolution | undefined;
  actions: ActionsEtape;
}

/**
 * Colonne centrale de l'espace d'une étape : les instructions, ou, en cas d'échec, le déroulé de résolution
 * (résolutions proposées, résolution en cours, reprise par le pilote) avec les instructions repliées.
 */
export function EtapeInstructions({ mep, etape, droits, liees, enCours, actions }: Props) {
  const { peutDemarrer, peutAgirResolution, enEchec, resolutionReussie, resolutionEchouee } = droits;
  const pilote = mep.droits.estPilote;

  return (
    <>
      {resolutionReussie && (
        <Alerte
          ton="ok"
          icone={CheckCircle2}
          role="status"
          titre="Résolution réussie : l’étape est de nouveau à faire."
          action={peutDemarrer ? (
            <Button variante="primaire" icone={<RotateCcw className="size-4" aria-hidden />} onClick={() => void actions.demarrer(etape)}>Redémarrer l’étape</Button>
          ) : undefined}
        >
          {!peutDemarrer && etape.disponibilite.libelle}
        </Alerte>
      )}
      {resolutionEchouee && (
        <Alerte
          ton="danger"
          icone={OctagonAlert}
          role="alert"
          titre={mep.phase === 'RetourArriere' ? 'Résolution en échec : retour arrière déclenché.' : 'Résolution en échec.'}
          action={mep.phase === 'RetourArriere' ? <Link to="?onglet=retour" className="font-semibold underline">Ouvrir l’onglet Retour arrière</Link> : undefined}
        >
          {mep.phase === 'RetourArriere' ? 'Le déploiement est arrêté : suivez le plan de retour arrière.' : 'Point de non-retour franchi : l’étape reste en échec, prévenez le pilote.'}
        </Alerte>
      )}
      {enEchec ? (
        <>
          {!resolutionEchouee && (
            <Alerte ton="danger" icone={OctagonAlert} role="alert" titre="Arrêter, prévenir le pilote, ne rien improviser.">
              {etape.preuve.trim() ? <>Preuve : {etape.preuve}</> : 'L’étape est en échec.'}
            </Alerte>
          )}
          {enCours ? (
            <ResolutionDeroule resolution={enCours} peutAgir={peutAgirResolution} actions={actions} />
          ) : !resolutionEchouee && (liees.length > 0 ? (
            <ResolutionsProposees etape={etape} liees={liees} peutAgir={peutAgirResolution} actions={actions} />
          ) : (
            <Alerte ton="warn" icone={OctagonAlert}>Aucune résolution prévue : arrêter et prévenir le pilote.</Alerte>
          ))}
          {pilote && !enCours && !resolutionEchouee && (
            <div><Button icone={<Play className="size-4" aria-hidden />} onClick={() => void actions.autoriserReprise(etape)}>Autoriser la reprise</Button></div>
          )}
          <details className="rounded-lg border border-line bg-surface px-3 py-2">
            <summary className="cursor-pointer text-sm font-medium">Instructions de l’étape</summary>
            <div className="mt-3 grid gap-4"><InstructionsEtape etape={etape} /></div>
          </details>
        </>
      ) : <InstructionsEtape etape={etape} />}
    </>
  );
}
