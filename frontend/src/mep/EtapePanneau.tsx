import { OctagonAlert } from 'lucide-react';
import { useContext, useState } from 'react';
import { AuthContext } from '../auth';
import { estAVous, nomParticipant } from '../domaine';
import { LIBELLE_CRITICITE } from '../format';
import { useDraft } from '../hooks';
import { metaEtape } from '../statuts';
import type { Etape, ExecutionResolution, Mep, PieceJointe, ResolutionType, ResultatControle } from '../types';
import { Alerte } from '../ui/Alerte';
import { ConfirmDialog } from '../ui/Dialog';
import { Markdown } from '../ui/Instructions';
import { Field, Textarea } from '../ui/Input';
import { Badge, StatusPill } from '../ui/StatusPill';
import { BadgesEtape } from './BadgesEtape';
import { droitsEtape, resolutionsDeLEtape } from './droitsEtape';
import { EtapeInfos } from './etape/EtapeInfos';
import { EtapeInstructions } from './etape/EtapeInstructions';
import { EtapeRetour } from './etape/EtapeRetour';
import { Surcouche } from './graphe/EspaceEtape';
import { estSimplifiee, resolutionsParSysteme } from './typeMep';

export interface ActionsEtape {
  demarrer: (e: Etape) => Promise<boolean | void> | void;
  terminer: (
    e: Etape,
    resultat: 'Reussie' | 'Echec',
    preuve: string,
    commentaire?: string,
    /** Résultat constaté d'un test unitaire réussi. */
    resultatConstate?: string,
  ) => Promise<boolean | void> | void;
  commenter: (e: Etape, commentaire: string) => Promise<boolean | void> | void;
  autoriserReprise: (e: Etape) => Promise<boolean | void> | void;
  prendreLaMain: (e: Etape, motif: string) => Promise<boolean | void> | void;
  autoriserNonRetour: (e: Etape) => Promise<boolean | void> | void;
  deposerPiece: (e: Etape, fichier: File, signaler: (message: string) => void) => Promise<boolean>;
  supprimerPiece: (p: PieceJointe, signaler: (message: string) => void) => Promise<boolean>;
  appliquerResolution: (e: Etape, resolutionTypeId: string) => Promise<boolean | void> | void;
  controlerCorrection: (
    r: ExecutionResolution,
    ordre: number,
    resultat: ResultatControle,
    observation: string,
  ) => Promise<boolean | void> | void;
  controlerVerification: (
    r: ExecutionResolution,
    resultat: ResultatControle,
    observation: string,
  ) => Promise<boolean | void> | void;
}

interface Props {
  mep: Mep;
  etape: Etape;
  /** Résolutions types actives (pour afficher celles liées à l'étape). */
  resolutionsTypes: ResolutionType[];
  actions: ActionsEtape;
  onSelectionner: (id: string) => void;
  onFermer?: () => void;
  /** Preuve déjà saisie pour cette étape avant d'en ouvrir une autre (gardée par le parent). */
  preuveSaisie?: string;
  onPreuveSaisie?: (preuve: string) => void;
}

/**
 * Espace d'une étape en exécution.
 * Gauche : état, exécutant, personnes à appeler, systèmes, liens. Centre : instructions (ou déroulé de résolution en
 * cas d'échec). Droite : le retour (preuve, pièces jointes, commentaire) et les actions.
 * Démarrer : tout participant relié à un compte, le pilote ou un administrateur ; il en devient l'exécutant.
 * Les droits sont calculés par `droitsEtape`, les colonnes rendues par `EtapeInfos`, `EtapeInstructions` et `EtapeRetour`.
 */
export function EtapePanneau({
  mep,
  etape,
  resolutionsTypes,
  actions,
  onSelectionner,
  onFermer = () => undefined,
  preuveSaisie = '',
  onPreuveSaisie,
}: Props) {
  const [preuve, setPreuveLocale] = useState(preuveSaisie);
  const setPreuve = (p: string) => {
    setPreuveLocale(p);
    onPreuveSaisie?.(p);
  };
  const [confirmation, setConfirmation] = useState<'reussie' | 'echec' | 'main' | null>(null);
  const [motifMain, setMotifMain] = useState('');
  const [constate, setConstate] = useState('');
  const testUnitaire = etape.fin === 'TestUnitaire';
  const commentaire = useDraft<string>(etape.commentaire ?? '');
  const admin = useContext(AuthContext)?.utilisateur?.estAdmin ?? false;

  const meta = metaEtape(etape);
  const droits = droitsEtape(mep, etape, admin);
  const nomExec = nomParticipant(mep, etape.executantId);
  const { enCours, historique } = resolutionsDeLEtape(mep, etape);
  // MEP simplifiée : pas de résolutions liées, toutes celles des systèmes de l'étape sont proposées (regroupées par système).
  const groupes = estSimplifiee(mep) ? resolutionsParSysteme(mep, etape, resolutionsTypes) : null;
  const liees = groupes
    ? groupes.flatMap((g) => g.resolutions)
    : etape.resolutionIds
        .map((id) => resolutionsTypes.find((r) => r.id === id))
        .filter((r): r is ResolutionType => !!r);
  const commentaireAEnvoyer = commentaire.dirty ? commentaire.value.trim() : undefined;

  const barre = (
    <div className="flex flex-wrap items-center gap-x-3 gap-y-1.5">
      <span className="rounded-md bg-surface-2 px-2 py-0.5 font-mono text-sm font-bold">{etape.code}</span>
      <StatusPill ton={meta.ton} icone={meta.Icone} anime={meta.anime}>
        {meta.libelle}
      </StatusPill>
      {estAVous(mep, etape) && (
        <Badge ton="run" className="text-xs">
          À vous
        </Badge>
      )}
      <BadgesEtape etape={etape} />
      <h2 className="min-w-0 text-lg leading-snug font-semibold tracking-tight">
        {etape.action || 'Étape sans libellé'}
      </h2>
      {(etape.criticite || etape.dureeEstimeeMinutes !== null) && (
        <span className="text-[13px] text-fg-muted">
          {etape.criticite && LIBELLE_CRITICITE[etape.criticite]}
          {etape.dureeEstimeeMinutes !== null && (
            <>
              {etape.criticite && ' · '}
              {etape.dureeEstimeeMinutes} min
            </>
          )}
        </span>
      )}
    </div>
  );

  return (
    <>
      <Surcouche
        etiquette={`Étape ${etape.code}`}
        onDemandeFermeture={onFermer}
        barre={barre}
        gauche={
          <EtapeInfos
            mep={mep}
            etape={etape}
            liees={liees}
            titreResolutions={groupes ? 'Résolutions des systèmes' : undefined}
            historique={historique}
            peutPrendreLaMain={droits.peutPrendreLaMain}
            onPrendreLaMain={() => setConfirmation('main')}
            onSelectionner={onSelectionner}
          />
        }
        centre={
          <EtapeInstructions
            mep={mep}
            etape={etape}
            droits={droits}
            liees={liees}
            groupes={groupes}
            enCours={enCours}
            actions={actions}
          />
        }
        droite={
          <EtapeRetour
            mep={mep}
            etape={etape}
            droits={droits}
            preuve={preuve}
            onPreuve={setPreuve}
            commentaire={commentaire}
            actions={actions}
            onTerminer={setConfirmation}
          />
        }
      />

      <ConfirmDialog
        open={confirmation === 'reussie'}
        onOpenChange={(o) => {
          if (!o) setConfirmation(null);
        }}
        titre={`${etape.code} réussie ?`}
        description={
          testUnitaire
            ? 'Test unitaire : saisissez le résultat que vous avez constaté.'
            : 'Simple vérification : confirmez que vous avez contrôlé le résultat.'
        }
        libelleConfirmer="J’ai vérifié et obtenu le résultat attendu"
        variante="succes"
        taille="md"
        desactive={testUnitaire && !constate.trim()}
        onConfirmer={async () => {
          const r = testUnitaire
            ? await actions.terminer(etape, 'Reussie', preuve.trim(), commentaireAEnvoyer, constate.trim())
            : await actions.terminer(etape, 'Reussie', preuve.trim(), commentaireAEnvoyer);
          if (r !== false) {
            setPreuve('');
            setConstate('');
          }
          return r;
        }}
      >
        {etape.resultatAttendu.trim() && (
          <div className="rounded-lg border border-ok-bd bg-ok-bg px-3.5 py-3 text-sm text-ok-fg">
            <p className="text-[11px] font-semibold tracking-wide uppercase">Résultat attendu</p>
            <Markdown source={etape.resultatAttendu} />
          </div>
        )}
        {testUnitaire && (
          <Field label="Résultat constaté *" className="mt-4">
            {(id) => (
              <Textarea
                id={id}
                rows={3}
                value={constate}
                onChange={(e) => setConstate(e.target.value)}
                placeholder="ex. GET /sante renvoie 200 avec la version 2.4.0."
                autoFocus
              />
            )}
          </Field>
        )}
      </ConfirmDialog>

      <ConfirmDialog
        open={confirmation === 'echec'}
        onOpenChange={(o) => {
          if (!o) setConfirmation(null);
        }}
        titre={`Déclarer l’échec de ${etape.code} ?`}
        libelleConfirmer="Déclarer l’échec"
        variante="danger"
        onConfirmer={async () => {
          const r = await actions.terminer(etape, 'Echec', preuve.trim(), commentaireAEnvoyer);
          if (r !== false) setPreuve('');
          return r;
        }}
      >
        <Alerte ton="danger" icone={OctagonAlert}>
          Arrêtez et prévenez le pilote. Les résolutions liées vous seront proposées.
        </Alerte>
      </ConfirmDialog>

      <ConfirmDialog
        open={confirmation === 'main'}
        onOpenChange={(o) => {
          if (!o) {
            setConfirmation(null);
            setMotifMain('');
          }
        }}
        titre={`Prendre la main sur ${etape.code} ?`}
        description={`Vous poursuivrez cette étape${nomExec ? ` à la place de ${nomExec}` : ''}. Tracé au journal.`}
        libelleConfirmer="Prendre la main"
        variante="primaire"
        taille="md"
        desactive={!motifMain.trim()}
        onConfirmer={async () => {
          const r = await actions.prendreLaMain(etape, motifMain.trim());
          if (r !== false) setMotifMain('');
          return r;
        }}
      >
        <Field label="Pourquoi prenez-vous la main ? *">
          {(id) => (
            <Textarea
              id={id}
              value={motifMain}
              onChange={(e) => setMotifMain(e.target.value)}
              rows={3}
              placeholder={nomExec ? `ex. ${nomExec} est injoignable.` : 'ex. L’exécutant est injoignable.'}
              autoFocus
            />
          )}
        </Field>
      </ConfirmDialog>
    </>
  );
}
