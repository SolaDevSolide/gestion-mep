import { Copy, Eye, Mail, OctagonAlert, Pencil, Plus, RotateCcw, Search, Trash2, UsersRound } from 'lucide-react';
import { Fragment, useEffect, useMemo, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { api } from '../api';
import { useAuth } from '../auth';
import { formatDateHeure } from '../format';
import { useAsync } from '../hooks';
import { correspond, termes } from '../recherche';
import { LIBELLES_USAGE } from '../textes/modelesTexte';
import type { Equipe, ModeleTexte } from '../types';
import { Alerte } from '../ui/Alerte';
import { BoutonIcone, Button } from '../ui/Button';
import { Card } from '../ui/Card';
import { ConfirmDialog, Dialog } from '../ui/Dialog';
import { EmptyState } from '../ui/EmptyState';
import { Field, Input, Select, Textarea } from '../ui/Input';
import { Badge } from '../ui/StatusPill';
import { useToast } from '../ui/Toast';
import { Tooltip } from '../ui/Tooltip';

/** Deux premières lignes utiles du texte (sans « Bonjour, » ni marques de gras). */
const premieresLignes = (corps: string) =>
  corps
    .split('\n')
    .map((l) => l.replace(/\*\*/g, '').trim())
    .filter((l) => l && !/^bonjour/i.test(l))
    .slice(0, 2);

/** Texte où les variables `{{cle}}` sont mises en évidence. */
export function AvecVariables({ texte }: { texte: string }) {
  return (
    <>
      {texte.split(/(\{\{\s*[a-z0-9_]+\s*\}\})/).map((s, i) =>
        s.startsWith('{{') ? (
          <code key={i} className="rounded bg-accent-soft px-1 font-mono text-[12px] text-accent-text">
            {s.slice(2, -2).trim()}
          </code>
        ) : (
          <Fragment key={i}>{s}</Fragment>
        ),
      )}
    </>
  );
}

const auteur = (m: ModeleTexte) =>
  m.fourni && !m.modifie
    ? 'Texte fourni, jamais modifié'
    : `${m.modifiePar ? `Modifié par ${m.modifiePar}` : 'Modifié'} le ${formatDateHeure(m.modifieLe)}`;

/** Contact support et signature de l'équipe, communs à tous ses modèles. */
function InfosEquipeDialog({
  equipe,
  ouvert,
  onFermer,
  onEnregistre,
}: {
  equipe: Equipe;
  ouvert: boolean;
  onFermer: () => void;
  onEnregistre: (e: Equipe) => void;
}) {
  const toast = useToast();
  const [contact, setContact] = useState('');
  const [signature, setSignature] = useState('');
  const [erreur, setErreur] = useState('');
  const [envoi, setEnvoi] = useState(false);
  useEffect(() => {
    if (!ouvert) return;
    setContact(equipe.contactSupport);
    setSignature(equipe.signature);
    setErreur('');
  }, [ouvert, equipe]);

  async function enregistrer() {
    setEnvoi(true);
    setErreur('');
    try {
      const e = await api.modifierCommunicationEquipe(equipe.id, { contactSupport: contact, signature });
      toast.succes('Informations de l’équipe enregistrées.');
      onEnregistre(e);
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
      titre={`Informations de l’équipe ${equipe.nom}`}
      description="Communes à tous les modèles de l’équipe, par les variables contact_support et signature."
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
      <div className="grid gap-4">
        <Field label="Contact support" hint="Adresse, poste ou outil de ticket à donner aux utilisateurs.">
          {(id) => (
            <Input
              id={id}
              value={contact}
              placeholder="ex. support-applicatif@exemple.fr · poste 4550"
              onChange={(e) => setContact(e.target.value)}
            />
          )}
        </Field>
        <Field label="Signature" hint={`Vide : « L’équipe ${equipe.nom} ».`}>
          {(id) => <Textarea id={id} rows={2} value={signature} onChange={(e) => setSignature(e.target.value)} />}
        </Field>
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
 * Paramètres › Modèles de texte : courriels types de chaque équipe (les quatre modèles fournis et ceux de l'équipe),
 * remplis avec les informations d'une MEP et copiés depuis Préparation › Communication. Les membres de l'équipe et les
 * administrateurs les modifient ; tout le monde les consulte.
 */
export default function ModelesTextePage() {
  const navigate = useNavigate();
  const toast = useToast();
  const { utilisateur } = useAuth();
  const [params, setParams] = useSearchParams();
  const equipes = useAsync(() => api.equipes(), []);
  const [recherche, setRecherche] = useState('');
  const [infos, setInfos] = useState(false);
  const [suppr, setSuppr] = useState<ModeleTexte | null>(null);
  const [retablir, setRetablir] = useState<ModeleTexte | null>(null);

  const liste = equipes.donnees ?? [];
  const demandee = params.get('equipe');
  const equipe =
    liste.find((e) => e.id === demandee) ??
    liste.find((e) => utilisateur?.equipeIds.includes(e.id)) ??
    liste[0] ??
    null;
  const modeles = useAsync(
    () => (equipe ? api.modelesTexte(equipe.id) : Promise.resolve([] as ModeleTexte[])),
    [equipe?.id],
  );
  const peutModifier = (utilisateur?.estAdmin ?? false) || (modeles.donnees ?? []).some((m) => m.peutModifier);

  const filtres = useMemo(() => {
    const ts = termes(recherche);
    return (modeles.donnees ?? []).filter((m) =>
      correspond({ titre: m.nom, textes: [m.objet, m.corps, LIBELLES_USAGE[m.usage]] }, ts),
    );
  }, [modeles.donnees, recherche]);

  const choisirEquipe = (id: string) => setParams(id ? { equipe: id } : {}, { replace: true });

  async function agir(action: () => Promise<unknown>, succes: string) {
    try {
      await action();
      toast.succes(succes);
      modeles.recharger();
      return true;
    } catch (e) {
      toast.erreur(e instanceof Error ? e.message : 'Action impossible.');
      return false;
    }
  }

  return (
    <main className="mx-auto w-full max-w-6xl px-6 py-8">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Modèles de texte</h1>
          <p className="mt-1 text-sm text-fg-muted">
            Courriels types de chaque équipe, remplis avec les informations de la MEP et copiés depuis Préparation ›
            Communication.
          </p>
        </div>
        {equipe && peutModifier && (
          <Button
            variante="primaire"
            icone={<Plus className="size-4" aria-hidden />}
            onClick={() => void navigate(`/parametres/textes/nouveau?equipe=${equipe.id}`)}
          >
            Nouveau modèle
          </Button>
        )}
      </div>

      {equipes.erreur && (
        <Alerte ton="danger" icone={OctagonAlert} role="alert" className="mt-6">
          {equipes.erreur}
        </Alerte>
      )}
      {equipes.donnees !== null && liste.length === 0 && (
        <Card className="mt-6">
          <EmptyState
            icone={UsersRound}
            titre="Aucune équipe"
            description="Les modèles de texte appartiennent aux équipes : un administrateur crée les équipes dans la console d’administration. Chaque équipe reçoit alors les quatre modèles fournis."
          />
        </Card>
      )}

      {equipe && (
        <>
          <div className="mt-6 flex flex-wrap items-center gap-3">
            <div className="w-64">
              <Select aria-label="Équipe" value={equipe.id} onChange={(e) => choisirEquipe(e.target.value)}>
                {liste.map((e) => (
                  <option key={e.id} value={e.id}>
                    {e.nom}
                  </option>
                ))}
              </Select>
            </div>
            <div className="relative min-w-60 flex-1 sm:max-w-sm">
              <Search
                className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-fg-subtle"
                aria-hidden
              />
              <Input
                aria-label="Rechercher un modèle"
                placeholder="Rechercher (nom, objet, texte)…"
                className="pl-9"
                value={recherche}
                onChange={(e) => setRecherche(e.target.value)}
              />
            </div>
          </div>

          <Card className="mt-6 flex flex-wrap items-start gap-x-6 gap-y-3 p-4">
            <div className="flex min-w-0 flex-1 basis-72 items-start gap-3">
              <UsersRound className="mt-0.5 size-4 shrink-0 text-accent" aria-hidden />
              <div className="min-w-0 text-sm">
                <p className="font-semibold">Informations de l’équipe {equipe.nom}</p>
                <p className="text-fg-muted">Communes à tous les modèles de l’équipe.</p>
              </div>
            </div>
            <dl className="grid flex-[2] basis-96 gap-x-6 gap-y-1 text-sm sm:grid-cols-2">
              <div>
                <dt className="text-[13px] text-fg-subtle">
                  Contact support <code className="font-mono text-[12px]">contact_support</code>
                </dt>
                <dd>{equipe.contactSupport || <span className="text-warn-fg">Non renseigné</span>}</dd>
              </div>
              <div>
                <dt className="text-[13px] text-fg-subtle">
                  Signature <code className="font-mono text-[12px]">signature</code>
                </dt>
                <dd className="whitespace-pre-line">{equipe.signature || `L’équipe ${equipe.nom}`}</dd>
              </div>
            </dl>
            {peutModifier && (
              <Button taille="sm" icone={<Pencil className="size-4" aria-hidden />} onClick={() => setInfos(true)}>
                Modifier
              </Button>
            )}
          </Card>

          {modeles.erreur && (
            <Alerte ton="danger" icone={OctagonAlert} role="alert" className="mt-4">
              {modeles.erreur}
            </Alerte>
          )}
          {modeles.donnees !== null && filtres.length === 0 && (
            <Card className="mt-4">
              <EmptyState
                icone={Mail}
                titre="Aucun résultat"
                description="Aucun modèle ne correspond à cette recherche."
              />
            </Card>
          )}

          <ul className="mt-4 grid gap-3 lg:grid-cols-2">
            {filtres.map((m) => (
              <li key={m.id}>
                <Card className="flex h-full flex-col p-4 transition-shadow duration-150 hover:shadow-md">
                  <div className="flex items-start justify-between gap-3">
                    <div className="min-w-0">
                      <h2 className="flex items-center gap-2 font-semibold">
                        <Mail className="size-4 shrink-0 text-accent" aria-hidden />
                        <span className="truncate">{m.nom}</span>
                      </h2>
                      <div className="mt-1.5 flex flex-wrap items-center gap-2">
                        <Badge>{LIBELLES_USAGE[m.usage]}</Badge>
                        {m.fourni && <Badge ton="done">Fourni par défaut</Badge>}
                      </div>
                    </div>
                    <div className="flex shrink-0 gap-0.5">
                      <Tooltip content={m.peutModifier ? 'Modifier' : 'Voir'}>
                        <BoutonIcone
                          label={`${m.peutModifier ? 'Modifier' : 'Voir'} ${m.nom}`}
                          taille="sm"
                          onClick={() => void navigate(`/parametres/textes/${m.id}`)}
                        >
                          {m.peutModifier ? (
                            <Pencil className="size-4" aria-hidden />
                          ) : (
                            <Eye className="size-4" aria-hidden />
                          )}
                        </BoutonIcone>
                      </Tooltip>
                      {peutModifier && (
                        <Tooltip content="Dupliquer">
                          <BoutonIcone
                            label={`Dupliquer ${m.nom}`}
                            taille="sm"
                            onClick={() =>
                              void navigate(`/parametres/textes/nouveau?depuis=${m.id}&equipe=${m.equipeId}`)
                            }
                          >
                            <Copy className="size-4" aria-hidden />
                          </BoutonIcone>
                        </Tooltip>
                      )}
                      {m.peutModifier && m.fourni && m.modifie && (
                        <Tooltip content="Rétablir le texte fourni">
                          <BoutonIcone label={`Rétablir ${m.nom}`} taille="sm" onClick={() => setRetablir(m)}>
                            <RotateCcw className="size-4" aria-hidden />
                          </BoutonIcone>
                        </Tooltip>
                      )}
                      {m.peutModifier && !m.fourni && (
                        <Tooltip content="Supprimer">
                          <BoutonIcone label={`Supprimer ${m.nom}`} taille="sm" onClick={() => setSuppr(m)}>
                            <Trash2 className="size-4" aria-hidden />
                          </BoutonIcone>
                        </Tooltip>
                      )}
                    </div>
                  </div>
                  <p className="mt-3 truncate text-sm">
                    <span className="text-fg-subtle">Objet : </span>
                    <AvecVariables texte={m.objet} />
                  </p>
                  <div className="mt-1 space-y-0.5 text-sm text-fg-muted">
                    {premieresLignes(m.corps).map((l, i) => (
                      <p key={i} className="truncate">
                        <AvecVariables texte={l} />
                      </p>
                    ))}
                  </div>
                  <p className="mt-auto pt-3 text-xs text-fg-subtle">{auteur(m)}</p>
                </Card>
              </li>
            ))}
          </ul>

          <InfosEquipeDialog
            equipe={equipe}
            ouvert={infos}
            onFermer={() => setInfos(false)}
            onEnregistre={(e) => equipes.setDonnees(liste.map((x) => (x.id === e.id ? e : x)))}
          />
        </>
      )}

      <ConfirmDialog
        open={retablir !== null}
        onOpenChange={(o) => {
          if (!o) setRetablir(null);
        }}
        titre={`Rétablir « ${retablir?.nom ?? ''} » ?`}
        description="Le nom, l’usage, l’objet et le texte fournis remplacent ceux de l’équipe."
        libelleConfirmer="Rétablir le texte fourni"
        onConfirmer={() =>
          retablir ? agir(() => api.retablirModeleTexte(retablir.id), 'Texte fourni rétabli.') : false
        }
      />
      <ConfirmDialog
        open={suppr !== null}
        onOpenChange={(o) => {
          if (!o) setSuppr(null);
        }}
        titre={`Supprimer « ${suppr?.nom ?? ''} » ?`}
        description="Le modèle disparaît pour toute l’équipe."
        libelleConfirmer="Supprimer"
        variante="danger"
        onConfirmer={() => (suppr ? agir(() => api.supprimerModeleTexte(suppr.id), 'Modèle supprimé.') : false)}
      />
    </main>
  );
}
