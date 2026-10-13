import { FileCog, OctagonAlert, RotateCcw, Save, TerminalSquare, TriangleAlert } from 'lucide-react';
import { useState } from 'react';
import { api } from '../api';
import { formatDateHeure } from '../format';
import { useAsync } from '../hooks';
import type { Reglage } from '../types';
import { Alerte } from '../ui/Alerte';
import { Button } from '../ui/Button';
import { Card, CardHeader } from '../ui/Card';
import { Field, Input } from '../ui/Input';
import { Segmented } from '../ui/Segmented';
import { Badge } from '../ui/StatusPill';
import { useToast } from '../ui/Toast';
import { commande, fichierDemarrage, useInstallation, type ModeInstallation } from '../installation';

/** Réglés au démarrage, dans le fichier .env : la console les rappelle sans pouvoir les changer. */
const REGLAGES_DEMARRAGE: [string, string][] = [
  [
    'ADRESSE_ECOUTE',
    'Adresse sur laquelle le port est ouvert : 127.0.0.1 (cette machine seulement) ou 0.0.0.0 (tout le réseau).',
  ],
  ['PORT', 'Port de l’application sur la machine (8080 par défaut).'],
  ['POSTGRES_USER, POSTGRES_PASSWORD', 'Compte de la base de données.'],
  ['DEMO', 'Comptes et MEP de démonstration.'],
  ['ADMIN_LOGIN, ADMIN_MOT_DE_PASSE', 'Premier administrateur, créé au premier démarrage.'],
  ['MEP_IMAGE, COMPOSE_PROFILES', 'Version installée et service de mise à jour.'],
];

/** Hors Docker : connexion à la base et dossier des données à la place du compte PostgreSQL et de l'image. */
const REGLAGES_DEMARRAGE_AUTONOME: [string, string][] = [
  ['ADRESSE_ECOUTE', REGLAGES_DEMARRAGE[0][1]],
  ['PORT', REGLAGES_DEMARRAGE[1][1]],
  ['BASE_DE_DONNEES', 'Connexion à la base PostgreSQL (serveur, nom, compte).'],
  ['DOSSIER_DONNEES', 'Dossier des pièces jointes, des sauvegardes et des clés de session.'],
  ['DEMO', REGLAGES_DEMARRAGE[3][1]],
  ['ADMIN_LOGIN, ADMIN_MOT_DE_PASSE', REGLAGES_DEMARRAGE[4][1]],
];

function Origine({ r, fichierDocker }: { r: Reglage; fichierDocker: boolean }) {
  if (r.origine === 'console')
    return (
      <Badge ton="run">
        Modifié le {formatDateHeure(r.modifieLe)}
        {r.modifiePar ? ` par ${r.modifiePar}` : ''}
      </Badge>
    );
  if (r.origine === 'fichier')
    return (
      <Badge>
        {fichierDocker ? 'Fichier .env' : 'Fichier de configuration'} ({r.variable})
      </Badge>
    );
  return <Badge>Valeur par défaut</Badge>;
}

function texte(v: string) {
  return v === '' ? '(vide)' : v;
}

function LigneReglage({
  r,
  installation,
  onChange,
}: {
  r: Reglage;
  installation: ModeInstallation;
  onChange: (r: Reglage) => void;
}) {
  const fichier = installation === 'docker' ? 'fichier .env' : 'fichier de configuration';
  const toast = useToast();
  const [saisie, setSaisie] = useState<string | null>(null);
  const [envoi, setEnvoi] = useState<'enregistrer' | 'retablir' | null>(null);
  const courant = saisie ?? r.valeur;
  const modifie = saisie !== null && saisie.trim() !== r.valeur;
  const valeurRetablie = r.valeurFichier ?? r.defaut;

  async function agir(action: 'enregistrer' | 'retablir', valeur = courant) {
    setEnvoi(action);
    try {
      const maj = action === 'enregistrer' ? await api.definirReglage(r.nom, valeur) : await api.retablirReglage(r.nom);
      setSaisie(null);
      onChange(maj);
      toast.succes(action === 'enregistrer' ? 'Réglage enregistré, appliqué tout de suite.' : 'Réglage rétabli.');
    } catch (e) {
      toast.erreur(e instanceof Error ? e.message : 'Enregistrement impossible.');
    } finally {
      setEnvoi(null);
    }
  }

  return (
    <div className="flex flex-col gap-3 px-5 py-4">
      <div className="flex flex-wrap items-end gap-3">
        <Field
          label={r.libelle}
          hint={r.aide}
          className={r.type === 'Booleen' ? 'min-w-0 flex-1 basis-64' : 'min-w-0 flex-1 basis-72'}
        >
          {(id) =>
            r.type === 'Booleen' ? (
              <Segmented
                label={r.libelle}
                valeur={courant === 'false' ? 'false' : 'true'}
                disabled={envoi !== null}
                onChange={(v) => void agir('enregistrer', v)}
                options={[
                  { valeur: 'true', libelle: 'Oui' },
                  { valeur: 'false', libelle: 'Non' },
                ]}
                className="self-start"
              />
            ) : (
              <Input
                id={id}
                value={courant}
                type={r.type === 'Entier' ? 'number' : r.type === 'Adresse' ? 'url' : 'text'}
                min={r.min ?? undefined}
                max={r.max ?? undefined}
                step={r.type === 'Entier' ? 1 : undefined}
                placeholder={r.type === 'Adresse' ? 'https://' : undefined}
                className="font-mono text-[13px]"
                onChange={(e) => setSaisie(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter' && modifie) void agir('enregistrer');
                }}
              />
            )
          }
        </Field>
        <div className="flex items-center gap-2 pb-6">
          {r.type !== 'Booleen' && (
            <Button
              variante="primaire"
              taille="sm"
              icone={<Save className="size-4" aria-hidden />}
              disabled={!modifie}
              chargement={envoi === 'enregistrer'}
              onClick={() => void agir('enregistrer')}
            >
              Enregistrer
            </Button>
          )}
          {r.origine === 'console' && (
            <Button
              variante="fantome"
              taille="sm"
              icone={<RotateCcw className="size-4" aria-hidden />}
              title={`Revenir à ${r.valeurFichier === null ? 'la valeur par défaut' : `la valeur du ${fichier}`} : ${texte(valeurRetablie)}`}
              chargement={envoi === 'retablir'}
              disabled={envoi !== null}
              onClick={() => void agir('retablir')}
            >
              Rétablir
            </Button>
          )}
        </div>
      </div>
      <div className="flex flex-wrap items-center gap-2 text-xs text-fg-subtle">
        <Origine r={r} fichierDocker={installation === 'docker'} />
        <code className="rounded bg-surface-2 px-1.5 py-0.5 font-mono text-[11px] text-fg-muted">
          {commande(installation, `reglages definir ${r.nom} …`)}
        </code>
      </div>
      {r.alertes.map((a) => (
        <Alerte key={a} ton="warn" icone={TriangleAlert}>
          {a}
        </Alerte>
      ))}
    </div>
  );
}

/** Réglages d'exploitation : valeur de départ dans le fichier .env, modifiables ici ou en ligne de commande, sans redémarrage. */
export default function ReglagesServeur() {
  const installation = useInstallation();
  const docker = installation === 'docker';
  const { donnees, erreur, setDonnees } = useAsync(() => api.reglages(), []);
  const groupes = new Map<string, Reglage[]>();
  for (const r of donnees ?? []) groupes.set(r.groupe, [...(groupes.get(r.groupe) ?? []), r]);
  const remplacer = (maj: Reglage) => setDonnees((liste) => liste?.map((r) => (r.nom === maj.nom ? maj : r)) ?? null);

  return (
    <>
      {erreur && (
        <Alerte ton="danger" icone={OctagonAlert} role="alert">
          {erreur}
        </Alerte>
      )}
      {[...groupes].map(([groupe, reglages], i) => (
        <Card key={groupe}>
          <CardHeader
            titre={groupe}
            description={
              i === 0
                ? `La valeur de départ vient du ${docker ? 'fichier .env' : 'fichier de configuration'}. Une valeur modifiée ici la remplace, sans redémarrage.`
                : undefined
            }
          />
          <div className="divide-y divide-line">
            {reglages.map((r) => (
              <LigneReglage key={r.nom} r={r} installation={installation} onChange={remplacer} />
            ))}
          </div>
        </Card>
      ))}
      {donnees && (
        <Card>
          <CardHeader titre="Réglés au démarrage" description={fichierDemarrage(installation)} />
          <dl className="divide-y divide-line">
            {(docker ? REGLAGES_DEMARRAGE : REGLAGES_DEMARRAGE_AUTONOME).map(([variables, description]) => (
              <div key={variables} className="flex flex-wrap gap-x-4 gap-y-1 px-5 py-3 text-sm">
                <dt className="flex min-w-0 basis-72 items-center gap-2 font-mono text-[13px] text-fg">
                  <FileCog className="size-4 shrink-0 text-fg-subtle" aria-hidden />
                  {variables}
                </dt>
                <dd className="min-w-0 flex-1 basis-64 text-fg-muted">{description}</dd>
              </div>
            ))}
          </dl>
          <div className="flex items-start gap-2 border-t border-line bg-surface-2/50 px-5 py-3 text-xs text-fg-subtle">
            <TerminalSquare className="mt-0.5 size-4 shrink-0" aria-hidden />
            <span>
              Tous les réglages ci-dessus se lisent aussi avec{' '}
              <code className="font-mono">{commande(installation, 'reglages lister')}</code>.
            </span>
          </div>
        </Card>
      )}
    </>
  );
}
