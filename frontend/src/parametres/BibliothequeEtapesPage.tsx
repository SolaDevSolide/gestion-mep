import { Blocks, Copy, OctagonAlert, Pencil, Plus, Trash2 } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { api } from '../api';
import { useAuth } from '../auth';
import { premiereLigne } from '../blocs';
import { analyserRecherche, filtrerEtapesModeles, nombre, rechercheVide, repartitionTypes } from '../bibliotheque';
import { formatDate, formatDateHeure } from '../format';
import { useAsync } from '../hooks';
import { PuceSysteme } from '../mep/graphe/systemes';
import type { ModeleEtape, TypeEtape } from '../types';
import { Alerte } from '../ui/Alerte';
import { BoutonIcone, Button } from '../ui/Button';
import { Card } from '../ui/Card';
import { ConfirmDialog } from '../ui/Dialog';
import { EmptyState } from '../ui/EmptyState';
import { useToast } from '../ui/Toast';
import { Tooltip } from '../ui/Tooltip';
import {
  AideRecherche,
  BadgesModele,
  CHAMPS_ETAPES,
  CompteurUtilisations,
  EnTeteSysteme,
  FiltreType,
  GrilleSystemes,
  LectureRecherche,
  MentionTri,
  RechercheBibliotheque,
  type SystemeBiblio,
  UNITE_ETAPES,
  SqueletteBibliotheque,
} from './bibliotheque';
import { EtapeModeleDialog, type EditionModele } from './EtapeModeleDialog';

function CarteModele({
  m,
  nomSysteme,
  admin,
  avecSystemes,
  onDupliquer,
  onModifier,
  onSupprimer,
}: {
  m: ModeleEtape;
  nomSysteme: (id: string) => string;
  admin: boolean;
  avecSystemes: boolean;
  onDupliquer: () => void;
  onModifier: () => void;
  onSupprimer: () => void;
}) {
  return (
    <Card className="flex h-full flex-col p-4 transition-shadow duration-150 hover:shadow-md">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <h2 className="flex items-center gap-2 font-semibold">
            <Blocks className="size-4 shrink-0 text-accent" aria-hidden />
            <span className="truncate">{m.action}</span>
          </h2>
          <div className="mt-1.5 flex flex-wrap items-center gap-2">
            {(avecSystemes || m.systemeCatalogueIds.length > 1) &&
              m.systemeCatalogueIds.map((s) => (
                <PuceSysteme key={s} nom={nomSysteme(s)} className="px-2 py-0 text-xs" />
              ))}
            <BadgesModele m={m} />
          </div>
        </div>
        <div className="flex shrink-0 items-center gap-0.5">
          <CompteurUtilisations n={m.utilisations} className="mr-1.5" />
          <Tooltip content="Dupliquer">
            <BoutonIcone label={`Dupliquer ${m.action}`} taille="sm" onClick={onDupliquer}>
              <Copy className="size-4" aria-hidden />
            </BoutonIcone>
          </Tooltip>
          <Tooltip content="Modifier">
            <BoutonIcone label={`Modifier ${m.action}`} taille="sm" onClick={onModifier}>
              <Pencil className="size-4" aria-hidden />
            </BoutonIcone>
          </Tooltip>
          {admin && (
            <Tooltip content="Supprimer">
              <BoutonIcone label={`Supprimer ${m.action}`} taille="sm" onClick={onSupprimer}>
                <Trash2 className="size-4" aria-hidden />
              </BoutonIcone>
            </Tooltip>
          )}
        </div>
      </div>
      <p className="mt-3 truncate font-mono text-ui text-fg-muted">
        {premiereLigne(m.instructions) || 'Instructions à compléter'}
      </p>
      {m.resultatAttendu && (
        <p className="mt-1 truncate text-sm text-fg-muted">
          <span className="font-medium text-fg">Attendu :</span> {m.resultatAttendu}
        </p>
      )}
      <p className="mt-auto pt-3 text-xs text-fg-subtle" title={`Modifiée le ${formatDateHeure(m.modifieLe)}`}>
        Créée le {formatDate(m.creeLe)}
        {m.modifiePar && ` · modifiée par ${m.modifiePar}`} · version {m.version}
      </p>
    </Card>
  );
}

/**
 * Bibliothèque d'étapes modèles (Paramètres). Accueil : choix du système (ordre alphabétique) ou recherche dans tous
 * les systèmes ; `?systeme=BDD` : la bibliothèque du système. Tri par nombre d'utilisations, puis ordre de création.
 * Tout le monde crée, modifie et duplique ; seul un administrateur supprime.
 */
export default function BibliothequeEtapesPage() {
  const { utilisateur } = useAuth();
  const toast = useToast();
  const admin = utilisateur?.estAdmin ?? false;
  const [params] = useSearchParams();
  const nomSysteme = params.get('systeme');
  const modeles = useAsync(() => api.etapesModeles(), []);
  const cat = useAsync(() => api.catalogueSystemes(), []);
  const res = useAsync(() => api.resolutions(), []);
  const [recherche, setRecherche] = useState('');
  const [type, setType] = useState<TypeEtape | ''>('');
  const [rechercheDe, setRechercheDe] = useState(nomSysteme);
  const [edition, setEdition] = useState<EditionModele | null>(null);
  const [suppr, setSuppr] = useState<ModeleEtape | null>(null);

  // Changer de système (lien, retour) repart d'une recherche vide.
  if (rechercheDe !== nomSysteme) {
    setRechercheDe(nomSysteme);
    setRecherche('');
  }

  const catalogue = useMemo(() => cat.donnees ?? [], [cat.donnees]);
  const liste = useMemo(() => modeles.donnees ?? [], [modeles.donnees]);
  const noms = useMemo(() => new Map(catalogue.map((s) => [s.id, s.nom])), [catalogue]);
  const nom = (id: string) => noms.get(id) ?? 'Système';
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
    () => filtrerEtapesModeles(liste, (id) => noms.get(id) ?? '', analyse, { systemeId: courant?.id, type }),
    [liste, noms, analyse, courant, type],
  );
  const duSysteme = (id: string) => liste.filter((m) => m.systemeCatalogueIds.includes(id));
  const systemes: SystemeBiblio[] = catalogue
    .filter((s) => s.actif || duSysteme(s.id).length > 0)
    .map((s) => ({ id: s.id, nom: s.nom, description: s.description, actif: s.actif }));

  const nouveau = (
    <Button
      variante="primaire"
      icone={<Plus className="size-4" aria-hidden />}
      onClick={() => setEdition({ mode: 'creation', systemeId: courant?.id })}
    >
      Nouvelle étape modèle
    </Button>
  );

  const resultats =
    filtres.length === 0 ? (
      <Card className="mt-4">
        <EmptyState
          icone={Blocks}
          titre={liste.length === 0 ? 'Bibliothèque vide' : 'Aucun résultat'}
          description={
            liste.length === 0
              ? 'Transformez une étape d’une MEP en étape modèle depuis Plan › Étapes, ou créez-en une ici.'
              : 'Aucune étape modèle ne correspond à ces critères.'
          }
        />
      </Card>
    ) : (
      <ul className="mt-4 grid gap-3 lg:grid-cols-2">
        {filtres.map((m) => (
          <li key={m.id}>
            <CarteModele
              m={m}
              nomSysteme={nom}
              admin={admin}
              avecSystemes={!courant}
              onDupliquer={() => setEdition({ mode: 'copie', modele: m })}
              onModifier={() => setEdition({ mode: 'modification', modele: m })}
              onSupprimer={() => setSuppr(m)}
            />
          </li>
        ))}
      </ul>
    );

  const dialogues = (
    <>
      <EtapeModeleDialog
        edition={edition}
        catalogue={catalogue}
        resolutions={res.donnees ?? []}
        onFermer={() => setEdition(null)}
        onEnregistre={() => modeles.recharger()}
      />
      <ConfirmDialog
        open={suppr !== null}
        onOpenChange={(o) => {
          if (!o) setSuppr(null);
        }}
        titre={`Supprimer « ${suppr?.action ?? ''} » ?`}
        description="L’étape modèle quitte la bibliothèque ; les étapes déjà insérées dans des MEP restent."
        libelleConfirmer="Supprimer"
        variante="danger"
        onConfirmer={async () => {
          if (!suppr) return false;
          try {
            await api.supprimerEtapeModele(suppr.id);
            toast.succes('Étape modèle supprimée.');
            modeles.recharger();
            return true;
          } catch (e) {
            toast.erreur(e instanceof Error ? e.message : 'Suppression impossible.');
            return false;
          }
        }}
      />
    </>
  );

  const erreur = (modeles.erreur || cat.erreur) && (
    <Alerte ton="danger" icone={OctagonAlert} role="alert" className="mt-6">
      {modeles.erreur || cat.erreur}
    </Alerte>
  );
  const pret = modeles.donnees !== null && cat.donnees !== null;

  if (nomSysteme) {
    const total = courant ? duSysteme(courant.id).length : 0;
    return (
      <main className="mx-auto w-full max-w-6xl px-6 py-8">
        <EnTeteSysteme
          bibliotheque="Bibliothèque d’étapes"
          retour="/parametres/etapes"
          systeme={nomSysteme}
          description={`${courant?.description ? `${courant.description} · ` : ''}${nombre(total, UNITE_ETAPES)}`}
          action={nouveau}
        />
        <div className="mt-6 flex flex-wrap items-center gap-3">
          <RechercheBibliotheque
            className="sm:max-w-sm"
            valeur={recherche}
            onChange={setRecherche}
            placeholder={`Rechercher dans ${nomSysteme} (nom, type)…`}
          />
          <FiltreType valeur={type} onChange={setType} />
          <MentionTri className="ml-auto" />
        </div>
        {erreur}
        {pret && !courant && (
          <Alerte ton="warn" icone={OctagonAlert} className="mt-6">
            Le système « {nomSysteme} » n’est pas dans le catalogue.
          </Alerte>
        )}
        {!pret && !erreur && <SqueletteBibliotheque accueil={false} />}
        {pret && courant && <div className="apparition">{resultats}</div>}
        {dialogues}
      </main>
    );
  }

  const enRecherche = !rechercheVide(analyse) || type !== '';
  return (
    <main className="mx-auto w-full max-w-6xl px-6 py-8">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Bibliothèque d’étapes</h1>
          <p className="mt-1 text-sm text-fg-muted">
            Étapes types, insérées par copie dans Plan › Étapes : modifier la copie ne change pas le modèle.
          </p>
        </div>
        {nouveau}
      </div>

      <div className="mt-6 flex flex-wrap items-center gap-3">
        <RechercheBibliotheque
          className="sm:max-w-xl"
          valeur={recherche}
          onChange={setRecherche}
          placeholder="Rechercher dans tous les systèmes, ex. « BDD sauvegarde »…"
        />
        <FiltreType valeur={type} onChange={setType} />
      </div>
      <AideRecherche champs="le nom ou le type de l’étape" />
      {erreur}

      {!pret && !erreur && <SqueletteBibliotheque accueil={!enRecherche} />}
      {pret &&
        (enRecherche ? (
          <div className="apparition">
            <div className="mt-6 flex flex-wrap items-center justify-between gap-3">
              <LectureRecherche analyse={analyse} nb={filtres.length} unite={UNITE_ETAPES} champs={CHAMPS_ETAPES} />
              <MentionTri />
            </div>
            {resultats}
          </div>
        ) : (
          <div className="apparition">
            <h2 className="mt-8 mb-3 text-xs font-semibold tracking-wider text-fg-subtle uppercase">
              Choisissez un système · {nombre(systemes.length, ['système', 'systèmes'])} du catalogue
            </h2>
            {systemes.length === 0 ? (
              <Card>
                <EmptyState
                  icone={Blocks}
                  titre="Catalogue vide"
                  description="Ajoutez des systèmes au catalogue (Paramètres › Systèmes) avant de créer des étapes modèles."
                />
              </Card>
            ) : (
              <GrilleSystemes
                systemes={systemes}
                compter={(s) => duSysteme(s.id).length}
                detail={(s) => repartitionTypes(duSysteme(s.id))}
                unite={['étape', 'étapes']}
                lien={(s) => `/parametres/etapes?systeme=${encodeURIComponent(s.nom)}`}
              />
            )}
          </div>
        ))}
      {dialogues}
    </main>
  );
}
