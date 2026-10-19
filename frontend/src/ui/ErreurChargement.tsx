import { ArrowLeft, Copy, OctagonAlert, RotateCw } from 'lucide-react';
import { useContext } from 'react';
import { Link } from 'react-router-dom';
import { ApiError } from '../api';
import { AuthContext } from '../auth';
import { Button } from './Button';
import { cn } from './cn';
import { useToast } from './Toast';

/** Ce qu'une erreur de chargement veut dire pour l'utilisateur, d'après le code de la réponse. */
export interface CauseErreur {
  /** Code HTTP (0 : réseau coupé), null pour une erreur hors réponse du serveur. */
  statut: number | null;
  /** Phrase complète (variantes bloc et page). */
  message: string;
  /** Forme courte, dans une phrase (variante ligne) : « le serveur est injoignable », « erreur 500 ». */
  court: string;
  /** Réessayer a un sens (pas pour un accès refusé ni un objet supprimé). */
  reessayable: boolean;
}

const sansPoint = (t: string) => t.trim().replace(/[.\s]+$/, '');

/**
 * Message selon la cause : réseau coupé, serveur en cours de mise à jour (502, 503, 504), erreur interne (500, avec le
 * message de l'API s'il y en a un), accès refusé (403), objet supprimé (404 : `introuvable`).
 */
export function causeErreur(e: unknown, introuvable?: string): CauseErreur {
  if (!(e instanceof ApiError)) {
    const texte = e instanceof Error ? e.message : typeof e === 'string' ? e : '';
    return {
      statut: null,
      message: texte || 'Chargement impossible.',
      court: 'erreur inattendue',
      reessayable: true,
    };
  }
  const s = e.status;
  if (s === 0)
    return {
      statut: 0,
      message: 'Le serveur est injoignable. Vérifiez votre connexion.',
      court: 'le serveur est injoignable',
      reessayable: true,
    };
  if (s === 502 || s === 503 || s === 504)
    return {
      statut: s,
      message: `Le serveur ne répond pas (${s}). Cela arrive pendant une mise à jour de Gestion MEP : réessayez dans un instant.`,
      court: `le serveur ne répond pas (${s})`,
      reessayable: true,
    };
  if (s === 403)
    return {
      statut: s,
      message: 'Vous n’avez pas accès à ces informations.',
      court: 'accès refusé',
      reessayable: false,
    };
  if (s === 404)
    return {
      statut: s,
      message: introuvable ?? 'Ces informations n’existent pas ou ont été supprimées.',
      court: 'introuvable',
      reessayable: false,
    };
  // Message de l'API s'il y en a un (le client met « Erreur 500. » faute de corps lisible).
  const lu = e.message && !/^Erreur \d+\.?$/.test(e.message) ? sansPoint(e.message) : '';
  if (s >= 500)
    return {
      statut: s,
      message: `${lu || 'Erreur interne du serveur'} (${s}).`,
      court: `erreur ${s}`,
      reessayable: true,
    };
  return { statut: s, message: `${lu || 'Requête refusée'} (${s}).`, court: `erreur ${s}`, reessayable: true };
}

/** Phrase de la variante ligne : « Catalogue des systèmes non chargé (erreur 500). », « MEP non chargées : le serveur… ». */
export function phraseLigne(titre: string, cause: CauseErreur) {
  return cause.court.startsWith('erreur ') ? `${titre} (${cause.court}).` : `${titre} : ${cause.court}.`;
}

function DetailsTechniques({ erreur, cause }: { erreur: unknown; cause: CauseErreur }) {
  const toast = useToast();
  const api = erreur instanceof ApiError ? erreur : null;
  const premiere = api ? `${api.requete || 'Requête'} → ${api.status}` : 'Erreur hors réponse du serveur';
  const seconde = api ? `${api.date.toLocaleString('fr-FR')} · ${api.message}` : cause.message;
  const texte = [premiere, seconde, ...(api?.erreurs ?? [])].join('\n');
  async function copier() {
    try {
      await navigator.clipboard.writeText(texte);
      toast.succes('Détails copiés.');
    } catch {
      toast.erreur('Copie impossible : sélectionnez le texte des détails.');
    }
  }
  return (
    <details className="mt-2 text-xs">
      <summary className="cursor-pointer font-medium select-none">
        Détails techniques <span className="font-normal opacity-80">(administrateurs)</span>
      </summary>
      <pre className="mt-1.5 border-t border-current/20 pt-1.5 font-mono text-[11px] whitespace-pre-wrap">{texte}</pre>
      <button
        type="button"
        onClick={() => void copier()}
        className="mt-1.5 inline-flex items-center gap-1 rounded font-medium hover:underline"
      >
        <Copy className="size-3.5" aria-hidden />
        Copier les détails
      </button>
    </details>
  );
}

interface Props {
  /** Erreur reçue (`ApiError` de préférence : son code choisit le message). */
  erreur: unknown;
  /**
   * `bloc` : à la place du contenu d'une section ou d'une carte. `ligne` : dans une liste, un menu ou un champ.
   * `page` : quand l'objet principal de la page manque.
   */
  variante?: 'bloc' | 'ligne' | 'page';
  /**
   * Bloc et page : le titre (« Impossible de charger le catalogue des systèmes »). Ligne : le début de la phrase
   * (« Catalogue des systèmes non chargé »), complété par la cause.
   */
  titre: string;
  /** Phrase ajoutée après la cause (« Votre saisie n’est pas touchée. », « Ajouter un fichier reste possible. »). */
  complement?: string;
  /** Relance le chargement (`recharger` de `useAsync`) ; sans lui, pas de bouton « Réessayer ». */
  onReessayer?: () => unknown;
  /** Rechargement en cours : le bouton « Réessayer » tourne. */
  enCours?: boolean;
  /** Message d'un 404 (« Cette MEP n’existe pas ou a été supprimée. »). */
  introuvable?: string;
  /** Lien de repli (variante page) : « Retour à la liste ». */
  retour?: { libelle: string; vers: string };
  className?: string;
}

/**
 * Erreur de chargement, avec « Réessayer » branché sur le rechargement. Jamais de liste vide à la place d'une erreur :
 * une liste vide ne s'affiche que si le serveur a répondu une liste vide. Un 401 n'affiche rien (le dialogue « Session
 * expirée » s'en charge). Les détails techniques (requête, code, heure) ne sont montrés qu'aux administrateurs.
 */
export function ErreurChargement({
  erreur,
  variante = 'bloc',
  titre,
  complement,
  onReessayer,
  enCours,
  introuvable,
  retour,
  className,
}: Props) {
  const admin = useContext(AuthContext)?.utilisateur?.estAdmin ?? false;
  if (erreur instanceof ApiError && erreur.status === 401) return null;
  const cause = causeErreur(erreur, introuvable);
  const reessayer = onReessayer && cause.reessayable && (
    <Button
      taille="sm"
      variante={variante === 'page' ? 'primaire' : 'secondaire'}
      icone={<RotateCw className="size-4" aria-hidden />}
      chargement={enCours}
      action={onReessayer}
    >
      Réessayer
    </Button>
  );

  if (variante === 'ligne')
    return (
      <div
        role="alert"
        className={cn(
          'flex flex-wrap items-center gap-x-3 gap-y-1.5 rounded-lg border border-danger-bd bg-danger-bg px-3 py-2 text-sm text-danger-fg',
          className,
        )}
      >
        <OctagonAlert className="size-4 shrink-0" aria-hidden />
        <p className="min-w-0 flex-1">
          {phraseLigne(titre, cause)}
          {complement && ` ${complement}`}
        </p>
        {reessayer}
        {admin && (
          <div className="basis-full">
            <DetailsTechniques erreur={erreur} cause={cause} />
          </div>
        )}
      </div>
    );

  if (variante === 'page')
    return (
      <div role="alert" className={cn('mx-auto flex max-w-lg flex-col items-center px-6 py-16 text-center', className)}>
        <span className="flex size-12 items-center justify-center rounded-full bg-danger-bg text-danger-solid">
          <OctagonAlert className="size-6" aria-hidden />
        </span>
        <h1 className="mt-4 text-xl font-semibold tracking-tight">{titre}</h1>
        <p className="mt-2 text-sm text-fg-muted">
          {cause.message}
          {complement && ` ${complement}`}
        </p>
        {(reessayer || retour) && (
          <div className="mt-5 flex flex-wrap justify-center gap-2">
            {reessayer}
            {retour && (
              <Link
                to={retour.vers}
                className="inline-flex h-8 items-center gap-1.5 rounded-md border border-line-strong bg-surface px-2.5 text-[13px] font-medium text-fg shadow-sm transition-colors duration-150 hover:bg-surface-2"
              >
                <ArrowLeft className="size-4" aria-hidden />
                {retour.libelle}
              </Link>
            )}
          </div>
        )}
        {admin && (
          <div className="mt-4 w-full text-left text-fg-muted">
            <DetailsTechniques erreur={erreur} cause={cause} />
          </div>
        )}
      </div>
    );

  return (
    <div
      role="alert"
      className={cn(
        'flex items-start gap-3 rounded-lg border border-danger-bd bg-danger-bg px-3.5 py-3 text-sm text-danger-fg',
        className,
      )}
    >
      <OctagonAlert className="mt-0.5 size-4.5 shrink-0" aria-hidden />
      <div className="min-w-0 flex-1">
        <p className="font-semibold">{titre}</p>
        <p className="mt-0.5">
          {cause.message}
          {complement && ` ${complement}`}
        </p>
        {reessayer && <div className="mt-2.5">{reessayer}</div>}
        {admin && <DetailsTechniques erreur={erreur} cause={cause} />}
      </div>
    </div>
  );
}
