import { Info, KeyRound, Plus, RefreshCw } from 'lucide-react';
import { useState } from 'react';
import { api } from '../api';
import { useAsync, useNow } from '../hooks';
import type { JetonApi, JetonCree } from '../types';
import { Button } from '../ui/Button';
import { Card, CardHeader } from '../ui/Card';
import { EmptyState } from '../ui/EmptyState';
import { ErreurChargement } from '../ui/ErreurChargement';
import { EnChargement, SqueletteTableau } from '../ui/Squelette';
import { useToast } from '../ui/Toast';
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
} from './composants';
import { aRecreer, JETONS_ACTIFS_MAX, JOURS_VISIBLES_APRES_FIN, trier, visible } from './jetons';

const DOC_JETONS = 'https://github.com/SolaDevSolide/gestion-mep/wiki/Jetons-d-accès';

/**
 * « Mon profil › Jetons d'accès personnels » (#128) : jetons de la personne connectée (actifs, puis révoqués et expirés
 * depuis moins de 30 jours), création, « Recréer » à l'approche de l'expiration, révocation.
 */
export function JetonsPersonnels() {
  const toast = useToast();
  const lu = useAsync(() => Promise.all([api.mesJetons(), api.porteesJetons()]), []);
  const maintenant = new Date(useNow(60_000));
  const [creation, setCreation] = useState<{ modele: ModeleJeton | null } | null>(null);
  const [cree, setCree] = useState<JetonCree | null>(null);
  const [revocation, setRevocation] = useState<JetonApi | null>(null);
  const [jetons, portees] = lu.donnees ?? [null, null];
  const affiches = jetons ? trier(jetons.filter((j) => visible(j, maintenant))) : null;

  const nouveau = (
    <Button
      variante="primaire"
      icone={<Plus className="size-4" aria-hidden />}
      disabled={!portees}
      onClick={() => setCreation({ modele: null })}
    >
      Nouveau jeton
    </Button>
  );

  async function revoquer(j: JetonApi) {
    try {
      await api.revoquerMonJeton(j.id);
      toast.succes(`Jeton « ${j.nom} » révoqué.`);
      lu.recharger();
      return true;
    } catch (e) {
      toast.erreur(e instanceof Error ? e.message : 'Révocation impossible.');
      return false;
    }
  }

  return (
    <Card className="mt-6">
      <CardHeader
        titre="Jetons d’accès personnels"
        description="Pour vos scripts et outils : ils appellent l’API avec vos droits, ou moins. Un jeton ne permet jamais d’ouvrir l’interface."
        action={nouveau}
      />
      {lu.erreur && (
        <div className="p-5">
          <ErreurChargement
            erreur={lu.echec}
            titre="Impossible de charger vos jetons"
            onReessayer={lu.recharger}
            enCours={lu.chargement}
          />
        </div>
      )}
      {!lu.erreur && !affiches && (
        <EnChargement libelle="Chargement de vos jetons…">
          <SqueletteTableau lignes={3} colonnes={5} className="rounded-none border-0" />
        </EnChargement>
      )}
      {affiches && affiches.length === 0 && (
        <EmptyState
          icone={KeyRound}
          titre="Aucun jeton"
          description="Un jeton permet à un script d’appeler l’API sans garder de session : rapport hebdomadaire, export, tableau de bord."
        />
      )}
      {affiches && affiches.length > 0 && (
        <TableauJetons
          entetes={[
            'Nom',
            'Portées',
            'Expire',
            'Dernière utilisation',
            <span key="actions" className="sr-only">
              Actions
            </span>,
          ]}
        >
          {affiches.map((j) => (
            <LigneJeton key={j.id} attenue={j.etat !== 'Actif'}>
              <CelluleJeton>
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
              <CelluleJeton actions>
                <div className="flex flex-wrap justify-end gap-1.5">
                  {aRecreer(j, maintenant) && (
                    <Button
                      taille="sm"
                      icone={<RefreshCw className="size-3.5" aria-hidden />}
                      aria-label={`Recréer « ${j.nom} »`}
                      onClick={() => setCreation({ modele: { nom: j.nom, portees: j.portees } })}
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
              </CelluleJeton>
            </LigneJeton>
          ))}
        </TableauJetons>
      )}
      <div className="flex flex-wrap items-center justify-between gap-x-4 gap-y-1.5 rounded-b-xl border-t border-line bg-surface-2/50 px-5 py-3 text-xs text-fg-subtle">
        <span className="flex items-start gap-1.5">
          <Info className="mt-px size-3.5 shrink-0" aria-hidden />
          Révoqués et expirés : visibles {JOURS_VISIBLES_APRES_FIN} jours ici, toujours au journal. Jusqu’à{' '}
          {JETONS_ACTIFS_MAX} jetons actifs par personne.
        </span>
        <a href={DOC_JETONS} target="_blank" rel="noreferrer" className="font-medium text-accent-text hover:underline">
          Exemples d’appel
        </a>
      </div>

      {portees && (
        <NouveauJetonDialog
          open={creation !== null}
          onOpenChange={(o) => !o && setCreation(null)}
          type="Personnel"
          portees={portees}
          modele={creation?.modele}
          onCreer={async (s) => {
            const r = await api.creerJeton({ nom: s.nom, portees: s.portees, dureeJours: s.dureeJours });
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
    </Card>
  );
}
