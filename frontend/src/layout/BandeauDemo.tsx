import { FlaskConical, OctagonAlert, RotateCcw, Square, Users } from 'lucide-react';
import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api';
import { useAuth } from '../auth';
import { useAsync } from '../hooks';
import { Alerte } from '../ui/Alerte';
import { ConfirmDialog } from '../ui/Dialog';
import { useToast } from '../ui/Toast';
import type { EtatSimulation } from '../types';

/** Rafraîchissement de l'état de la simulation pendant qu'elle tourne (elle s'arrête seule au bout de quelques minutes). */
const SONDAGE_SIMULATION_MS = 5000;

/** Hauteur du bandeau, réservée en haut de page par la variable CSS --bandeau-demo (menu et en-têtes collants). */
const HAUTEUR = '2.25rem';

/** Écran de connexion affiché après une réinitialisation (message de confirmation). */
export const URL_APRES_REINITIALISATION = '/connexion?demo=reinitialisee';

function rechargerPage(url: string) {
  window.location.assign(url);
}

/**
 * Bandeau permanent du mode démonstration, sur toutes les pages (écran de connexion compris) : il n'apparaît que si le
 * serveur déclare la démonstration active (/api/demo). Les administrateurs peuvent y réinitialiser la démonstration,
 * après confirmation ; la page est alors rechargée sur l'écran de connexion (toutes les sessions sont fermées).
 */
export function BandeauDemo({
  recharger = rechargerPage,
}: {
  /** Rechargement complet de la page (remplaçable dans les tests). */
  recharger?: (url: string) => void;
} = {}) {
  const { utilisateur } = useAuth();
  const { donnees: demo } = useAsync(() => api.demo(), []);
  const [confirmation, setConfirmation] = useState(false);
  const [erreur, setErreur] = useState('');
  const actif = Boolean(demo?.actif);
  const simulation = useSimulation(actif && Boolean(utilisateur));

  useEffect(() => {
    if (!actif) return;
    document.documentElement.style.setProperty('--bandeau-demo', HAUTEUR);
    return () => {
      document.documentElement.style.removeProperty('--bandeau-demo');
    };
  }, [actif]);

  if (!actif) return null;

  async function reinitialiser() {
    setErreur('');
    try {
      await api.reinitialiserDemo();
      recharger(URL_APRES_REINITIALISATION);
      return true;
    } catch (e) {
      setErreur(e instanceof Error ? e.message : 'Réinitialisation impossible.');
      return false;
    }
  }

  return (
    <>
      <output
        aria-label="Mode démonstration"
        className="sticky top-0 z-40 flex h-9 items-center justify-center gap-2 border-b border-warn-bd bg-warn-bg px-3 text-ui text-warn-fg print:hidden"
      >
        <FlaskConical className="size-4 shrink-0" aria-hidden />
        <p className="min-w-0 truncate">
          <strong className="font-semibold">Mode démonstration</strong>
          <span className="hidden sm:inline"> — données fictives, réinitialisables</span>
        </p>
        {utilisateur && simulation.etat && (
          <BoutonSimulation
            etat={simulation.etat}
            enCours={simulation.enCours}
            basculer={() => void simulation.basculer()}
          />
        )}
        {utilisateur?.estAdmin && (
          <button
            type="button"
            aria-label="Réinitialiser la démonstration"
            onClick={() => {
              setErreur('');
              setConfirmation(true);
            }}
            className="ml-1 inline-flex shrink-0 items-center gap-1.5 rounded-md border border-warn-bd bg-surface/60 px-2 py-0.5 text-xs font-medium transition-colors duration-150 hover:bg-surface focus-visible:ring-2 focus-visible:ring-warn-solid focus-visible:outline-none"
          >
            <RotateCcw className="size-3.5" aria-hidden />
            <span className="hidden md:inline">Réinitialiser la démonstration</span>
            <span className="md:hidden">Réinitialiser</span>
          </button>
        )}
      </output>
      <ConfirmDialog
        open={confirmation}
        onOpenChange={setConfirmation}
        titre="Réinitialiser la démonstration ?"
        description="La base revient exactement à son état initial."
        libelleConfirmer="Réinitialiser"
        variante="danger"
        onConfirmer={reinitialiser}
      >
        <ul className="list-disc space-y-1 pl-5 text-sm text-fg-muted">
          <li>Comptes, MEP, catalogue, résolutions types, critères, paramètres et réglages sont recréés.</li>
          <li>
            Toutes les modifications, pièces jointes et journaux depuis le début de la démonstration sont effacés.
          </li>
          <li>Tout le monde est déconnecté : reconnectez-vous ensuite avec un compte de démonstration.</li>
        </ul>
        {erreur && (
          <Alerte ton="danger" icone={OctagonAlert} role="alert" className="mt-4">
            {erreur}
          </Alerte>
        )}
      </ConfirmDialog>
    </>
  );
}

/** État de la simulation de collègues, relu régulièrement pendant qu'elle tourne. */
function useSimulation(active: boolean) {
  const [etat, setEtat] = useState<EtatSimulation | null>(null);
  const [enCours, setEnCours] = useState(false);
  const toast = useToast();
  const lire = useCallback(() => {
    // Lecture tolérante : sans simulation possible (serveur plus ancien…), pas de bouton.
    Promise.resolve()
      .then(() => api.simulationDemo())
      .then(setEtat, () => setEtat(null));
  }, []);
  useEffect(() => {
    if (active) lire();
  }, [active, lire]);
  useEffect(() => {
    if (!active || !etat?.actif) return;
    const t = window.setInterval(lire, SONDAGE_SIMULATION_MS);
    return () => window.clearInterval(t);
  }, [active, etat?.actif, lire]);
  async function basculer() {
    if (!etat) return;
    setEnCours(true);
    try {
      setEtat(await api.simulerCollegues(!etat.actif));
    } catch (e) {
      toast.erreur(e instanceof Error ? e.message : 'Simulation impossible.');
    } finally {
      setEnCours(false);
    }
  }
  return { etat, enCours, basculer };
}

/**
 * « Simuler des collègues » : Bob et Claire se connectent pour de vrai à la MEP « Facturation électronique » (présence,
 * verrous, modifications au journal, prise de main) ; un second clic, ou le délai écoulé, les fait partir.
 */
function BoutonSimulation({
  etat,
  enCours,
  basculer,
}: {
  etat: EtatSimulation;
  enCours: boolean;
  basculer: () => void;
}) {
  const classe =
    'ml-1 inline-flex shrink-0 items-center gap-1.5 rounded-md border border-warn-bd px-2 py-0.5 text-xs font-medium transition-colors duration-150 focus-visible:ring-2 focus-visible:ring-warn-solid focus-visible:outline-none disabled:opacity-60';
  if (!etat.actif)
    return (
      <button
        type="button"
        onClick={basculer}
        disabled={enCours}
        title={`${etat.collegues.join(' et ')} travaillent quelques minutes sur « ${etat.outil} » : présence, verrous, modifications, prise de main.`}
        className={`${classe} bg-surface/60 hover:bg-surface`}
      >
        <Users className="size-3.5" aria-hidden />
        <span className="hidden md:inline">Simuler des collègues</span>
        <span className="md:hidden">Simuler</span>
      </button>
    );
  return (
    <>
      <span className="hidden min-w-0 truncate lg:inline">
        {etat.collegues.join(' et ')} travaillent sur{' '}
        {etat.mepId ? (
          <Link to={`/meps/${etat.mepId}?onglet=etapes`} className="font-medium underline underline-offset-2">
            {etat.outil}
          </Link>
        ) : (
          etat.outil
        )}
      </span>
      <button
        type="button"
        onClick={basculer}
        disabled={enCours}
        className={`${classe} bg-warn-solid text-warn-solid-fg hover:opacity-90`}
      >
        <Square className="size-3 fill-current" aria-hidden />
        Arrêter la simulation
      </button>
    </>
  );
}
