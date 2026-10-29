import { CheckCircle2, OctagonAlert, Send, TriangleAlert } from 'lucide-react';
import { forwardRef, useEffect, useState } from 'react';
import { api } from '../../api';
import { formatDate } from '../../format';
import type {
  ApercuConnexion as Apercu,
  ApercuConnexionSaisie,
  EssaiConnexion,
  MepResume,
  VariableTexte,
} from '../../types';
import { Alerte } from '../../ui/Alerte';
import { Button } from '../../ui/Button';
import { Card } from '../../ui/Card';
import { ErreurChargement } from '../../ui/ErreurChargement';
import { Field, Select } from '../../ui/Input';
import { Segmented } from '../../ui/Segmented';
import { Squelette, SqueletteLignes } from '../../ui/Squelette';
import { Badge } from '../../ui/StatusPill';
import { useToast } from '../../ui/Toast';
import { duree, heureCourte, methode } from './connexions';

type Vue = 'requete' | 'lisible';

/** Message d'une erreur d'essai, pour dire quoi faire (le détail brut reste dans l'historique). */
export function phraseEssai(e: EssaiConnexion): string {
  const t = e.tentative;
  if (e.reussi) return 'L’outil a accepté le message.';
  if (t.erreur) return t.erreur;
  if (t.code === 401 || t.code === 403)
    return 'L’outil refuse l’identification : vérifiez l’authentification et ses secrets.';
  if (t.code === 404) return 'L’outil ne connaît pas cette adresse : vérifiez-la.';
  if (t.code !== null && t.code >= 500) return 'L’outil est en difficulté : un envoi automatique serait réessayé.';
  return 'L’outil a refusé le message : voyez sa réponse ci-dessous.';
}

/**
 * Aperçu de l'éditeur : la requête exacte qui partirait pour une MEP et un événement (calculée par le serveur avec les
 * fonctions de l'envoi, secrets masqués), sa forme lisible, et « Essayer » qui l'envoie vraiment.
 */
export const ApercuConnexion = forwardRef<
  HTMLButtonElement,
  {
    saisie: ApercuConnexionSaisie['brouillon'];
    connexionId: string | null;
    meps: MepResume[] | null;
    variables: VariableTexte[];
    /** Erreurs locales qui empêchent l'envoi (JSON invalide, variable inconnue) : « Essayer » est désactivé. */
    bloque: boolean;
    onEssai: () => void;
  }
>(function ApercuConnexion({ saisie, connexionId, meps, variables, bloque, onEssai }, refEssayer) {
  const toast = useToast();
  const [vue, setVue] = useState<Vue>('requete');
  const [mepId, setMepId] = useState<string>('');
  const [evenement, setEvenement] = useState<string>('');
  const [apercu, setApercu] = useState<Apercu | null>(null);
  const [erreur, setErreur] = useState<unknown>(null);
  const [tick, setTick] = useState(0);
  const [essai, setEssai] = useState<EssaiConnexion | null>(null);
  const [essaiErreur, setEssaiErreur] = useState('');

  useEffect(() => {
    if (!mepId && meps && meps.length > 0) setMepId(meps[0].id);
  }, [meps, mepId]);

  const demande: ApercuConnexionSaisie = {
    brouillon: saisie,
    ...(connexionId ? { connexionId } : {}),
    ...(mepId ? { mepId } : {}),
    ...(evenement.startsWith('id:')
      ? { evenementId: Number(evenement.slice(3)) }
      : evenement
        ? { evenement: evenement.slice(4) }
        : {}),
  };
  const cle = JSON.stringify(demande);

  // Recalcul une fois la frappe posée (300 ms) : le formulaire reste utilisable pendant ce temps.
  useEffect(() => {
    if (meps === null) return;
    let annule = false;
    const t = window.setTimeout(() => {
      api
        .apercuConnexion(demande)
        .then((a) => {
          if (annule) return;
          setApercu(a);
          setErreur(null);
        })
        .catch((e: unknown) => {
          if (!annule) setErreur(e);
        });
    }, 300);
    return () => {
      annule = true;
      window.clearTimeout(t);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [cle, tick, meps === null]);

  async function essayer() {
    setEssaiErreur('');
    setEssai(null);
    try {
      const r = await api.essayerConnexion(demande);
      setEssai(r);
      if (r.reussi) toast.succes(`Essai réussi (${r.tentative.code ?? ''}).`);
      onEssai();
    } catch (e) {
      setEssaiErreur(e instanceof Error ? e.message : 'Essai impossible.');
    }
  }

  const libelles = new Map(variables.map((v) => [v.cle, v.libelle]));
  const valeurEvenement = apercu
    ? apercu.evenement.id !== null
      ? `id:${apercu.evenement.id}`
      : `cle:${apercu.evenement.cle}`
    : evenement;
  const jsonValide = apercu && saisie.format === 'Json' && !apercu.erreurs.some((e) => e.champ === 'corps');
  const texte =
    apercu &&
    (vue === 'lisible'
      ? apercu.lisible || '(message vide)'
      : [...apercu.entetes.map((e) => `${e.nom}: ${e.valeur}`), ...(apercu.corps ? ['', apercu.corps] : [])].join(
          '\n',
        ));

  return (
    <Card role="region" aria-label="Aperçu" className="p-5 lg:sticky lg:top-[calc(var(--bandeau-demo,0px)+1.5rem)]">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 className="text-md font-semibold tracking-tight">Aperçu</h2>
        <Segmented<Vue>
          label="Forme de l’aperçu"
          valeur={vue}
          onChange={setVue}
          options={[
            { valeur: 'requete', libelle: 'Requête' },
            { valeur: 'lisible', libelle: 'Lisible' },
          ]}
        />
      </div>
      <div className="mt-4 grid gap-3 sm:grid-cols-2 lg:grid-cols-1 xl:grid-cols-2">
        <Field label="Avec les informations de la MEP">
          {(i) => (
            <Select
              id={i}
              value={mepId}
              onChange={(e) => {
                setMepId(e.target.value);
                setEvenement('');
              }}
            >
              {meps?.length === 0 && <option value="">Aucune MEP (valeurs d’exemple)</option>}
              {(meps ?? []).map((m) => (
                <option key={m.id} value={m.id}>
                  {m.outil}
                  {m.versionCible ? ` ${m.versionCible}` : ''} · {formatDate(m.dateMep ?? m.creeLe)}
                </option>
              ))}
            </Select>
          )}
        </Field>
        <Field label="Pour l’événement">
          {(i) => (
            <Select id={i} value={valeurEvenement} onChange={(e) => setEvenement(e.target.value)} disabled={!apercu}>
              {(apercu?.evenements ?? []).map((ev) => {
                const v = ev.id !== null ? `id:${ev.id}` : `cle:${ev.cle}`;
                return (
                  <option key={v} value={v}>
                    {ev.libelle}
                    {ev.detail ? ` : ${ev.detail.slice(0, 60)}` : ''}
                    {ev.quand && ev.id !== null ? ` (${heureCourte(ev.quand)})` : ev.id === null ? ' (simulé)' : ''}
                  </option>
                );
              })}
            </Select>
          )}
        </Field>
      </div>

      {erreur !== null && (
        <ErreurChargement
          variante="ligne"
          className="mt-4"
          erreur={erreur}
          titre="Aperçu non calculé"
          complement="Le formulaire reste modifiable."
          onReessayer={() => setTick((t) => t + 1)}
        />
      )}
      {!apercu && erreur === null && (
        <div className="mt-4" aria-busy>
          <Squelette className="h-5 w-64" />
          <SqueletteLignes lignes={10} className="mt-3" />
        </div>
      )}
      {apercu && (
        <div className="apparition mt-4 grid gap-3">
          {apercu.manquantes.length > 0 && (
            <Alerte ton="warn" icone={TriangleAlert}>
              Non renseigné dans cette MEP : {apercu.manquantes.map((c) => libelles.get(c) ?? c).join(', ')}. Ces
              valeurs partiront vides.
            </Alerte>
          )}
          {apercu.erreurs.map((e, i) => (
            <Alerte key={i} ton="danger" icone={OctagonAlert}>
              {e.champ === 'adresse' ? 'Adresse' : e.champ === 'corps' ? 'Message' : `En-tête ${e.champ.slice(7)}`},
              ligne {e.ligne}, colonne {e.colonne} : {e.message}
            </Alerte>
          ))}
          <div className="overflow-hidden rounded-lg border border-line">
            <div className="flex flex-wrap items-baseline justify-between gap-2 border-b border-line bg-surface-2/50 px-3 py-2">
              <p className="min-w-0 font-mono text-ui break-all">
                <span className="font-semibold">{methode(apercu.methode ?? 'Post')}</span> {apercu.url}
              </p>
              {saisie.format === 'Json' && (
                <Badge ton={jsonValide ? 'ok' : 'danger'}>{jsonValide ? 'JSON valide' : 'JSON invalide'}</Badge>
              )}
            </div>
            <pre
              tabIndex={0}
              className="max-h-[32rem] overflow-auto px-3 py-2.5 font-mono text-xs leading-relaxed break-words whitespace-pre-wrap text-fg"
            >
              {texte}
            </pre>
          </div>
        </div>
      )}

      <div className="mt-4 grid gap-2 border-t border-line pt-4">
        <div className="flex flex-wrap items-center gap-3">
          <Button
            ref={refEssayer}
            variante="primaire"
            icone={<Send className="size-4" aria-hidden />}
            disabled={!apercu || bloque}
            action={essayer}
          >
            Essayer
          </Button>
          <p className="min-w-0 flex-1 text-xs text-fg-subtle">
            Envoie vraiment ce message, sans enregistrer
            {saisie.corps.includes('{{essai}}') ? ' : il est précédé de « [Essai] »' : ''}. Les secrets laissés vides
            sont ceux qui sont enregistrés.
          </p>
        </div>
        {essaiErreur && (
          <Alerte ton="danger" icone={OctagonAlert} role="alert">
            {essaiErreur}
          </Alerte>
        )}
        {essai && (
          <Alerte
            ton={essai.reussi ? 'ok' : 'danger'}
            icone={essai.reussi ? CheckCircle2 : OctagonAlert}
            role="status"
            titre={
              essai.tentative.code !== null
                ? `Réponse ${essai.tentative.code}${essai.tentative.libelle ? ` ${essai.tentative.libelle}` : ''} en ${duree(essai.tentative.dureeMs)}`
                : essai.tentative.libelle || 'Pas de réponse'
            }
          >
            {phraseEssai(essai)}
            {(essai.tentative.extraitReponse || essai.tentative.entetesReponse) && (
              <pre className="mt-2 max-h-48 overflow-auto rounded-md bg-surface/70 px-2 py-1.5 font-mono text-xs whitespace-pre-wrap text-fg">
                {essai.tentative.entetesReponse ? `${essai.tentative.entetesReponse}\n` : ''}
                {essai.tentative.extraitReponse}
                {essai.tronque ? '\n… (réponse coupée à 4 Ko)' : ''}
              </pre>
            )}
          </Alerte>
        )}
      </div>
    </Card>
  );
});
