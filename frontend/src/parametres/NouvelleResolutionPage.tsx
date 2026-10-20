import { ArrowLeft, OctagonAlert, Save, TriangleAlert } from 'lucide-react';
import { useEffect, useId, useRef, useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { api } from '../api';
import { useAsync } from '../hooks';
import { useReferentiel } from '../referentiels';
import { saisieDepuis as saisieEtape } from '../mep/graphe/PanneauEdition';
import {
  ApercuResolution,
  brouillonDepuis,
  FormulaireResolution,
  saisieResolution,
  type BrouillonResolution,
} from '../mep/ResolutionEditeur';
import { Alerte } from '../ui/Alerte';
import { Button } from '../ui/Button';
import { Card } from '../ui/Card';
import { ErreurChargement } from '../ui/ErreurChargement';
import { Squelette } from '../ui/Squelette';
import { useToast } from '../ui/Toast';

/**
 * Création d'une fiche de résolution type (spec jalon 4, §9), en pleine page avec aperçu en direct.
 * Avec `?mep=…&etape=…` : système pré-sélectionné d'après l'étape, puis « Enregistrer et lier à E0006 ». Tant que
 * l'étape n'est pas arrivée, ce bouton est désactivé avec son motif (la saisie reste possible) : jamais de fiche créée
 * sans lien par un clic trop rapide. Si la MEP ne se charge pas, « Enregistrer sans lier » le dit.
 */
export default function NouvelleResolutionPage() {
  const [params] = useSearchParams();
  const mepId = params.get('mep');
  const etapeId = params.get('etape');
  const navigate = useNavigate();
  const toast = useToast();
  const catalogue = useReferentiel('catalogue');
  const mep = useAsync(() => (mepId ? api.mep(mepId) : Promise.resolve(null)), [mepId]);
  const etape = (etapeId && mep.donnees?.etapes.find((e) => e.id === etapeId)) || null;
  // Étape à lier en cours de lecture : fil d'Ariane, titre et bouton attendent (pas de changement sous le curseur).
  const attenteEtape = !!mepId && !!etapeId && mep.donnees === null && mep.echec === null;
  const sansLien = !!mepId && !!etapeId && mep.echec !== null && mep.donnees === null;
  const motifId = useId();

  // `titre` : texte cherché dans le sélecteur de résolutions de l'étape.
  const [b, setB] = useState<BrouillonResolution>(() => ({
    ...brouillonDepuis(undefined),
    titre: params.get('titre') ?? '',
  }));
  const [erreur, setErreur] = useState('');
  const [envoi, setEnvoi] = useState(false);
  const systemeInitialise = useRef(false);

  // Systèmes pré-sélectionnés : ceux (du catalogue) des systèmes de l'étape.
  useEffect(() => {
    if (systemeInitialise.current || !etape || !mep.donnees) return;
    systemeInitialise.current = true;
    const ids = mep.donnees.systemes
      .filter((s) => etape.systemeIds.includes(s.id))
      .map((s) => s.catalogueId)
      .filter((c): c is string => !!c);
    if (ids.length) setB((x) => (x.systemeCatalogueIds.length ? x : { ...x, systemeCatalogueIds: ids }));
  }, [etape, mep.donnees]);

  const retour =
    etape && mepId
      ? `/meps/${encodeURIComponent(mepId)}?onglet=plan&plan=${etape.plan === 'RetourArriere' ? 'retour' : 'etapes'}&etape=${encodeURIComponent(etape.id)}`
      : '/parametres/resolutions';
  const libelleRetour = etape
    ? `Retour à ${etape.code}${etape.action ? ` · ${etape.action}` : ''}`
    : 'Résolutions types';

  async function enregistrer() {
    if (attenteEtape) return;
    setErreur('');
    if (!b.titre.trim()) {
      setErreur('Donnez un titre à la fiche de résolution.');
      return;
    }
    setEnvoi(true);
    try {
      const r = await api.creerResolution(saisieResolution(b));
      if (etape && mepId) {
        try {
          // Relecture de l'étape : sa révision a pu changer depuis l'ouverture de la page.
          const frais = await api.mep(mepId);
          const e = frais.etapes.find((x) => x.id === etape.id);
          if (!e) throw new Error(`${etape.code} n’existe plus.`);
          await api.modifierEtape(mepId, e.id, {
            ...saisieEtape(e),
            resolutionIds: [...new Set([...e.resolutionIds, r.id])],
          });
          toast.succes(`Fiche « ${r.titre} » enregistrée et liée à ${etape.code}.`);
        } catch (err) {
          toast.erreur(
            `Fiche « ${r.titre} » enregistrée, mais non liée à ${etape.code} : ${err instanceof Error ? err.message : 'erreur inconnue'}`,
          );
        }
      } else {
        toast.succes(`Fiche « ${r.titre} » enregistrée.`);
      }
      void navigate(retour);
    } catch (err) {
      setErreur(err instanceof Error ? err.message : 'Enregistrement impossible.');
    } finally {
      setEnvoi(false);
    }
  }

  return (
    <main className="mx-auto w-full max-w-[1400px] px-6 py-8">
      <nav aria-label="Fil d’Ariane" className="mb-3">
        {attenteEtape ? (
          <Squelette className="h-5 w-48" />
        ) : (
          <Link
            to={retour}
            className="inline-flex items-center gap-1.5 text-sm font-semibold text-accent-text hover:underline"
          >
            <ArrowLeft className="size-4" aria-hidden />
            {libelleRetour}
          </Link>
        )}
      </nav>
      <h1 className="text-2xl font-semibold tracking-tight">
        Nouvelle fiche de résolution
        {etape ? (
          <span className="ml-2 text-base font-normal text-fg-subtle">
            liée à <span className="font-mono">{etape.code}</span>
          </span>
        ) : attenteEtape ? (
          <Squelette className="ml-2 inline-block h-5 w-24 align-middle" />
        ) : null}
      </h1>
      {sansLien && (
        <ErreurChargement
          variante="ligne"
          className="mt-4"
          erreur={mep.echec}
          titre="Étape à lier non chargée"
          introuvable="Cette MEP n’existe pas ou a été supprimée : la fiche sera créée sans être liée."
          complement="La fiche peut être enregistrée sans lien, puis liée depuis l’étape."
          onReessayer={mep.recharger}
          enCours={mep.chargement}
        />
      )}
      {mepId && mep.donnees && etapeId && !etape && (
        <Alerte ton="warn" icone={TriangleAlert} className="mt-4">
          Étape introuvable dans cette MEP : la fiche sera créée sans être liée.
        </Alerte>
      )}

      <div className="mt-6 grid items-start gap-5 lg:grid-cols-[minmax(0,1.1fr)_minmax(0,0.9fr)]">
        <Card className="grid gap-5 p-5">
          {erreur && (
            <Alerte ton="danger" icone={OctagonAlert} role="alert">
              {erreur}
            </Alerte>
          )}
          <FormulaireResolution
            b={b}
            onChange={setB}
            systemes={catalogue.donnees}
            erreurSystemes={
              catalogue.erreur != null && (
                <ErreurChargement
                  variante="ligne"
                  erreur={catalogue.erreur}
                  titre="Catalogue des systèmes non chargé"
                  onReessayer={catalogue.recharger}
                  enCours={catalogue.enCours}
                />
              )
            }
            autoFocus
          />
          <div className="flex flex-wrap items-center gap-2 border-t border-line pt-4">
            <Button
              variante="primaire"
              icone={<Save className="size-4" aria-hidden />}
              chargement={envoi}
              // Pas `disabled` : le bouton garde le focus clavier et son motif reste lu.
              aria-disabled={attenteEtape || undefined}
              aria-describedby={attenteEtape ? motifId : undefined}
              className={attenteEtape ? 'cursor-not-allowed opacity-50 active:translate-y-0' : undefined}
              onClick={() => void enregistrer()}
            >
              {etape
                ? `Enregistrer et lier à ${etape.code}`
                : attenteEtape
                  ? 'Enregistrer et lier'
                  : sansLien
                    ? 'Enregistrer sans lier'
                    : 'Enregistrer'}
            </Button>
            <Button variante="fantome" onClick={() => void navigate(retour)}>
              Annuler
            </Button>
            {attenteEtape && (
              <span id={motifId} className="text-sm text-fg-subtle">
                Chargement de l’étape à lier… La saisie reste possible.
              </span>
            )}
          </div>
        </Card>
        <Card
          role="region"
          aria-label="Aperçu en exécution"
          className="p-5 lg:sticky lg:top-[calc(var(--bandeau-demo,0px)+1.5rem)]"
        >
          <ApercuResolution b={b} systemes={catalogue.donnees ?? []} />
        </Card>
      </div>
    </main>
  );
}
