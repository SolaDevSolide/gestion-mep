import { Library, ListOrdered, OctagonAlert, Pencil, Plus, Search, Trash2 } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { api } from '../api';
import { useAuth } from '../auth';
import { premiereLigne } from '../blocs';
import { formatDateHeure } from '../format';
import { useAsync } from '../hooks';
import { EtapesTestEditeur, type EtapeBrouillon } from '../mep/recette/EtapesTestEditeur';
import { correspond, termes } from '../recherche';
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

/** « Modifié par Claire le … » : auteur et date de la dernière modification d'un modèle. */
const auteur = (m: ModeleRecette) =>
  `${m.modifiePar ? `Modifié par ${m.modifiePar}` : 'Modifié'} le ${formatDateHeure(m.modifieLe)}`;

/** Création ou modification d'un modèle : titre, système du catalogue et étapes de test (blocs puis résultat attendu). */
function ModeleDialog({
  ouvert,
  existant,
  catalogue,
  onFermer,
  onEnregistre,
}: {
  ouvert: boolean;
  existant?: ModeleRecette;
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
    setSysteme(existant?.systemeCatalogueId ?? '');
    setEtapes(
      (existant?.etapes ?? []).map((e) => ({
        cle: `e${e.ordre}`,
        instructions: e.instructions,
        resultatAttendu: e.resultatAttendu,
      })),
    );
    setErreur('');
  }, [ouvert, existant]);

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

/**
 * Bibliothèque de recettes (Paramètres) : modèles de fiches rattachés à un système du catalogue, à insérer par copie
 * dans les MEP (« Depuis la bibliothèque » dans Plan › Recette). Recherche et filtre par système.
 */
export default function BibliothequeRecettesPage() {
  const { utilisateur } = useAuth();
  const toast = useToast();
  const admin = utilisateur?.estAdmin ?? false;
  const modeles = useAsync(() => api.modelesRecette(), []);
  const cat = useAsync(() => api.catalogueSystemes(), []);
  const [recherche, setRecherche] = useState('');
  const [systeme, setSysteme] = useState('');
  const [edition, setEdition] = useState<{ existant?: ModeleRecette } | null>(null);
  const [suppr, setSuppr] = useState<ModeleRecette | null>(null);

  const noms = useMemo(() => new Map((cat.donnees ?? []).map((s) => [s.id, s.nom])), [cat.donnees]);
  const filtres = useMemo(() => {
    const ts = termes(recherche);
    return (modeles.donnees ?? []).filter(
      (m) =>
        (!systeme || m.systemeCatalogueId === systeme) &&
        correspond(
          {
            titre: m.titre,
            textes: [
              noms.get(m.systemeCatalogueId) ?? '',
              ...m.etapes.flatMap((e) => [e.instructions, e.resultatAttendu]),
            ],
          },
          ts,
        ),
    );
  }, [modeles.donnees, recherche, systeme, noms]);

  return (
    <main className="mx-auto w-full max-w-6xl px-6 py-8">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Bibliothèque de recettes</h1>
          <p className="mt-1 text-sm text-fg-muted">
            Fiches de recette types, à insérer dans une MEP depuis Plan › Recette.
          </p>
        </div>
        <Button variante="primaire" icone={<Plus className="size-4" aria-hidden />} onClick={() => setEdition({})}>
          Nouveau modèle
        </Button>
      </div>

      <div className="mt-6 flex flex-wrap items-center gap-3">
        <div className="relative min-w-60 flex-1 sm:max-w-sm">
          <Search
            className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-fg-subtle"
            aria-hidden
          />
          <Input
            aria-label="Rechercher un modèle"
            placeholder="Rechercher (titre, déroulé)…"
            className="pl-9"
            value={recherche}
            onChange={(e) => setRecherche(e.target.value)}
          />
        </div>
        <div className="w-56">
          <Select aria-label="Filtrer par système" value={systeme} onChange={(e) => setSysteme(e.target.value)}>
            <option value="">Tous les systèmes</option>
            {(cat.donnees ?? []).map((s) => (
              <option key={s.id} value={s.id}>
                {s.nom}
              </option>
            ))}
          </Select>
        </div>
      </div>

      {modeles.erreur && (
        <Alerte ton="danger" icone={OctagonAlert} role="alert" className="mt-6">
          {modeles.erreur}
        </Alerte>
      )}

      {modeles.donnees !== null && filtres.length === 0 && (
        <Card className="mt-6">
          <EmptyState
            icone={Library}
            titre={modeles.donnees.length === 0 ? 'Bibliothèque vide' : 'Aucun résultat'}
            description={
              modeles.donnees.length === 0
                ? 'Enregistrez une fiche de recette comme modèle depuis sa page, ou créez-en un ici.'
                : 'Aucun modèle ne correspond à ces critères.'
            }
          />
        </Card>
      )}

      <ul className="mt-6 grid gap-3 lg:grid-cols-2">
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
                    <Badge>{noms.get(m.systemeCatalogueId) ?? 'Système'}</Badge>
                    <Badge>
                      <ListOrdered className="size-3" aria-hidden />
                      {m.etapes.length} étape{m.etapes.length > 1 ? 's' : ''} de test
                    </Badge>
                  </div>
                </div>
                <div className="flex shrink-0 gap-0.5">
                  <Tooltip content="Modifier">
                    <BoutonIcone label={`Modifier ${m.titre}`} taille="sm" onClick={() => setEdition({ existant: m })}>
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

      <ModeleDialog
        ouvert={edition !== null}
        existant={edition?.existant}
        catalogue={cat.donnees ?? []}
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
