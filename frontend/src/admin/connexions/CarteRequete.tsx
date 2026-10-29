import { Lock, Plus, Trash2 } from 'lucide-react';
import type { CatalogueGabarit, Rendu } from '../../textes/gabarits';
import type { AuthentificationApi, ConnexionApi, MethodeHttp, VariableTexte } from '../../types';
import { BoutonIcone, Button } from '../../ui/Button';
import { Card } from '../../ui/Card';
import { CaseSeule, Checkbox } from '../../ui/Checkbox';
import { EditeurGabarit } from '../../ui/EditeurGabarit';
import { Field, Input, Select } from '../../ui/Input';
import { Segmented } from '../../ui/Segmented';
import type { Brouillon, EnteteBrouillon } from './brouillon';
import { ChampSecret, TitreCarte } from './ChampsConnexion';
import { LIBELLE_AUTHENTIFICATION, methode, METHODES, NOM_SECRET } from './connexions';

const AUTHENTIFICATIONS: AuthentificationApi[] = ['Aucune', 'Jeton', 'Basic', 'CleEntete', 'OAuth2'];

/** Où envoyer, et comment s'identifier auprès de l'outil (carte 2 de l'éditeur). */
export function CarteRequete({
  b,
  maj,
  connexion,
  version,
  catalogue,
  variables,
  rendu,
  ressaisir,
  domaineChange,
  surChamp,
}: {
  b: Brouillon;
  maj: (p: Partial<Brouillon>) => void;
  connexion: ConnexionApi | null;
  version: string;
  catalogue: CatalogueGabarit;
  variables: VariableTexte[];
  /** Adresse rendue avec des valeurs d'exemple (erreurs, variables inconnues) ; null pour une adresse secrète. */
  rendu: Rendu | null;
  /** Le domaine a changé : message pour chaque secret enregistré à ressaisir (null sinon). */
  ressaisir: (quoi: string) => string | null;
  domaineChange: boolean;
  /** Champ de gabarit qui prend le focus (cible de « Insérer une variable »). */
  surChamp: (cle: string, el: HTMLTextAreaElement | null) => void;
}) {
  const nomSecret = NOM_SECRET[b.authentification];
  const erreurAdresse = rendu?.erreurs[0];

  function majEntete(i: number, p: Partial<EnteteBrouillon>) {
    maj({ entetes: b.entetes.map((e, j) => (j === i ? { ...e, ...p } : e)) });
  }

  return (
    <Card className="grid gap-5 p-5">
      <TitreCarte numero={2} titre="Requête" description="Où envoyer, et comment s’identifier auprès de l’outil." />

      <div className="grid gap-1.5">
        <span className="text-ui font-medium text-fg" id="libelle-adresse">
          Méthode et adresse *
        </span>
        <div className="flex flex-col gap-2 sm:flex-row sm:items-start">
          <Select
            aria-label="Méthode"
            value={b.methode}
            className="sm:w-28"
            onChange={(e) => {
              const m = METHODES.find((x) => x === e.target.value);
              if (m)
                maj({
                  methode: m,
                  ...(m === 'Get'
                    ? { format: 'Aucun' as const }
                    : b.format === 'Aucun'
                      ? { format: 'Json' as const }
                      : {}),
                });
            }}
          >
            {METHODES.map((m: MethodeHttp) => (
              <option key={m} value={m}>
                {methode(m)}
              </option>
            ))}
          </Select>
          <div className="min-w-0 flex-1">
            {b.urlSecrete ? (
              <ChampSecret
                label="Adresse secrète"
                valeur={b.adresseSecrete}
                onChange={(v) => maj({ adresseSecrete: v })}
                etat={connexion?.adresseSecrete}
                ressaisir={null}
                obligatoire
                hint={
                  connexion?.adresseSecrete?.enregistre
                    ? `Enregistrée : ${b.url || connexion.url}/•••. Saisissez-la de nouveau pour la remplacer.`
                    : 'L’adresse complète, avec sa clé (sig=…). Chiffrée, jamais réaffichée : seul le domaine reste visible.'
                }
              />
            ) : (
              <EditeurGabarit
                aria-label="Adresse"
                value={b.url}
                onChange={(v) => maj({ url: v })}
                format="Adresse"
                catalogue={catalogue}
                variables={variables}
                uneLigne
                rows={1}
                placeholder="https://outil.exemple.fr/api/…"
                erreur={erreurAdresse && erreurAdresse.message.includes('domaine') ? erreurAdresse : null}
                aria-invalid={Boolean(erreurAdresse && b.url.trim()) || undefined}
                onFocus={() => surChamp('url', null)}
                ref={(el) => surChamp('url', el)}
              />
            )}
          </div>
        </div>
        {!b.urlSecrete && erreurAdresse && b.url.trim() && (
          <p className="text-xs text-danger-fg" role="alert">
            {erreurAdresse.message}
          </p>
        )}
        {!b.urlSecrete && rendu && rendu.inconnues.length > 0 && (
          <p className="text-xs text-danger-fg" role="alert">
            Variable inconnue dans l’adresse : {rendu.inconnues.map((c) => `{{${c}}}`).join(', ')}.
          </p>
        )}
        {!b.urlSecrete && (
          <p className="text-xs text-fg-subtle">
            Les variables sont permises dans le chemin et les paramètres, pas dans le domaine. Leurs valeurs y sont
            encodées (espaces, accents, barres obliques).
          </p>
        )}
        <Checkbox
          className="mt-1"
          checked={b.urlSecrete}
          onChange={(v) => maj({ urlSecrete: v, ...(v ? { adresseSecrete: '' } : {}) })}
          label="L’adresse contient un secret"
          description="Teams et Slack mettent la clé dans l’adresse : elle est chiffrée et n’est plus réaffichée, sauf le domaine."
        />
      </div>

      <div className="grid gap-3">
        <span className="text-ui font-medium text-fg">Authentification</span>
        <Segmented<AuthentificationApi>
          label="Authentification"
          valeur={b.authentification}
          onChange={(a) => maj({ authentification: a, secret: '' })}
          className="flex-wrap"
          options={AUTHENTIFICATIONS.map((a) => ({ valeur: a, libelle: LIBELLE_AUTHENTIFICATION[a] }))}
        />
        {b.authentification === 'Aucune' && (
          <p className="text-xs text-fg-subtle">« Aucune » : la clé est dans l’adresse, ou l’outil n’en demande pas.</p>
        )}
        <div className="grid gap-4 md:grid-cols-2">
          {b.authentification === 'Basic' && (
            <Field label="Identifiant *">
              {(i) => (
                <Input
                  id={i}
                  value={b.utilisateur}
                  autoComplete="off"
                  className="font-mono text-ui"
                  onChange={(e) => maj({ utilisateur: e.target.value })}
                />
              )}
            </Field>
          )}
          {b.authentification === 'CleEntete' && (
            <Field label="Nom de l’en-tête *" hint="Par exemple X-Api-Key.">
              {(i) => (
                <Input
                  id={i}
                  value={b.nomEntete}
                  autoComplete="off"
                  className="font-mono text-ui"
                  onChange={(e) => maj({ nomEntete: e.target.value })}
                />
              )}
            </Field>
          )}
          {b.authentification === 'OAuth2' && (
            <>
              <Field label="Point de jeton *" className="md:col-span-2">
                {(i) => (
                  <Input
                    id={i}
                    value={b.tokenUrl}
                    placeholder="https://login.microsoftonline.com/…/oauth2/v2.0/token"
                    className="font-mono text-ui"
                    onChange={(e) => maj({ tokenUrl: e.target.value })}
                  />
                )}
              </Field>
              <Field label="ID client *">
                {(i) => (
                  <Input
                    id={i}
                    value={b.clientId}
                    autoComplete="off"
                    className="font-mono text-ui"
                    onChange={(e) => maj({ clientId: e.target.value })}
                  />
                )}
              </Field>
              <Field label="Portée" hint="Facultatif. Par exemple api://outil/.default.">
                {(i) => (
                  <Input
                    id={i}
                    value={b.scope}
                    className="font-mono text-ui"
                    onChange={(e) => maj({ scope: e.target.value })}
                  />
                )}
              </Field>
            </>
          )}
          {b.authentification !== 'Aucune' && (
            <ChampSecret
              label={nomSecret}
              valeur={b.secret}
              onChange={(v) => maj({ secret: v })}
              etat={connexion && connexion.authentification === b.authentification ? connexion.secret : null}
              ressaisir={ressaisir(nomSecret.toLowerCase())}
              obligatoire
              hint={
                b.authentification === 'OAuth2'
                  ? 'Le jeton obtenu est gardé en mémoire jusqu’à son expiration, jamais en base.'
                  : undefined
              }
            />
          )}
        </div>
      </div>

      <div className="grid gap-2">
        <span className="text-ui font-medium text-fg">En-têtes</span>
        {b.entetes.length > 0 && (
          <div className="grid gap-2">
            {b.entetes.map((e, i) => (
              <div key={i} className="grid gap-2 sm:grid-cols-[11rem_minmax(0,1fr)_auto_auto] sm:items-start">
                <Input
                  aria-label={`Nom de l’en-tête ${i + 1}`}
                  value={e.nom}
                  placeholder="X-Source"
                  className="font-mono text-ui"
                  onChange={(ev) => majEntete(i, { nom: ev.target.value })}
                />
                {e.secret ? (
                  <Input
                    type="password"
                    aria-label={`Valeur secrète de ${e.nom || `l’en-tête ${i + 1}`}`}
                    value={e.valeurSecrete}
                    autoComplete="new-password"
                    className="font-mono text-ui"
                    placeholder={
                      e.enregistre?.enregistre && !domaineChange
                        ? 'Enregistrée — laisser vide pour la conserver'
                        : 'Valeur secrète'
                    }
                    onChange={(ev) => majEntete(i, { valeurSecrete: ev.target.value })}
                  />
                ) : (
                  <EditeurGabarit
                    aria-label={`Valeur de ${e.nom || `l’en-tête ${i + 1}`}`}
                    value={e.valeur}
                    onChange={(v) => majEntete(i, { valeur: v })}
                    format="Texte"
                    catalogue={catalogue}
                    variables={variables}
                    uneLigne
                    rows={1}
                    onFocus={() => surChamp(`entete:${i}`, null)}
                    ref={(el) => surChamp(`entete:${i}`, el)}
                  />
                )}
                <div className="flex h-9 items-center gap-2 text-ui text-fg-muted">
                  <CaseSeule
                    checked={e.secret}
                    label={`En-tête ${e.nom || i + 1} secret`}
                    onChange={(v) => majEntete(i, { secret: v, valeurSecrete: '' })}
                  />
                  <span aria-hidden className="inline-flex items-center gap-1">
                    <Lock className="size-3.5" />
                    Secret
                  </span>
                </div>
                <BoutonIcone
                  label={`Retirer l’en-tête ${e.nom || i + 1}`}
                  taille="ic-md"
                  onClick={() => maj({ entetes: b.entetes.filter((_, j) => j !== i) })}
                >
                  <Trash2 className="size-4" aria-hidden />
                </BoutonIcone>
              </div>
            ))}
          </div>
        )}
        <div>
          <Button
            taille="sm"
            variante="fantome"
            icone={<Plus className="size-4" aria-hidden />}
            onClick={() =>
              maj({
                entetes: [...b.entetes, { nom: '', valeur: '', secret: false, valeurSecrete: '', enregistre: null }],
              })
            }
          >
            Ajouter un en-tête
          </Button>
        </div>
        <p className="text-xs text-fg-subtle">
          Ajoutés d’office : Content-Type (selon le format du message), User-Agent: GestionMep/{version},
          X-Gestion-Mep-Appel (identifiant unique de l’appel, pour écarter les doublons chez le destinataire) et
          X-Gestion-Mep-Evenement.
        </p>
      </div>

      <div className="grid gap-3">
        <Checkbox
          checked={b.signature}
          onChange={(v) => maj({ signature: v, secretSignature: '' })}
          label="Signer le message (HMAC SHA-256)"
          description="En-tête X-Gestion-Mep-Signature : « sha256= » suivi de la signature du corps, pour que le destinataire vérifie l’origine."
        />
        {b.signature && (
          <ChampSecret
            label="Secret de signature"
            className="md:max-w-md"
            valeur={b.secretSignature}
            onChange={(v) => maj({ secretSignature: v })}
            etat={connexion?.secretSignature}
            ressaisir={ressaisir('le secret de signature')}
            obligatoire
          />
        )}
      </div>

      <div className="grid gap-4 sm:grid-cols-2">
        <Field label="Délai d’attente (secondes)" hint="De 1 à 30.">
          {(i) => (
            <Input
              id={i}
              type="number"
              min={1}
              max={30}
              value={b.delaiSecondes}
              onChange={(e) => maj({ delaiSecondes: Number(e.target.value) })}
            />
          )}
        </Field>
        <Field
          label="Tentatives"
          hint={`${['tout de suite', 'après 30 s', 'après 2 min', 'après 10 min', 'après 10 min'].slice(0, Math.max(1, Math.min(5, b.tentatives))).join(', ')}. Nouvelle tentative seulement si l’outil ne répond pas ou répond 408, 429 ou 5xx.`}
        >
          {(i) => (
            <Input
              id={i}
              type="number"
              min={1}
              max={5}
              value={b.tentatives}
              onChange={(e) => maj({ tentatives: Number(e.target.value) })}
            />
          )}
        </Field>
      </div>
    </Card>
  );
}
