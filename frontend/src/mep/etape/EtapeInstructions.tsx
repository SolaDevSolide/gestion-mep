import { Check, CheckCircle2, CircleHelp, Lock, OctagonAlert, Play, RotateCcw, Wrench } from 'lucide-react';
import { Link } from 'react-router-dom';
import { couleurSysteme } from '../../couleursSysteme';
import { LIBELLE_FIN } from '../../format';
import type { Etape, ExecutionResolution, Mep, ResolutionType } from '../../types';
import { Alerte } from '../../ui/Alerte';
import { BoutonDeclencherRepli } from '../ActionsPilote';
import { Button } from '../../ui/Button';
import { Instructions, Markdown } from '../../ui/Instructions';
import type { DroitsEtape } from '../droitsEtape';
import type { ActionsEtape } from '../EtapePanneau';
import { Bloc } from '../graphe/EspaceEtape';
import { ResolutionDeroule } from '../ResolutionDeroule';
import type { GroupeResolutions } from '../typeMep';

/** Instructions exactes de l'étape et résultat attendu, avec sa fin (test unitaire ou simple vérification). */
function InstructionsEtape({ etape }: { etape: Etape }) {
  return (
    <>
      <Bloc titre="Instructions exactes">
        <Instructions source={etape.instructions} vide="Aucune instruction renseignée." />
      </Bloc>
      {etape.resultatAttendu.trim() && (
        <section className="rounded-lg border border-ok-bd bg-ok-bg px-3.5 py-3 text-ok-fg">
          <h3 className="mb-1.5 flex items-center gap-1.5 text-[11px] font-semibold tracking-[0.06em] uppercase">
            <Check className="size-3.5" aria-hidden />
            Résultat attendu · {LIBELLE_FIN[etape.fin]}
          </h3>
          <Markdown source={etape.resultatAttendu} />
        </section>
      )}
    </>
  );
}

/** Carte d'une résolution type proposée, avec « Appliquer cette résolution ». */
function CarteResolution({
  r,
  etape,
  peutAgir,
  actions,
}: {
  r: ResolutionType;
  etape: Etape;
  peutAgir: boolean;
  actions: ActionsEtape;
}) {
  return (
    <li className="rounded-lg border border-warn-bd bg-warn-bg/40 p-3">
      <p className="flex items-center gap-2 font-semibold">
        <Wrench className="size-4 text-warn-solid" aria-hidden />
        {r.titre}
      </p>
      {r.symptomes.trim() && <Markdown className="mt-1 text-fg-muted" source={`**Symptômes** : ${r.symptomes}`} />}
      <div className="mt-2 flex flex-wrap items-center gap-3">
        <Button
          variante="primaire"
          taille="sm"
          disabled={!peutAgir}
          icone={<Wrench className="size-4" aria-hidden />}
          action={() => actions.appliquerResolution(etape, r.id)}
        >
          Appliquer cette résolution
        </Button>
        {!peutAgir && (
          <span className="flex items-center gap-1 text-xs text-fg-subtle">
            <Lock className="size-3.5" aria-hidden />
            Réservé à l’exécutant ou au pilote.
          </span>
        )}
      </div>
    </li>
  );
}

/**
 * Résolutions types proposées pour une étape en échec (« Problème connu ? ») : celles liées à l'étape, ou, pour une MEP
 * simplifiée, toutes celles des systèmes de l'étape regroupées par système (<paramref name="groupes"/>).
 */
function ResolutionsProposees({
  etape,
  liees,
  groupes,
  peutAgir,
  actions,
}: {
  etape: Etape;
  liees: ResolutionType[];
  groupes: GroupeResolutions[] | null;
  peutAgir: boolean;
  actions: ActionsEtape;
}) {
  const carte = (r: ResolutionType) => (
    <CarteResolution key={r.id} r={r} etape={etape} peutAgir={peutAgir} actions={actions} />
  );
  return (
    <section>
      <h3 className="mb-2 flex items-center gap-1.5 text-sm font-semibold">
        <CircleHelp className="size-4" aria-hidden />
        Problème connu ?
      </h3>
      {groupes ? (
        <>
          <p className="mb-3 text-[13px] text-fg-muted">
            MEP simplifiée : toutes les résolutions types des systèmes de l’étape sont proposées ({liees.length}).
            Choisissez celle qui correspond au problème constaté.
          </p>
          <div className="grid gap-4">
            {groupes.map((g) => (
              <section key={g.systeme ?? ''} aria-label={g.systeme ?? 'Tous les systèmes'}>
                <h4 className="mb-1.5 flex items-center gap-2 text-[11px] font-semibold tracking-[0.06em] text-fg-subtle uppercase">
                  {g.systeme && (
                    <i className="size-2 rounded-full" style={{ background: couleurSysteme(g.systeme) }} aria-hidden />
                  )}
                  {g.systeme ?? 'Tous les systèmes'} · {g.resolutions.length}
                </h4>
                <ul className="grid gap-2">{g.resolutions.map(carte)}</ul>
              </section>
            ))}
          </div>
        </>
      ) : (
        <ul className="grid gap-2">{liees.map(carte)}</ul>
      )}
    </section>
  );
}

interface Props {
  mep: Mep;
  etape: Etape;
  droits: DroitsEtape;
  /** Résolutions types liées à l'étape (MEP simplifiée : toutes celles de ses systèmes). */
  liees: ResolutionType[];
  /** MEP simplifiée : les mêmes résolutions, regroupées par système ; null pour une MEP complète. */
  groupes?: GroupeResolutions[] | null;
  /** Résolution en cours de déroulé, s'il y en a une. */
  enCours: ExecutionResolution | undefined;
  actions: ActionsEtape;
}

/**
 * Colonne centrale de l'espace d'une étape : les instructions, ou, en cas d'échec, le déroulé de résolution
 * (résolutions proposées, résolution en cours, reprise par le pilote) avec les instructions repliées.
 */
export function EtapeInstructions({ mep, etape, droits, liees, groupes = null, enCours, actions }: Props) {
  const { peutDemarrer, peutAgirResolution, enEchec, resolutionReussie, resolutionEchouee } = droits;
  const pilote = mep.droits.estPilote;
  const repliPossible = mep.retourArriereActive && mep.nonRetourFranchi === null;

  return (
    <>
      {resolutionReussie && (
        <Alerte
          ton="ok"
          icone={CheckCircle2}
          role="status"
          titre="Résolution réussie : l’étape est de nouveau à faire."
          action={
            peutDemarrer ? (
              <Button
                variante="primaire"
                icone={<RotateCcw className="size-4" aria-hidden />}
                action={() => actions.demarrer(etape)}
              >
                Redémarrer l’étape
              </Button>
            ) : undefined
          }
        >
          {!peutDemarrer && etape.disponibilite.libelle}
        </Alerte>
      )}
      {resolutionEchouee && mep.phase === 'RetourArriere' && (
        <Alerte
          ton="danger"
          icone={OctagonAlert}
          role="alert"
          titre="Résolution en échec : retour arrière déclenché."
          action={
            <Link to="?onglet=plan&plan=retour" className="font-semibold underline">
              Ouvrir l’onglet Retour arrière
            </Link>
          }
        >
          Le déploiement est arrêté : suivez le plan de retour arrière.
        </Alerte>
      )}
      {resolutionEchouee && mep.phase !== 'RetourArriere' && (
        <Alerte
          ton="danger"
          icone={OctagonAlert}
          role="alert"
          titre={pilote ? 'Résolution en échec : à vous de décider.' : 'Résolution en échec : le pilote décide.'}
          action={
            pilote && mep.phase === 'Execution' ? (
              <div className="flex flex-wrap gap-2">
                {repliPossible && <BoutonDeclencherRepli />}
                <Button
                  taille="sm"
                  icone={<Play className="size-4" aria-hidden />}
                  action={() => actions.autoriserReprise(etape)}
                >
                  Autoriser une nouvelle tentative
                </Button>
              </div>
            ) : undefined
          }
        >
          {pilote
            ? repliPossible
              ? 'L’étape reste en échec. Déclenchez le retour arrière, ou autorisez une nouvelle tentative de l’étape.'
              : 'L’étape reste en échec. Autorisez une nouvelle tentative, ou corrigez en avant.'
            : 'L’étape reste en échec : prévenez le pilote, il décide du retour arrière ou d’une nouvelle tentative.'}
        </Alerte>
      )}
      {enEchec ? (
        <>
          {!resolutionEchouee && (
            <Alerte
              ton="danger"
              icone={OctagonAlert}
              role="alert"
              titre="Arrêter, prévenir le pilote, ne rien improviser."
            >
              {etape.preuve.trim() ? <>Preuve : {etape.preuve}</> : 'L’étape est en échec.'}
            </Alerte>
          )}
          {enCours ? (
            <ResolutionDeroule resolution={enCours} peutAgir={peutAgirResolution} actions={actions} />
          ) : (
            !resolutionEchouee &&
            (liees.length > 0 ? (
              <ResolutionsProposees
                etape={etape}
                liees={liees}
                groupes={groupes}
                peutAgir={peutAgirResolution}
                actions={actions}
              />
            ) : (
              <Alerte ton="warn" icone={OctagonAlert}>
                {groupes
                  ? 'Aucune résolution type pour les systèmes de l’étape : arrêter et prévenir le pilote.'
                  : 'Aucune résolution prévue : arrêter et prévenir le pilote.'}
              </Alerte>
            ))
          )}
          {pilote && !enCours && !resolutionEchouee && (
            <div>
              <Button icone={<Play className="size-4" aria-hidden />} action={() => actions.autoriserReprise(etape)}>
                Autoriser la reprise
              </Button>
            </div>
          )}
          <details className="rounded-lg border border-line bg-surface px-3 py-2">
            <summary className="cursor-pointer text-sm font-medium">Instructions de l’étape</summary>
            <div className="mt-3 grid gap-4">
              <InstructionsEtape etape={etape} />
            </div>
          </details>
        </>
      ) : (
        <InstructionsEtape etape={etape} />
      )}
    </>
  );
}
