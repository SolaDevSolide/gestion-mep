import {
  ChevronDown,
  CircleCheck,
  Download,
  FileJson,
  Link2,
  Moon,
  OctagonAlert,
  RotateCcw,
  Save,
  Sun,
  TerminalSquare,
  TriangleAlert,
  Upload,
  Wand2,
} from 'lucide-react';
import { useMemo, useRef, useState } from 'react';
import { api } from '../api';
import { ApercuAppli, ApercuExcel } from '../charte/ApercuCharte';
import { appliquerEmpreinte } from '../charte/appliquer';
import {
  appliquerCorrection,
  CHARTE_DEFAUT,
  controleReussi,
  controles,
  couleurRole,
  couleurSaisie,
  ETAT_DEFAUT,
  ETATS,
  GRIS,
  LIBELLES_GRIS,
  LIBELLES_MENU,
  LONGUEUR_NOM,
  lireFichier,
  memeCharte,
  NOM_DEFAUT,
  ROLES,
  SUIT_PRIMAIRE,
  tertiaireLiee,
  versFichier,
  type Charte,
  type Controle,
  type Mode,
  type Role,
} from '../charte/charte';
import { DEFAUT_SOMBRE } from '../charte/defauts';
import { formatRapport } from '../charte/couleurs';
import { SelecteurCouleur } from '../charte/SelecteurCouleur';
import { compte, formatDateHeure, formatTaille } from '../format';
import { useAsync, useReportDirty } from '../hooks';
import type { EtatCharteServeur, VerificationCharte } from '../types';
import { Alerte } from '../ui/Alerte';
import { ErreurChargement } from '../ui/ErreurChargement';
import { Button } from '../ui/Button';
import { Card, CardHeader } from '../ui/Card';
import { Checkbox } from '../ui/Checkbox';
import { cn } from '../ui/cn';
import { ConfirmDialog, Dialog } from '../ui/Dialog';
import { Input } from '../ui/Input';
import { Segmented } from '../ui/Segmented';
import { EnChargement, Squelette } from '../ui/Squelette';
import { Badge } from '../ui/StatusPill';
import { useToast } from '../ui/Toast';

/** Charte reçue du serveur (format du fichier) ; une réponse illisible donne la charte par défaut. */
const lue = (f: unknown): Charte => lireFichier(f).charte ?? CHARTE_DEFAUT;

function Pastille({
  couleur,
  taille = 'lg',
  className,
}: {
  couleur: string;
  taille?: 'lg' | 'sm';
  className?: string;
}) {
  return (
    <span
      className={cn(
        'block shrink-0 border border-black/10 shadow-sm',
        taille === 'lg' ? 'size-11 rounded-xl' : 'size-4 rounded',
        className,
      )}
      style={{ background: couleur }}
    />
  );
}

const lien = 'font-medium text-accent-text hover:underline';

function LigneRole({
  charte,
  enregistree,
  role,
  libelle,
  usage,
  modifier,
  ouvert,
  setOuvert,
  alertes,
}: {
  charte: Charte;
  enregistree: Charte;
  role: Role;
  libelle: string;
  usage: string;
  modifier: (c: Charte) => void;
  ouvert: string | null;
  setOuvert: (o: string | null) => void;
  alertes: number;
}) {
  const liee = role === 'tertiaire' && tertiaireLiee(charte);
  const clair = couleurRole(charte, role, 'clair');
  const sombre = couleurRole(charte, role, 'sombre');
  const retouche = !liee && charte.sombre[role] !== undefined;
  const palette = ROLES.map((r) => ({ libelle: r.libelle, couleur: couleurRole(charte, r.role, 'clair') }));
  return (
    <div className="flex gap-4 px-5 py-4">
      <SelecteurCouleur
        titre={`${libelle} · thème clair`}
        valeur={clair}
        initiale={couleurSaisie(enregistree, role)}
        charte={palette}
        ouvert={ouvert === role}
        onOuvert={(o) => setOuvert(o ? role : null)}
        onChange={(hex) => modifier({ ...charte, couleurs: { ...charte.couleurs, [role]: hex } })}
        enfant={
          <button
            type="button"
            aria-label={`Choisir la couleur ${libelle.toLowerCase()}`}
            className={cn(
              'relative h-fit rounded-xl outline-none focus-visible:ring-2 focus-visible:ring-accent focus-visible:ring-offset-2',
              liee && 'opacity-60',
            )}
          >
            <Pastille couleur={clair} />
            <span
              className="absolute -right-1 -bottom-1 size-5 rounded-md border-2 border-surface shadow-sm"
              style={{ background: sombre }}
              title={`Thème sombre : ${sombre.toUpperCase()}`}
            />
          </button>
        }
      />
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-2">
          <p className="text-sm font-semibold">{libelle}</p>
          <code className="rounded bg-surface-2 px-1.5 py-0.5 font-mono text-[12px] text-fg-muted uppercase">
            {clair}
          </code>
          {liee ? (
            <Badge>
              <Link2 className="size-3" aria-hidden /> Suit la primaire
            </Badge>
          ) : alertes > 0 ? (
            <Badge ton="warn">
              <TriangleAlert className="size-3" aria-hidden /> {alertes} à revoir
            </Badge>
          ) : (
            <Badge ton="ok">
              <CircleCheck className="size-3" aria-hidden /> Contrastes AA
            </Badge>
          )}
        </div>
        <p className="mt-0.5 text-[13px] text-fg-subtle">{usage}</p>
        <div className="mt-2 flex flex-wrap items-center gap-x-3 gap-y-1.5 text-xs text-fg-muted">
          <span className="inline-flex items-center gap-1.5">
            <Moon className="size-3.5" aria-hidden /> Thème sombre
            <Pastille couleur={sombre} taille="sm" />
            <code className="font-mono uppercase">{sombre}</code>
            <span className="text-fg-subtle">{retouche ? '· retouchée' : '· calculée'}</span>
          </span>
          {!liee && (
            <SelecteurCouleur
              titre={`${libelle} · thème sombre`}
              valeur={sombre}
              initiale={couleurRole(enregistree, role, 'sombre')}
              charte={palette}
              fondTexte={DEFAUT_SOMBRE.surface}
              ouvert={ouvert === `${role}-sombre`}
              onOuvert={(o) => setOuvert(o ? `${role}-sombre` : null)}
              onChange={(hex) => modifier({ ...charte, sombre: { ...charte.sombre, [role]: hex } })}
              cote="bottom"
              enfant={
                <button
                  type="button"
                  className={lien}
                  aria-label={`Retoucher la ${libelle.toLowerCase()} du thème sombre`}
                >
                  Retoucher
                </button>
              }
            />
          )}
          {retouche && (
            <button
              type="button"
              className={lien}
              onClick={() => {
                const s = { ...charte.sombre };
                delete s[role];
                modifier({ ...charte, sombre: s });
              }}
            >
              Revenir au calcul
            </button>
          )}
        </div>
        {role === 'tertiaire' && (
          <Checkbox
            className="mt-2.5"
            checked={liee}
            onChange={(v) => {
              if (v) {
                const s = { ...charte.sombre };
                delete s.tertiaire;
                modifier({ ...charte, couleurs: { ...charte.couleurs, tertiaire: SUIT_PRIMAIRE }, sombre: s });
              } else modifier({ ...charte, couleurs: { ...charte.couleurs, tertiaire: charte.couleurs.primaire } });
            }}
            label="Identique à la primaire"
          />
        )}
      </div>
    </div>
  );
}

function LigneControle({ c, corriger }: { c: Controle; corriger: (c: Controle) => void }) {
  const ok = controleReussi(c);
  return (
    <li className="flex flex-wrap items-center gap-3 px-5 py-2.5">
      <span
        className="grid h-7 w-10 shrink-0 place-content-center rounded-md border border-black/10 text-[13px] font-semibold"
        aria-hidden
        style={
          c.type === 'distinction'
            ? { background: `linear-gradient(90deg, ${c.avant} 50%, ${c.arriere} 50%)` }
            : c.minimum === 3
              ? { background: c.arriere, boxShadow: `inset 0 0 0 3px ${c.avant}` }
              : { background: c.arriere, color: c.avant }
        }
      >
        {c.type === 'contraste' && c.minimum > 3 ? 'Aa' : ''}
      </span>
      <div className="min-w-0 flex-1">
        <p className="text-[13px] font-medium">
          {c.libelle}
          <span className="ml-2 inline-flex items-center gap-1 text-xs font-normal text-fg-subtle">
            {c.mode === 'clair' ? <Sun className="size-3" aria-hidden /> : <Moon className="size-3" aria-hidden />}
            {c.mode === 'clair' ? 'clair' : 'sombre'}
          </span>
        </p>
        <p className="text-xs text-fg-subtle">
          {c.type === 'distinction'
            ? c.message
            : `${formatRapport(c.rapport)} · minimum ${c.minimum === 3 ? '3:1 (élément graphique)' : '4,5:1 (texte)'}`}
        </p>
      </div>
      {ok ? (
        <Badge ton="ok">
          <CircleCheck className="size-3" aria-hidden /> AA
        </Badge>
      ) : c.correction ? (
        <Button
          taille="sm"
          variante="secondaire"
          icone={<Wand2 className="size-3.5" aria-hidden />}
          onClick={() => corriger(c)}
        >
          Corriger
          <span className="ml-1 inline-flex items-center gap-1 font-mono text-[11px] text-fg-subtle uppercase">
            <span className="size-3 rounded-sm border border-black/10" style={{ background: c.correction.couleur }} />
            {c.correction.couleur}
          </span>
        </Button>
      ) : (
        <Badge ton="warn">
          <TriangleAlert className="size-3" aria-hidden /> À vérifier
        </Badge>
      )}
    </li>
  );
}

function Origine({ etat }: { etat: EtatCharteServeur }) {
  const f = etat.fichier;
  return (
    <>
      {etat.origine === 'console' ? (
        <Badge ton="run">
          Modifiée le {formatDateHeure(etat.modifieLe)}
          {etat.modifiePar ? ` par ${etat.modifiePar}` : ''}
        </Badge>
      ) : etat.origine === 'fichier' ? (
        <Badge>Fichier charte.json</Badge>
      ) : (
        <Badge>Valeur par défaut</Badge>
      )}
      <span>
        {etat.origine === 'fichier'
          ? 'Lue dans'
          : f.charte
            ? 'Remplace le fichier'
            : f.present
              ? 'Fichier ignoré (invalide)'
              : 'Fichier facultatif, absent'}
      </span>
      <code className="rounded bg-surface-2 px-1.5 py-0.5 font-mono text-[11px] break-all text-fg-muted">
        {f.chemin}
      </code>
    </>
  );
}

/** Ligne du tableau de comparaison de l'import. */
function Comparaison({
  libelle,
  actuelle,
  importee,
}: {
  libelle: string;
  actuelle: React.ReactNode;
  importee: React.ReactNode;
}) {
  return (
    <tr>
      <td className="py-2 font-medium">{libelle}</td>
      <td className="py-2 text-fg-muted">{actuelle}</td>
      <td className="py-2">{importee}</td>
    </tr>
  );
}

function CouleurTexte({ c, role }: { c: Charte; role: Role }) {
  const couleur = couleurSaisie(c, role);
  return (
    <span className="inline-flex items-center gap-2 font-mono text-[12px] uppercase">
      <Pastille couleur={couleur} taille="sm" />
      {role === 'tertiaire' && tertiaireLiee(c) ? 'primaire' : couleur}
    </span>
  );
}

interface Import {
  nom: string;
  taille: number;
  contenu: string;
  verification: VerificationCharte | null;
  erreur?: string;
}

function DialogueImport({
  ouvert,
  onOuvert,
  actuelle,
  reprendre,
}: {
  ouvert: boolean;
  onOuvert: (o: boolean) => void;
  actuelle: Charte;
  reprendre: (c: Charte) => void;
}) {
  const [fichier, setFichier] = useState<Import | null>(null);
  const [lecture, setLecture] = useState(false);
  const champ = useRef<HTMLInputElement>(null);
  const importee = fichier?.verification?.charte ? lue(fichier.verification.charte) : null;
  const v = fichier?.verification;

  async function lire(f: File) {
    setLecture(true);
    try {
      const contenu = await f.text();
      const verification = await api.verifierCharte(contenu);
      setFichier({ nom: f.name, taille: f.size, contenu, verification });
    } catch (e) {
      setFichier({
        nom: f.name,
        taille: f.size,
        contenu: '',
        verification: null,
        erreur: e instanceof Error ? e.message : 'Lecture impossible.',
      });
    } finally {
      setLecture(false);
    }
  }

  return (
    <Dialog
      open={ouvert}
      onOpenChange={(o) => {
        onOuvert(o);
        if (!o) setFichier(null);
      }}
      titre="Importer une charte"
      description="Fichier charte.json exporté d’une autre installation ou préparé à la main. Rien n’est enregistré avant « Enregistrer »."
      taille="md"
      pied={
        <>
          <Button variante="fantome" onClick={() => onOuvert(false)}>
            Annuler
          </Button>
          <Button
            variante="primaire"
            disabled={!importee}
            onClick={() => {
              if (!importee) return;
              reprendre(importee);
              onOuvert(false);
              setFichier(null);
            }}
          >
            Reprendre ces couleurs
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <input
          ref={champ}
          type="file"
          accept=".json,application/json"
          className="sr-only"
          aria-label="Fichier charte.json"
          onChange={(e) => {
            const f = e.target.files?.[0];
            e.target.value = '';
            if (f) void lire(f);
          }}
        />
        <div className="flex items-center gap-3 rounded-lg border border-line bg-surface-2/60 px-3.5 py-3">
          <FileJson className="size-5 shrink-0 text-fg-subtle" aria-hidden />
          <div className="min-w-0 flex-1">
            {fichier ? (
              <>
                <p className="truncate text-sm font-medium">{fichier.nom}</p>
                <p className="text-xs text-fg-subtle">
                  {importee ? `« ${importee.nom} » · version 1 · ` : ''}
                  {formatTaille(fichier.taille)}
                </p>
              </>
            ) : (
              <p className="text-sm text-fg-muted">Choisissez un fichier .json.</p>
            )}
          </div>
          {v?.valide && (
            <Badge ton="ok">
              <CircleCheck className="size-3" aria-hidden /> Fichier valide
            </Badge>
          )}
          {fichier && !v?.valide && (
            <Badge ton="danger">
              <OctagonAlert className="size-3" aria-hidden /> Fichier refusé
            </Badge>
          )}
          <Button
            taille="sm"
            chargement={lecture}
            icone={<Upload className="size-4" aria-hidden />}
            onClick={() => champ.current?.click()}
          >
            {fichier ? 'Autre fichier…' : 'Choisir…'}
          </Button>
        </div>
        {fichier?.erreur && (
          <Alerte ton="danger" icone={OctagonAlert} role="alert">
            {fichier.erreur}
          </Alerte>
        )}
        {v && v.erreurs.length > 0 && (
          <Alerte ton="danger" icone={OctagonAlert} titre="Le fichier ne peut pas être repris" role="alert">
            {v.erreurs.map((e) => `• ${e.message}`).join('\n')}
          </Alerte>
        )}
        {importee && (
          <table className="w-full text-sm">
            <thead>
              <tr className="text-left text-xs text-fg-subtle">
                <th className="pb-1.5 font-medium">Rôle</th>
                <th className="pb-1.5 font-medium">Actuelle</th>
                <th className="pb-1.5 font-medium">Importée</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-line">
              {ROLES.map((r) => (
                <Comparaison
                  key={r.role}
                  libelle={r.libelle}
                  actuelle={<CouleurTexte c={actuelle} role={r.role} />}
                  importee={<CouleurTexte c={importee} role={r.role} />}
                />
              ))}
              <Comparaison
                libelle="Menu latéral"
                actuelle={LIBELLES_MENU[actuelle.menuLateral]}
                importee={LIBELLES_MENU[importee.menuLateral]}
              />
              <Comparaison
                libelle="Gris"
                actuelle={LIBELLES_GRIS[actuelle.gris]}
                importee={LIBELLES_GRIS[importee.gris]}
              />
              <Comparaison
                libelle="Couleurs d’état"
                actuelle={
                  Object.keys(actuelle.etats).length
                    ? compte(Object.keys(actuelle.etats).length, 'personnalisée')
                    : 'Standard'
                }
                importee={
                  Object.keys(importee.etats).length
                    ? compte(Object.keys(importee.etats).length, 'personnalisée')
                    : 'Standard'
                }
              />
            </tbody>
          </table>
        )}
        {v && v.valide && v.avertissements.length > 0 && (
          <Alerte ton="warn" icone={TriangleAlert}>
            {v.avertissements.map((a) => `• ${a.message}`).join('\n')}
            {'\n'}Les autres valeurs sont reprises ; rien n’est bloqué.
          </Alerte>
        )}
        {fichier && fichier.contenu && (
          <details className="rounded-lg border border-line">
            <summary className="cursor-pointer px-3.5 py-2 text-xs font-medium text-fg-muted">
              Contenu du fichier
            </summary>
            <pre className="max-h-56 overflow-auto border-t border-line bg-code-bg px-3.5 py-2 font-mono text-[11px] text-code-fg">
              {fichier.contenu.length > 20000 ? `${fichier.contenu.slice(0, 20000)}\n…` : fichier.contenu}
            </pre>
          </details>
        )}
      </div>
    </Dialog>
  );
}

/** Télécharge la charte affichée au format charte.json. */
function exporter(c: Charte) {
  const schema = `${window.location.origin}/api/charte/schema.json`;
  const texte = `${JSON.stringify(versFichier(c, schema), null, 2)}\n`;
  const url = URL.createObjectURL(new Blob([texte], { type: 'application/json' }));
  const a = document.createElement('a');
  a.href = url;
  a.download = 'charte.json';
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

/**
 * Console › Charte visuelle : couleurs de l'application pour tous (primaire, secondaire, tertiaire, gris, menu latéral,
 * couleurs d'état), thème sombre calculé et retouchable, lisibilité contrôlée, aperçu de l'application et des exports
 * Excel. Enregistrée en base, elle remplace le fichier charte.json ; « Charte par défaut » efface la charte de la console.
 */
export default function CharteTab() {
  const toast = useToast();
  const { donnees: serveur, erreur, echec, chargement, recharger, setDonnees } = useAsync(() => api.charte(), []);
  const enregistree = useMemo(() => (serveur ? lue(serveur.charte) : null), [serveur]);
  const [brouillon, setBrouillon] = useState<Charte | null>(null);
  const charte = brouillon ?? enregistree ?? CHARTE_DEFAUT;
  const modifie = brouillon !== null && enregistree !== null && !memeCharte(brouillon, enregistree);
  useReportDirty(modifie, 'La charte visuelle');

  const [ouvert, setOuvert] = useState<string | null>(null);
  const [mode, setMode] = useState<Mode>('clair');
  const [vue, setVue] = useState<'appli' | 'excel'>('appli');
  const [etatsOuverts, setEtatsOuverts] = useState(false);
  const [importer, setImporter] = useState(false);
  const [retablir, setRetablir] = useState(false);
  const [tousControles, setTousControles] = useState(false);
  const [envoi, setEnvoi] = useState(false);

  const ctrl = useMemo(() => controles(charte), [charte]);
  const aRevoir = ctrl.filter((c) => !controleReussi(c));
  const reussis = ctrl.filter((c) => controleReussi(c));
  const parRole = (r: string) => aRevoir.filter((c) => c.role === r).length;
  const modifier = (c: Charte) => setBrouillon(c);

  if (!serveur || !enregistree)
    return erreur ? (
      <ErreurChargement
        erreur={echec}
        titre="Impossible de charger la charte visuelle"
        onReessayer={recharger}
        enCours={chargement}
      />
    ) : (
      <EnChargement libelle="Chargement de la charte visuelle…">
        <Squelette className="h-28 w-full rounded-xl" />
        <Squelette className="mt-6 h-96 w-full rounded-xl" />
      </EnChargement>
    );

  const appliquer = (r: EtatCharteServeur, message: string) => {
    setDonnees(r);
    setBrouillon(null);
    appliquerEmpreinte(r.empreinte);
    toast.succes(message);
  };

  async function enregistrer() {
    setEnvoi(true);
    try {
      appliquer(
        await api.enregistrerCharte(versFichier(charte)),
        'Charte enregistrée : elle s’applique à tous les écrans ouverts, sans recharger.',
      );
    } catch (e) {
      toast.erreur(e instanceof Error ? e.message : 'Enregistrement impossible.');
    } finally {
      setEnvoi(false);
    }
  }

  const fichierValide = serveur.fichier.charte ? lue(serveur.fichier.charte) : null;
  const nomEtats = Object.keys(charte.etats).length;

  return (
    <div className="space-y-6 pb-4">
      {serveur.fichier.erreur && (
        <Alerte ton="danger" icone={OctagonAlert} titre="Fichier charte.json invalide, ignoré" role="alert">
          {`${serveur.fichier.chemin} : ${serveur.fichier.erreur}\n`}
          {serveur.origine === 'console'
            ? 'La charte enregistrée depuis la console s’applique ; le fichier ne servira qu’une fois corrigé, si elle est effacée.'
            : 'La charte par défaut s’applique tant que le fichier n’est pas corrigé (relu toutes les 10 secondes).'}
        </Alerte>
      )}
      {!serveur.fichier.erreur && serveur.fichier.avertissements.length > 0 && (
        <Alerte ton="warn" icone={TriangleAlert} titre="Fichier charte.json : valeurs ignorées">
          {serveur.fichier.avertissements.map((a) => `• ${a.message}`).join('\n')}
        </Alerte>
      )}
      <Card>
        <CardHeader
          titre="Charte visuelle"
          description="Couleurs de l’application pour tous les utilisateurs, en thème clair et en thème sombre. Les exports Excel reprennent la primaire et la secondaire."
          action={
            <div className="flex flex-wrap gap-2">
              <Button taille="sm" icone={<Upload className="size-4" aria-hidden />} onClick={() => setImporter(true)}>
                Importer…
              </Button>
              <Button
                taille="sm"
                icone={<Download className="size-4" aria-hidden />}
                onClick={() => exporter(charte)}
                title={modifie ? 'Exporte la charte affichée, modifications comprises' : 'Exporte la charte en vigueur'}
              >
                Exporter
              </Button>
              <Button
                taille="sm"
                variante="fantome"
                icone={<RotateCcw className="size-4" aria-hidden />}
                disabled={serveur.origine === 'defaut' && memeCharte(charte, CHARTE_DEFAUT)}
                onClick={() => {
                  if (serveur.origine === 'console') setRetablir(true);
                  else setBrouillon(CHARTE_DEFAUT);
                }}
              >
                Charte par défaut
              </Button>
            </div>
          }
        />
        <div className="flex flex-wrap items-center gap-2 px-5 py-3 text-xs text-fg-subtle">
          <Origine etat={serveur} />
          <span className="ml-auto inline-flex items-center gap-1.5">
            <TerminalSquare className="size-3.5" aria-hidden />
            <code className="font-mono text-[11px] text-fg-muted">{serveur.commande}</code>
          </span>
        </div>
      </Card>

      <div className="grid items-start gap-6 lg:grid-cols-[minmax(0,1fr)_minmax(0,540px)]">
        <div className="space-y-6">
          <Card>
            <CardHeader
              titre="Couleurs"
              description="Un clic sur une pastille ouvre le sélecteur. Le thème sombre est calculé, retouchable."
            />
            <div className="divide-y divide-line">
              {ROLES.map((r) => (
                <LigneRole
                  key={r.role}
                  charte={charte}
                  enregistree={enregistree}
                  {...r}
                  modifier={modifier}
                  ouvert={ouvert}
                  setOuvert={setOuvert}
                  alertes={parRole(r.role)}
                />
              ))}
              <div className="grid gap-4 px-5 py-4 sm:grid-cols-2">
                <div>
                  <p className="mb-1.5 text-sm font-semibold">Gris</p>
                  <Segmented
                    label="Gris"
                    valeur={charte.gris}
                    onChange={(g) => modifier({ ...charte, gris: g })}
                    options={GRIS.map((g) => ({ valeur: g, libelle: LIBELLES_GRIS[g] }))}
                  />
                  <p className="mt-1 text-xs text-fg-subtle">
                    Fonds, bordures et textes discrets. Teintés : nuance de la primaire.
                  </p>
                </div>
                <div>
                  <p className="mb-1.5 text-sm font-semibold">Menu latéral</p>
                  <Segmented
                    label="Menu latéral"
                    valeur={charte.menuLateral}
                    onChange={(m) => modifier({ ...charte, menuLateral: m })}
                    options={(['pages', 'secondaire'] as const).map((m) => ({ valeur: m, libelle: LIBELLES_MENU[m] }))}
                  />
                  <p className="mt-1 text-xs text-fg-subtle">Fond du menu de gauche.</p>
                </div>
              </div>
              <div>
                <button
                  type="button"
                  onClick={() => setEtatsOuverts(!etatsOuverts)}
                  className="flex w-full items-center gap-3 px-5 py-3.5 text-left hover:bg-surface-2/60"
                  aria-expanded={etatsOuverts}
                >
                  <span className="flex -space-x-1">
                    {ETATS.map((e) => (
                      <Pastille
                        key={e.etat}
                        couleur={charte.etats[e.etat] ?? ETAT_DEFAUT[e.etat]}
                        taille="sm"
                        className="rounded-full ring-2 ring-surface"
                      />
                    ))}
                  </span>
                  <span className="min-w-0 flex-1">
                    <span className="block text-sm font-semibold">Couleurs d’état</span>
                    <span className="block text-xs text-fg-subtle">
                      Réussie, en cours, échec, résolution. À changer avec prudence : elles portent le sens du déroulé.
                    </span>
                  </span>
                  <Badge>{nomEtats ? compte(nomEtats, 'personnalisée') : 'Standard'}</Badge>
                  <ChevronDown
                    className={cn('size-4 text-fg-subtle transition-transform', etatsOuverts && 'rotate-180')}
                    aria-hidden
                  />
                </button>
                {etatsOuverts && (
                  <div className="grid gap-2 px-5 pb-4 sm:grid-cols-2">
                    {ETATS.map((e) => {
                      const v = charte.etats[e.etat] ?? ETAT_DEFAUT[e.etat];
                      const perso = charte.etats[e.etat] !== undefined;
                      const alertes = parRole(e.etat);
                      return (
                        <div key={e.etat} className="flex items-center gap-3 rounded-lg border border-line px-3 py-2.5">
                          <SelecteurCouleur
                            titre={e.libelle}
                            valeur={v}
                            initiale={enregistree.etats[e.etat] ?? ETAT_DEFAUT[e.etat]}
                            ouvert={ouvert === e.etat}
                            onOuvert={(o) => setOuvert(o ? e.etat : null)}
                            onChange={(hex) => modifier({ ...charte, etats: { ...charte.etats, [e.etat]: hex } })}
                            enfant={
                              <button type="button" aria-label={`Couleur ${e.libelle}`} className="rounded-lg">
                                <Pastille couleur={v} className="size-8 rounded-lg" />
                              </button>
                            }
                          />
                          <div className="min-w-0 flex-1">
                            <p className="text-[13px] font-medium">
                              {e.libelle}{' '}
                              <code className="ml-1 font-mono text-[11px] text-fg-subtle uppercase">{v}</code>
                              {alertes > 0 && (
                                <TriangleAlert className="ml-1 inline size-3.5 text-warn-fg" aria-label="À revoir" />
                              )}
                            </p>
                            <p className="text-xs text-fg-subtle">{e.usage}</p>
                          </div>
                          {perso && (
                            <button
                              type="button"
                              className={cn('text-xs', lien)}
                              onClick={() => {
                                const s = { ...charte.etats };
                                delete s[e.etat];
                                modifier({ ...charte, etats: s });
                              }}
                            >
                              Rétablir
                            </button>
                          )}
                        </div>
                      );
                    })}
                  </div>
                )}
              </div>
              <div className="flex flex-wrap items-center gap-x-4 gap-y-1.5 px-5 py-3.5">
                <label htmlFor="charte-nom" className="text-sm font-semibold">
                  Nom de la charte
                </label>
                <Input
                  id="charte-nom"
                  className="h-8 max-w-xs flex-1"
                  value={charte.nom}
                  maxLength={LONGUEUR_NOM}
                  placeholder={NOM_DEFAUT}
                  onChange={(e) => modifier({ ...charte, nom: e.target.value })}
                />
                <p className="basis-full text-xs text-fg-subtle">Repris dans le fichier exporté et le journal.</p>
              </div>
            </div>
          </Card>

          <Card>
            <CardHeader
              titre="Lisibilité (WCAG AA)"
              description="Contrôlée à chaque changement, dans les deux thèmes. « Corriger » garde la teinte et ajuste la luminosité. Rien n’est bloqué."
              action={
                aRevoir.length ? (
                  <Badge ton="warn">
                    <TriangleAlert className="size-3" aria-hidden /> {aRevoir.length} à revoir
                  </Badge>
                ) : (
                  <Badge ton="ok">
                    <CircleCheck className="size-3" aria-hidden /> Tout est lisible
                  </Badge>
                )
              }
            />
            <ul className="divide-y divide-line">
              {aRevoir.map((c) => (
                <LigneControle key={c.id} c={c} corriger={(k) => modifier(appliquerCorrection(charte, k))} />
              ))}
              {(tousControles || aRevoir.length === 0 ? reussis : reussis.slice(0, 2)).map((c) => (
                <LigneControle key={c.id} c={c} corriger={(k) => modifier(appliquerCorrection(charte, k))} />
              ))}
            </ul>
            {!tousControles && aRevoir.length > 0 && reussis.length > 2 && (
              <button
                type="button"
                onClick={() => setTousControles(true)}
                className="w-full rounded-b-xl border-t border-line px-5 py-2.5 text-left text-xs font-medium text-accent-text hover:bg-surface-2/60"
              >
                Voir les {reussis.length - 2} autres contrôles réussis
              </button>
            )}
          </Card>
        </div>

        <Card className="lg:sticky lg:top-6">
          <CardHeader
            titre="Aperçu"
            action={
              <div className="flex gap-2">
                <Segmented
                  label="Aperçu"
                  valeur={vue}
                  onChange={setVue}
                  options={[
                    { valeur: 'appli', libelle: 'Application' },
                    { valeur: 'excel', libelle: 'Excel' },
                  ]}
                />
                {vue === 'appli' && (
                  <Segmented
                    label="Thème de l’aperçu"
                    valeur={mode}
                    onChange={setMode}
                    options={[
                      { valeur: 'clair', libelle: <Sun className="size-3.5" aria-hidden />, titre: 'Thème clair' },
                      { valeur: 'sombre', libelle: <Moon className="size-3.5" aria-hidden />, titre: 'Thème sombre' },
                    ]}
                  />
                )}
              </div>
            }
          />
          <div className="p-4">
            {vue === 'appli' ? <ApercuAppli charte={charte} mode={mode} /> : <ApercuExcel charte={charte} />}
            <p className="mt-2.5 text-xs text-fg-subtle">
              {vue === 'appli'
                ? 'L’aperçu suit chaque changement. L’application ne change pour personne avant « Enregistrer ».'
                : 'Classeurs exportés : titre en secondaire, en-têtes de tableau et liens en primaire.'}
            </p>
          </div>
        </Card>
      </div>

      {modifie && (
        <div
          role="status"
          className="sticky bottom-4 z-20 flex flex-wrap items-center gap-3 rounded-xl border border-line bg-surface/95 px-4 py-3 shadow-lg backdrop-blur"
        >
          <span className="size-2 rounded-full bg-warn-solid" aria-hidden />
          <p className="min-w-0 flex-1 text-sm">
            <span className="font-medium">Modifications non enregistrées.</span>{' '}
            <span className="text-fg-subtle">
              À l’enregistrement, la charte s’applique à tous les écrans ouverts, sans recharger.
            </span>
          </p>
          <Button variante="fantome" onClick={() => setBrouillon(null)} disabled={envoi}>
            Annuler
          </Button>
          <Button
            variante="primaire"
            icone={<Save className="size-4" aria-hidden />}
            chargement={envoi}
            onClick={() => void enregistrer()}
          >
            Enregistrer
          </Button>
        </div>
      )}

      <DialogueImport
        ouvert={importer}
        onOuvert={setImporter}
        actuelle={charte}
        reprendre={(c) => {
          setBrouillon(c);
          toast.succes(`Couleurs de « ${c.nom} » reprises : vérifiez l’aperçu, puis enregistrez.`);
        }}
      />

      <ConfirmDialog
        open={retablir}
        onOpenChange={setRetablir}
        titre="Revenir à la charte par défaut ?"
        description={
          fichierValide
            ? `La charte enregistrée depuis la console est effacée : celle du fichier ${serveur.fichier.chemin} (« ${fichierValide.nom} ») s’applique de nouveau, pour tous.`
            : 'La charte enregistrée depuis la console est effacée : les couleurs par défaut s’appliquent de nouveau, pour tous.'
        }
        libelleConfirmer={fichierValide ? 'Revenir au fichier' : 'Revenir à la charte par défaut'}
        variante="danger"
        onConfirmer={async () => {
          try {
            appliquer(
              await api.retablirCharte(),
              fichierValide ? 'Charte du fichier rétablie.' : 'Charte par défaut rétablie.',
            );
            return true;
          } catch (e) {
            toast.erreur(e instanceof Error ? e.message : 'Opération impossible.');
            return false;
          }
        }}
      />
    </div>
  );
}
