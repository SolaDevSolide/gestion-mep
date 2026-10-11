import { Loader2, NotebookText, OctagonAlert, RefreshCw } from 'lucide-react';
import { api } from '../api';
import { formatDateHeure } from '../format';
import { useAsync } from '../hooks';
import { Alerte } from '../ui/Alerte';
import { Button } from '../ui/Button';
import { Card, CardHeader } from '../ui/Card';
import { EmptyState } from '../ui/EmptyState';
import { Badge } from '../ui/StatusPill';
import type { Ton } from '../format';

export const LIBELLE_EVENEMENT_ADMIN: Record<string, string> = {
  'compte.cree': 'Compte créé',
  'compte.modifie': 'Compte modifié',
  'compte.desactive': 'Compte désactivé',
  'compte.reactive': 'Compte réactivé',
  'compte.mdp-reinitialise': 'Mot de passe réinitialisé',
  'compte.mdp-change': 'Mot de passe changé',
  'parametres.modifies': 'Paramètres modifiés',
  'reglage.modifie': 'Réglage modifié',
  'reglage.retabli': 'Réglage rétabli',
  'critere.cree': 'Critère Go / no-go créé',
  'critere.modifie': 'Critère Go / no-go modifié',
  'critere.supprime': 'Critère Go / no-go supprimé',
  'compte.renomme': 'Nom affiché changé',
  'connexion.echecs': 'Échecs de connexion répétés',
  'sso.regle': 'Connexion unique réglée',
  'sso.active': 'Connexion unique activée',
  'sso.desactive': 'Connexion unique désactivée',
  'sso.delie': 'Liaison supprimée',
  'sso.lie': 'Compte relié à la connexion unique',
  'donnees.controle-reussi': 'Contrôle des données réussi',
  'donnees.controle-echoue': 'Contrôle des données en échec',
};

export function libelleEvenementAdmin(type: string): string {
  return LIBELLE_EVENEMENT_ADMIN[type] ?? type;
}

function tonEvenement(type: string): Ton | undefined {
  if (type === 'compte.desactive' || type === 'critere.supprime' || type === 'donnees.controle-echoue') return 'danger';
  if (type === 'connexion.echecs') return 'warn';
  if (type === 'compte.mdp-reinitialise') return 'warn';
  if (type === 'compte.cree' || type === 'compte.reactive') return 'ok';
  return undefined;
}

export default function JournalTab() {
  const { donnees, erreur, recharger } = useAsync(() => api.adminJournal(200), []);
  return (
    <Card>
      <CardHeader
        titre="Journal d’administration"
        description="Les 200 derniers événements, du plus récent au plus ancien."
        action={
          <Button taille="sm" icone={<RefreshCw className="size-4" aria-hidden />} onClick={recharger}>
            Actualiser
          </Button>
        }
      />
      {erreur && (
        <div className="p-5">
          <Alerte ton="danger" icone={OctagonAlert} role="alert">
            {erreur}
          </Alerte>
        </div>
      )}
      {!erreur && donnees === null && (
        <p className="flex items-center justify-center gap-2 px-5 py-10 text-sm text-fg-subtle" role="status">
          <Loader2 className="size-4 animate-spin" aria-hidden /> Chargement…
        </p>
      )}
      {donnees && donnees.length === 0 && (
        <EmptyState
          icone={NotebookText}
          titre="Journal vide"
          description="Aucune action d’administration n’a encore été enregistrée."
        />
      )}
      {donnees && donnees.length > 0 && (
        <ul className="divide-y divide-line">
          {donnees.map((ev) => (
            <li
              key={ev.id}
              className="grid gap-1 px-5 py-3 text-sm md:grid-cols-[11rem_12rem_minmax(0,1fr)] md:items-baseline md:gap-4"
            >
              <time dateTime={ev.quand} className="text-fg-subtle tabular-nums">
                {formatDateHeure(ev.quand)}
              </time>
              <span>
                <Badge ton={tonEvenement(ev.type)}>{libelleEvenementAdmin(ev.type)}</Badge>
              </span>
              <span className="min-w-0 text-fg">
                <span className="font-medium">{ev.auteur}</span>
                {ev.auteurLogin && <span className="text-fg-subtle"> ({ev.auteurLogin})</span>}
                {ev.detail && <span className="text-fg-muted"> — {ev.detail}</span>}
              </span>
            </li>
          ))}
        </ul>
      )}
    </Card>
  );
}
