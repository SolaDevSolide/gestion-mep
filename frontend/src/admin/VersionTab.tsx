import { CircleArrowUp, CircleCheck, ExternalLink, Megaphone, RefreshCw, TriangleAlert } from 'lucide-react';
import { useState } from 'react';
import { api } from '../api';
import { formatDate, formatDateHeure } from '../format';
import { useAsync } from '../hooks';
import type { MiseAJour } from '../types';
import { Alerte } from '../ui/Alerte';
import { Button } from '../ui/Button';
import { Card, CardHeader } from '../ui/Card';
import { useToast } from '../ui/Toast';

const DOC_MISE_A_JOUR = 'https://github.com/SolaDevSolide/gestion-mep/wiki/Exploiter#mettre-à-jour';

/** Version en cours, dernière version publiée sur GitHub et marche à suivre pour mettre à jour. */
export default function VersionTab() {
  const toast = useToast();
  const { donnees, erreur } = useAsync(() => api.application(), []);
  const [verifie, setVerifie] = useState<MiseAJour | null>(null);
  const [envoi, setEnvoi] = useState(false);
  const etat = verifie ?? donnees?.miseAJour ?? null;
  // Version construite hors publication (« 0.0.0-dev ») : elle n'est pas comparée aux versions publiées.
  const developpement = donnees?.version.includes('-') ?? false;

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
      <Card>
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
              L’équipe informatique change MEP_IMAGE dans le fichier .env du serveur, puis relance l’application. Une
              sauvegarde est faite automatiquement avant toute modification de la base.
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
