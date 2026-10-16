import {
  CircleArrowUp,
  CircleCheck,
  Download,
  ExternalLink,
  Loader2,
  Megaphone,
  OctagonAlert,
  RefreshCw,
  TriangleAlert,
  type LucideIcon,
} from 'lucide-react';
import { useEffect, useState } from 'react';
import { api } from '../api';
import { formatDate, formatDateHeure, type Ton } from '../format';
import { useAsync } from '../hooks';
import type { InfosApplication, MiseAJour } from '../types';
import { Alerte } from '../ui/Alerte';
import { Button } from '../ui/Button';
import { Card, CardHeader } from '../ui/Card';
import { ConfirmDialog } from '../ui/Dialog';
import { EnChargement, Squelette, SqueletteLignes } from '../ui/Squelette';
import { useToast } from '../ui/Toast';

/** Présentation du dernier statut du service de mise à jour (« pret » : rien à afficher). */
const PRESENTATION: Record<string, { ton: Ton; icone: LucideIcon; titre: string }> = {
  demandee: { ton: 'run', icone: Loader2, titre: 'Mise à jour en cours' },
  'en-cours': { ton: 'run', icone: Loader2, titre: 'Mise à jour en cours' },
  terminee: { ton: 'ok', icone: CircleCheck, titre: 'Dernière mise à jour réussie' },
  echec: { ton: 'danger', icone: OctagonAlert, titre: 'Dernière mise à jour en échec' },
  indisponible: { ton: 'warn', icone: TriangleAlert, titre: 'Service de mise à jour mal configuré' },
};

const DOC_MISE_A_JOUR = 'https://github.com/SolaDevSolide/gestion-mep/wiki/Exploiter#mettre-à-jour';

/** Version en cours, dernière version publiée sur GitHub et marche à suivre pour mettre à jour. */
/** Installation sans Docker : la mise à jour se fait sur le serveur avec le script d'installation (pas de bouton). */
function miseAJourSansDocker(installation: string | undefined, version: string | null | undefined) {
  const v = version ?? 'x.y.z';
  if (installation === 'linux')
    return `L’équipe informatique lance « sudo /opt/gestion-mep/installer.sh --version ${v} » sur le serveur. Le script fait une sauvegarde, installe la version et revient à l’ancienne si la nouvelle ne démarre pas.`;
  if (installation === 'windows')
    return `L’équipe informatique lance « installer.ps1 -Version ${v} » (dossier C:\\Program Files\\GestionMep, PowerShell administrateur) sur le serveur. Le script fait une sauvegarde, installe la version et revient à l’ancienne si la nouvelle ne démarre pas.`;
  return null;
}

export default function VersionTab() {
  const toast = useToast();
  const chargees = useAsync(() => api.application(), []);
  const [suivi, setSuivi] = useState<InfosApplication | null>(null);
  const donnees = suivi ?? chargees.donnees;
  const erreur = suivi ? '' : chargees.erreur;
  const [verifie, setVerifie] = useState<MiseAJour | null>(null);
  const [envoi, setEnvoi] = useState(false);
  const [confirmer, setConfirmer] = useState(false);
  const etat = verifie ?? donnees?.miseAJour ?? null;
  const installateur = etat?.installateur;
  // Version construite hors publication (« 0.0.0-dev ») : elle n'est pas comparée aux versions publiées.
  const developpement = donnees?.version.includes('-') ?? false;
  const installationEnCours = installateur?.statut === 'demandee' || installateur?.statut === 'en-cours';

  // Pendant l'installation, l'état est relu toutes les 3 secondes ; l'application redémarre au milieu (erreurs ignorées).
  useEffect(() => {
    if (!installationEnCours) return;
    const t = window.setInterval(() => {
      api
        .application()
        .then((a) => {
          setSuivi(a);
          setVerifie(null);
        })
        .catch(() => undefined);
    }, 3000);
    return () => window.clearInterval(t);
  }, [installationEnCours]);

  async function installer(): Promise<boolean> {
    if (!etat?.derniereVersion) return false;
    try {
      setVerifie(await api.installerMiseAJour(etat.derniereVersion));
      return true;
    } catch (e) {
      toast.erreur(e instanceof Error ? e.message : 'Mise à jour impossible.');
      return false;
    }
  }

  async function verifier() {
    setEnvoi(true);
    try {
      setVerifie(await api.verifierMisesAJour());
    } catch (e) {
      toast.erreur(e instanceof Error ? e.message : 'Vérification impossible.');
    } finally {
      setEnvoi(false);
    }
  }

  return (
    <div className="space-y-6">
      {erreur && (
        <Alerte ton="danger" icone={TriangleAlert} role="alert">
          {erreur}
        </Alerte>
      )}
      {!donnees && !erreur ? (
        <EnChargement libelle="Chargement de la version…">
          <Card className="p-5">
            <Squelette className="h-5 w-56" />
            <Squelette className="mt-2 h-4 w-96 max-w-full" />
            <SqueletteLignes lignes={3} className="mt-6 max-w-md" />
          </Card>
        </EnChargement>
      ) : (
        <Card className="apparition">
          <CardHeader
            titre="Version de l’application"
            description="Les nouvelles versions sont publiées sur GitHub ; l’application les vérifie deux fois par jour."
            action={
              etat?.active && (
                <Button
                  taille="sm"
                  icone={<RefreshCw className="size-4" aria-hidden />}
                  chargement={envoi}
                  onClick={() => void verifier()}
                >
                  Vérifier maintenant
                </Button>
              )
            }
          />
          <div className="grid gap-4 px-5 py-5">
            <dl className="grid grid-cols-[max-content_1fr] gap-x-6 gap-y-2 text-sm">
              <dt className="text-fg-subtle">Version en cours</dt>
              <dd className="font-mono font-semibold">{donnees?.version ?? '…'}</dd>
              {etat?.derniereVersion && (
                <>
                  <dt className="text-fg-subtle">Dernière version publiée</dt>
                  <dd>
                    <span className="font-mono font-semibold">{etat.derniereVersion}</span>
                    {etat.publieeLe && <span className="text-fg-subtle"> · {formatDate(etat.publieeLe, true)}</span>}
                  </dd>
                </>
              )}
              {etat?.verifieeLe && (
                <>
                  <dt className="text-fg-subtle">Dernière vérification</dt>
                  <dd>{formatDateHeure(etat.verifieeLe)}</dd>
                </>
              )}
            </dl>
            {etat && !etat.active && (
              <Alerte ton="later" icone={TriangleAlert}>
                La vérification des nouvelles versions est désactivée sur ce serveur (MisesAJour__Verifier=false).
              </Alerte>
            )}
            {etat?.erreur && (
              <Alerte ton="warn" icone={TriangleAlert}>
                {etat.erreur}
              </Alerte>
            )}
            {etat?.disponible && (
              <Alerte
                ton="run"
                icone={CircleArrowUp}
                titre={`La version ${etat.derniereVersion} est disponible`}
                action={
                  <span className="flex flex-wrap gap-x-4 gap-y-1">
                    {etat.notesUrl && (
                      <a
                        href={etat.notesUrl}
                        target="_blank"
                        rel="noopener noreferrer"
                        className="inline-flex items-center gap-1.5 font-medium underline-offset-2 hover:underline"
                      >
                        Voir les nouveautés <ExternalLink className="size-3.5" aria-hidden />
                      </a>
                    )}
                    <a
                      href={DOC_MISE_A_JOUR}
                      target="_blank"
                      rel="noopener noreferrer"
                      className="inline-flex items-center gap-1.5 font-medium underline-offset-2 hover:underline"
                    >
                      Comment mettre à jour <ExternalLink className="size-3.5" aria-hidden />
                    </a>
                  </span>
                }
              >
                {miseAJourSansDocker(chargees.donnees?.installation, etat?.derniereVersion) ??
                  (installateur?.present
                    ? 'Une sauvegarde est faite automatiquement avant toute modification de la base.'
                    : 'L’équipe informatique change MEP_IMAGE dans le fichier .env du serveur, puis relance l’application. Une sauvegarde est faite automatiquement avant toute modification de la base. Elle peut aussi activer le service de mise à jour pour faire apparaître ici un bouton « Mettre à jour ».')}
              </Alerte>
            )}
            {etat?.disponible && installateur?.present && !installationEnCours && (
              <div>
                <Button
                  variante="primaire"
                  icone={<Download className="size-4" aria-hidden />}
                  onClick={() => setConfirmer(true)}
                >
                  Mettre à jour vers {etat.derniereVersion}
                </Button>
              </div>
            )}
            {installateur?.message && installateur.statut && installateur.statut in PRESENTATION && (
              <Alerte
                ton={PRESENTATION[installateur.statut].ton}
                icone={PRESENTATION[installateur.statut].icone}
                titre={PRESENTATION[installateur.statut].titre}
              >
                {installateur.message}
                {installationEnCours && ' Les utilisateurs seront déconnectés le temps du redémarrage.'}
              </Alerte>
            )}
            {developpement && (
              <p className="text-sm text-fg-subtle">
                Version de développement (construite depuis le code) : elle n’est pas comparée aux versions publiées.
              </p>
            )}
            {etat?.active && !developpement && !etat.disponible && etat.derniereVersion && !etat.erreur && (
              <p className="flex items-center gap-2 text-sm text-ok-fg">
                <CircleCheck className="size-4" aria-hidden /> L’application est à jour.
              </p>
            )}
          </div>
        </Card>
      )}
      <ConfirmDialog
        open={confirmer}
        onOpenChange={setConfirmer}
        titre={`Mettre à jour vers la version ${etat?.derniereVersion ?? ''} ?`}
        description="L’application redémarre : les personnes connectées sont déconnectées environ une minute. Une sauvegarde de la base et des pièces jointes est faite avant toute migration. Si la nouvelle version ne démarre pas, la version actuelle est remise. Refusé tant qu’une MEP est en cours."
        libelleConfirmer="Mettre à jour"
        onConfirmer={installer}
      />
      {donnees?.lienSignalement && (
        <Card>
          <CardHeader
            titre="Signaler un problème ou proposer une idée"
            description="Le lien est aussi dans le menu de chaque utilisateur. Il se change avec Signalement__Lien (vide = masqué)."
          />
          <div className="px-5 py-5">
            <a
              href={donnees.lienSignalement}
              target="_blank"
              rel="noopener noreferrer"
              className="inline-flex items-center gap-2 text-sm font-medium text-accent-text hover:underline"
            >
              <Megaphone className="size-4" aria-hidden /> {donnees.lienSignalement}
            </a>
          </div>
        </Card>
      )}
    </div>
  );
}
