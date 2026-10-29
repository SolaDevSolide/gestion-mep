import { Ellipsis, OctagonAlert, Plus, RefreshCw, Webhook } from 'lucide-react';
import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { api } from '../api';
import { useAsync } from '../hooks';
import { commande, useInstallation } from '../installation';
import type {
  CatalogueConnexions,
  ConnexionApiResume,
  EtatAppel,
  MessageRecu,
  OrigineAppel,
  PageAppels,
} from '../types';
import { Alerte } from '../ui/Alerte';
import { BoutonIcone, Button } from '../ui/Button';
import { Card, CardHeader } from '../ui/Card';
import { CodeBlock } from '../ui/CodeBlock';
import { ConfirmDialog } from '../ui/Dialog';
import { EmptyState } from '../ui/EmptyState';
import { ErreurChargement } from '../ui/ErreurChargement';
import { Menu, MenuContent, MenuItem, MenuSeparator, MenuTrigger } from '../ui/Menu';
import { Segmented } from '../ui/Segmented';
import { EnChargement, Squelette, SqueletteTableau } from '../ui/Squelette';
import { Badge, StatusPill } from '../ui/StatusPill';
import { useToast } from '../ui/Toast';
import {
  codeAppel,
  contexteAppel,
  duree,
  heureCourte,
  LIBELLE_ETAT_CONNEXION,
  LIBELLE_MODELE,
  quandCourt,
  quandListe,
  resultatAppel,
  TON_ETAT_CONNEXION,
} from './connexions/connexions';
import { DetailAppel } from './connexions/DetailAppel';
import { NouvelleConnexionDialog } from './connexions/NouvelleConnexionDialog';

/**
 * Onglet « API externes » de la console (#111) : connexions, derniers appels (chargés à part), réseau et ligne de
 * commande. Une connexion en erreur (dernier appel définitivement en échec) est signalée en tête.
 */
export default function ApiExternesTab() {
  const liste = useAsync(() => Promise.all([api.connexionsApi(), api.catalogueConnexions()]), []);
  const [detail, setDetail] = useState<string | null>(null);
  const [nouvelle, setNouvelle] = useState(false);
  const [tickAppels, setTickAppels] = useState(0);
  const [connexions, catalogue] = liste.donnees ?? [null, null];
  const enErreur = (connexions ?? []).filter((c) => c.etat === 'EnErreur');

  return (
    <div className="grid grid-cols-[minmax(0,1fr)] gap-5">
      {enErreur.length > 0 && (
        <Alerte
          ton="danger"
          icone={OctagonAlert}
          role="alert"
          titre={`${enErreur.length} connexion${enErreur.length > 1 ? 's' : ''} en erreur`}
        >
          <ul className="grid gap-1 [overflow-wrap:anywhere]">
            {enErreur.map((c) => (
              <li key={c.id}>
                « {c.nom} » échoue depuis {heureCourte(c.dernierAppel?.termineLe ?? c.dernierAppel?.creeLe)}
                {c.dernierAppel ? ` : ${c.dernierAppel.erreur || codeAppel(c.dernierAppel)}` : ''}
                {c.dernierAppel?.mepNom
                  ? ` (${c.dernierAppel.libelleEvenement} de ${c.dernierAppel.mepNom})`
                  : ''}.{' '}
                {c.dernierAppel && (
                  <button
                    type="button"
                    className="font-semibold underline underline-offset-2"
                    onClick={() => setDetail(c.dernierAppel?.id ?? null)}
                  >
                    Voir l’appel
                  </button>
                )}
              </li>
            ))}
          </ul>
        </Alerte>
      )}

      <Card>
        <CardHeader
          titre="API externes"
          description="Envoie les événements des MEP à d’autres outils : messagerie d’équipe, outil de tickets, supervision."
          action={
            <Button
              variante="primaire"
              icone={<Plus className="size-4" aria-hidden />}
              onClick={() => setNouvelle(true)}
            >
              Nouvelle connexion
            </Button>
          }
        />
        {liste.erreur && (
          <div className="p-5">
            <ErreurChargement
              erreur={liste.echec}
              titre="Impossible de charger les connexions"
              onReessayer={liste.recharger}
              enCours={liste.chargement}
            />
          </div>
        )}
        {!liste.erreur && !liste.donnees && (
          <EnChargement libelle="Chargement des connexions…">
            <SqueletteTableau lignes={4} colonnes={4} className="rounded-none border-0" />
          </EnChargement>
        )}
        {connexions && connexions.length === 0 && (
          <EmptyState
            icone={Webhook}
            titre="Aucune connexion"
            description="Envoyez le Go, les échecs ou la clôture des MEP à un canal Teams, un outil de tickets ou toute API qui accepte une requête HTTP."
            action={
              <Button
                variante="primaire"
                icone={<Plus className="size-4" aria-hidden />}
                onClick={() => setNouvelle(true)}
              >
                Nouvelle connexion
              </Button>
            }
          />
        )}
        {connexions && catalogue && connexions.length > 0 && (
          <ListeConnexions
            connexions={connexions}
            catalogue={catalogue}
            onDetail={setDetail}
            onChange={() => {
              liste.recharger();
              setTickAppels((t) => t + 1);
            }}
          />
        )}
      </Card>

      <DerniersAppels tick={tickAppels} onDetail={setDetail} />

      <div className="grid grid-cols-[minmax(0,1fr)] gap-5 lg:grid-cols-2">
        <Reseau catalogue={catalogue} />
        <LigneDeCommande />
      </div>

      <RecepteurDemo tick={tickAppels} />

      <DetailAppel
        appelId={detail}
        onFermer={() => setDetail(null)}
        onRenvoye={() => {
          setDetail(null);
          setTickAppels((t) => t + 1);
        }}
      />
      {catalogue && <NouvelleConnexionDialog open={nouvelle} onOpenChange={setNouvelle} catalogue={catalogue} />}
    </div>
  );
}

function ListeConnexions({
  connexions,
  catalogue,
  onDetail,
  onChange,
}: {
  connexions: ConnexionApiResume[];
  catalogue: CatalogueConnexions;
  onDetail: (id: string) => void;
  onChange: () => void;
}) {
  const navigate = useNavigate();
  const toast = useToast();
  const [suppression, setSuppression] = useState<ConnexionApiResume | null>(null);
  const libelles = new Map(catalogue.declencheurs.map((d) => [d.cle, d.libelle]));

  async function changerEtat(c: ConnexionApiResume) {
    try {
      await api.activerConnexionApi(c.id, !c.active);
      toast.succes(c.active ? `« ${c.nom} » désactivée : plus aucun envoi.` : `« ${c.nom} » activée.`);
      onChange();
    } catch (e) {
      toast.erreur(e instanceof Error ? e.message : 'Action impossible.');
    }
  }

  return (
    <>
      <div
        role="row"
        className="hidden gap-4 border-b border-line bg-surface-2/50 px-5 py-2 text-2xs font-semibold tracking-wider text-fg-subtle uppercase md:grid md:grid-cols-[minmax(0,1.3fr)_minmax(0,1.2fr)_8rem_minmax(0,1fr)_2rem]"
      >
        <span>Connexion</span>
        <span>Déclencheurs</span>
        <span>État</span>
        <span>Dernier appel</span>
        <span className="sr-only">Actions</span>
      </div>
      <ul className="apparition divide-y divide-line">
        {connexions.map((c) => {
          const declencheurs = c.declencheurs.map((d) => libelles.get(d) ?? d);
          if (c.envoiManuel) declencheurs.push('Envoi depuis une MEP');
          const dernier = c.dernierAppel ?? c.dernierEssai;
          return (
            <li
              key={c.id}
              className="grid gap-2 px-5 py-3.5 text-sm md:grid-cols-[minmax(0,1.3fr)_minmax(0,1.2fr)_8rem_minmax(0,1fr)_2rem] md:items-start md:gap-4"
            >
              <div className="min-w-0">
                <Link to={`/admin/api/${c.id}`} className="font-medium text-fg hover:text-accent-text hover:underline">
                  {c.nom}
                </Link>
                <p className="mt-0.5 text-ui text-fg-subtle">{LIBELLE_MODELE[c.modele]}</p>
                <p className="mt-0.5 truncate font-mono text-xs text-fg-muted" title={c.adresse}>
                  {c.adresse}
                </p>
              </div>
              <div className="flex min-w-0 flex-wrap gap-1">
                {declencheurs.length === 0 && <span className="text-fg-subtle">Aucun</span>}
                {declencheurs.slice(0, 4).map((d) => (
                  <Badge key={d}>{d}</Badge>
                ))}
                {declencheurs.length > 4 && <Badge>+{declencheurs.length - 4}</Badge>}
              </div>
              <div>
                <StatusPill ton={TON_ETAT_CONNEXION[c.etat]} taille="sm">
                  {LIBELLE_ETAT_CONNEXION[c.etat]}
                </StatusPill>
              </div>
              <div className="min-w-0 text-ui">
                {dernier ? (
                  <button
                    type="button"
                    className="text-left hover:underline"
                    onClick={() => onDetail(dernier.id)}
                    title="Voir l’appel"
                  >
                    <span className="font-mono font-medium">{codeAppel(dernier)}</span>{' '}
                    <span className="text-fg-muted">{quandCourt(dernier.creeLe)}</span>
                    <span className="block text-fg-subtle">
                      {dernier === c.dernierEssai && !c.dernierAppel
                        ? `Essai ${dernier.etat === 'Reussi' ? 'réussi' : 'en échec'}`
                        : `${dernier.tentatives > 1 ? `${dernier.tentatives} tentatives · ` : ''}${contexteAppel(dernier)}`}
                    </span>
                  </button>
                ) : (
                  <span className="text-fg-subtle">—</span>
                )}
              </div>
              <div className="md:justify-self-end">
                <Menu>
                  <MenuTrigger asChild>
                    <BoutonIcone label={`Actions de « ${c.nom} »`} taille="ic-sm">
                      <Ellipsis className="size-4" aria-hidden />
                    </BoutonIcone>
                  </MenuTrigger>
                  <MenuContent>
                    <MenuItem onSelect={() => void navigate(`/admin/api/${c.id}`)}>Modifier</MenuItem>
                    <MenuItem onSelect={() => void navigate(`/admin/api/${c.id}?essayer=1`)}>Essayer</MenuItem>
                    <MenuItem onSelect={() => void navigate(`/admin/api/nouvelle?depuis=${c.id}`)}>Dupliquer</MenuItem>
                    <MenuItem onSelect={() => void changerEtat(c)}>{c.active ? 'Désactiver' : 'Activer'}</MenuItem>
                    <MenuSeparator />
                    <MenuItem className="text-danger-fg" onSelect={() => setSuppression(c)}>
                      Supprimer…
                    </MenuItem>
                  </MenuContent>
                </Menu>
              </div>
            </li>
          );
        })}
      </ul>
      <ConfirmDialog
        open={suppression !== null}
        onOpenChange={(o) => {
          if (!o) setSuppression(null);
        }}
        titre={`Supprimer « ${suppression?.nom ?? ''} » ?`}
        description="La connexion n’enverra plus rien. L’historique de ses appels est supprimé avec elle ; les lignes du journal des MEP restent."
        libelleConfirmer="Supprimer"
        variante="danger"
        onConfirmer={async () => {
          if (!suppression) return false;
          try {
            await api.supprimerConnexionApi(suppression.id);
            toast.succes(`« ${suppression.nom} » supprimée.`);
            onChange();
            return true;
          } catch (e) {
            toast.erreur(e instanceof Error ? e.message : 'Suppression impossible.');
            return false;
          }
        }}
      />
    </>
  );
}

type FiltreAppels = 'tous' | 'echecs' | 'essais';
const FILTRES: Record<FiltreAppels, { etat?: EtatAppel; origine?: OrigineAppel }> = {
  tous: {},
  echecs: { etat: 'Echec' },
  essais: { origine: 'Essai' },
};

/** Derniers appels de toutes les connexions : carte chargée à part (une erreur ici laisse la liste utilisable). */
function DerniersAppels({ tick, onDetail }: { tick: number; onDetail: (id: string) => void }) {
  const [filtre, setFiltre] = useState<FiltreAppels>('tous');
  const [suite, setSuite] = useState<PageAppels[]>([]);
  const [plus, setPlus] = useState(false);
  const page = useAsync(() => {
    setSuite([]);
    return api.appelsApi({ ...FILTRES[filtre], limite: 20 });
  }, [filtre, tick]);
  const pages = page.donnees ? [page.donnees, ...suite] : [];
  const appels = pages.flatMap((p) => p.appels);
  const suivant = pages.length > 0 ? pages[pages.length - 1].suivant : null;

  async function afficherPlus() {
    if (!suivant) return;
    setPlus(true);
    try {
      const p = await api.appelsApi({ ...FILTRES[filtre], limite: 20, avant: suivant });
      setSuite((s) => [...s, p]);
    } finally {
      setPlus(false);
    }
  }

  return (
    <Card>
      <CardHeader
        titre="Derniers appels"
        description="Toutes les connexions, du plus récent au plus ancien. Conservés selon le réglage api-appels-conservation-jours."
        action={
          <div className="flex flex-wrap items-center gap-2">
            <Segmented<FiltreAppels>
              label="Appels montrés"
              valeur={filtre}
              onChange={setFiltre}
              options={[
                { valeur: 'tous', libelle: 'Tous' },
                {
                  valeur: 'echecs',
                  libelle: `Échecs${page.donnees && page.donnees.echecs > 0 ? ` ${page.donnees.echecs}` : ''}`,
                },
                { valeur: 'essais', libelle: 'Essais' },
              ]}
            />
            <Button taille="sm" icone={<RefreshCw className="size-4" aria-hidden />} onClick={page.recharger}>
              Actualiser
            </Button>
          </div>
        }
      />
      {page.erreur && (
        <div className="p-5">
          <ErreurChargement
            erreur={page.echec}
            titre="Impossible de charger les derniers appels"
            onReessayer={page.recharger}
            enCours={page.chargement}
          />
        </div>
      )}
      {!page.erreur && !page.donnees && (
        <EnChargement libelle="Chargement des appels…">
          <SqueletteTableau lignes={4} colonnes={5} className="rounded-none border-0" />
        </EnChargement>
      )}
      {page.donnees && appels.length === 0 && (
        <p className="px-5 py-8 text-center text-sm text-fg-subtle">
          {filtre === 'echecs'
            ? 'Aucun échec : tous les appels terminés ont réussi.'
            : filtre === 'essais'
              ? 'Aucun essai : « Essayer » dans l’éditeur envoie le message pour de vrai.'
              : 'Aucun appel pour l’instant : ils apparaîtront au premier événement d’une MEP.'}
        </p>
      )}
      {appels.length > 0 && (
        <>
          <div
            role="row"
            className="hidden gap-4 border-b border-line bg-surface-2/50 px-5 py-2 text-2xs font-semibold tracking-wider text-fg-subtle uppercase md:grid md:grid-cols-[7rem_minmax(0,1fr)_minmax(0,1.4fr)_minmax(0,1fr)_5rem]"
          >
            <span>Quand</span>
            <span>Connexion</span>
            <span>MEP · événement</span>
            <span>Résultat</span>
            <span className="text-right">Durée</span>
          </div>
          <ul className="apparition divide-y divide-line">
            {appels.map((a) => {
              const r = resultatAppel(a);
              return (
                <li key={a.id}>
                  <button
                    type="button"
                    onClick={() => onDetail(a.id)}
                    className="grid w-full gap-1 px-5 py-2.5 text-left text-sm transition-colors duration-150 hover:bg-surface-2/60 md:grid-cols-[7rem_minmax(0,1fr)_minmax(0,1.4fr)_minmax(0,1fr)_5rem] md:items-baseline md:gap-4"
                  >
                    <time dateTime={a.creeLe} className="text-fg-subtle tabular-nums">
                      {quandListe(a.creeLe)}
                    </time>
                    <span className="truncate font-medium">{a.connexion}</span>
                    <span className="truncate text-fg-muted">{contexteAppel(a)}</span>
                    <span className="flex items-center gap-2">
                      <span className="font-mono text-ui">{codeAppel(a)}</span>
                      <StatusPill ton={r.ton} taille="sm">
                        {r.libelle}
                      </StatusPill>
                    </span>
                    <span className="text-fg-subtle tabular-nums md:text-right">
                      {a.dureeMs > 0 ? duree(a.dureeMs) : a.tentatives > 0 ? '< 1 ms' : '—'}
                    </span>
                  </button>
                </li>
              );
            })}
          </ul>
          {suivant && (
            <div className="border-t border-line px-5 py-3 text-center">
              <Button taille="sm" variante="fantome" chargement={plus} onClick={() => void afficherPlus()}>
                Afficher plus
              </Button>
            </div>
          )}
        </>
      )}
    </Card>
  );
}

const LIBELLE_RESEAU: Record<string, string> = {
  'api-reseaux-autorises': 'Réseaux internes autorisés',
  'api-proxy': 'Proxy sortant',
  'url-publique': 'Adresse publique',
};

/** Où les connexions ont le droit d'aller : réglages du serveur (modifiables dans Paramètres). */
function Reseau({ catalogue }: { catalogue: CatalogueConnexions | null }) {
  const reglages = catalogue?.reglages ?? [];
  const envois = reglages.find((r) => r.nom === 'api-envois-actifs');
  return (
    <Card>
      <CardHeader titre="Réseau" description="Où les connexions ont le droit d’aller." />
      <div className="grid gap-3 p-5 text-sm">
        {!catalogue && <Squelette className="h-24 w-full" />}
        {envois && envois.valeur === 'false' && (
          <Alerte ton="warn" icone={OctagonAlert}>
            Envois suspendus (réglage api-envois-actifs) : les appels restent en file jusqu’à leur reprise.
          </Alerte>
        )}
        {catalogue && (
          <dl className="grid gap-2.5">
            {Object.entries(LIBELLE_RESEAU).map(([nom, libelle]) => {
              const r = reglages.find((x) => x.nom === nom);
              return (
                <div key={nom} className="grid gap-0.5 sm:grid-cols-[12rem_minmax(0,1fr)] sm:gap-3">
                  <dt className="text-fg-subtle">{libelle}</dt>
                  <dd className="min-w-0 font-mono text-ui break-all">
                    {r?.valeur ? r.valeur : <span className="font-sans text-fg-subtle">aucun</span>}
                    {r?.origine === 'fichier' && (
                      <span className="block font-sans text-xs text-fg-subtle">
                        Fichier de démarrage ({r.variable})
                      </span>
                    )}
                    {nom === 'url-publique' && (
                      <span className="font-sans text-fg-subtle">
                        {' '}
                        (pour <code className="font-mono">{'{{lien_mep}}'}</code>)
                      </span>
                    )}
                  </dd>
                </div>
              );
            })}
          </dl>
        )}
        <Link to="/admin?onglet=parametres" className="text-sm font-semibold text-accent-text hover:underline">
          Modifier dans Paramètres
        </Link>
      </div>
    </Card>
  );
}

function LigneDeCommande() {
  const mode = useInstallation();
  return (
    <Card>
      <CardHeader titre="En ligne de commande" description="Les mêmes réglages sont possibles depuis le serveur." />
      <div className="p-5">
        <CodeBlock
          titre="Commandes"
          code={[
            commande(mode, 'connexions lister'),
            commande(mode, 'connexions essayer "<connexion>" --mep <id>'),
            commande(mode, 'connexions appels --echecs'),
          ].join('\n')}
        />
      </div>
    </Card>
  );
}

/**
 * Mode démonstration seulement : messages reçus par le récepteur intégré, que visent les connexions d'exemple (les 50
 * derniers, gardés en mémoire). Rien ne part sur le réseau.
 */
function RecepteurDemo({ tick }: { tick: number }) {
  const demo = useAsync(() => api.demo(), []);
  const actif = demo.donnees !== null && demo.donnees !== undefined;
  const messages = useAsync(() => (actif ? api.recepteurDemo() : Promise.resolve<MessageRecu[]>([])), [actif, tick]);
  if (!actif) return null;
  const liste = messages.donnees ?? [];
  return (
    <Card>
      <CardHeader
        titre="Récepteur de démonstration"
        description="Messages reçus par les connexions d’exemple (adresse recepteur.demo.invalid) : rien ne part sur le réseau. Les 50 derniers."
        action={
          <div className="flex flex-wrap gap-2">
            <Button
              taille="sm"
              variante="fantome"
              icone={<RefreshCw className="size-4" aria-hidden />}
              onClick={messages.recharger}
            >
              Actualiser
            </Button>
            <Button
              taille="sm"
              variante="fantome"
              disabled={liste.length === 0}
              action={async () => {
                await api.viderRecepteurDemo();
                messages.recharger();
              }}
            >
              Vider
            </Button>
          </div>
        }
      />
      <div className="grid gap-2 p-5">
        {messages.erreur && (
          <ErreurChargement
            variante="ligne"
            erreur={messages.echec}
            titre="Impossible de lire le récepteur"
            onReessayer={messages.recharger}
          />
        )}
        {!messages.erreur && !messages.donnees && <Squelette className="h-16 w-full" />}
        {messages.donnees && liste.length === 0 && (
          <p className="text-sm text-fg-subtle">
            Aucun message reçu. « Essayer » dans l’éditeur d’une connexion d’exemple, ou le Go d’une MEP, en envoie un.
          </p>
        )}
        {liste.map((m, i) => (
          <details key={`${m.quand}-${i}`} className="rounded-lg border border-line px-3 py-2 text-sm">
            <summary className="cursor-pointer">
              <span className="font-mono text-xs font-medium">{m.methode}</span>{' '}
              <span className="font-medium">{m.canal}</span>{' '}
              <span className="text-fg-subtle">{quandCourt(m.quand)}</span>
            </summary>
            <CodeBlock
              className="mt-2"
              titre={m.adresse}
              code={[...m.entetes.map((e) => `${e.nom}: ${e.valeur}`), '', m.corps].join('\n')}
            />
          </details>
        ))}
      </div>
    </Card>
  );
}
