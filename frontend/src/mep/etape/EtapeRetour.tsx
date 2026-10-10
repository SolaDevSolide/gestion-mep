import { Check, Flag, Play, Save, ShieldCheck, XCircle } from 'lucide-react';
import { formatDateHeure } from '../../format';
import type { useDraft } from '../../hooks';
import type { Etape, Mep } from '../../types';
import { Alerte } from '../../ui/Alerte';
import { Button } from '../../ui/Button';
import { ZoneAuto } from '../../ui/EditeurBlocs';
import { Field, Textarea } from '../../ui/Input';
import { PiecesJointes } from '../../ui/PiecesJointes';
import type { DroitsEtape } from '../droitsEtape';
import type { ActionsEtape } from '../EtapePanneau';
import { Bloc } from '../graphe/EspaceEtape';

/** Point de non-retour : demande d'autorisation au pilote, ou rappel une fois franchi. */
function PointDeNonRetour({ etape, droits, pilote, actions }: { etape: Etape; droits: DroitsEtape; pilote: boolean; actions: ActionsEtape }) {
  const { nonRetourAttendu, planAgissant } = droits;
  return (
    <>
      {etape.pointDeNonRetour && etape.etat === 'AFaire' && planAgissant && (
        <Alerte
          ton="warn"
          icone={Flag}
          role="alert"
          titre="Point de non-retour"
          action={nonRetourAttendu && pilote ? (
            <Button variante="primaire" taille="sm" icone={<ShieldCheck className="size-4" aria-hidden />} onClick={() => void actions.autoriserNonRetour(etape)}>Autoriser le passage</Button>
          ) : undefined}
        >
          Après le démarrage, <strong>plus de retour arrière</strong>.
          {nonRetourAttendu
            ? (pilote ? ' Autorisez le passage.' : ' En attente de l’accord du pilote.')
            : ` Autorisé par ${etape.nonRetourAutorise?.par ?? 'le pilote'}${etape.nonRetourAutorise ? ` le ${formatDateHeure(etape.nonRetourAutorise.le)}` : ''}.`}
        </Alerte>
      )}
      {etape.pointDeNonRetour && etape.etat !== 'AFaire' && (
        <p className="flex items-center gap-1.5 text-[13px] font-medium text-warn-fg"><Flag className="size-4 text-warn-solid" aria-hidden />Point de non-retour franchi.</p>
      )}
    </>
  );
}

/** Commentaire de l'étape : saisie (brouillon) si autorisé, sinon simple lecture. */
function CommentaireEtape({ etape, peutCommenter, commentaire, actions }: { etape: Etape; peutCommenter: boolean; commentaire: ReturnType<typeof useDraft<string>>; actions: ActionsEtape }) {
  if (!peutCommenter) {
    return etape.commentaire?.trim() ? (
      <Bloc titre="Commentaire"><p className="rounded-md bg-surface-2/60 px-2.5 py-1.5 text-sm whitespace-pre-line">{etape.commentaire}</p></Bloc>
    ) : null;
  }
  return (
    <div className="grid gap-1.5">
      <label htmlFor={`commentaire-${etape.id}`} className="text-[13px] font-medium">Commentaire <span className="font-normal text-fg-subtle">(facultatif)</span></label>
      <ZoneAuto
        id={`commentaire-${etape.id}`}
        value={commentaire.value}
        onChange={(e) => commentaire.setValeur(e.target.value)}
        placeholder="Remarque pour le pilote, la suite, le bilan…"
        className="border border-line-strong bg-surface text-fg placeholder:text-fg-subtle"
      />
      {commentaire.dirty && (
        <div className="flex items-center gap-2">
          <Button taille="sm" icone={<Save className="size-4" aria-hidden />} onClick={() => void (async () => { const ok = await actions.commenter(etape, commentaire.value.trim()); if (ok !== false) commentaire.reset(); })()}>Enregistrer le commentaire</Button>
          <Button taille="sm" variante="fantome" onClick={commentaire.reset}>Annuler</Button>
        </div>
      )}
    </div>
  );
}

interface Props {
  mep: Mep;
  etape: Etape;
  droits: DroitsEtape;
  /** Preuve en cours de saisie. */
  preuve: string;
  onPreuve: (preuve: string) => void;
  /** Brouillon du commentaire de l'étape. */
  commentaire: ReturnType<typeof useDraft<string>>;
  actions: ActionsEtape;
  /** Demande la confirmation de fin d'étape (réussie ou en échec). */
  onTerminer: (resultat: 'reussie' | 'echec') => void;
}

/**
 * Colonne de droite de l'espace d'une étape : point de non-retour, preuve, pièces jointes, commentaire
 * et barre d'actions (Démarrer, Réussie, Échec).
 */
export function EtapeRetour({ mep, etape, droits, preuve, onPreuve, commentaire, actions, onTerminer }: Props) {
  const { peutDemarrer, peutTerminer, peutCommenter, resolutionReussie } = droits;
  const pilote = mep.droits.estPilote;
  const pieces = mep.piecesJointes.filter((p) => p.etapeId === etape.id);

  return (
    <>
      <PointDeNonRetour etape={etape} droits={droits} pilote={pilote} actions={actions} />

      {peutTerminer ? (
        <Field label="Preuve : ce que vous avez constaté" hint="Facultative si l’étape réussit, obligatoire en cas d’échec.">
          {(id) => <Textarea id={id} rows={8} value={preuve} onChange={(e) => onPreuve(e.target.value)} placeholder="Sortie de commande, capture décrite, constat…" className="min-h-40" />}
        </Field>
      ) : etape.preuve.trim() && etape.etat === 'Reussie' && (
        <Bloc titre="Preuve"><p className="rounded-md bg-surface-2/60 px-2.5 py-1.5 text-sm whitespace-pre-line">{etape.preuve}</p></Bloc>
      )}

      <PiecesJointes
        compact
        lectureSeule={mep.phase === 'Cloturee'}
        pilote={pilote}
        pieces={pieces}
        onDeposer={(f, signaler) => actions.deposerPiece(etape, f, signaler)}
        onSupprimer={(p, signaler) => actions.supprimerPiece(p, signaler)}
      />

      <CommentaireEtape etape={etape} peutCommenter={peutCommenter} commentaire={commentaire} actions={actions} />

      {((peutDemarrer && !resolutionReussie) || peutTerminer) && (
        <div className="sticky bottom-0 -mx-4 -mb-4 grid gap-2 border-t border-line bg-surface px-4 py-3">
          {peutDemarrer && !resolutionReussie && (
            <Button variante="primaire" taille="lg" className="h-12 w-full" icone={<Play className="size-5" aria-hidden />} onClick={() => void actions.demarrer(etape)}>Démarrer</Button>
          )}
          {peutTerminer && (
            <div className="grid grid-cols-2 gap-2">
              <Button variante="succes" taille="lg" className="h-12" icone={<Check className="size-5" aria-hidden />} onClick={() => onTerminer('reussie')}>Réussie</Button>
              <Button variante="danger" taille="lg" className="h-12" icone={<XCircle className="size-5" aria-hidden />} disabled={!preuve.trim()} onClick={() => onTerminer('echec')}>Échec</Button>
            </div>
          )}
        </div>
      )}
    </>
  );
}
