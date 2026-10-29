import { ArrowLeft, OctagonAlert, Power, Save, TriangleAlert } from 'lucide-react';
import { useEffect, useMemo, useRef, useState } from 'react';
import { Link, Navigate, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { api, ApiError } from '../../api';
import { useAuth } from '../../auth';
import { useAsync, useReportDirty } from '../../hooks';
import {
  catalogueDepuis,
  DONNEES_JSON,
  rendreJson,
  rendreTexte,
  rendreUrl,
  type ValeurGabarit,
} from '../../textes/gabarits';
import type { CatalogueConnexions, ConnexionApi } from '../../types';
import { Alerte } from '../../ui/Alerte';
import { Button } from '../../ui/Button';
import { Card } from '../../ui/Card';
import { Checkbox } from '../../ui/Checkbox';
import { ErreurChargement } from '../../ui/ErreurChargement';
import { Field, Input } from '../../ui/Input';
import { EnChargement, Squelette, SqueletteFormulaire, SqueletteLignes } from '../../ui/Squelette';
import { StatusPill } from '../../ui/StatusPill';
import { useToast } from '../../ui/Toast';
import { ApercuConnexion } from './ApercuConnexion';
import {
  aDesSecrets,
  copie,
  depuisConnexion,
  depuisModele,
  hote,
  origineSecrets,
  versSaisie,
  type Brouillon,
} from './brouillon';
import { CarteMessage } from './CarteMessage';
import { CarteQuand } from './CarteQuand';
import { CarteRequete } from './CarteRequete';
import { TitreCarte } from './ChampsConnexion';
import { LIBELLE_ETAT_CONNEXION, TON_ETAT_CONNEXION } from './connexions';

const RETOUR = '/admin?onglet=api';

/** Valeurs d'exemple (une par variable) : la vérification locale du message ne dépend d'aucune MEP. */
function valeursExemple(catalogue: CatalogueConnexions): Map<string, ValeurGabarit> {
  return new Map(
    catalogue.variables.map((v) => [
      v.cle,
      v.cle === DONNEES_JSON ? { texte: '{}' } : v.liste ? { liste: ['exemple'] } : { texte: 'exemple' },
    ]),
  );
}

/**
 * Éditeur d'une connexion à une API externe (`/admin/api/:id`, `nouvelle` pour une nouvelle connexion, `?depuis=` pour
 * dupliquer, `?modele=` et `?nom=` depuis « Nouvelle connexion ») : quatre cartes numérotées à gauche (connexion,
 * requête, message, quand appeler), aperçu exact et « Essayer » à droite. Administrateurs seulement.
 */
export default function ConnexionEditeurPage() {
  const { id = 'nouvelle' } = useParams();
  const [params] = useSearchParams();
  const nouvelle = id === 'nouvelle';
  const depuis = params.get('depuis');
  const navigate = useNavigate();
  const toast = useToast();
  const { utilisateur } = useAuth();

  const existant = useAsync(() => (nouvelle ? Promise.resolve(null) : api.connexionApi(id)), [id]);
  const source = useAsync(() => (nouvelle && depuis ? api.connexionApi(depuis) : Promise.resolve(null)), [depuis]);
  const catalogue = useAsync(() => api.catalogueConnexions(), []);
  const equipes = useAsync(() => api.equipes(), []);
  const systemes = useAsync(() => api.catalogueSystemes(), []);
  const meps = useAsync(() => api.meps(false), []);

  const [connexion, setConnexion] = useState<ConnexionApi | null>(null);
  const [b, setB] = useState<Brouillon | null>(null);
  const [initial, setInitial] = useState<Brouillon | null>(null);
  const [erreur, setErreur] = useState('');
  const [erreurs, setErreurs] = useState<string[]>([]);
  const [conflit, setConflit] = useState(false);
  const [envoi, setEnvoi] = useState(false);
  const champs = useRef(new Map<string, HTMLTextAreaElement>());
  const dernierChamp = useRef('corps');
  const boutonEssayer = useRef<HTMLButtonElement>(null);

  // Brouillon initial : la connexion, sa copie (« Dupliquer ») ou le modèle de départ choisi.
  useEffect(() => {
    if (b || !catalogue.donnees) return;
    let v: Brouillon | null = null;
    if (!nouvelle && existant.donnees) {
      setConnexion(existant.donnees);
      v = depuisConnexion(existant.donnees);
    } else if (nouvelle && depuis && source.donnees) v = copie(source.donnees);
    else if (nouvelle && !depuis) {
      const modele = catalogue.donnees.modeles.find((m) => m.modele === params.get('modele'))?.modele ?? 'Libre';
      v = depuisModele(catalogue.donnees, modele, params.get('nom') ?? '');
    }
    if (v) {
      setB(v);
      setInitial(v);
    }
  }, [b, nouvelle, depuis, existant.donnees, source.donnees, catalogue.donnees, params]);

  const pret = b !== null && catalogue.donnees !== null && equipes.donnees !== null && systemes.donnees !== null;

  // « Essayer » depuis la liste : le bouton est amené à l'écran.
  useEffect(() => {
    if (pret && params.get('essayer'))
      requestAnimationFrame(() => boutonEssayer.current?.scrollIntoView?.({ block: 'center' }));
  }, [pret, params]);

  const dirty = b !== null && initial !== null && JSON.stringify(b) !== JSON.stringify(initial);
  useReportDirty(dirty && !envoi, 'Connexion à une API');

  const cat = useMemo(() => catalogueDepuis(catalogue.donnees?.variables ?? []), [catalogue.donnees]);
  const exemple = useMemo(
    () => (catalogue.donnees ? valeursExemple(catalogue.donnees) : new Map<string, ValeurGabarit>()),
    [catalogue.donnees],
  );

  if (!utilisateur?.estAdmin) return <Navigate to="/" replace />;

  const chargementErreur = existant.echec ?? source.echec ?? catalogue.echec ?? equipes.echec ?? systemes.echec;
  if (chargementErreur)
    return (
      <main className="mx-auto w-full max-w-[1400px] px-6 py-8">
        <ErreurChargement
          variante="page"
          erreur={chargementErreur}
          titre="Impossible d’ouvrir la connexion"
          introuvable="Cette connexion n’existe plus ou a été supprimée."
          retour={{ libelle: 'Retour aux API externes', vers: RETOUR }}
          onReessayer={() => {
            existant.recharger();
            source.recharger();
            catalogue.recharger();
            equipes.recharger();
            systemes.recharger();
          }}
        />
      </main>
    );

  const variables = catalogue.donnees?.variables ?? [];
  const maj = (p: Partial<Brouillon>) => setB((x) => (x ? { ...x, ...p } : x));

  // Vérification locale (valeurs d'exemple) : erreurs et variables inconnues bloquent l'enregistrement et l'essai.
  const urlRendu = b && !b.urlSecrete ? rendreUrl(b.url, exemple, cat) : null;
  const corpsRendu =
    b?.format === 'Json'
      ? rendreJson(b.corps, exemple, cat)
      : b?.format === 'Texte'
        ? rendreTexte(b.corps, exemple, cat)
        : null;
  const inconnues = b
    ? [
        ...new Set([
          ...(urlRendu?.inconnues ?? []),
          ...(corpsRendu?.inconnues ?? []),
          ...b.entetes.filter((e) => !e.secret).flatMap((e) => rendreTexte(e.valeur, exemple, cat).inconnues),
          ...(b.format === 'Formulaire' ? b.champs.flatMap((c) => rendreTexte(c.valeur, exemple, cat).inconnues) : []),
        ]),
      ]
    : [];
  const bloque = Boolean(corpsRendu?.erreurs.length) || inconnues.length > 0;

  // Secrets liés au domaine : un changement d'adresse les efface à l'enregistrement.
  const ancienneOrigine = connexion ? origineSecrets(depuisConnexion(connexion), connexion.url) : '';
  const domaineChange =
    connexion !== null &&
    b !== null &&
    aDesSecrets(connexion) &&
    ancienneOrigine !== origineSecrets(b, connexion.url) &&
    origineSecrets(b, connexion.url).trim() !== '';
  const ressaisir = (quoi: string) =>
    domaineChange
      ? `L’adresse ne mène plus à ${hote(ancienneOrigine.split(' ')[0])} : saisissez ${quoi} de la nouvelle adresse, l’ancien sera effacé à l’enregistrement.`
      : null;

  function surChamp(cle: string, el: HTMLTextAreaElement | null) {
    if (el) champs.current.set(cle, el);
    else dernierChamp.current = cle;
  }

  function lire(cle: string, x: Brouillon): string {
    if (cle === 'url') return x.url;
    if (cle.startsWith('entete:')) return x.entetes[Number(cle.slice(7))]?.valeur ?? '';
    if (cle.startsWith('champ:')) return x.champs[Number(cle.slice(6))]?.valeur ?? '';
    return x.corps;
  }

  function ecrire(cle: string, v: string): Partial<Brouillon> {
    if (!b) return {};
    if (cle === 'url') return { url: v };
    if (cle.startsWith('entete:')) {
      const i = Number(cle.slice(7));
      return { entetes: b.entetes.map((e, j) => (j === i ? { ...e, valeur: v } : e)) };
    }
    if (cle.startsWith('champ:')) {
      const i = Number(cle.slice(6));
      return { champs: b.champs.map((c, j) => (j === i ? { ...c, valeur: v } : c)) };
    }
    return { corps: v };
  }

  function inserer(variable: string) {
    if (!b) return;
    const cle = champs.current.has(dernierChamp.current) ? dernierChamp.current : 'corps';
    const el = champs.current.get(cle);
    const actuel = lire(cle, b);
    const jeton = `{{${variable}}}`;
    const debut = el?.selectionStart ?? actuel.length;
    const fin = el?.selectionEnd ?? actuel.length;
    maj(ecrire(cle, actuel.slice(0, debut) + jeton + actuel.slice(fin)));
    requestAnimationFrame(() => {
      el?.focus();
      el?.setSelectionRange(debut + jeton.length, debut + jeton.length);
    });
  }

  async function enregistrer() {
    if (!b) return;
    setErreur('');
    setErreurs([]);
    if (!b.nom.trim()) {
      setErreur('Le nom de la connexion est obligatoire.');
      return;
    }
    if (bloque) {
      setErreur(
        inconnues.length > 0
          ? `Variable inconnue : ${inconnues.map((c) => `{{${c}}}`).join(', ')}. Corrigez-la avant d’enregistrer.`
          : 'Le message est invalide : corrigez-le avant d’enregistrer (carte 3).',
      );
      return;
    }
    if (b.envoiManuel && !b.libelleEnvoiManuel.trim()) {
      setErreur('Le libellé de l’envoi depuis une MEP est obligatoire.');
      return;
    }
    setEnvoi(true);
    try {
      if (connexion) {
        const c = await api.modifierConnexionApi(connexion.id, versSaisie(b, connexion.version));
        const v = depuisConnexion(c);
        setConnexion(c);
        setB(v);
        setInitial(v);
        toast.succes('Connexion enregistrée.');
      } else {
        const c = await api.creerConnexionApi(versSaisie(b));
        setInitial(b);
        toast.succes(c.active ? 'Connexion créée et active.' : 'Connexion créée (désactivée).');
        void navigate(`/admin/api/${c.id}`, { replace: true });
        const v = depuisConnexion(c);
        setConnexion(c);
        setB(v);
        setInitial(v);
      }
    } catch (e) {
      if (e instanceof ApiError && e.status === 409) setConflit(true);
      else {
        setErreur(e instanceof Error ? e.message : 'Enregistrement impossible.');
        setErreurs(e instanceof ApiError ? e.erreurs : []);
      }
    } finally {
      setEnvoi(false);
    }
  }

  async function changerEtat() {
    if (!connexion) return;
    try {
      const c = await api.activerConnexionApi(connexion.id, !connexion.active);
      setConnexion(c);
      toast.succes(c.active ? 'Connexion activée.' : 'Connexion désactivée : plus aucun envoi.');
    } catch (e) {
      toast.erreur(e instanceof Error ? e.message : 'Action impossible.');
    }
  }

  async function recharger(garder: boolean) {
    try {
      const c = await api.connexionApi(id);
      setConnexion(c);
      if (!garder) {
        const v = depuisConnexion(c);
        setB(v);
        setInitial(v);
      }
      setConflit(false);
    } catch (e) {
      toast.erreur(e instanceof Error ? e.message : 'Rechargement impossible.');
    }
  }

  const titre = nouvelle
    ? depuis
      ? 'Dupliquer une connexion'
      : 'Nouvelle connexion'
    : `Modifier « ${connexion?.nom ?? existant.donnees?.nom ?? ''} »`;
  const corpsModele = catalogue.donnees?.modeles.find((m) => m.modele === b?.modele)?.corps || null;

  return (
    <main className="mx-auto w-full max-w-[1400px] px-6 py-8">
      <nav aria-label="Fil d’Ariane" className="mb-3">
        <Link
          to={RETOUR}
          className="inline-flex items-center gap-1.5 text-sm font-semibold text-accent-text hover:underline"
        >
          <ArrowLeft className="size-4" aria-hidden />
          API externes
        </Link>
      </nav>
      <h1 className="flex flex-wrap items-center gap-3 text-2xl font-semibold tracking-tight">
        {titre}
        {connexion && (
          <StatusPill ton={TON_ETAT_CONNEXION[connexion.etat ?? 'Active']}>
            {LIBELLE_ETAT_CONNEXION[connexion.etat ?? 'Active']}
          </StatusPill>
        )}
      </h1>

      {!pret && (
        <EnChargement
          libelle="Chargement de la connexion…"
          className="mt-6 grid items-start gap-5 lg:grid-cols-[minmax(0,1.1fr)_minmax(0,0.9fr)]"
        >
          <Card className="p-5">
            <SqueletteFormulaire champs={5} />
            <Squelette className="mt-4 h-72 w-full" />
          </Card>
          <Card className="p-5">
            <Squelette className="h-5 w-40" />
            <SqueletteLignes lignes={12} className="mt-4" />
          </Card>
        </EnChargement>
      )}

      {pret && b && catalogue.donnees && (
        <div className="apparition mt-6 grid items-start gap-5 lg:grid-cols-[minmax(0,1.1fr)_minmax(0,0.9fr)]">
          <div className="grid min-w-0 gap-5">
            {conflit && (
              <Alerte
                ton="warn"
                icone={TriangleAlert}
                role="alert"
                titre="Modifiée par un autre administrateur"
                action={
                  <span className="flex flex-wrap gap-2">
                    <Button taille="sm" action={() => recharger(false)}>
                      Recharger
                    </Button>
                    <Button taille="sm" variante="fantome" action={() => recharger(true)}>
                      Garder mes modifications
                    </Button>
                  </span>
                }
              >
                La connexion a changé depuis votre lecture. « Garder mes modifications » puis Enregistrer écrasera ce
                changement.
              </Alerte>
            )}
            <Card className="grid gap-4 p-5">
              <TitreCarte numero={1} titre="Connexion" />
              <div className="grid gap-4 md:grid-cols-[minmax(0,1fr)_auto] md:items-end">
                <Field label="Nom *">
                  {(i) => <Input id={i} value={b.nom} maxLength={100} onChange={(e) => maj({ nom: e.target.value })} />}
                </Field>
                {connexion ? (
                  <Button icone={<Power className="size-4" aria-hidden />} action={changerEtat}>
                    {connexion.active ? 'Désactiver' : 'Activer'}
                  </Button>
                ) : (
                  <Checkbox
                    className="pb-2"
                    checked={b.active}
                    onChange={(v) => maj({ active: v })}
                    label="Active dès l’enregistrement"
                  />
                )}
              </div>
              <Field
                label="Description"
                hint="Facultatif. Affichée dans la liste et dans le dialogue d’envoi d’une MEP."
              >
                {(i) => (
                  <Input
                    id={i}
                    value={b.description}
                    maxLength={300}
                    onChange={(e) => maj({ description: e.target.value })}
                  />
                )}
              </Field>
            </Card>

            <CarteRequete
              b={b}
              maj={maj}
              connexion={connexion}
              version={catalogue.donnees.version}
              catalogue={cat}
              variables={variables}
              rendu={urlRendu}
              ressaisir={ressaisir}
              domaineChange={domaineChange}
              surChamp={surChamp}
            />
            {domaineChange && (
              <Alerte ton="warn" icone={TriangleAlert}>
                Le domaine a changé : les secrets enregistrés (liés à {ancienneOrigine.split(' ')[0]}) seront effacés à
                l’enregistrement. Saisissez ceux de la nouvelle adresse.
              </Alerte>
            )}
            <CarteMessage
              b={b}
              maj={maj}
              catalogue={cat}
              variables={variables}
              rendu={corpsRendu}
              inconnues={inconnues}
              corpsModele={corpsModele}
              surChamp={surChamp}
              inserer={inserer}
            />
            <CarteQuand
              b={b}
              maj={maj}
              catalogue={catalogue.donnees}
              equipes={equipes.donnees ?? []}
              systemes={systemes.donnees ?? []}
              saisie={versSaisie(b)}
            />

            {erreur && (
              <Alerte ton="danger" icone={OctagonAlert} role="alert">
                {erreur}
                {erreurs.length > 1 && (
                  <ul className="mt-1 list-disc pl-5">
                    {erreurs.map((x) => (
                      <li key={x}>{x}</li>
                    ))}
                  </ul>
                )}
              </Alerte>
            )}
            <div className="flex flex-wrap items-center gap-2">
              <Button
                variante="primaire"
                icone={<Save className="size-4" aria-hidden />}
                chargement={envoi}
                disabled={!nouvelle && !dirty}
                onClick={() => void enregistrer()}
              >
                Enregistrer
              </Button>
              <Button variante="fantome" onClick={() => void navigate(RETOUR)}>
                Annuler
              </Button>
            </div>
          </div>

          <ApercuConnexion
            ref={boutonEssayer}
            saisie={versSaisie(b, connexion?.version)}
            connexionId={connexion?.id ?? null}
            meps={meps.donnees ?? (meps.echec ? [] : null)}
            variables={variables}
            bloque={bloque}
            onEssai={() => {
              if (connexion)
                void api
                  .connexionApi(connexion.id)
                  .then(setConnexion)
                  .catch(() => undefined);
            }}
          />
        </div>
      )}
    </main>
  );
}
