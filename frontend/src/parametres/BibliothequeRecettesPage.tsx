import { Library, ListOrdered, OctagonAlert, Pencil, Plus, Trash2 } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { api } from '../api';
import { useAuth } from '../auth';
import { premiereLigne } from '../blocs';
import { analyserRecherche, correspondRecherche, nombre, rechercheVide, trierParUsage } from '../bibliotheque';
import { formatDateHeure } from '../format';
import { useAsync } from '../hooks';
import { EtapesTestEditeur, type EtapeBrouillon } from '../mep/recette/EtapesTestEditeur';
import type { ModeleRecette, SystemeCatalogue } from '../types';
import { Alerte } from '../ui/Alerte';
import { BoutonIcone, Button } from '../ui/Button';
import { Card } from '../ui/Card';
import { ConfirmDialog, Dialog } from '../ui/Dialog';
import { EmptyState } from '../ui/EmptyState';
import { Field, Input, Select } from '../ui/Input';
import { Badge } from '../ui/StatusPill';
import { useToast } from '../ui/Toast';
import { Tooltip } from '../ui/Tooltip';
import { cn } from '../ui/cn';
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

/** « Modifié par Claire le … » : auteur et date de la dernière modification d'un modèle. */
const auteur = (m: ModeleRecette) =>
  `${m.modifiePar ? `Modifié par ${m.modifiePar}` : 'Modifié'} le ${formatDateHeure(m.modifieLe)}`;

/** Création ou modification d'un modèle : titre, système du catalogue et étapes de test (blocs puis résultat attendu). */
function ModeleDialog({
  ouvert,
  existant,
  systemePropose,
  catalogue,
  onFermer,
  onEnregistre,
}: {
  ouvert: boolean;
  existant?: ModeleRecette;
  systemePropose?: string;
  catalogue: SystemeCatalogue[];
  onFermer: () => void;
  onEnregistre: () => void;
}) {
  const toast = useToast();
  const [titre, setTitre] = useState('');
  const [systeme, setSysteme] = useState('');
  const [etapes, setEtapes] = useState<EtapeBrouillon[]>([]);
  const [erreur, setErreur] = useState('');
  const [envoi, setEnvoi] = useState(false);

  useEffect(() => {
    if (!ouvert) return;
    setTitre(existant?.titre ?? '');
    setSysteme(existant?.systemeCatalogueId ?? systemePropose ?? '');
    setEtapes(
      (existant?.etapes ?? []).map((e) => ({
        cle: `e${e.ordre}`,
        instructions: e.instructions,
        resultatAttendu: e.resultatAttendu,
      })),
    );
    setErreur('');
  }, [ouvert, existant, systemePropose]);

  async function enregistrer() {
    setErreur('');
    if (!systeme) {
      setErreur('Choisissez le système du catalogue testé par le modèle.');
      return;
    }
    const saisie = {
      titre: titre.trim(),
      systemeCatalogueId: systeme,
      etapes: etapes.map((e) => ({ instructions: e.instructions.trim(), resultatAttendu: e.resultatAttendu.trim() })),
    };
    setEnvoi(true);
    try {
      if (existant) await api.modifierModeleRecette(existant.id, { ...saisie, version: existant.version });
      else await api.creerModeleRecette(saisie);
      toast.succes(existant ? 'Modèle enregistré.' : 'Modèle ajouté à la bibliothèque.');
      onEnregistre();
      onFermer();
    } catch (e) {
      setErreur(e instanceof Error ? e.message : 'Enregistrement impossible.');
    } finally {
      setEnvoi(false);
    }
  }

  return (
    <Dialog
      open={ouvert}
      onOpenChange={(o) => {
        if (!o) onFermer();
      }}
      titre={existant ? `Modifier « ${existant.titre} »` : 'Nouveau modèle de recette'}
      description="Les fiches déjà insérées dans des MEP sont des copies : elles ne changent pas."
      taille="xl"
      pied={
        <>
          <Button variante="fantome" onClick={onFermer}>
            Annuler
          </Button>
          <Button variante="primaire" chargement={envoi} onClick={() => void enregistrer()}>
            Enregistrer
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <div className="grid gap-4 md:grid-cols-[minmax(0,1fr)_16rem]">
          <Field label="Titre du modèle *">
            {(id) => (
              <Input
                id={id}
                value={titre}
                onChange={(e) => setTitre(e.target.value)}
                placeholder="ex. Connexion au portail"
              />
            )}
          </Field>
          <Field label="Système du catalogue *">
            {(id) => (
              <Select id={id} value={systeme} onChange={(e) => setSysteme(e.target.value)}>
                <option value="">— Choisir —</option>
                {catalogue
                  .filter((s) => s.actif || s.id === systeme)
                  .map((s) => (
                    <option key={s.id} value={s.id}>
                      {s.nom}
                    </option>
                  ))}
              </Select>
            )}
          </Field>
        </div>
        <EtapesTestEditeur etapes={etapes} onChange={setEtapes} />
        {erreur && (
          <Alerte ton="danger" icone={OctagonAlert} role="alert">
            {erreur}
          </Alerte>
        )}
      </div>
    </Dialog>
  );
}

const UNITE = ['modèle', 'modèles'] as const;

/**
 * Bibliothèque de recettes (Paramètres) : modèles de fiches rattachés à un système du catalogue, à insérer par copie
 * dans les MEP (« Depuis la bibliothèque » dans Plan › Recette). Accueil : choix du système (ordre alphabétique) ou
 * recherche dans tous les systèmes ; `?systeme=WEB` : la bibliothèque du système. Tri par nombre d'utilisations, puis
 * ordre de création ; la recherche porte sur le titre.
 */
export default function BibliothequeRecettesPage() {
  const { utilisateur } = useAuth();
  const toast = useToast();
  const admin = utilisateur?.estAdmin ?? false;
  const [params] = useSearchParams();
  const nomSysteme = params.get('systeme');
  const modeles = useAsync(() => api.modelesRecette(), []);
  const cat = useAsync(() => api.catalogueSystemes(), []);
  const [recherche, setRecherche] = useState('');
  const [rechercheDe, setRechercheDe] = useState(nomSysteme);
  const [edition, setEdition] = useState<{ existant?: ModeleRecette } | null>(null);
  const [suppr, setSuppr] = useState<ModeleRecette | null>(null);

  // Changer de système (lien, retour) repart d'une recherche vide.
  if (rechercheDe !== nomSysteme) {
    setRechercheDe(nomSysteme);
    setRecherche('');
  }

  const catalogue = useMemo(() => cat.donnees ?? [], [cat.donnees]);
  const liste = useMemo(() => modeles.donnees ?? [], [modeles.donnees]);
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
  const filtres = useMemo(
    () =>
      trierParUsage(
        liste.filter(
          (m) =>
            (!courant || m.systemeCatalogueId === courant.id) &&
            correspondRecherche(analyse, { systemes: [noms.get(m.systemeCatalogueId) ?? ''], textes: [m.titre] }),
        ),
        (m) => m.creeLe,
      ),
    [liste, courant, analyse, noms],
  );
  const duSysteme = (id: string) => liste.filter((m) => m.systemeCatalogueId === id).length;
  const systemes: SystemeBiblio[] = catalogue
    .filter((s) => s.actif || duSysteme(s.id) > 0)
    .map((s) => ({ id: s.id, nom: s.nom, description: s.description, actif: s.actif }));
  const accueil = !nomSysteme && rechercheVide(analyse);
  const pret = modeles.donnees !== null && cat.donnees !== null;

  const nouveau = (
    <Button variante="primaire" icone={<Plus className="size-4" aria-hidden />} onClick={() => setEdition({})}>
      Nouveau modèle
    </Button>
  );

  return (
    <main className="mx-auto w-full max-w-6xl px-6 py-8">
      {nomSysteme ? (
        <EnTeteSysteme
          bibliotheque="Bibliothèque de recettes"
          retour="/parametres/recettes"
          systeme={nomSysteme}
          description={`${courant?.description ? `${courant.description} · ` : ''}${nombre(courant ? duSysteme(courant.id) : 0, ['modèle de fiche', 'modèles de fiche'])}`}
          action={nouveau}
        />
      ) : (
        <div className="flex flex-wrap items-end justify-between gap-4">
          <div>
            <h1 className="text-2xl font-semibold tracking-tight">Bibliothèque de recettes</h1>
            <p className="mt-1 text-sm text-fg-muted">
              Fiches de recette types, à insérer dans une MEP depuis Plan › Recette.
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
              ? `Rechercher dans ${nomSysteme} (titre)…`
              : 'Rechercher dans tous les systèmes, ex. « WEB portail »…'
          }
        />
        {!accueil && <MentionTri className="ml-auto" />}
      </div>
      {!nomSysteme && <AideRecherche champs="le titre du modèle" />}

      {(modeles.erreur || cat.erreur) && (
        <Alerte ton="danger" icone={OctagonAlert} role="alert" className="mt-6">
          {modeles.erreur || cat.erreur}
        </Alerte>
      )}

      {!pret && !(modeles.erreur || cat.erreur) && <SqueletteBibliotheque accueil={accueil} />}
      <div className={cn(pret && 'apparition')}>
        {pret && !nomSysteme && !accueil && (
          <div className="mt-6">
            <LectureRecherche analyse={analyse} nb={filtres.length} unite={UNITE} champs="le titre" />
          </div>
        )}

        {pret && accueil && (
          <>
            <h2 className="mt-8 mb-3 text-xs font-semibold tracking-wider text-fg-subtle uppercase">
              Choisissez un système · {nombre(systemes.length, ['système', 'systèmes'])} du catalogue
            </h2>
            <GrilleSystemes
              systemes={systemes}
              compter={(s) => duSysteme(s.id)}
              unite={UNITE}
              lien={(s) => `/parametres/recettes?systeme=${encodeURIComponent(s.nom)}`}
            />
          </>
        )}

        {pret && nomSysteme && !courant && (
          <Alerte ton="warn" icone={OctagonAlert} className="mt-6">
            Le système « {nomSysteme} » n’est pas dans le catalogue.
          </Alerte>
        )}

        {pret && !accueil && (!nomSysteme || courant) && filtres.length === 0 && (
          <Card className="mt-4">
            <EmptyState
              icone={Library}
              titre={liste.length === 0 ? 'Bibliothèque vide' : 'Aucun résultat'}
              description={
                liste.length === 0
                  ? 'Enregistrez une fiche de recette comme modèle depuis sa page, ou créez-en un ici.'
                  : 'Aucun modèle ne correspond à ces critères.'
              }
            />
          </Card>
        )}

        {pret && !accueil && (!nomSysteme || courant) && (
          <ul className="mt-4 grid gap-3 lg:grid-cols-2">
            {filtres.map((m) => (
              <li key={m.id}>
                <Card className="flex h-full flex-col p-4 transition-shadow duration-150 hover:shadow-md">
                  <div className="flex items-start justify-between gap-3">
                    <div className="min-w-0">
                      <h2 className="flex items-center gap-2 font-semibold">
                        <Library className="size-4 shrink-0 text-accent" aria-hidden />
                        <span className="truncate">{m.titre}</span>
                      </h2>
                      <div className="mt-1.5 flex flex-wrap items-center gap-2">
                        {!nomSysteme && <Badge>{noms.get(m.systemeCatalogueId) ?? 'Système'}</Badge>}
                        <Badge>
                          <ListOrdered className="size-3" aria-hidden />
                          {nombre(m.etapes.length, ['étape de test', 'étapes de test'])}
                        </Badge>
                      </div>
                    </div>
                    <div className="flex shrink-0 items-center gap-0.5">
                      <CompteurUtilisations n={m.utilisations} className="mr-1.5" />
                      <Tooltip content="Modifier">
                        <BoutonIcone
                          label={`Modifier ${m.titre}`}
                          taille="sm"
                          onClick={() => setEdition({ existant: m })}
                        >
                          <Pencil className="size-4" aria-hidden />
                        </BoutonIcone>
                      </Tooltip>
                      {admin && (
                        <Tooltip content="Supprimer">
                          <BoutonIcone label={`Supprimer ${m.titre}`} taille="sm" onClick={() => setSuppr(m)}>
                            <Trash2 className="size-4" aria-hidden />
                          </BoutonIcone>
                        </Tooltip>
                      )}
                    </div>
                  </div>
                  <ol className="mt-3 space-y-1 text-sm text-fg-muted">
                    {m.etapes.slice(0, 3).map((e) => (
                      <li key={e.ordre} className="truncate">
                        {e.ordre}. {premiereLigne(e.instructions) || 'Déroulé à compléter'}
                      </li>
                    ))}
                    {m.etapes.length > 3 && <li className="text-fg-subtle">…</li>}
                  </ol>
                  <p className="mt-auto pt-3 text-xs text-fg-subtle">
                    {auteur(m)} · version {m.version}
                  </p>
                </Card>
              </li>
            ))}
          </ul>
        )}
      </div>

      <ModeleDialog
        ouvert={edition !== null}
        existant={edition?.existant}
        systemePropose={courant?.id}
        catalogue={catalogue}
        onFermer={() => setEdition(null)}
        onEnregistre={() => modeles.recharger()}
      />
      <ConfirmDialog
        open={suppr !== null}
        onOpenChange={(o) => {
          if (!o) setSuppr(null);
        }}
        titre={`Supprimer « ${suppr?.titre ?? ''} » ?`}
        description="Le modèle quitte la bibliothèque ; les fiches déjà insérées dans des MEP restent."
        libelleConfirmer="Supprimer"
        variante="danger"
        onConfirmer={async () => {
          if (!suppr) return false;
          try {
            await api.supprimerModeleRecette(suppr.id);
            toast.succes('Modèle supprimé.');
            modeles.recharger();
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
