import { ArrowLeft, Braces, OctagonAlert, RotateCcw, Save, Search, TriangleAlert } from 'lucide-react';
import { useEffect, useMemo, useRef, useState } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { api } from '../api';
import { useAuth } from '../auth';
import { formatDate } from '../format';
import { useAsync, useReportDirty } from '../hooks';
import { normaliser, termes } from '../recherche';
import { ApercuTexte } from '../textes/ApercuTexte';
import { generer, inconnues, libellesDepuis, LIBELLES_USAGE, manquantes, valeursDepuis } from '../textes/modelesTexte';
import { listeManquantes, type FormatTexte } from '../textes/TextesACopier';
import type { UsageModeleTexte, VariableTexte } from '../types';
import { Alerte } from '../ui/Alerte';
import { Button } from '../ui/Button';
import { Card } from '../ui/Card';
import { ConfirmDialog } from '../ui/Dialog';
import { Field, Input, Select, Textarea } from '../ui/Input';
import { Segmented } from '../ui/Segmented';
import { Badge } from '../ui/StatusPill';
import { useToast } from '../ui/Toast';
import { Tooltip } from '../ui/Tooltip';

interface Brouillon {
  nom: string;
  usage: UsageModeleTexte;
  equipeId: string;
  objet: string;
  corps: string;
}

const USAGES: UsageModeleTexte[] = ['Annonce', 'Rappel', 'DemandeMep', 'CompteRendu', 'Autre'];

const VIDE: Brouillon = { nom: '', usage: 'Autre', equipeId: '', objet: '', corps: '' };

/** Variables groupées (ordre du catalogue), filtrées par une recherche multi-termes sur le libellé, la clé ou la description. */
export function grouperVariables(
  variables: VariableTexte[],
  recherche: string,
): { groupe: string; variables: VariableTexte[] }[] {
  const ts = termes(recherche);
  const groupes: { groupe: string; variables: VariableTexte[] }[] = [];
  for (const v of variables) {
    const tout = normaliser(`${v.groupe} ${v.libelle} ${v.cle} ${v.cle.replace(/_/g, ' ')} ${v.description}`);
    if (!ts.every((t) => tout.includes(t))) continue;
    const g = groupes.find((x) => x.groupe === v.groupe);
    if (g) g.variables.push(v);
    else groupes.push({ groupe: v.groupe, variables: [v] });
  }
  return groupes;
}

/**
 * Éditeur d'un modèle de texte (`/parametres/textes/:id`, `nouveau` pour un nouveau modèle, `?depuis=` pour dupliquer) :
 * formulaire à gauche avec les variables groupées et leur recherche, aperçu à droite calculé sur une MEP choisie.
 */
export default function ModeleTexteEditeurPage() {
  const { id = 'nouveau' } = useParams();
  const [params] = useSearchParams();
  const nouveau = id === 'nouveau';
  const depuis = params.get('depuis');
  const navigate = useNavigate();
  const toast = useToast();
  const { utilisateur } = useAuth();
  const admin = utilisateur?.estAdmin ?? false;

  const existant = useAsync(() => (nouveau ? Promise.resolve(null) : api.modeleTexte(id)), [id]);
  const source = useAsync(() => (nouveau && depuis ? api.modeleTexte(depuis) : Promise.resolve(null)), [depuis]);
  const equipes = useAsync(() => api.equipes(), []);
  const catalogue = useAsync(() => api.variablesModelesTexte(), []);
  const meps = useAsync(() => api.meps(false), []);

  const [b, setB] = useState<Brouillon>(VIDE);
  const [initial, setInitial] = useState<Brouillon>(VIDE);
  const [pret, setPret] = useState(false);
  const [recherche, setRecherche] = useState('');
  const [format, setFormat] = useState<FormatTexte>('forme');
  const [mepId, setMepId] = useState('');
  const [erreur, setErreur] = useState('');
  const [envoi, setEnvoi] = useState(false);
  const [retablir, setRetablir] = useState(false);
  const zoneObjet = useRef<HTMLInputElement>(null);
  const zoneCorps = useRef<HTMLTextAreaElement>(null);
  const dernierChamp = useRef<'objet' | 'corps'>('corps');

  // Brouillon initial : le modèle, sa copie (« Dupliquer ») ou un modèle vide de l'équipe demandée.
  useEffect(() => {
    if (pret) return;
    if (!nouveau && existant.donnees) {
      const m = existant.donnees;
      const v = { nom: m.nom, usage: m.usage, equipeId: m.equipeId, objet: m.objet, corps: m.corps };
      setB(v);
      setInitial(v);
      setPret(true);
    } else if (nouveau && (!depuis || source.donnees) && equipes.donnees) {
      const s = source.donnees;
      const equipeId = params.get('equipe') ?? s?.equipeId ?? equipes.donnees[0]?.id ?? '';
      const v: Brouillon = s
        ? { nom: `${s.nom} (copie)`, usage: s.usage, equipeId, objet: s.objet, corps: s.corps }
        : { ...VIDE, equipeId };
      setB(v);
      setInitial(v);
      setPret(true);
    }
  }, [pret, nouveau, depuis, existant.donnees, source.donnees, equipes.donnees, params]);

  useEffect(() => {
    if (!mepId && meps.donnees && meps.donnees.length > 0) setMepId(meps.donnees[0].id);
  }, [meps.donnees, mepId]);

  const modele = existant.donnees;
  const lecture = !nouveau && modele !== null && !modele.peutModifier;
  const dirty = pret && JSON.stringify(b) !== JSON.stringify(initial);
  useReportDirty(dirty && !envoi, 'Modèle de texte');

  const valeurs = useAsync(
    () => (mepId && b.equipeId ? api.variablesTexte(mepId, b.equipeId) : Promise.resolve(null)),
    [mepId, b.equipeId],
  );
  const libelles = useMemo(() => libellesDepuis(catalogue.donnees ?? []), [catalogue.donnees]);
  const vals = useMemo(() => valeursDepuis(valeurs.donnees?.valeurs ?? []), [valeurs.donnees]);
  const groupes = useMemo(() => grouperVariables(catalogue.donnees ?? [], recherche), [catalogue.donnees, recherche]);
  const texte = `${b.objet}\n${b.corps}`;
  const manque = valeurs.donnees ? manquantes(texte, vals) : [];
  const inconnu = catalogue.donnees ? inconnues(texte, libelles) : [];

  function inserer(cle: string) {
    const jeton = `{{${cle}}}`;
    const champ = dernierChamp.current;
    const el = champ === 'objet' ? zoneObjet.current : zoneCorps.current;
    const actuel = b[champ];
    const debut = el?.selectionStart ?? actuel.length;
    const fin = el?.selectionEnd ?? actuel.length;
    setB({ ...b, [champ]: actuel.slice(0, debut) + jeton + actuel.slice(fin) });
    requestAnimationFrame(() => {
      el?.focus();
      el?.setSelectionRange(debut + jeton.length, debut + jeton.length);
    });
  }

  async function enregistrer() {
    setErreur('');
    if (!b.nom.trim()) {
      setErreur('Le nom du modèle est obligatoire.');
      return;
    }
    setEnvoi(true);
    try {
      const saisie = { nom: b.nom.trim(), usage: b.usage, objet: b.objet, corps: b.corps };
      if (modele) await api.modifierModeleTexte(modele.id, { ...saisie, version: modele.version });
      else await api.creerModeleTexte({ ...saisie, equipeId: b.equipeId });
      toast.succes(modele ? 'Modèle enregistré.' : 'Modèle créé.');
      setInitial(b);
      void navigate(`/parametres/textes?equipe=${b.equipeId}`);
    } catch (e) {
      setErreur(e instanceof Error ? e.message : 'Enregistrement impossible.');
    } finally {
      setEnvoi(false);
    }
  }

  const retour = `/parametres/textes${b.equipeId ? `?equipe=${b.equipeId}` : ''}`;
  const titre = nouveau
    ? depuis
      ? 'Dupliquer un modèle'
      : 'Nouveau modèle de texte'
    : lecture
      ? `« ${modele?.nom ?? ''} »`
      : `Modifier « ${modele?.nom ?? ''} »`;
  const chargementErreur = existant.erreur || source.erreur || equipes.erreur || catalogue.erreur;
  // Équipes où l'on peut créer un modèle : les siennes (un administrateur : toutes).
  const equipesPossibles = (equipes.donnees ?? []).filter((e) => admin || utilisateur?.equipeIds.includes(e.id));

  return (
    <main className="mx-auto w-full max-w-[1400px] px-6 py-8">
      <nav aria-label="Fil d’Ariane" className="mb-3">
        <Link
          to={retour}
          className="inline-flex items-center gap-1.5 text-sm font-semibold text-accent-text hover:underline"
        >
          <ArrowLeft className="size-4" aria-hidden />
          Modèles de texte
        </Link>
      </nav>
      <h1 className="flex flex-wrap items-center gap-3 text-2xl font-semibold tracking-tight">
        {titre}
        {modele?.fourni && <Badge ton="done">Fourni par défaut</Badge>}
      </h1>
      {chargementErreur && (
        <Alerte ton="danger" icone={OctagonAlert} role="alert" className="mt-6">
          {chargementErreur}
        </Alerte>
      )}

      {pret && (
        <div className="mt-6 grid items-start gap-5 lg:grid-cols-[minmax(0,1.1fr)_minmax(0,0.9fr)]">
          <Card className="grid gap-5 p-5">
            {lecture && (
              <Alerte ton="wait" icone={TriangleAlert}>
                Lecture seule : seuls les membres de l’équipe ou un administrateur modifient ses modèles.
              </Alerte>
            )}
            <div className="grid gap-4 md:grid-cols-2">
              <Field label="Nom du modèle *" className="md:col-span-2">
                {(i) => (
                  <Input
                    id={i}
                    value={b.nom}
                    disabled={lecture}
                    onChange={(e) => setB({ ...b, nom: e.target.value })}
                  />
                )}
              </Field>
              <Field label="Usage">
                {(i) => (
                  <Select
                    id={i}
                    value={b.usage}
                    disabled={lecture}
                    onChange={(e) => {
                      const usage = USAGES.find((u) => u === e.target.value);
                      if (usage) setB({ ...b, usage });
                    }}
                  >
                    {Object.entries(LIBELLES_USAGE).map(([v, l]) => (
                      <option key={v} value={v}>
                        {l}
                      </option>
                    ))}
                  </Select>
                )}
              </Field>
              <Field label="Équipe" hint={nouveau ? undefined : 'L’équipe ne change pas : dupliquez le modèle.'}>
                {(i) => (
                  <Select
                    id={i}
                    value={b.equipeId}
                    disabled={!nouveau}
                    onChange={(e) => setB({ ...b, equipeId: e.target.value })}
                  >
                    {(nouveau ? equipesPossibles : (equipes.donnees ?? [])).map((e) => (
                      <option key={e.id} value={e.id}>
                        {e.nom}
                      </option>
                    ))}
                  </Select>
                )}
              </Field>
            </div>
            <Field label="Objet du courriel">
              {(i) => (
                <Input
                  id={i}
                  ref={zoneObjet}
                  className="font-mono text-[13px]"
                  value={b.objet}
                  disabled={lecture}
                  onFocus={() => (dernierChamp.current = 'objet')}
                  onChange={(e) => setB({ ...b, objet: e.target.value })}
                />
              )}
            </Field>
            <Field
              label="Texte"
              hint={
                <>
                  <code className="font-mono">**gras**</code> pour mettre en gras, une ligne qui commence par{' '}
                  <code className="font-mono">- </code> pour une puce. Une variable de liste seule sur sa ligne
                  (systèmes, livrables, bénéfices…) devient une liste à puces.
                </>
              }
            >
              {(i) => (
                <Textarea
                  id={i}
                  ref={zoneCorps}
                  className="font-mono text-[13px]"
                  rows={18}
                  value={b.corps}
                  disabled={lecture}
                  onFocus={() => (dernierChamp.current = 'corps')}
                  onChange={(e) => setB({ ...b, corps: e.target.value })}
                />
              )}
            </Field>

            {!lecture && (
              <section aria-label="Insérer une variable" className="rounded-lg border border-line bg-surface-2/50 p-4">
                <div className="flex flex-wrap items-center justify-between gap-3">
                  <p className="flex items-center gap-2 text-[13px] font-medium">
                    <Braces className="size-4 text-accent" aria-hidden />
                    Insérer une variable
                    <span className="font-normal text-fg-subtle">
                      : cliquez pour l’ajouter à l’emplacement du curseur.
                    </span>
                  </p>
                  <div className="relative w-full sm:w-64">
                    <Search
                      className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-fg-subtle"
                      aria-hidden
                    />
                    <Input
                      aria-label="Rechercher une variable"
                      placeholder="Rechercher une variable…"
                      className="h-8 pl-9"
                      value={recherche}
                      onChange={(e) => setRecherche(e.target.value)}
                    />
                  </div>
                </div>
                <div className="mt-3 grid gap-y-2.5">
                  {groupes.map((g) => (
                    <div key={g.groupe} className="grid gap-2 sm:grid-cols-[9rem_minmax(0,1fr)]">
                      <p className="pt-1 text-[11px] font-semibold tracking-wider text-fg-subtle uppercase">
                        {g.groupe}
                      </p>
                      <div className="flex flex-wrap gap-1.5">
                        {g.variables.map((v) => (
                          <Tooltip key={v.cle} content={`{{${v.cle}}} : ${v.description}`}>
                            <button
                              type="button"
                              onMouseDown={(e) => e.preventDefault()}
                              onClick={() => inserer(v.cle)}
                              className="rounded-md border border-line bg-surface px-2 py-0.5 text-[12px] text-fg-muted transition-colors duration-150 hover:border-accent hover:text-accent-text"
                            >
                              {v.libelle}
                            </button>
                          </Tooltip>
                        ))}
                      </div>
                    </div>
                  ))}
                  {catalogue.donnees && groupes.length === 0 && (
                    <p className="text-sm text-fg-subtle">Aucune variable ne correspond à « {recherche} ».</p>
                  )}
                </div>
              </section>
            )}

            {erreur && (
              <Alerte ton="danger" icone={OctagonAlert} role="alert">
                {erreur}
              </Alerte>
            )}
            {!lecture && (
              <div className="flex flex-wrap items-center gap-2 border-t border-line pt-4">
                <Button
                  variante="primaire"
                  icone={<Save className="size-4" aria-hidden />}
                  chargement={envoi}
                  disabled={!nouveau && !dirty}
                  onClick={() => void enregistrer()}
                >
                  Enregistrer
                </Button>
                <Button variante="fantome" onClick={() => void navigate(retour)}>
                  Annuler
                </Button>
                {modele?.fourni && modele.modifie && (
                  <Button
                    variante="fantome"
                    className="ml-auto"
                    icone={<RotateCcw className="size-4" aria-hidden />}
                    onClick={() => setRetablir(true)}
                  >
                    Rétablir le texte fourni
                  </Button>
                )}
              </div>
            )}
          </Card>

          <Card
            role="region"
            aria-label="Aperçu"
            className="p-5 lg:sticky lg:top-[calc(var(--bandeau-demo,0px)+1.5rem)]"
          >
            <div className="flex flex-wrap items-center justify-between gap-3">
              <h2 className="text-[15px] font-semibold tracking-tight">Aperçu</h2>
              <Segmented<FormatTexte>
                label="Format de l’aperçu"
                valeur={format}
                onChange={setFormat}
                options={[
                  { valeur: 'forme', libelle: 'Mis en forme' },
                  { valeur: 'simple', libelle: 'Texte simple' },
                ]}
              />
            </div>
            <Field label="Avec les informations de la MEP" className="mt-4">
              {(i) => (
                <Select id={i} value={mepId} onChange={(e) => setMepId(e.target.value)}>
                  {(meps.donnees ?? []).map((m) => (
                    <option key={m.id} value={m.id}>
                      {m.outil}
                      {m.versionCible ? ` ${m.versionCible}` : ''} · {formatDate(m.dateMep ?? m.creeLe)}
                    </option>
                  ))}
                </Select>
              )}
            </Field>
            {inconnu.length > 0 && (
              <Alerte ton="danger" icone={OctagonAlert} className="mt-4">
                Variable{inconnu.length > 1 ? 's' : ''} inconnue{inconnu.length > 1 ? 's' : ''} :{' '}
                {inconnu.map((c) => `{{${c}}}`).join(', ')}. Choisissez-la dans « Insérer une variable ».
              </Alerte>
            )}
            {manque.length > inconnu.length && (
              <Alerte ton="warn" icone={TriangleAlert} className="mt-4">
                Non renseigné dans cette MEP :{' '}
                {listeManquantes(
                  manque.filter((c) => !inconnu.includes(c)),
                  libelles,
                )}
                .
              </Alerte>
            )}
            {valeurs.erreur && (
              <Alerte ton="danger" icone={OctagonAlert} className="mt-4">
                {valeurs.erreur}
              </Alerte>
            )}
            {meps.donnees !== null && meps.donnees.length === 0 && (
              <p className="mt-4 text-sm text-fg-subtle">
                Aucune MEP pour l’aperçu : les variables restent à compléter.
              </p>
            )}
            <div className="mt-4 overflow-hidden rounded-lg border border-line">
              <div className="flex items-baseline gap-3 border-b border-line bg-surface-2/50 px-4 py-2.5 text-sm">
                <span className="w-12 shrink-0 text-[13px] text-fg-subtle">Objet</span>
                <ApercuTexte
                  lignes={generer(b.objet, vals)}
                  libelles={libelles}
                  simple={format === 'simple'}
                  className="font-medium"
                />
              </div>
              <div className="px-4 py-4">
                <ApercuTexte lignes={generer(b.corps, vals)} libelles={libelles} simple={format === 'simple'} />
              </div>
            </div>
          </Card>
        </div>
      )}

      <ConfirmDialog
        open={retablir}
        onOpenChange={setRetablir}
        titre="Rétablir le texte fourni ?"
        description="Le nom, l’usage, l’objet et le texte fournis remplacent ceux de l’équipe."
        libelleConfirmer="Rétablir le texte fourni"
        onConfirmer={async () => {
          if (!modele) return false;
          try {
            const m = await api.retablirModeleTexte(modele.id);
            const v = { nom: m.nom, usage: m.usage, equipeId: m.equipeId, objet: m.objet, corps: m.corps };
            existant.setDonnees(m);
            setB(v);
            setInitial(v);
            toast.succes('Texte fourni rétabli.');
            return true;
          } catch (e) {
            toast.erreur(e instanceof Error ? e.message : 'Action impossible.');
            return false;
          }
        }}
      />
    </main>
  );
}
