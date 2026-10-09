import { Library, Package, Pencil, Plus, Trash2, UserRound, Users, Layers } from 'lucide-react';
import { useEffect, useMemo, useState, type ReactNode } from 'react';
import { api } from '../../api';
import { useMep } from '../../mepContext';
import type { Livrable, Participant, Systeme, SystemeCatalogue, Utilisateur } from '../../types';
import { Avatar } from '../../ui/Avatar';
import { Badge } from '../../ui/StatusPill';
import { BoutonIcone, Button } from '../../ui/Button';
import { Combobox } from '../../ui/Combobox';
import { ConfirmDialog } from '../../ui/Dialog';
import { EmptyState } from '../../ui/EmptyState';
import { Field, Input } from '../../ui/Input';
import { Tooltip } from '../../ui/Tooltip';
import { FormulaireDialog, SectionCarte, vide, type ChampForm } from './Formulaires';

function Ligne({ gauche, centre, actions }: { gauche: ReactNode; centre?: ReactNode; actions?: ReactNode }) {
  return (
    <li className="flex flex-wrap items-center gap-x-4 gap-y-2 px-4 py-3">
      <div className="flex min-w-0 flex-1 basis-56 items-center gap-3">{gauche}</div>
      {centre && <div className="min-w-0 flex-1 basis-56 text-sm text-fg-muted">{centre}</div>}
      <div className="flex shrink-0 items-center gap-1">{actions}</div>
    </li>
  );
}

function Actions({ onModifier, onSupprimer, nom }: { onModifier: () => void; onSupprimer: () => void; nom: string }) {
  return (
    <>
      <Tooltip content="Modifier"><BoutonIcone label={`Modifier ${nom}`} taille="sm" onClick={onModifier}><Pencil className="size-4" aria-hidden /></BoutonIcone></Tooltip>
      <Tooltip content="Supprimer"><BoutonIcone label={`Supprimer ${nom}`} taille="sm" onClick={onSupprimer}><Trash2 className="size-4" aria-hidden /></BoutonIcone></Tooltip>
    </>
  );
}

const Liste = ({ children }: { children: ReactNode }) => <ul className="divide-y divide-line overflow-hidden rounded-lg border border-line">{children}</ul>;

/* ------------------------------------------------------------------ Personnes */

export function PersonnesSection() {
  const { mep, run } = useMep();
  const edition = mep.droits.peutModifierPlan;
  const [utilisateurs, setUtilisateurs] = useState<Utilisateur[]>([]);
  const [forme, setForme] = useState<{ existant?: Participant } | null>(null);
  const [suppr, setSuppr] = useState<Participant | null>(null);
  useEffect(() => { api.utilisateurs().then(setUtilisateurs).catch(() => setUtilisateurs([])); }, []);

  const champs: ChampForm[] = [
    { cle: 'nom', label: 'Nom', requis: true },
    { cle: 'role', label: 'Rôle', placeholder: 'ex. Opérateur base de données' },
    { cle: 'contact', label: 'Contact', placeholder: 'e-mail ou téléphone' },
    { cle: 'utilisateurId', label: 'Compte relié', type: 'select', options: [{ valeur: '', libelle: '— aucun compte —' }, ...utilisateurs.map((u) => ({ valeur: u.id, libelle: `${u.nomAffiche} (${u.login})` }))] },
  ];
  const initial = useMemo(() => ({
    nom: forme?.existant?.nom ?? '', role: forme?.existant?.role ?? '', contact: forme?.existant?.contact ?? '', utilisateurId: forme?.existant?.utilisateurId ?? '',
  }), [forme]);

  return (
    <SectionCarte
      numero={2}
      titre="Personnes"
      description="Qui participe à l’intervention. Reliez chaque personne à son compte pour qu’elle puisse exécuter ses étapes."
      action={edition && <Button variante="primaire" taille="sm" icone={<Plus className="size-4" aria-hidden />} onClick={() => setForme({})}>Ajouter</Button>}
    >
      {mep.participants.length === 0 ? (
        <EmptyState icone={Users} titre="Aucune personne" description="Ajoutez le pilote, son suppléant et les opérateurs." />
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
                      {p.id === mep.suppleantId && <Badge>Suppléant</Badge>}
                    </p>
                    <p className="truncate text-xs text-fg-subtle">{p.role || 'Rôle non précisé'}</p>
                  </div>
                </>
              }
              centre={
                <div className="space-y-0.5">
                  <p className="truncate">{p.contact || '—'}</p>
                  <p className="flex items-center gap-1 text-xs text-fg-subtle"><UserRound className="size-3" aria-hidden />{p.utilisateurNom ? `Compte : ${p.utilisateurNom}` : 'Pas de compte relié'}</p>
                </div>
              }
              actions={edition && <Actions nom={p.nom} onModifier={() => setForme({ existant: p })} onSupprimer={() => setSuppr(p)} />}
            />
          ))}
        </Liste>
      )}
      <FormulaireDialog
        open={forme !== null}
        onOpenChange={(o) => { if (!o) setForme(null); }}
        titre={forme?.existant ? 'Modifier la personne' : 'Ajouter une personne'}
        champs={champs}
        initial={initial}
        libelleValider={forme?.existant ? 'Enregistrer' : 'Ajouter'}
        onValider={(v) => {
          const s = { nom: v.nom.trim(), role: v.role.trim(), contact: v.contact.trim(), utilisateurId: vide(v.utilisateurId) };
          const ex = forme?.existant;
          return run(() => (ex ? api.modifierParticipant(mep.id, ex.id, s) : api.ajouterParticipant(mep.id, s)), ex ? 'Personne modifiée.' : 'Personne ajoutée.');
        }}
      />
      <ConfirmDialog
        open={suppr !== null}
        onOpenChange={(o) => { if (!o) setSuppr(null); }}
        titre={`Supprimer ${suppr?.nom ?? ''} ?`}
        description="Impossible si la personne est pilote, suppléante ou responsable d’une étape."
        libelleConfirmer="Supprimer"
        variante="danger"
        onConfirmer={() => suppr ? run(() => api.supprimerParticipant(mep.id, suppr.id), 'Personne supprimée.') : false}
      />
    </SectionCarte>
  );
}

/* ------------------------------------------------------------------ Systèmes */

export function SystemesSection() {
  const { mep, run } = useMep();
  const edition = mep.droits.peutModifierPlan;
  const [catalogue, setCatalogue] = useState<SystemeCatalogue[]>([]);
  const [choix, setChoix] = useState<string | null>(null);
  const [impact, setImpact] = useState('');
  const [ajout, setAjout] = useState(false);
  const [forme, setForme] = useState<{ existant?: Systeme } | null>(null);
  const [suppr, setSuppr] = useState<Systeme | null>(null);
  useEffect(() => { api.catalogueSystemes().then(setCatalogue).catch(() => setCatalogue([])); }, []);

  const dejaPresents = new Set(mep.systemes.map((s) => s.catalogueId).filter(Boolean));
  const options = catalogue.filter((c) => c.actif && !dejaPresents.has(c.id)).map((c) => ({ valeur: c.id, libelle: c.nom, detail: c.cible || c.description }));
  const initial = useMemo(() => ({ nom: forme?.existant?.nom ?? '', cible: forme?.existant?.cible ?? '', impact: forme?.existant?.impact ?? '' }), [forme]);

  async function ajouterCatalogue() {
    if (!choix) return;
    setAjout(true);
    const ok = await run(() => api.ajouterSystemeDepuisCatalogue(mep.id, choix, impact.trim()), 'Système ajouté depuis le catalogue.');
    setAjout(false);
    if (ok) { setChoix(null); setImpact(''); }
  }

  return (
    <SectionCarte
      numero={3}
      titre="Systèmes"
      description="Les systèmes touchés par l’intervention, avec la cible exacte et l’impact attendu."
      action={edition && <Button taille="sm" icone={<Plus className="size-4" aria-hidden />} onClick={() => setForme({})}>Ajout libre</Button>}
    >
      {edition && (
        <div className="mb-5 rounded-lg border border-dashed border-line-strong bg-surface-2/50 p-4">
          <p className="mb-3 flex items-center gap-2 text-sm font-semibold"><Library className="size-4 text-accent-text" aria-hidden /> Ajouter depuis le catalogue</p>
          <div className="grid gap-3 md:grid-cols-[1.2fr_1.2fr_auto] md:items-end">
            <Field label="Système du catalogue">
              {(id) => <Combobox id={id} options={options} valeur={choix} onChange={setChoix} placeholder="Choisir un système…" placeholderRecherche="Rechercher un système…" vide="Aucun système disponible dans le catalogue." />}
            </Field>
            <Field label="Impact attendu">
              {(id) => <Input id={id} value={impact} onChange={(e) => setImpact(e.target.value)} placeholder="ex. Indisponible 10 minutes" />}
            </Field>
            <Button variante="primaire" icone={<Plus className="size-4" aria-hidden />} disabled={!choix} chargement={ajout} onClick={() => void ajouterCatalogue()}>Ajouter</Button>
          </div>
        </div>
      )}
      {mep.systemes.length === 0 ? (
        <EmptyState icone={Layers} titre="Aucun système" description="Choisissez un système du catalogue ou ajoutez-en un librement." />
      ) : (
        <Liste>
          {mep.systemes.map((s) => (
            <Ligne
              key={s.id}
              gauche={
                <div className="min-w-0">
                  <p className="flex flex-wrap items-center gap-2 font-medium">{s.nom}{s.catalogueId && <Badge ton="run"><Library className="size-3" aria-hidden />Catalogue</Badge>}</p>
                  <p className="truncate font-mono text-xs text-fg-subtle">{s.cible || 'Cible non précisée'}</p>
                </div>
              }
              centre={<p>{s.impact || <span className="text-fg-subtle">Impact non précisé</span>}</p>}
              actions={edition && <Actions nom={s.nom} onModifier={() => setForme({ existant: s })} onSupprimer={() => setSuppr(s)} />}
            />
          ))}
        </Liste>
      )}
      <FormulaireDialog
        open={forme !== null}
        onOpenChange={(o) => { if (!o) setForme(null); }}
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
          return run(() => (ex ? api.modifierSysteme(mep.id, ex.id, s) : api.ajouterSysteme(mep.id, s)), ex ? 'Système modifié.' : 'Système ajouté.');
        }}
      />
      <ConfirmDialog
        open={suppr !== null}
        onOpenChange={(o) => { if (!o) setSuppr(null); }}
        titre={`Retirer ${suppr?.nom ?? ''} ?`}
        description="Impossible si une étape utilise ce système. Les livrables rattachés seront détachés."
        libelleConfirmer="Retirer"
        variante="danger"
        onConfirmer={() => suppr ? run(() => api.supprimerSysteme(mep.id, suppr.id), 'Système retiré.') : false}
      />
    </SectionCarte>
  );
}

/* ------------------------------------------------------------------ Livrables */

export function LivrablesSection() {
  const { mep, run } = useMep();
  const edition = mep.droits.peutModifierPlan;
  const [forme, setForme] = useState<{ existant?: Livrable } | null>(null);
  const [suppr, setSuppr] = useState<Livrable | null>(null);
  const initial = useMemo(() => ({
    objet: forme?.existant?.objet ?? '', version: forme?.existant?.version ?? '', emplacement: forme?.existant?.emplacement ?? '', systemeId: forme?.existant?.systemeId ?? '',
  }), [forme]);

  return (
    <SectionCarte
      numero={4}
      titre="Livrables"
      description="Les paquets, scripts et configurations à déployer, avec leur version et leur emplacement."
      action={edition && <Button variante="primaire" taille="sm" icone={<Plus className="size-4" aria-hidden />} onClick={() => setForme({})}>Ajouter</Button>}
    >
      {mep.livrables.length === 0 ? (
        <EmptyState icone={Package} titre="Aucun livrable" description="Listez ce qui sera déployé pour que tout le monde parle du même paquet." />
      ) : (
        <Liste>
          {mep.livrables.map((l) => (
            <Ligne
              key={l.id}
              gauche={
                <div className="min-w-0">
                  <p className="flex flex-wrap items-center gap-2 font-medium">{l.objet}{l.version && <Badge className="font-mono">{l.version}</Badge>}</p>
                  <p className="truncate text-xs text-fg-subtle">{mep.systemes.find((s) => s.id === l.systemeId)?.nom ?? 'Aucun système rattaché'}</p>
                </div>
              }
              centre={<p className="truncate font-mono text-[13px]">{l.emplacement || '—'}</p>}
              actions={edition && <Actions nom={l.objet} onModifier={() => setForme({ existant: l })} onSupprimer={() => setSuppr(l)} />}
            />
          ))}
        </Liste>
      )}
      <FormulaireDialog
        open={forme !== null}
        onOpenChange={(o) => { if (!o) setForme(null); }}
        titre={forme?.existant ? 'Modifier le livrable' : 'Ajouter un livrable'}
        champs={[
          { cle: 'objet', label: 'Objet', requis: true, placeholder: 'ex. Archive applicative' },
          { cle: 'version', label: 'Version' },
          { cle: 'emplacement', label: 'Emplacement', large: true, placeholder: 'chemin, dépôt ou URL interne' },
          { cle: 'systemeId', label: 'Système concerné', type: 'select', large: true, options: [{ valeur: '', libelle: '— aucun —' }, ...mep.systemes.map((s) => ({ valeur: s.id, libelle: s.nom }))] },
        ]}
        initial={initial}
        libelleValider={forme?.existant ? 'Enregistrer' : 'Ajouter'}
        onValider={(v) => {
          const s = { objet: v.objet.trim(), version: v.version.trim(), emplacement: v.emplacement.trim(), systemeId: vide(v.systemeId) };
          const ex = forme?.existant;
          return run(() => (ex ? api.modifierLivrable(mep.id, ex.id, s) : api.ajouterLivrable(mep.id, s)), ex ? 'Livrable modifié.' : 'Livrable ajouté.');
        }}
      />
      <ConfirmDialog
        open={suppr !== null}
        onOpenChange={(o) => { if (!o) setSuppr(null); }}
        titre={`Supprimer ${suppr?.objet ?? ''} ?`}
        libelleConfirmer="Supprimer"
        variante="danger"
        onConfirmer={() => suppr ? run(() => api.supprimerLivrable(mep.id, suppr.id), 'Livrable supprimé.') : false}
      />
    </SectionCarte>
  );
}
