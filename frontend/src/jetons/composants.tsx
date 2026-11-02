import { Ban, Check, Clock, Copy, KeyRound, OctagonAlert, TriangleAlert } from 'lucide-react';
import { useEffect, useId, useRef, useState, type ReactNode } from 'react';
import { formatDate } from '../format';
import { useNow } from '../hooks';
import type { JetonApi, JetonCree, PorteeJeton, TypeJeton } from '../types';
import { Alerte } from '../ui/Alerte';
import { Button } from '../ui/Button';
import { Checkbox } from '../ui/Checkbox';
import { CodeBlock, copierTexte } from '../ui/CodeBlock';
import { cn } from '../ui/cn';
import { ConfirmDialog, Dialog } from '../ui/Dialog';
import { Field, Input, Select } from '../ui/Input';
import { Badge, StatusPill } from '../ui/StatusPill';
import {
  DEBITS,
  DUREE_DEFAUT_JOURS,
  DUREE_MAX_JOURS,
  DUREES,
  dansJours,
  echeance,
  exempleCurl,
  expirationPour,
  joursJusquA,
  LIBELLE_PORTEE,
  loginCompteTechnique,
  porteesPour,
  quandUtilise,
  versChampDate,
} from './jetons';

/** Portées d'un jeton, en chasse fixe ; `admin` en rouge partout. */
export function PorteesJeton({ portees }: { portees: string[] }) {
  return (
    <span className="flex flex-wrap gap-1">
      {portees.map((p) => (
        <Badge key={p} ton={p === 'admin' ? 'danger' : undefined} className="font-mono">
          {p}
        </Badge>
      ))}
    </span>
  );
}

/** Expiration : date et délai, rappel en orange avant l'échéance, état final pour un jeton expiré ou révoqué. */
export function EcheanceJeton({ jeton, maintenant }: { jeton: JetonApi; maintenant: Date }) {
  const e = echeance(jeton, maintenant);
  if (e.etat === 'revoque')
    return (
      <StatusPill ton="later" icone={Ban} taille="sm">
        Révoqué le {formatDate(e.le)}
      </StatusPill>
    );
  if (e.etat === 'expire')
    return (
      <StatusPill ton="later" icone={Clock} taille="sm">
        Expiré le {formatDate(e.le)}
      </StatusPill>
    );
  if (e.etat === 'bientot')
    return (
      <span className="flex flex-col items-start gap-0.5">
        <StatusPill ton="warn" icone={Clock} taille="sm">
          {dansJours(e.jours)}
        </StatusPill>
        <span className="text-xs text-fg-subtle tabular-nums">{formatDate(jeton.expireLe)}</span>
      </span>
    );
  return (
    <span className="flex flex-col whitespace-nowrap">
      <span className="tabular-nums">{formatDate(jeton.expireLe)}</span>
      <span className="text-xs text-fg-subtle">{dansJours(e.jours)}</span>
    </span>
  );
}

/** Dernière utilisation (à 5 minutes près) et adresse IP de l'appel. */
export function UtilisationJeton({ jeton, maintenant }: { jeton: JetonApi; maintenant: Date }) {
  return (
    <span className="flex flex-col">
      <span className={cn(!jeton.derniereUtilisationLe && 'text-fg-subtle')}>
        {quandUtilise(jeton.derniereUtilisationLe, maintenant)}
      </span>
      {jeton.derniereAdresseIp && <span className="font-mono text-xs text-fg-subtle">{jeton.derniereAdresseIp}</span>}
    </span>
  );
}

/**
 * Tableau des jetons : un vrai tableau sur grand écran ; sous 768 px, chaque ligne devient une fiche (cellules empilées,
 * libellé de colonne devant chaque valeur), sans défilement horizontal.
 */
export function TableauJetons({ entetes, children }: { entetes: ReactNode[]; children: ReactNode }) {
  return (
    <table className="apparition w-full text-left text-sm max-md:block">
      <thead className="border-b border-line bg-surface-2/50 text-2xs tracking-wider text-fg-subtle uppercase max-md:hidden">
        <tr>
          {entetes.map((e, i) => (
            <th
              key={i}
              scope="col"
              className={cn('px-3 py-2.5 font-semibold first:pl-5 last:pr-5', i === entetes.length - 1 && 'text-right')}
            >
              {e}
            </th>
          ))}
        </tr>
      </thead>
      <tbody className="divide-y divide-line max-md:block">{children}</tbody>
    </table>
  );
}

export function LigneJeton({ attenue, children }: { attenue?: boolean; children: ReactNode }) {
  return (
    <tr
      className={cn('max-md:grid max-md:gap-2 max-md:px-4 max-md:py-3.5', attenue && 'bg-surface-2/40 text-fg-muted')}
    >
      {children}
    </tr>
  );
}

/** Cellule : sur mobile, son libellé de colonne s'affiche devant la valeur. */
export function CelluleJeton({
  libelle,
  actions,
  className,
  children,
}: {
  libelle?: string;
  actions?: boolean;
  className?: string;
  children: ReactNode;
}) {
  return (
    <td
      className={cn(
        'px-3 py-3 align-top first:pl-5 last:pr-5 max-md:flex max-md:min-w-0 max-md:items-start max-md:gap-3 max-md:p-0 max-md:first:pl-0 max-md:last:pr-0',
        actions && 'text-right max-md:justify-end max-md:pt-1',
        className,
      )}
    >
      {libelle && (
        <span className="w-24 shrink-0 pt-0.5 text-2xs font-semibold tracking-wider text-fg-subtle uppercase md:hidden">
          {libelle}
        </span>
      )}
      <div className="min-w-0 flex-1">{children}</div>
    </td>
  );
}

/** Nom du jeton, début visible et création. */
export function NomJeton({ jeton, detail }: { jeton: JetonApi; detail?: string }) {
  return (
    <span className="flex min-w-0 flex-col">
      <span className="font-medium [overflow-wrap:anywhere] text-fg">{jeton.nom}</span>
      <span className="text-xs text-fg-subtle">
        <span className="font-mono">{jeton.prefixe}…</span> · {detail ?? `créé le ${formatDate(jeton.creeLe)}`}
      </span>
    </span>
  );
}

/** Début du nouveau jeton à créer : nom, portées, durée et, pour un jeton de service, compte technique et débit. */
export interface ModeleJeton {
  nom: string;
  portees: string[];
  compteTechniqueId?: string;
  requetesParMinute?: number | null;
}

export interface SaisieJeton {
  nom: string;
  portees: string[];
  dureeJours: number;
  compteTechniqueId?: string;
  requetesParMinute?: number;
}

const AUCUN_COMPTE: { id: string; login: string; nom: string }[] = [];

/**
 * Création d'un jeton (personnel ou de service) : nom, portées (cases à cocher, `admin` pour un jeton de service
 * seulement), expiration (90 jours par défaut, 1 an au plus) et, pour un jeton de service, compte technique et limite
 * de débit. « Recréer » l'ouvre avec le nom, les portées et le compte du jeton à remplacer.
 */
export function NouveauJetonDialog({
  open,
  onOpenChange,
  type,
  portees,
  modele,
  comptes = AUCUN_COMPTE,
  debitGlobal,
  onCreer,
}: {
  open: boolean;
  onOpenChange: (v: boolean) => void;
  type: TypeJeton;
  portees: PorteeJeton[];
  /** « Recréer » : jeton à remplacer. */
  modele?: ModeleJeton | null;
  /** Comptes techniques existants (jetons de service). */
  comptes?: { id: string; login: string; nom: string }[];
  /** Réglage global jetons-par-minute, pour le libellé « Par défaut ». */
  debitGlobal?: number | null;
  onCreer: (s: SaisieJeton) => Promise<JetonCree>;
}) {
  const service = type === 'Service';
  const [nom, setNom] = useState('');
  const [choix, setChoix] = useState<string[]>([]);
  const [duree, setDuree] = useState(String(DUREE_DEFAUT_JOURS));
  const [date, setDate] = useState('');
  const [compte, setCompte] = useState<'nouveau' | 'existant'>('nouveau');
  const [compteId, setCompteId] = useState('');
  const [debit, setDebit] = useState('');
  const [erreur, setErreur] = useState('');
  const [envoi, setEnvoi] = useState(false);
  const [essai, setEssai] = useState(false);
  const idCompte = useId();
  const maintenant = new Date(useNow(60_000));

  // Valeurs de départ à chaque ouverture (nouveau jeton ou « Recréer »).
  const [ouvert, setOuvert] = useState(false);
  if (open !== ouvert) {
    setOuvert(open);
    if (open) {
      setNom(modele?.nom ?? '');
      setChoix(modele?.portees ?? (service ? [] : ['meps:lecture']));
      setDuree(service ? String(DUREE_MAX_JOURS) : String(DUREE_DEFAUT_JOURS));
      setDate(versChampDate(expirationPour(DUREE_DEFAUT_JOURS, maintenant)));
      setCompte(modele?.compteTechniqueId ? 'existant' : 'nouveau');
      setCompteId(modele?.compteTechniqueId ?? comptes[0]?.id ?? '');
      setDebit(modele?.requetesParMinute ? String(modele.requetesParMinute) : '');
      setErreur('');
      setEssai(false);
    }
  }

  const proposees = porteesPour(portees, type);
  const jours = duree === 'date' ? joursJusquA(date, maintenant) : Number(duree);
  const nomVide = !nom.trim();
  const sansPortee = choix.length === 0;
  const compteManquant = service && compte === 'existant' && !compteId;
  const invalide = nomVide || sansPortee || jours === null || compteManquant;
  const debits =
    modele?.requetesParMinute && !DEBITS.includes(modele.requetesParMinute)
      ? [...DEBITS, modele.requetesParMinute]
      : DEBITS;

  async function creer() {
    setEssai(true);
    if (invalide || jours === null) return;
    setEnvoi(true);
    setErreur('');
    try {
      await onCreer({
        nom: nom.trim(),
        portees: proposees.map((p) => p.nom).filter((p) => choix.includes(p)),
        dureeJours: jours,
        ...(service && compte === 'existant' ? { compteTechniqueId: compteId } : {}),
        ...(service && debit ? { requetesParMinute: Number(debit) } : {}),
      });
    } catch (e) {
      setErreur(e instanceof Error ? e.message : 'Création impossible.');
    } finally {
      setEnvoi(false);
    }
  }

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      taille="md"
      titre={modele ? `Recréer « ${modele.nom} »` : service ? 'Nouveau jeton de service' : 'Nouveau jeton personnel'}
      description={
        modele
          ? 'Nouveau jeton, mêmes portées : sa valeur change, mettez à jour l’outil. L’ancien reste valable jusqu’à son expiration ou sa révocation.'
          : service
            ? 'Pour un outil, pas pour une personne : il reste valable si son créateur quitte l’équipe.'
            : 'Le jeton agit en votre nom, avec vos droits. Les portées le limitent encore.'
      }
      pied={
        <>
          <Button variante="fantome" onClick={() => onOpenChange(false)}>
            Annuler
          </Button>
          <Button
            variante="primaire"
            icone={<KeyRound className="size-4" aria-hidden />}
            chargement={envoi}
            onClick={() => void creer()}
          >
            Créer le jeton
          </Button>
        </>
      }
    >
      <form
        className="grid gap-5"
        onSubmit={(e) => {
          e.preventDefault();
          void creer();
        }}
      >
        {erreur && (
          <Alerte ton="danger" icone={OctagonAlert} role="alert">
            {erreur}
          </Alerte>
        )}
        <Field
          label={service ? 'Outil *' : 'Nom *'}
          hint={
            service
              ? 'Le nom signe le journal et les MEP où l’outil agit.'
              : 'Pour le reconnaître dans la liste et au journal.'
          }
          erreur={essai && nomVide ? 'Le nom est obligatoire.' : undefined}
        >
          {(id) => (
            <Input
              id={id}
              value={nom}
              maxLength={100}
              autoComplete="off"
              placeholder={service ? 'ex. Chaîne CI GitLab' : 'ex. Tableau de bord Grafana'}
              onChange={(e) => setNom(e.target.value)}
            />
          )}
        </Field>

        {service && (
          <fieldset className="grid gap-2">
            <legend className="mb-1.5 text-ui font-medium text-fg">Compte technique</legend>
            <ChoixRadio
              nom={idCompte}
              coche={compte === 'nouveau'}
              onChoisir={() => setCompte('nouveau')}
              libelle={
                <>
                  Créer le compte <code className="font-mono">{loginCompteTechnique(nom)}</code>
                </>
              }
              description="Sans mot de passe ni connexion à l’interface, visible dans Comptes avec l’étiquette « Technique ». Ajoutez-le à des équipes et aux MEP où l’outil agit."
            />
            <ChoixRadio
              nom={idCompte}
              coche={compte === 'existant'}
              desactive={comptes.length === 0}
              onChoisir={() => setCompte('existant')}
              libelle="Utiliser un compte technique existant"
              description={
                comptes.length === 0
                  ? 'Aucun compte technique pour l’instant.'
                  : 'Il garde ses équipes et sa place dans les MEP : pour remplacer un jeton qui expire.'
              }
            />
            {compte === 'existant' && comptes.length > 0 && (
              <div className="pl-7">
                <Select
                  aria-label="Compte technique existant"
                  value={compteId}
                  onChange={(e) => setCompteId(e.target.value)}
                >
                  {comptes.map((c) => (
                    <option key={c.id} value={c.id}>
                      {c.login} ({c.nom})
                    </option>
                  ))}
                </Select>
              </div>
            )}
          </fieldset>
        )}

        <fieldset className="grid gap-2.5">
          <legend className="mb-1.5 text-ui font-medium text-fg">Portées *</legend>
          {proposees.map((p) => (
            <Checkbox
              key={p.nom}
              checked={choix.includes(p.nom)}
              onChange={(v) => setChoix((c) => (v ? [...c, p.nom] : c.filter((x) => x !== p.nom)))}
              label={
                <>
                  {LIBELLE_PORTEE[p.nom] ?? p.nom}{' '}
                  <code className={cn('font-mono text-xs', p.nom === 'admin' ? 'text-danger-fg' : 'text-fg-muted')}>
                    {p.nom}
                  </code>
                </>
              }
              description={
                !service && p.nom === 'catalogue:ecriture'
                  ? `${p.description}. Réservé aux administrateurs : un jeton personnel n’a jamais ce droit, la portée n’ajoutera rien.`
                  : p.nom === 'admin'
                    ? `${p.description}. Tous les droits d’administration par l’API : à réserver à un outil de provisionnement.`
                    : p.description
              }
            />
          ))}
          {essai && sansPortee && (
            <p className="text-xs text-danger-fg" role="alert">
              Cochez au moins une portée.
            </p>
          )}
          {!service && (
            <p className="text-xs text-fg-subtle">
              La portée <code className="font-mono">admin</code> n’existe que pour les jetons de service.
            </p>
          )}
        </fieldset>

        <div className="grid gap-4 sm:grid-cols-2">
          <Field label="Expiration" hint="1 an au plus, pour tous les jetons.">
            {(id) => (
              <Select id={id} value={duree} onChange={(e) => setDuree(e.target.value)}>
                {DUREES.map((d) => (
                  <option key={d.valeur} value={d.valeur}>
                    {service && d.valeur === '90' ? '90 jours' : d.libelle}
                  </option>
                ))}
              </Select>
            )}
          </Field>
          {duree === 'date' ? (
            <Field
              label="Expire le"
              erreur={jours === null ? 'Choisissez une date entre demain et dans un an.' : undefined}
              hint={`Rappel dans la liste ${service ? '30' : '7'} jours avant.`}
            >
              {(id) => (
                <Input
                  id={id}
                  type="date"
                  value={date}
                  min={versChampDate(expirationPour(1, maintenant))}
                  max={versChampDate(expirationPour(DUREE_MAX_JOURS, maintenant))}
                  onChange={(e) => setDate(e.target.value)}
                />
              )}
            </Field>
          ) : (
            <Field label="Expire le" hint={`Rappel dans la liste ${service ? '30' : '7'} jours avant.`}>
              {(id) => (
                <Input
                  id={id}
                  readOnly
                  value={jours === null ? '—' : formatDate(expirationPour(jours, maintenant).toISOString())}
                  className="bg-surface-2"
                />
              )}
            </Field>
          )}
          {service && (
            <Field label="Limite de débit" hint="Requêtes par minute ; au-delà, 429.">
              {(id) => (
                <Select id={id} value={debit} onChange={(e) => setDebit(e.target.value)}>
                  <option value="">
                    Par défaut{debitGlobal ? ` (${debitGlobal.toLocaleString('fr-FR')} / min)` : ''}
                  </option>
                  {debits.map((d) => (
                    <option key={d} value={String(d)}>
                      {d.toLocaleString('fr-FR')} / min
                    </option>
                  ))}
                </Select>
              )}
            </Field>
          )}
        </div>
        <button type="submit" hidden aria-hidden tabIndex={-1} />
      </form>
    </Dialog>
  );
}

function ChoixRadio({
  nom,
  coche,
  desactive,
  onChoisir,
  libelle,
  description,
}: {
  nom: string;
  coche: boolean;
  desactive?: boolean;
  onChoisir: () => void;
  libelle: ReactNode;
  description: ReactNode;
}) {
  const id = useId();
  return (
    <div className="flex items-start gap-3">
      <input
        id={id}
        type="radio"
        name={nom}
        checked={coche}
        disabled={desactive}
        onChange={onChoisir}
        className="mt-0.5 size-[18px] shrink-0 accent-accent disabled:opacity-60"
      />
      <label htmlFor={id} className={cn('text-sm leading-5 text-fg', desactive && 'text-fg-muted')}>
        {libelle}
        <span className="block text-xs text-fg-subtle">{description}</span>
      </label>
    </div>
  );
}

/**
 * Jeton créé : sa valeur, montrée cette seule fois, à copier ; exemple d'appel. Fermer sans avoir copié demande une
 * confirmation (le jeton serait perdu).
 */
export function JetonCreeDialog({ cree, onFermer }: { cree: JetonCree | null; onFermer: () => void }) {
  const [copie, setCopie] = useState(false);
  const [confirmer, setConfirmer] = useState(false);
  const [echec, setEchec] = useState(false);
  const minuteur = useRef<number | undefined>(undefined);
  useEffect(() => () => window.clearTimeout(minuteur.current), []);
  const [vu, setVu] = useState<JetonCree | null>(null);
  if (cree !== vu) {
    setVu(cree);
    setCopie(false);
    setEchec(false);
  }
  const [affiche, setAffiche] = useState(false);

  async function copier() {
    if (!cree) return;
    if (await copierTexte(cree.secret)) {
      setCopie(true);
      setAffiche(true);
      setEchec(false);
      window.clearTimeout(minuteur.current);
      minuteur.current = window.setTimeout(() => setAffiche(false), 1800);
    } else setEchec(true);
  }

  const fermer = () => (copie ? onFermer() : setConfirmer(true));
  const j = cree?.jeton;
  return (
    <>
      <Dialog
        open={cree !== null}
        onOpenChange={(o) => {
          if (!o) fermer();
        }}
        taille="md"
        titre="Jeton créé"
        description={
          j && (
            <>
              {j.nom} · {j.portees.join(', ')} · expire le {formatDate(j.expireLe)}
            </>
          )
        }
        pied={
          <>
            <span className="mr-auto text-xs text-fg-subtle">Fermer sans avoir copié demande une confirmation.</span>
            <Button variante="primaire" icone={<Check className="size-4" aria-hidden />} onClick={fermer}>
              J’ai copié le jeton
            </Button>
          </>
        }
      >
        {cree && (
          <div className="grid gap-4">
            <Alerte ton="warn" icone={TriangleAlert} titre="Copiez-le maintenant : il ne sera plus jamais affiché.">
              Gestion MEP n’en garde qu’une empreinte. Perdu, il se révoque et se recrée.
            </Alerte>
            <div className="flex min-w-0 items-center gap-2 rounded-lg border border-line bg-code-bg py-1.5 pr-1.5 pl-3">
              <code
                className="min-w-0 flex-1 font-mono text-ui break-all text-code-fg select-all"
                aria-label="Jeton d’accès"
              >
                {cree.secret}
              </code>
              <Button
                taille="sm"
                variante={copie ? 'secondaire' : 'primaire'}
                icone={affiche ? <Check className="size-3.5" aria-hidden /> : <Copy className="size-3.5" aria-hidden />}
                onClick={() => void copier()}
              >
                {affiche ? 'Copié' : 'Copier'}
              </Button>
            </div>
            <span className="sr-only" role="status">
              {affiche ? 'Jeton copié dans le presse-papiers' : ''}
            </span>
            {echec && (
              <p className="text-xs text-danger-fg" role="alert">
                Copie impossible : sélectionnez le jeton et copiez-le à la main.
              </p>
            )}
            {j?.type === 'Service' && (
              <p className="text-sm text-fg-muted">
                Il agit sous le compte technique <code className="font-mono">{j.compteLogin}</code> : ajoutez ce compte
                aux MEP où l’outil doit écrire ou exécuter.
              </p>
            )}
            <div className="grid gap-1.5">
              <CodeBlock titre="Exemple" code={exempleCurl(window.location.origin, cree.jeton.portees)} />
              <p className="text-xs text-fg-subtle">
                Le préfixe <code className="font-mono">gmep_</code> permet aux outils de détection de fuites (GitHub,
                GitLab) de le repérer. Ligne de commande :{' '}
                <code className="font-mono">gestion-mep jetons creer|liste|revoquer</code>.
              </p>
            </div>
          </div>
        )}
      </Dialog>
      <ConfirmDialog
        open={confirmer}
        onOpenChange={setConfirmer}
        titre="Fermer sans copier le jeton ?"
        description="Il ne sera plus jamais affiché. S’il n’a pas été copié, il faudra le révoquer et en créer un autre."
        libelleConfirmer="Fermer quand même"
        variante="danger-doux"
        auPremierPlan
        onConfirmer={() => {
          onFermer();
          return true;
        }}
      />
    </>
  );
}

/** Révocation, immédiate et définitive. */
export function RevoquerJetonDialog({
  jeton,
  onOpenChange,
  onRevoquer,
  maintenant,
}: {
  jeton: JetonApi | null;
  onOpenChange: (v: boolean) => void;
  onRevoquer: (j: JetonApi) => Promise<boolean>;
  maintenant: Date;
}) {
  // Garde le dernier jeton pendant l'animation de fermeture.
  const [dernier, setDernier] = useState<JetonApi | null>(jeton);
  if (jeton && jeton !== dernier) setDernier(jeton);
  const j = jeton ?? dernier;
  return (
    <ConfirmDialog
      open={jeton !== null}
      onOpenChange={onOpenChange}
      taille="md"
      titre={j ? `Révoquer « ${j.nom} » ?` : 'Révoquer ?'}
      libelleConfirmer="Révoquer"
      variante="danger"
      onConfirmer={() => (j ? onRevoquer(j) : false)}
    >
      {j && (
        <div className="grid gap-4 text-sm">
          <p className="text-fg-muted">
            Les appels avec ce jeton recevront aussitôt <code className="font-mono">401</code>. Cette action ne peut pas
            être annulée ; elle est inscrite au journal d’administration.
            {j.type === 'Service' &&
              ' Son compte technique est désactivé s’il n’a plus d’autre jeton actif ; son nom reste dans l’historique des MEP.'}
          </p>
          <dl className="grid grid-cols-[minmax(0,9rem)_minmax(0,1fr)] gap-x-4 gap-y-2">
            {j.type === 'Personnel' && j.compteNom && (
              <>
                <dt className="text-fg-subtle">Compte</dt>
                <dd>{j.compteNom}</dd>
              </>
            )}
            <dt className="text-fg-subtle">Jeton</dt>
            <dd className="font-mono text-xs">{j.prefixe}…</dd>
            <dt className="text-fg-subtle">Dernière utilisation</dt>
            <dd>
              {quandUtilise(j.derniereUtilisationLe, maintenant)}
              {j.derniereAdresseIp && (
                <>
                  , depuis <span className="font-mono">{j.derniereAdresseIp}</span>
                </>
              )}
            </dd>
            <dt className="text-fg-subtle">Portées</dt>
            <dd>
              <PorteesJeton portees={j.portees} />
            </dd>
          </dl>
        </div>
      )}
    </ConfirmDialog>
  );
}
