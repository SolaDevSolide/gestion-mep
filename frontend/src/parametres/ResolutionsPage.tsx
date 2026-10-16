import { History, ListOrdered, OctagonAlert, Pencil, Plus, Trash2, Wrench } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { api } from '../api';
import { useAuth } from '../auth';
import { analyserRecherche, correspondRecherche, nombre, rechercheVide, trierParUsage } from '../bibliotheque';
import { useAsync } from '../hooks';
import { ResolutionEditeurDialog } from '../mep/ResolutionEditeur';
import type { ResolutionType } from '../types';
import { Alerte } from '../ui/Alerte';
import { BoutonIcone, Button } from '../ui/Button';
import { Card } from '../ui/Card';
import { ConfirmDialog } from '../ui/Dialog';
import { EmptyState } from '../ui/EmptyState';
import { Badge } from '../ui/StatusPill';
import { Markdown } from '../ui/Instructions';
import { Tooltip } from '../ui/Tooltip';
import { useToast } from '../ui/Toast';
import {
  AideRecherche,
  CompteurUtilisations,
  EnTeteSysteme,
  GrilleSystemes,
  LectureRecherche,
  MentionTri,
  RechercheBibliotheque,
  type SystemeBiblio,
  SqueletteBibliotheque,
} from './bibliotheque';
import { cn } from '../ui/cn';
import { auteurModification, HistoriqueResolutionDialog } from './HistoriqueResolution';

const UNITE = ['résolution', 'résolutions'] as const;

/**
 * Résolutions types (Paramètres). Accueil : choix du système (ordre alphabétique) ou recherche dans tous les systèmes ;
 * `?systeme=BDD` : les résolutions du système, plus celles qui ne visent aucun système précis (elles valent pour tous).
 * Tri par nombre d'utilisations, puis ordre de création ; la recherche porte sur le titre et les symptômes.
 */
export default function ResolutionsPage() {
  const { utilisateur } = useAuth();
  const toast = useToast();
  const navigate = useNavigate();
  const admin = utilisateur?.estAdmin ?? false;
  const [params] = useSearchParams();
  const nomSysteme = params.get('systeme');
  const res = useAsync(() => api.resolutions(), []);
  const cat = useAsync(() => api.catalogueSystemes(), []);
  const [recherche, setRecherche] = useState('');
  const [rechercheDe, setRechercheDe] = useState(nomSysteme);
  const [edition, setEdition] = useState<{ existant?: ResolutionType } | null>(null);
  const [suppr, setSuppr] = useState<ResolutionType | null>(null);
  const [historique, setHistorique] = useState<ResolutionType | null>(null);

  // Changer de système (lien, retour) repart d'une recherche vide.
  if (rechercheDe !== nomSysteme) {
    setRechercheDe(nomSysteme);
    setRecherche('');
  }

  const catalogue = useMemo(() => cat.donnees ?? [], [cat.donnees]);
  const liste = useMemo(() => res.donnees ?? [], [res.donnees]);
  const noms = useMemo(() => new Map(catalogue.map((s) => [s.id, s.nom])), [catalogue]);
  const courant = catalogue.find((s) => s.nom === nomSysteme) ?? null;
  const analyse = useMemo(
    () =>
      analyserRecherche(
        recherche,
        catalogue.map((s) => s.nom),
      ),
    [recherche, catalogue],
  );
  const filtrees = useMemo(
    () =>
      trierParUsage(
        liste.filter(
          (r) =>
            (!courant || r.systemeCatalogueIds.length === 0 || r.systemeCatalogueIds.includes(courant.id)) &&
            correspondRecherche(analyse, {
              systemes: r.systemeCatalogueIds.map((s) => noms.get(s) ?? ''),
              tousSystemes: r.systemeCatalogueIds.length === 0,
              textes: [r.titre, r.symptomes],
            }),
        ),
        (r) => r.creeeLe,
      ),
    [liste, courant, analyse, noms],
  );
  const duSysteme = (id: string) => liste.filter((r) => r.systemeCatalogueIds.includes(id));
  const communes = liste.filter((r) => r.systemeCatalogueIds.length === 0).length;
  const systemes: SystemeBiblio[] = catalogue
    .filter((s) => s.actif || duSysteme(s.id).length > 0)
    .map((s) => ({ id: s.id, nom: s.nom, description: s.description, actif: s.actif }));
  const accueil = !nomSysteme && rechercheVide(analyse);
  const pret = res.donnees !== null && cat.donnees !== null;

  const nouveau = (
    <Button
      variante="primaire"
      icone={<Plus className="size-4" aria-hidden />}
      onClick={() => void navigate('/parametres/resolutions/nouvelle')}
    >
      Nouvelle résolution
    </Button>
  );

  return (
    <main className="mx-auto w-full max-w-6xl px-6 py-8">
      {nomSysteme ? (
        <EnTeteSysteme
          bibliotheque="Résolutions types"
          retour="/parametres/resolutions"
          systeme={nomSysteme}
          description={`${nombre(courant ? duSysteme(courant.id).length : 0, UNITE)}${communes ? ` · + ${nombre(communes, ['commune', 'communes'])} à tous les systèmes` : ''}`}
          action={nouveau}
        />
      ) : (
        <div className="flex flex-wrap items-end justify-between gap-4">
          <div>
            <h1 className="text-2xl font-semibold tracking-tight">Résolutions types</h1>
            <p className="mt-1 text-sm text-fg-muted">
              Procédures de correction, liées aux étapes et proposées pendant l’exécution en cas d’échec.
            </p>
          </div>
          {nouveau}
        </div>
      )}

      <div className="mt-6 flex flex-wrap items-center gap-3">
        <RechercheBibliotheque
          className={nomSysteme ? 'sm:max-w-sm' : 'sm:max-w-xl'}
          valeur={recherche}
          onChange={setRecherche}
          placeholder={
            nomSysteme
              ? `Rechercher dans ${nomSysteme} (titre, symptômes)…`
              : 'Rechercher dans tous les systèmes, ex. « BDD droits »…'
          }
        />
        {!accueil && <MentionTri className="ml-auto" />}
      </div>
      {!nomSysteme && <AideRecherche champs="le titre ou les symptômes" />}

      {(res.erreur || cat.erreur) && (
        <Alerte ton="danger" icone={OctagonAlert} role="alert" className="mt-6">
          {res.erreur || cat.erreur}
        </Alerte>
      )}

      {!pret && !(res.erreur || cat.erreur) && <SqueletteBibliotheque accueil={accueil} />}
      <div className={cn(pret && 'apparition')}>
        {pret && !nomSysteme && !accueil && (
          <div className="mt-6">
            <LectureRecherche analyse={analyse} nb={filtrees.length} unite={UNITE} champs="le titre ou les symptômes" />
          </div>
        )}

        {pret && accueil && liste.length === 0 && (
          <Card className="mt-6">
            <EmptyState
              icone={Wrench}
              titre="Aucune résolution type"
              description="Écrivez une première procédure : symptômes, corrections ordonnées et vérification finale."
              action={nouveau}
            />
          </Card>
        )}

        {pret && accueil && liste.length > 0 && (
          <>
            <h2 className="mt-8 mb-3 text-xs font-semibold tracking-wider text-fg-subtle uppercase">
              Choisissez un système · {nombre(systemes.length, ['système', 'systèmes'])} du catalogue
            </h2>
            <GrilleSystemes
              systemes={systemes}
              compter={(s) => duSysteme(s.id).length}
              detail={(s) => {
                const multi = duSysteme(s.id).filter((r) => r.systemeCatalogueIds.length > 1).length;
                return multi ? `dont ${multi} multi-systèmes` : 'Toutes propres à ce système';
              }}
              unite={UNITE}
              lien={(s) => `/parametres/resolutions?systeme=${encodeURIComponent(s.nom)}`}
            />
            {communes > 0 && (
              <p className="mt-3 text-xs text-fg-subtle">
                + {nombre(communes, ['résolution commune', 'résolutions communes'])} à tous les systèmes, proposée
                {communes > 1 ? 's' : ''} dans chacun.
              </p>
            )}
          </>
        )}

        {pret && nomSysteme && !courant && (
          <Alerte ton="warn" icone={OctagonAlert} className="mt-6">
            Le système « {nomSysteme} » n’est pas dans le catalogue.
          </Alerte>
        )}

        {pret && !accueil && (!nomSysteme || courant) && filtrees.length === 0 && (
          <Card className="mt-4">
            <EmptyState
              icone={Wrench}
              titre="Aucun résultat"
              description="Aucune résolution ne correspond à ces critères."
            />
          </Card>
        )}

        {pret && !accueil && (!nomSysteme || courant) && (
          <ul className="mt-4 grid gap-3 lg:grid-cols-2">
            {filtrees.map((r) => (
              <li key={r.id}>
                <Card className="flex h-full flex-col p-4 transition-shadow duration-150 hover:shadow-md">
                  <div className="flex items-start justify-between gap-3">
                    <div className="min-w-0">
                      <h2 className="flex items-center gap-2 font-semibold">
                        <Wrench className="size-4 shrink-0 text-warn-solid" aria-hidden />
                        <span className="truncate">{r.titre}</span>
                      </h2>
                      <div className="mt-1.5 flex flex-wrap items-center gap-2">
                        {r.systemeCatalogueIds.length ? (
                          (!nomSysteme || r.systemeCatalogueIds.length > 1) &&
                          r.systemeCatalogueIds.map((s) => <Badge key={s}>{noms.get(s) ?? 'Système'}</Badge>)
                        ) : (
                          <Badge>Tous les systèmes</Badge>
                        )}
                        <Badge>
                          <ListOrdered className="size-3" aria-hidden />
                          {nombre(r.corrections.length, ['correction', 'corrections'])}
                        </Badge>
                      </div>
                    </div>
                    <div className="flex shrink-0 items-center gap-0.5">
                      <CompteurUtilisations n={r.utilisations} className="mr-1.5" />
                      <Tooltip content="Historique">
                        <BoutonIcone label={`Historique de ${r.titre}`} taille="sm" onClick={() => setHistorique(r)}>
                          <History className="size-4" aria-hidden />
                        </BoutonIcone>
                      </Tooltip>
                      <Tooltip content="Modifier">
                        <BoutonIcone
                          label={`Modifier ${r.titre}`}
                          taille="sm"
                          onClick={() => setEdition({ existant: r })}
                        >
                          <Pencil className="size-4" aria-hidden />
                        </BoutonIcone>
                      </Tooltip>
                      {admin && (
                        <Tooltip content="Supprimer">
                          <BoutonIcone label={`Supprimer ${r.titre}`} taille="sm" onClick={() => setSuppr(r)}>
                            <Trash2 className="size-4" aria-hidden />
                          </BoutonIcone>
                        </Tooltip>
                      )}
                    </div>
                  </div>
                  {r.symptomes ? (
                    <Markdown className="mt-3 line-clamp-3 text-fg-muted" source={r.symptomes} />
                  ) : (
                    <p className="mt-3 text-sm text-fg-muted">Symptômes non décrits.</p>
                  )}
                  <p className="mt-auto pt-3 text-xs text-fg-subtle">
                    {auteurModification(r)} · version {r.version}
                  </p>
                </Card>
              </li>
            ))}
          </ul>
        )}
      </div>

      <ResolutionEditeurDialog
        key={edition?.existant?.id ?? 'nouvelle'}
        open={edition !== null}
        onOpenChange={(o) => {
          if (!o) setEdition(null);
        }}
        initial={edition?.existant}
        onEnregistree={() => res.recharger()}
      />
      <HistoriqueResolutionDialog resolution={historique} onFermer={() => setHistorique(null)} />
      <ConfirmDialog
        open={suppr !== null}
        onOpenChange={(o) => {
          if (!o) setSuppr(null);
        }}
        titre={`Supprimer « ${suppr?.titre ?? ''} » ?`}
        description="Si elle est liée à une étape ou à une étape modèle, elle sera seulement désactivée."
        libelleConfirmer="Supprimer"
        variante="danger"
        onConfirmer={async () => {
          if (!suppr) return false;
          try {
            await api.supprimerResolution(suppr.id);
            toast.succes('Résolution supprimée ou désactivée.');
            res.recharger();
            return true;
          } catch (e) {
            toast.erreur(e instanceof Error ? e.message : 'Suppression impossible.');
            return false;
          }
        }}
      />
    </main>
  );
}
