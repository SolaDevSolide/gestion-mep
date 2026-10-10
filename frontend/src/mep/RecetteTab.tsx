import { ClipboardCheck, ShieldAlert, XCircle } from 'lucide-react';
import { useContext, useState } from 'react';
import { api } from '../api';
import { AuthContext } from '../auth';
import { bilanRecette, fichesRecetteTriees } from '../domaine';
import { useMep } from '../mepContext';
import { Alerte } from '../ui/Alerte';
import { Card } from '../ui/Card';
import { EmptyState } from '../ui/EmptyState';
import { ProgressBar } from '../ui/ProgressBar';
import { BoutonCloturer, BoutonDeclencherRepli } from './ActionsPilote';
import { FormulaireDialog } from './preparation/Formulaires';
import { SectionSysteme } from './recette/SectionSysteme';

/* ------------------------------------------------------------------ Onglet */

/**
 * Onglet Recette : les systèmes de la MEP en sections colorées, chacun avec ses fiches de recette.
 * En préparation : édition des fiches. En phase Recette : déroulé (Conforme / Échec + observation), « Refaire » pour le pilote.
 * Sous-composants dans `./recette/` : `SectionSysteme`, `FicheCarte`, `FicheEdition`, `FicheLecture`.
 */
const INITIAL_AJOUT = { titre: '' };

export default function RecetteTab() {
  const { mep, run } = useMep();
  const auth = useContext(AuthContext);
  const [ajout, setAjout] = useState<{ systemeId: string | null } | null>(null);
  const fiches = fichesRecetteTriees(mep);
  const bilan = bilanRecette(mep);
  const enRecette = mep.phase === 'Recette';
  const edition = mep.droits.peutEditerContenu;
  const peutAgir = mep.droits.estPilote || mep.droits.participantId !== null || (auth?.utilisateur?.estAdmin ?? false);

  const idsSystemes = new Set(mep.systemes.map((s) => s.id));
  const sansSysteme = fiches.filter((f) => f.systemeId === null || !idsSystemes.has(f.systemeId));

  return (
    <div className="mx-auto w-full max-w-5xl space-y-5 px-6 py-6">
      {bilan.total > 0 && (enRecette || mep.phase === 'Cloturee') ? (
        <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
          <ProgressBar
            className="min-w-48 flex-1"
            segments={[
              { valeur: bilan.conformes, ton: 'ok', libelle: 'Conformes' },
              { valeur: bilan.echecs, ton: 'danger', libelle: 'Échecs' },
              { valeur: bilan.restantes, ton: 'wait', libelle: 'Restantes' },
            ]}
          />
          <p className="text-sm font-medium text-fg-muted tnum">
            {bilan.conformes}/{bilan.total} conforme{bilan.conformes > 1 ? 's' : ''}
            {bilan.echecs > 0 && <span className="font-semibold text-danger-solid"> · {bilan.echecs} en échec</span>}
          </p>
        </div>
      ) : (
        <p className="text-sm text-fg-muted">
          {fiches.length} fiche{fiches.length > 1 ? 's' : ''} de recette, {bilan.total} étape
          {bilan.total > 1 ? 's' : ''} de test.
        </p>
      )}

      {enRecette && bilan.echecs > 0 && (
        <Alerte ton="danger" icone={XCircle} role="alert" titre="Une étape de test est en échec">
          Clôture impossible tant qu’une étape n’est pas conforme.
        </Alerte>
      )}
      {enRecette && mep.droits.estPilote && (
        <Alerte
          ton={mep.nonRetourFranchi ? 'warn' : 'rollback'}
          icone={ShieldAlert}
          titre="Décision du pilote"
          action={
            <div className="flex flex-wrap items-center gap-2">
              <BoutonDeclencherRepli taille="md" />
              <BoutonCloturer taille="md" />
            </div>
          }
        >
          {mep.nonRetourFranchi
            ? `Point de non-retour franchi (${mep.nonRetourFranchi}) : le retour arrière n’est plus possible, on corrige en avant.`
            : 'Clôture réussie : toutes les étapes de test doivent être conformes.'}
        </Alerte>
      )}

      {mep.systemes.length === 0 && sansSysteme.length === 0 ? (
        <Card>
          <EmptyState
            icone={ClipboardCheck}
            titre="Aucun système"
            description="Ajoutez les systèmes de la MEP en préparation : la recette s’organise par système."
          />
        </Card>
      ) : (
        <div className="space-y-5">
          {mep.systemes.map((s) => (
            <SectionSysteme
              key={s.id}
              systeme={s}
              fiches={fiches.filter((f) => f.systemeId === s.id)}
              peutAgir={peutAgir}
              onAjouter={() => setAjout({ systemeId: s.id })}
            />
          ))}
          {(sansSysteme.length > 0 || edition) && (
            <SectionSysteme
              systeme={null}
              fiches={sansSysteme}
              peutAgir={peutAgir}
              onAjouter={() => setAjout({ systemeId: null })}
            />
          )}
        </div>
      )}

      <FormulaireDialog
        open={ajout !== null}
        onOpenChange={(o) => {
          if (!o) setAjout(null);
        }}
        titre="Ajouter une recette"
        description={
          ajout?.systemeId
            ? `Pour le système ${mep.systemes.find((s) => s.id === ajout.systemeId)?.nom ?? ''}. Vous ajouterez ensuite ses étapes de test.`
            : 'Sans système. Vous ajouterez ensuite ses étapes de test.'
        }
        champs={[
          { cle: 'titre', label: 'Titre de la fiche', requis: true, large: true, placeholder: 'ex. Tables clients' },
        ]}
        initial={INITIAL_AJOUT}
        libelleValider="Ajouter"
        onValider={(v) =>
          run(
            () => api.ajouterFicheRecette(mep.id, ajout?.systemeId ?? null, v.titre.trim()),
            'Fiche de recette ajoutée.',
          )
        }
      />
    </div>
  );
}
