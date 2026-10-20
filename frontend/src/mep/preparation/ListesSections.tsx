import { Library, Pencil, Plus, Server, Trash2, UserRound, Users, Layers } from 'lucide-react';
import { useContext, useMemo, useState, type FormEvent, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../../api';
import { useReferentiel } from '../../referentiels';
import { AuthContext } from '../../auth';
import { useMep } from '../../mepContext';
import { filtrerParEquipe } from '../../personnes';
import { ENVIRONNEMENTS, memeEnvironnement, nomDeProduction } from '../../environnements';
import type { Participant, Systeme, SystemeCatalogue } from '../../types';
import { cn } from '../../ui/cn';
import { Avatar } from '../../ui/Avatar';
import { Badge } from '../../ui/StatusPill';
import { BoutonIcone, Button } from '../../ui/Button';
import { Combobox } from '../../ui/Combobox';
import { ConfirmDialog } from '../../ui/Dialog';
import { EmptyState } from '../../ui/EmptyState';
import { ErreurChargement } from '../../ui/ErreurChargement';
import { Field, Input } from '../../ui/Input';
import { Tooltip } from '../../ui/Tooltip';
import { FormulaireDialog, SectionCarte, vide, type ChampForm } from './Formulaires';
import { participantDepuisCompte } from './suggestions';
import { NomSystemeMep } from '../SystemeCliquable';

function Ligne({
  gauche,
  centre,
  actions,
  bas,
}: {
  gauche: ReactNode;
  centre?: ReactNode;
  actions?: ReactNode;
  /** Ligne pleine largeur sous la ligne principale. */
  bas?: ReactNode;
}) {
  return (
    <li className="flex flex-wrap items-center gap-x-4 gap-y-2 px-4 py-3">
      <div className="flex min-w-0 flex-1 basis-56 items-center gap-3">{gauche}</div>
      {centre && <div className="min-w-0 flex-1 basis-56 text-sm text-fg-muted">{centre}</div>}
      <div className="flex shrink-0 items-center gap-1">{actions}</div>
      {bas && <div className="basis-full">{bas}</div>}
    </li>
  );
}

function Actions({ onModifier, onSupprimer, nom }: { onModifier: () => void; onSupprimer: () => void; nom: string }) {
  return (
    <>
      <Tooltip content="Modifier">
        <BoutonIcone label={`Modifier ${nom}`} taille="sm" onClick={onModifier}>
          <Pencil className="size-4" aria-hidden />
        </BoutonIcone>
      </Tooltip>
      <Tooltip content="Supprimer">
        <BoutonIcone label={`Supprimer ${nom}`} taille="sm" onClick={onSupprimer}>
          <Trash2 className="size-4" aria-hidden />
        </BoutonIcone>
      </Tooltip>
    </>
  );
}

const Liste = ({ children }: { children: ReactNode }) => (
  <ul className="divide-y divide-line overflow-hidden rounded-lg border border-line">{children}</ul>
);

/* ------------------------------------------------------------------ Personnes */

export function PersonnesSection() {
  const { mep, run } = useMep();
  const edition = mep.droits.peutEditerContenu;
  /** Après le Go, le pilote, un suppléant ou un administrateur ajoute encore une personne (astreinte, renfort). */
  const ajoutApresGo = !edition && mep.droits.peutAjouterPersonne;
  const comptesLus = useReferentiel('comptes');
  const equipesLues = useReferentiel('equipes');
  const utilisateurs = comptesLus.donnees ?? [];
  const equipes = equipesLues.donnees ?? [];
  const [forme, setForme] = useState<{ existant?: Participant } | null>(null);
  const [suppr, setSuppr] = useState<Participant | null>(null);
  /** Filtre des comptes proposés par équipe, et compte choisi (toujours proposé, même hors du filtre). */
  const [equipe, setEquipe] = useState('');
  const [compteChoisi, setCompteChoisi] = useState('');
  const ouvrir = (f: { existant?: Participant }) => {
    setEquipe('');
    setCompteChoisi(f.existant?.utilisateurId ?? '');
    setForme(f);
  };
  const parId = new Map(utilisateurs.map((u) => [u.id, u]));

  const admin = useContext(AuthContext)?.utilisateur?.estAdmin ?? false;
  /** Le compte du pilote ou d'un suppléant porte leurs droits : seuls le pilote, un suppléant ou un administrateur le changent. */
  const existant = forme?.existant;
  const compteVerrouille =
    !!existant &&
    (existant.id === mep.piloteId || mep.suppleantIds.includes(existant.id)) &&
    !mep.droits.estPilote &&
    !admin;
  // Comptes désactivés (personnes parties) : plus proposés, sauf celui déjà relié à la personne.
  const comptes = compteVerrouille
    ? utilisateurs.filter((u) => u.id === existant?.utilisateurId)
    : filtrerParEquipe(
        utilisateurs.filter((u) => u.actif || u.id === compteChoisi),
        equipe,
      ).concat(
        equipe && compteChoisi && !filtrerParEquipe(utilisateurs, equipe).some((u) => u.id === compteChoisi)
          ? utilisateurs.filter((u) => u.id === compteChoisi)
          : [],
      );
  const champs: ChampForm[] = [
    ...(equipes.length > 0 && !compteVerrouille
      ? [
          {
            cle: 'equipe',
            label: 'Équipe',
            type: 'select' as const,
            options: [
              { valeur: '', libelle: 'Toutes les équipes' },
              ...equipes.map((e) => ({ valeur: e.id, libelle: e.nom })),
            ],
          },
        ]
      : []),
    {
      cle: 'utilisateurId',
      label: compteVerrouille ? 'Compte (modifiable par le pilote ou un administrateur)' : 'Compte',
      type: 'select',
      large: true,
      options: [
        ...(compteVerrouille && existant?.utilisateurId ? [] : [{ valeur: '', libelle: '— aucun compte —' }]),
        ...comptes.map((u) => ({
          valeur: u.id,
          libelle: `${u.nomPersonne} (${u.login})${u.actif ? '' : ' — désactivé'}`,
        })),
      ],
    },
    { cle: 'nom', label: 'Nom', requis: true },
    { cle: 'role', label: 'Rôle', placeholder: 'ex. Opérateur base de données' },
    { cle: 'contact', label: 'Contact', placeholder: 'e-mail ou téléphone', large: true },
  ];
  /** Choisir un compte pré-remplit nom, rôle et contact depuis son profil (modifiables ensuite). */
  const prefillerDepuisCompte = (cle: string, valeur: string) => {
    if (cle === 'equipe') setEquipe(valeur);
    if (cle !== 'utilisateurId') return;
    setCompteChoisi(valeur);
    const u = utilisateurs.find((x) => x.id === valeur);
    if (!u) return;
    const p = participantDepuisCompte(u);
    return { nom: p.nom, role: p.role, contact: p.contact };
  };
  const initial = useMemo(
    () => ({
      nom: forme?.existant?.nom ?? '',
      role: forme?.existant?.role ?? '',
      contact: forme?.existant?.contact ?? '',
      utilisateurId: forme?.existant?.utilisateurId ?? '',
      equipe: '',
    }),
    [forme],
  );

  return (
    <SectionCarte
      numero={2}
      titre="Personnes"
      description={
        ajoutApresGo
          ? 'Après le Go, vous pouvez encore ajouter une personne (astreinte, renfort) : l’ajout est tracé au journal.'
          : 'Reliez chaque personne à son compte pour qu’elle puisse exécuter ses étapes.'
      }
      action={
        (edition || ajoutApresGo) && (
          <Button
            variante="primaire"
            taille="sm"
            icone={<Plus className="size-4" aria-hidden />}
            onClick={() => ouvrir({})}
          >
            Ajouter
          </Button>
        )
      }
    >
      {(comptesLus.erreur ?? equipesLues.erreur) != null && (edition || ajoutApresGo) && (
        <div className="mb-4 space-y-2">
          {comptesLus.erreur != null && (
            <ErreurChargement
              variante="ligne"
              erreur={comptesLus.erreur}
              titre="Comptes non chargés"
              complement="Une personne peut être ajoutée sans compte, puis reliée plus tard."
              onReessayer={comptesLus.recharger}
              enCours={comptesLus.enCours}
            />
          )}
          {equipesLues.erreur != null && (
            <ErreurChargement
              variante="ligne"
              erreur={equipesLues.erreur}
              titre="Équipes non chargées"
              onReessayer={equipesLues.recharger}
              enCours={equipesLues.enCours}
            />
          )}
        </div>
      )}
      {mep.participants.length === 0 ? (
        <EmptyState
          icone={Users}
          titre="Aucune personne"
          description="Ajoutez le pilote, son suppléant et les exécutants."
        />
      ) : (
        <Liste>
          {mep.participants.map((p) => (
            <Ligne
              key={p.id}
              gauche={
                <>
                  <Avatar nom={p.nom} />
                  <div className="min-w-0">
                    <p className="flex flex-wrap items-center gap-2 font-medium">
                      {p.nom}
                      {p.id === mep.piloteId && <Badge ton="run">Pilote</Badge>}
                      {mep.suppleantIds.includes(p.id) && <Badge>Suppléant</Badge>}
                    </p>
                    <p className="truncate text-xs text-fg-subtle">{p.role || 'Rôle non précisé'}</p>
                  </div>
                </>
              }
              centre={
                <div className="space-y-0.5">
                  <p className="truncate">{p.contact || '—'}</p>
                  <p className="flex items-center gap-1 text-xs text-fg-subtle">
                    <UserRound className="size-3" aria-hidden />
                    {p.utilisateurNom ? `Compte : ${p.utilisateurNom}` : 'Pas de compte relié'}
                    {p.utilisateurId && parId.get(p.utilisateurId)?.actif === false && ' (désactivé)'}
                  </p>
                </div>
              }
              actions={
                edition && (
                  <Actions nom={p.nom} onModifier={() => ouvrir({ existant: p })} onSupprimer={() => setSuppr(p)} />
                )
              }
            />
          ))}
        </Liste>
      )}
      <FormulaireDialog
        open={forme !== null}
        onOpenChange={(o) => {
          if (!o) setForme(null);
        }}
        titre={forme?.existant ? 'Modifier la personne' : 'Ajouter une personne'}
        champs={champs}
        initial={initial}
        surChangement={prefillerDepuisCompte}
        libelleValider={forme?.existant ? 'Enregistrer' : 'Ajouter'}
        onValider={(v) => {
          const s = {
            nom: v.nom.trim(),
            role: v.role.trim(),
            contact: v.contact.trim(),
            utilisateurId: vide(v.utilisateurId),
          };
          const ex = forme?.existant;
          return run(
            () => (ex ? api.modifierParticipant(mep.id, ex.id, s) : api.ajouterParticipant(mep.id, s)),
            ex ? 'Personne modifiée.' : 'Personne ajoutée.',
          );
        }}
      />
      <ConfirmDialog
        open={suppr !== null}
        onOpenChange={(o) => {
          if (!o) setSuppr(null);
        }}
        titre={`Supprimer ${suppr?.nom ?? ''} ?`}
        description="Impossible si la personne est pilote, suppléante ou responsable d’une étape."
        libelleConfirmer="Supprimer"
        variante="danger"
        onConfirmer={() =>
          suppr ? run(() => api.supprimerParticipant(mep.id, suppr.id), 'Personne supprimée.') : false
        }
      />
    </SectionCarte>
  );
}

/* ------------------------------------------------------------------ Systèmes */

export function SystemesSection() {
  const { mep, run } = useMep();
  const edition = mep.droits.peutEditerContenu;
  const catalogueLu = useReferentiel('catalogue');
  const catalogue = catalogueLu.donnees ?? [];
  const [choix, setChoix] = useState<string | null>(null);
  const [impact, setImpact] = useState('');
  const [ajout, setAjout] = useState(false);
  const [forme, setForme] = useState<{ existant?: Systeme } | null>(null);
  const [suppr, setSuppr] = useState<Systeme | null>(null);
  const dejaPresents = new Set(mep.systemes.map((s) => s.catalogueId).filter(Boolean));
  const options = catalogue
    .filter((c) => c.actif && !dejaPresents.has(c.id))
    .map((c) => ({ valeur: c.id, libelle: c.nom, detail: c.cible || c.description }));
  const initial = useMemo(
    () => ({
      nom: forme?.existant?.nom ?? '',
      cible: forme?.existant?.cible ?? '',
      impact: forme?.existant?.impact ?? '',
    }),
    [forme],
  );

  async function ajouterCatalogue() {
    if (!choix) return;
    setAjout(true);
    const ok = await run(
      () => api.ajouterSystemeDepuisCatalogue(mep.id, choix, impact.trim()),
      'Système ajouté depuis le catalogue.',
    );
    setAjout(false);
    if (ok) {
      setChoix(null);
      setImpact('');
    }
  }

  return (
    <SectionCarte
      numero={3}
      titre="Systèmes"
      description="Systèmes touchés, cible, impact et environnements touchés par la MEP sur chacun."
      action={
        edition && (
          <Button taille="sm" icone={<Plus className="size-4" aria-hidden />} onClick={() => setForme({})}>
            Ajout libre
          </Button>
        )
      }
    >
      {edition && (
        <div className="mb-5 rounded-lg border border-dashed border-line-strong bg-surface-2/50 p-4">
          <p className="mb-3 flex items-center gap-2 text-sm font-semibold">
            <Library className="size-4 text-accent-text" aria-hidden /> Ajouter depuis le catalogue
          </p>
          <div className="grid gap-3 md:grid-cols-[1.2fr_1.2fr_auto] md:items-end">
            <Field label="Système du catalogue">
              {(id) => (
                <Combobox
                  id={id}
                  options={options}
                  valeur={choix}
                  onChange={setChoix}
                  placeholder="Choisir un système…"
                  placeholderRecherche="Rechercher un système…"
                  vide="Aucun système disponible dans le catalogue."
                  remplacement={
                    catalogueLu.erreur != null ? (
                      <ErreurChargement
                        variante="ligne"
                        erreur={catalogueLu.erreur}
                        titre="Catalogue des systèmes non chargé"
                        onReessayer={catalogueLu.recharger}
                        enCours={catalogueLu.enCours}
                      />
                    ) : undefined
                  }
                />
              )}
            </Field>
            <Field label="Impact attendu">
              {(id) => (
                <Input
                  id={id}
                  value={impact}
                  onChange={(e) => setImpact(e.target.value)}
                  placeholder="ex. Indisponible 10 minutes"
                />
              )}
            </Field>
            <Button
              variante="primaire"
              icone={<Plus className="size-4" aria-hidden />}
              disabled={!choix}
              chargement={ajout}
              onClick={() => void ajouterCatalogue()}
            >
              Ajouter
            </Button>
          </div>
        </div>
      )}
      {mep.systemes.length === 0 ? (
        <EmptyState
          icone={Layers}
          titre="Aucun système"
          description="Choisissez un système du catalogue ou ajoutez-en un librement."
        />
      ) : (
        <Liste>
          {mep.systemes.map((s) => (
            <Ligne
              key={s.id}
              gauche={
                <div className="min-w-0">
                  <p className="flex flex-wrap items-center gap-2 font-medium">
                    {/* Menu du système : historique de ses MEP, MEP sur ce système (#59). */}
                    <NomSystemeMep s={s} />
                    {s.catalogueId && (
                      <Badge ton="run">
                        <Library className="size-3" aria-hidden />
                        Catalogue
                      </Badge>
                    )}
                  </p>
                  <p className="truncate font-mono text-xs text-fg-subtle">{s.cible || 'Cible non précisée'}</p>
                </div>
              }
              centre={<p>{s.impact || <span className="text-fg-subtle">Impact non précisé</span>}</p>}
              actions={
                edition && (
                  <Actions nom={s.nom} onModifier={() => setForme({ existant: s })} onSupprimer={() => setSuppr(s)} />
                )
              }
              bas={
                <EnvironnementsSysteme
                  systeme={s}
                  fiche={s.catalogueId ? (catalogue.find((c) => c.id === s.catalogueId) ?? null) : null}
                  ficheNonChargee={catalogueLu.erreur != null}
                  edition={edition}
                />
              }
            />
          ))}
        </Liste>
      )}
      <FormulaireDialog
        open={forme !== null}
        onOpenChange={(o) => {
          if (!o) setForme(null);
        }}
        titre={forme?.existant ? 'Modifier le système' : 'Ajouter un système'}
        champs={[
          { cle: 'nom', label: 'Nom', requis: true },
          { cle: 'cible', label: 'Cible exacte', placeholder: 'ex. srv-app-01 / base portail' },
          { cle: 'impact', label: 'Impact', type: 'zone' },
        ]}
        initial={initial}
        libelleValider={forme?.existant ? 'Enregistrer' : 'Ajouter'}
        onValider={(v) => {
          const s = { nom: v.nom.trim(), cible: v.cible.trim(), impact: v.impact.trim() };
          const ex = forme?.existant;
          return run(
            () => (ex ? api.modifierSysteme(mep.id, ex.id, s) : api.ajouterSysteme(mep.id, s)),
            ex ? 'Système modifié.' : 'Système ajouté.',
          );
        }}
      />
      <ConfirmDialog
        open={suppr !== null}
        onOpenChange={(o) => {
          if (!o) setSuppr(null);
        }}
        titre={`Retirer ${suppr?.nom ?? ''} ?`}
        description="Impossible si une étape utilise ce système. Ses objets impactés sont retirés de la MEP."
        libelleConfirmer="Retirer"
        variante="danger"
        onConfirmer={() => (suppr ? run(() => api.supprimerSysteme(mep.id, suppr.id), 'Système retiré.') : false)}
      />
    </SectionCarte>
  );
}

/**
 * Environnements touchés par la MEP sur un système : ceux de sa fiche au catalogue, ou, pour un système libre, des
 * suggestions et une saisie libre. Un environnement déjà choisi puis retiré de la fiche reste affiché pour être décoché.
 */
function EnvironnementsSysteme({
  systeme,
  fiche,
  ficheNonChargee,
  edition,
}: {
  systeme: Systeme;
  fiche: SystemeCatalogue | null;
  /** Catalogue en erreur : seuls les environnements déjà choisis sont montrés, sans dire que la fiche est vide. */
  ficheNonChargee: boolean;
  edition: boolean;
}) {
  const { mep, run } = useMep();
  const [autre, setAutre] = useState('');
  const [envoi, setEnvoi] = useState(false);
  const libre = !systeme.catalogueId;
  const proposes: { nom: string; production: boolean; cible?: string }[] = libre
    ? ENVIRONNEMENTS.map((nom) => ({ nom, production: nomDeProduction(nom) }))
    : (fiche?.environnements ?? []);
  const options: { nom: string; production: boolean; cible?: string }[] = [
    ...proposes,
    ...systeme.environnements.filter((e) => !proposes.some((p) => memeEnvironnement(p.nom, e.nom))),
  ];
  const choisi = (nom: string) => systeme.environnements.some((e) => memeEnvironnement(e.nom, nom));

  async function definir(noms: string[]) {
    setEnvoi(true);
    // Ordre stable : celui des options (fiche du catalogue ou suggestions), les ajouts libres à la fin.
    const ordre = (n: string) => {
      const i = options.findIndex((o) => memeEnvironnement(o.nom, n));
      return i < 0 ? options.length : i;
    };
    await run(
      () =>
        api.environnementsSysteme(
          mep.id,
          systeme.id,
          [...noms].sort((a, b) => ordre(a) - ordre(b)),
        ),
      `Environnements de ${systeme.nom} enregistrés.`,
    );
    setEnvoi(false);
  }
  const basculer = (nom: string) =>
    void definir(
      choisi(nom)
        ? systeme.environnements.filter((e) => !memeEnvironnement(e.nom, nom)).map((e) => e.nom)
        : [...systeme.environnements.map((e) => e.nom), nom],
    );
  function ajouterAutre(e: FormEvent) {
    e.preventDefault();
    const nom = autre.trim();
    if (!nom || choisi(nom)) return;
    setAutre('');
    void definir([...systeme.environnements.map((x) => x.nom), nom]);
  }

  return (
    <div
      className="flex flex-wrap items-center gap-1.5 text-xs"
      role="group"
      aria-label={`Environnements de ${systeme.nom}`}
    >
      <span className="mr-1 inline-flex items-center gap-1 text-fg-subtle">
        <Server className="size-3.5" aria-hidden />
        Environnements
      </span>
      {options.length === 0 && !ficheNonChargee && (
        <span className="text-fg-subtle">
          Aucun environnement dans la fiche du catalogue.{' '}
          {systeme.catalogueId && (
            <Link to={`/parametres/systemes/${systeme.catalogueId}`} className="text-accent-text underline">
              Compléter la fiche {systeme.nom}
            </Link>
          )}
        </span>
      )}
      {options.map((o) => {
        const actif = choisi(o.nom);
        return (
          <button
            key={o.nom}
            type="button"
            disabled={!edition || envoi}
            aria-pressed={actif}
            title={o.cible ? `${o.nom} : ${o.cible}` : o.nom}
            onClick={() => basculer(o.nom)}
            className={cn(
              'rounded-full border px-2.5 py-0.5 font-medium transition-colors duration-150 disabled:cursor-default',
              actif
                ? o.production
                  ? 'border-warn-bd bg-warn-bg text-warn-fg'
                  : 'border-accent bg-accent-soft text-accent-text'
                : 'border-line bg-surface text-fg-subtle enabled:hover:border-line-strong enabled:hover:text-fg',
              !edition && !actif && 'hidden',
            )}
          >
            {o.nom}
          </button>
        );
      })}
      {!edition && systeme.environnements.length === 0 && <span className="text-warn-text">aucun choisi</span>}
      {edition && libre && (
        <form onSubmit={ajouterAutre} className="inline-flex">
          <Input
            value={autre}
            onChange={(e) => setAutre(e.target.value)}
            placeholder="Autre…"
            aria-label={`Autre environnement pour ${systeme.nom}`}
            className="h-6 w-28 px-2 text-xs"
          />
        </form>
      )}
    </div>
  );
}
