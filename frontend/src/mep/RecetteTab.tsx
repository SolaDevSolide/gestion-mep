import { ClipboardCheck, ShieldAlert, Siren, Upload, XCircle } from 'lucide-react';
import { useContext, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { api } from '../api';
import { AuthContext } from '../auth';
import { useNow } from '../hooks';
import { bilanRecette, estTerminee, fichesRecetteTriees } from '../domaine';
import { useMep } from '../mepContext';
import { BoutonExportExcel } from '../ui/BoutonExportExcel';
import type { Systeme } from '../types';
import { Alerte } from '../ui/Alerte';
import { Button } from '../ui/Button';
import { Card } from '../ui/Card';
import { EmptyState } from '../ui/EmptyState';
import { ProgressBar } from '../ui/ProgressBar';
import { BoutonCloturer, BoutonDeclencherRepli } from './ActionsPilote';
import { BoutonCloturerEchec, BoutonRevenirExecution, retourArriereImpossible } from './CycleDeVie';
import { BoutonModifierEcheance, texteEcheance } from './EcheanceRepli';
import { FormulaireDialog } from './preparation/Formulaires';
import { BibliothequeDialog } from './recette/BibliothequeDialog';
import { ImportValidationDialog } from './recette/ImportValidationDialog';
import { PageFiche } from './recette/PageFiche';
import { SectionSysteme } from './recette/SectionSysteme';

/* ------------------------------------------------------------------ Onglet */

/**
 * Onglet Recette : les systèmes de la MEP en sections colorées, chacun avec ses fiches de recette (une fiche appartient à
 * un seul système). Un clic sur une fiche ouvre sa page (`?fiche=…`) : rédaction en préparation, déroulé après le Go.
 * En haut : export Excel de la recette pour les métiers, puis réimport de leur validation.
 * Sous-composants dans `./recette/`.
 */
const INITIAL_AJOUT = { titre: '' };

export default function RecetteTab() {
  const { mep, run } = useMep();
  const auth = useContext(AuthContext);
  const [params, setParams] = useSearchParams();
  const [ajout, setAjout] = useState<Systeme | null>(null);
  const [bibliotheque, setBibliotheque] = useState<Systeme | null>(null);
  const [importer, setImporter] = useState(false);
  const fiches = fichesRecetteTriees(mep);
  const bilan = bilanRecette(mep);
  const enRecette = mep.phase === 'Recette';
  const impossible = retourArriereImpossible(mep) !== null;
  // Échéance atteinte en recette : aucun retour arrière automatique, seulement ce bandeau (et une ligne au journal).
  const now = useNow(15_000);
  const echeanceDepassee =
    enRecette &&
    !impossible &&
    mep.echeanceRepli !== null &&
    (mep.echeanceSignalee !== null || Date.parse(mep.echeanceRepli) <= now);
  const peutAgir = mep.droits.estPilote || mep.droits.participantId !== null || (auth?.utilisateur?.estAdmin ?? false);

  const ficheOuverte = params.get('fiche');
  if (ficheOuverte) {
    return (
      <PageFiche
        ficheId={ficheOuverte}
        peutAgir={peutAgir}
        onRetour={() =>
          setParams((p) => {
            p.delete('fiche');
            return p;
          })
        }
      />
    );
  }

  const idsSystemes = new Set(mep.systemes.map((s) => s.id));
  const sansSysteme = fiches.filter((f) => f.systemeId === null || !idsSystemes.has(f.systemeId));

  return (
    <div className="mx-auto w-full max-w-5xl space-y-5 px-6 py-6">
      <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
        {bilan.total > 0 && (enRecette || estTerminee(mep)) ? (
          <>
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
          </>
        ) : (
          <p className="flex-1 text-sm text-fg-muted">
            {fiches.length} fiche{fiches.length > 1 ? 's' : ''} de recette, {bilan.total} étape
            {bilan.total > 1 ? 's' : ''} de test.
          </p>
        )}
        <span className="flex flex-wrap gap-2">
          <BoutonExportExcel
            url={api.urlExportRecette(mep.id)}
            disabled={fiches.length === 0}
            title="Fichier à faire remplir par les métiers : résultat constaté, Conforme ou Non conforme, nom et date."
          />
          {enRecette && peutAgir && (
            <Button taille="sm" icone={<Upload className="size-4" aria-hidden />} onClick={() => setImporter(true)}>
              Importer la validation
            </Button>
          )}
        </span>
      </div>

      {echeanceDepassee && mep.echeanceRepli && (
        <Alerte
          ton="danger"
          icone={Siren}
          role="alert"
          titre="Échéance de retour arrière dépassée"
          action={<BoutonModifierEcheance taille="md" />}
        >
          {`Échéance : ${texteEcheance(mep.echeanceRepli, mep.fuseauHoraire)}. En recette, aucun retour arrière automatique : le pilote décide de déclencher le retour arrière, ou de reporter l’échéance avec un motif. Revenir en exécution n’est possible qu’après un report.`}
        </Alerte>
      )}
      {enRecette && bilan.echecs > 0 && (
        <Alerte ton="danger" icone={XCircle} role="alert" titre="Une étape de test est en échec">
          Clôture impossible tant qu’une étape n’est pas conforme.
        </Alerte>
      )}
      {enRecette && mep.droits.estPilote && (
        <Alerte
          ton={mep.nonRetourFranchi || !mep.retourArriereActive ? 'warn' : 'rollback'}
          icone={ShieldAlert}
          titre="Décision du pilote"
          action={
            <div className="flex flex-wrap items-center gap-2">
              {mep.retourArriereActive && !mep.nonRetourFranchi && <BoutonDeclencherRepli taille="md" />}
              {!echeanceDepassee && <BoutonRevenirExecution taille="md" />}
              {impossible && <BoutonCloturerEchec taille="md" />}
              <BoutonCloturer taille="md" />
            </div>
          }
        >
          {!mep.retourArriereActive
            ? 'Cette MEP n’a pas de plan de retour arrière : corrigez en avant, ou clôturez en échec. Clôture réussie : toutes les étapes de test doivent être conformes.'
            : mep.nonRetourFranchi
              ? `Point de non-retour franchi (${mep.nonRetourFranchi}) : le retour arrière n’est plus possible. Corrigez en avant, ou clôturez en échec.`
              : 'Clôture réussie : toutes les étapes de test doivent être conformes. Sinon : revenir en exécution pour corriger, ou déclencher le retour arrière.'}
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
              onAjouter={() => setAjout(s)}
              onBibliotheque={() => setBibliotheque(s)}
            />
          ))}
          {sansSysteme.length > 0 && <SectionSysteme systeme={null} fiches={sansSysteme} />}
        </div>
      )}

      <FormulaireDialog
        open={ajout !== null}
        onOpenChange={(o) => {
          if (!o) setAjout(null);
        }}
        titre="Nouvelle fiche"
        description={`Pour le système ${ajout?.nom ?? ''} : une fiche couvre une partie du système. Vous rédigerez ensuite ses étapes de test.`}
        champs={[
          {
            cle: 'titre',
            label: 'Titre de la fiche',
            requis: true,
            large: true,
            placeholder: 'ex. Tables clients',
          },
        ]}
        initial={INITIAL_AJOUT}
        libelleValider="Créer la fiche"
        onValider={async (v) => {
          if (!ajout) return false;
          const avant = new Set(mep.fichesRecette.map((f) => f.id));
          let nouvelle: string | null = null;
          const ok = await run(async () => {
            const m = await api.ajouterFicheRecette(mep.id, ajout.id, v.titre.trim());
            nouvelle = m.fichesRecette.find((f) => !avant.has(f.id))?.id ?? null;
            return m;
          }, 'Fiche de recette créée.');
          // La nouvelle fiche s'ouvre directement pour rédiger ses étapes.
          if (ok && nouvelle) {
            const id: string = nouvelle;
            setParams((p) => {
              p.set('fiche', id);
              return p;
            });
          }
          return ok;
        }}
      />
      <BibliothequeDialog systeme={bibliotheque} onFermer={() => setBibliotheque(null)} />
      <ImportValidationDialog open={importer} onOpenChange={setImporter} />
    </div>
  );
}
