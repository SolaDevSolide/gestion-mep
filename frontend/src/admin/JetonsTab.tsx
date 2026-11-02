import { KeyRound, NotebookText, Plus, RefreshCw, Search } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api';
import { useAsync, useNow } from '../hooks';
import {
  CelluleJeton,
  EcheanceJeton,
  JetonCreeDialog,
  LigneJeton,
  NomJeton,
  NouveauJetonDialog,
  PorteesJeton,
  RevoquerJetonDialog,
  TableauJetons,
  UtilisationJeton,
  type ModeleJeton,
} from '../jetons/composants';
import { aRecreer, comptesTechniques, trier, visible } from '../jetons/jetons';
import { formatDate } from '../format';
import type { JetonApi, JetonCree } from '../types';
import { Avatar } from '../ui/Avatar';
import { Button } from '../ui/Button';
import { Card, CardHeader } from '../ui/Card';
import { EmptyState } from '../ui/EmptyState';
import { ErreurChargement } from '../ui/ErreurChargement';
import { Input } from '../ui/Input';
import { EnChargement, SqueletteTableau } from '../ui/Squelette';
import { Badge } from '../ui/StatusPill';
import { useToast } from '../ui/Toast';

/** Réglage global des requêtes par minute d'un jeton (null s'il n'a pas pu être lu). */
async function debitGlobal(): Promise<number | null> {
  try {
    const r = (await api.reglages()).find((x) => x.nom === 'jetons-par-minute');
    return r ? Number(r.valeur) || null : null;
  } catch {
    return null;
  }
}

/**
 * Console › Jetons d'API (#128) : jetons de service (chacun lié à un compte technique) et jetons personnels de tous les
 * comptes, sans leur valeur. L'administrateur crée les jetons de service et révoque n'importe quel jeton ; il ne crée
 * pas de jeton personnel pour autrui.
 */
export default function JetonsTab() {
  const toast = useToast();
  const lu = useAsync(() => Promise.all([api.jetons(), api.porteesJetons(), debitGlobal()]), []);
  const maintenant = new Date(useNow(60_000));
  const [creation, setCreation] = useState<{ modele: ModeleJeton | null } | null>(null);
  const [cree, setCree] = useState<JetonCree | null>(null);
  const [revocation, setRevocation] = useState<JetonApi | null>(null);
  const [filtre, setFiltre] = useState('');
  const [jetons, portees, debit] = lu.donnees ?? [null, null, null];

  const service = jetons ? trier(jetons.filter((j) => j.type === 'Service' && visible(j, maintenant))) : null;
  const tousPersonnels = jetons ? trier(jetons.filter((j) => j.type === 'Personnel' && visible(j, maintenant))) : null;
  const f = filtre.trim().toLocaleLowerCase('fr');
  const personnels =
    tousPersonnels && f
      ? tousPersonnels.filter((j) => `${j.compteNom} ${j.compteLogin}`.toLocaleLowerCase('fr').includes(f))
      : tousPersonnels;

  async function revoquer(j: JetonApi) {
    try {
      await api.revoquerJeton(j.id);
      toast.succes(`Jeton « ${j.nom} » révoqué.`);
      lu.recharger();
      return true;
    } catch (e) {
      toast.erreur(e instanceof Error ? e.message : 'Révocation impossible.');
      return false;
    }
  }

  const chargement = (libelle: string) =>
    !jetons && (
      <EnChargement libelle={libelle}>
        <SqueletteTableau lignes={3} colonnes={6} className="rounded-none border-0" />
      </EnChargement>
    );
  const actionsLigne = (j: JetonApi, recreer: boolean) => (
    <div className="flex flex-wrap justify-end gap-1.5">
      {recreer && aRecreer(j, maintenant) && (
        <Button
          taille="sm"
          icone={<RefreshCw className="size-3.5" aria-hidden />}
          aria-label={`Recréer « ${j.nom} »`}
          onClick={() =>
            setCreation({
              modele: {
                nom: j.nom,
                portees: j.portees,
                compteTechniqueId: j.compteId,
                requetesParMinute: j.requetesParMinute,
              },
            })
          }
        >
          Recréer
        </Button>
      )}
      {j.etat === 'Actif' && (
        <Button
          taille="sm"
          variante="danger-doux"
          aria-label={`Révoquer « ${j.nom} »`}
          onClick={() => setRevocation(j)}
        >
          Révoquer
        </Button>
      )}
    </div>
  );

  if (lu.erreur && !jetons)
    return (
      <Card className="p-5">
        <ErreurChargement
          erreur={lu.echec}
          titre="Impossible de charger les jetons"
          onReessayer={lu.recharger}
          enCours={lu.chargement}
        />
      </Card>
    );

  return (
    <div className="grid grid-cols-[minmax(0,1fr)] gap-5">
      <Card>
        <CardHeader
          titre="Jetons de service"
          description="Pour un outil (CI/CD, supervision, import) : chaque jeton est lié à un compte technique, qui ne peut pas se connecter à l’interface."
          action={
            <Button
              variante="primaire"
              icone={<Plus className="size-4" aria-hidden />}
              disabled={!portees}
              onClick={() => setCreation({ modele: null })}
            >
              Nouveau jeton de service
            </Button>
          }
        />
        {chargement('Chargement des jetons de service…')}
        {service && service.length === 0 && (
          <EmptyState
            icone={KeyRound}
            titre="Aucun jeton de service"
            description="Créez-en un pour qu’une chaîne CI/CD coche ses étapes, qu’une supervision lise les MEP ou qu’un import tienne le catalogue à jour."
          />
        )}
        {service && service.length > 0 && (
          <TableauJetons
            entetes={[
              'Outil',
              'Compte technique',
              'Portées',
              'Expire',
              'Dernière utilisation',
              'Débit',
              <span key="actions" className="sr-only">
                Actions
              </span>,
            ]}
          >
            {service.map((j) => (
              <LigneJeton key={j.id} attenue={j.etat !== 'Actif'}>
                <CelluleJeton>
                  <NomJeton jeton={j} detail={`par ${j.creePar}, ${formatDate(j.creeLe)}`} />
                </CelluleJeton>
                <CelluleJeton libelle="Compte">
                  <span className="flex flex-wrap items-center gap-1.5">
                    <span className="font-mono text-ui [overflow-wrap:anywhere]">{j.compteLogin}</span>
                    <Badge>Technique</Badge>
                  </span>
                </CelluleJeton>
                <CelluleJeton libelle="Portées">
                  <PorteesJeton portees={j.portees} />
                </CelluleJeton>
                <CelluleJeton libelle="Expire">
                  <EcheanceJeton jeton={j} maintenant={maintenant} />
                </CelluleJeton>
                <CelluleJeton libelle="Dernière util.">
                  <UtilisationJeton jeton={j} maintenant={maintenant} />
                </CelluleJeton>
                <CelluleJeton libelle="Débit">
                  {j.requetesParMinute ? (
                    <span className="whitespace-nowrap tabular-nums">
                      {j.requetesParMinute.toLocaleString('fr-FR')} / min
                    </span>
                  ) : (
                    <span className="whitespace-nowrap text-fg-subtle">Par défaut</span>
                  )}
                </CelluleJeton>
                <CelluleJeton actions>{actionsLigne(j, true)}</CelluleJeton>
              </LigneJeton>
            ))}
          </TableauJetons>
        )}
      </Card>

      <Card>
        <CardHeader
          titre="Jetons personnels de tous les comptes"
          description="Vous voyez leur nom, leurs portées et leur dernière utilisation, jamais leur valeur. Vous pouvez les révoquer, pas en créer pour autrui."
          action={
            <div className="relative w-full sm:w-64">
              <Search
                className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-fg-subtle"
                aria-hidden
              />
              <Input
                type="search"
                aria-label="Filtrer par compte"
                placeholder="Filtrer par compte…"
                value={filtre}
                onChange={(e) => setFiltre(e.target.value)}
                className="pl-9"
              />
            </div>
          }
        />
        {chargement('Chargement des jetons personnels…')}
        {personnels && personnels.length === 0 && (
          <EmptyState
            icone={KeyRound}
            titre={f ? 'Aucun jeton pour ce compte' : 'Aucun jeton personnel'}
            description={
              f
                ? 'Aucun jeton personnel actif ou récent ne correspond à ce filtre.'
                : 'Chacun crée ses jetons depuis « Mon profil » ; ils apparaîtront ici.'
            }
          />
        )}
        {personnels && personnels.length > 0 && (
          <TableauJetons
            entetes={[
              'Compte',
              'Jeton',
              'Portées',
              'Expire',
              'Dernière utilisation',
              <span key="actions" className="sr-only">
                Actions
              </span>,
            ]}
          >
            {personnels.map((j) => (
              <LigneJeton key={j.id} attenue={j.etat !== 'Actif'}>
                <CelluleJeton>
                  <span className="flex min-w-0 items-center gap-2">
                    <Avatar nom={j.compteNom} taille="sm" />
                    <span className="min-w-0">
                      <span className="block font-medium text-fg">{j.compteNom}</span>
                      <span className="block font-mono text-xs text-fg-subtle">{j.compteLogin}</span>
                    </span>
                  </span>
                </CelluleJeton>
                <CelluleJeton libelle="Jeton">
                  <NomJeton jeton={j} />
                </CelluleJeton>
                <CelluleJeton libelle="Portées">
                  <PorteesJeton portees={j.portees} />
                </CelluleJeton>
                <CelluleJeton libelle="Expire">
                  <EcheanceJeton jeton={j} maintenant={maintenant} />
                </CelluleJeton>
                <CelluleJeton libelle="Dernière util.">
                  <UtilisationJeton jeton={j} maintenant={maintenant} />
                </CelluleJeton>
                <CelluleJeton actions>{actionsLigne(j, false)}</CelluleJeton>
              </LigneJeton>
            ))}
          </TableauJetons>
        )}
        <div className="flex flex-wrap items-center justify-between gap-x-4 gap-y-1.5 rounded-b-xl border-t border-line bg-surface-2/50 px-5 py-3 text-xs text-fg-subtle">
          <span>
            Limite de débit :{' '}
            <b className="text-fg-muted">
              {debit ? `${debit.toLocaleString('fr-FR')} appels / minute` : 'réglage « jetons-par-minute »'}
            </b>{' '}
            par jeton (
            <Link to="/admin?onglet=parametres" className="font-medium text-accent-text hover:underline">
              Paramètres
            </Link>
            ) · au-delà : <code className="font-mono">429</code> avec <code className="font-mono">Retry-After</code>.
          </span>
          <Link
            to="/admin?onglet=journal&filtre=jetons"
            className="inline-flex items-center gap-1 font-medium text-accent-text hover:underline"
          >
            <NotebookText className="size-3.5" aria-hidden /> Voir au journal
          </Link>
        </div>
      </Card>

      {portees && (
        <NouveauJetonDialog
          open={creation !== null}
          onOpenChange={(o) => !o && setCreation(null)}
          type="Service"
          portees={portees}
          modele={creation?.modele}
          comptes={jetons ? comptesTechniques(jetons) : []}
          debitGlobal={debit}
          onCreer={async (s) => {
            const r = await api.creerJetonService(s);
            setCreation(null);
            setCree(r);
            lu.recharger();
            return r;
          }}
        />
      )}
      <JetonCreeDialog cree={cree} onFermer={() => setCree(null)} />
      <RevoquerJetonDialog
        jeton={revocation}
        onOpenChange={(o) => !o && setRevocation(null)}
        onRevoquer={revoquer}
        maintenant={maintenant}
      />
    </div>
  );
}
