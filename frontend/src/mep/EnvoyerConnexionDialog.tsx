import { OctagonAlert, Send, TriangleAlert } from 'lucide-react';
import { useEffect, useState } from 'react';
import { heureCourte } from '../admin/connexions/connexions';
import { api } from '../api';
import type { ApercuConnexion, ConnexionManuelle } from '../types';
import { Alerte } from '../ui/Alerte';
import { Button } from '../ui/Button';
import { Dialog } from '../ui/Dialog';
import { ErreurChargement } from '../ui/ErreurChargement';
import { Field, Textarea } from '../ui/Input';
import { Segmented } from '../ui/Segmented';
import { SqueletteLignes } from '../ui/Squelette';
import { useToast } from '../ui/Toast';

type Vue = 'lisible' | 'requete';

const attendre = (ms: number) => new Promise((r) => window.setTimeout(r, ms));

/**
 * Envoi depuis une MEP (« ⋯ Plus d'actions » › Envoyer) : commentaire facultatif repris par {{commentaire}}, aperçu du
 * message, puis envoi. Le résultat est suivi quelques secondes ; un échec passager est réessayé et suivi au journal.
 */
export function EnvoyerConnexionDialog({
  mepId,
  connexion,
  onOpenChange,
}: {
  mepId: string;
  connexion: ConnexionManuelle | null;
  onOpenChange: (o: boolean) => void;
}) {
  const toast = useToast();
  const [commentaire, setCommentaire] = useState('');
  const [vue, setVue] = useState<Vue>('lisible');
  const [apercu, setApercu] = useState<ApercuConnexion | null>(null);
  const [erreurApercu, setErreurApercu] = useState<unknown>(null);
  const [tick, setTick] = useState(0);
  const [erreur, setErreur] = useState('');
  const open = connexion !== null;

  useEffect(() => {
    if (open) {
      setCommentaire('');
      setErreur('');
      setApercu(null);
    }
  }, [open, connexion?.id]);

  useEffect(() => {
    if (!connexion) return;
    let annule = false;
    const t = window.setTimeout(() => {
      api
        .apercuEnvoiMep(mepId, connexion.id, commentaire)
        .then((a) => {
          if (annule) return;
          setApercu(a);
          setErreurApercu(null);
        })
        .catch((e: unknown) => {
          if (!annule) setErreurApercu(e);
        });
    }, 300);
    return () => {
      annule = true;
      window.clearTimeout(t);
    };
  }, [mepId, connexion, commentaire, tick]);

  async function envoyer() {
    if (!connexion) return;
    setErreur('');
    try {
      const { appelId } = await api.envoyerConnexionMep(mepId, connexion.id, commentaire.trim());
      // Suivi du résultat : la plupart des outils répondent en moins d'une seconde.
      for (let i = 0; i < 12; i++) {
        await attendre(i === 0 ? 400 : 1000);
        const a = await api.appelMep(mepId, appelId).catch(() => null);
        if (!a) continue;
        if (a.etat === 'Reussi') {
          toast.succes(`Message envoyé : « ${connexion.libelle || connexion.nom} » (${a.code ?? 'réussi'}).`);
          onOpenChange(false);
          return;
        }
        if (a.etat === 'Echec') {
          setErreur(a.erreur || `L’envoi a échoué (${a.code ?? 'pas de réponse'}).`);
          return;
        }
        if (a.etat === 'EnAttente' && a.tentatives > 0) {
          toast.erreur(
            `L’outil ne répond pas : nouvelle tentative à ${heureCourte(a.prochaineTentative)}. Suivi dans le journal.`,
          );
          onOpenChange(false);
          return;
        }
      }
      toast.succes('Message en cours d’envoi : le résultat s’inscrira au journal de la MEP.');
      onOpenChange(false);
    } catch (e) {
      setErreur(e instanceof Error ? e.message : 'Envoi impossible.');
    }
  }

  const texte =
    apercu &&
    (vue === 'lisible'
      ? apercu.lisible || '(message vide)'
      : [
          ...(apercu.url ? [`${apercu.methode?.toUpperCase() ?? ''} ${apercu.url}`] : []),
          ...apercu.entetes.map((e) => `${e.nom}: ${e.valeur}`),
          ...(apercu.url || apercu.entetes.length ? [''] : []),
          apercu.corps,
        ].join('\n'));

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      taille="lg"
      titre={connexion?.libelle || connexion?.nom || 'Envoyer'}
      description={
        connexion
          ? `Envoie le message de la connexion « ${connexion.nom} » avec les informations de la MEP.${connexion.description ? ` ${connexion.description}` : ''}`
          : undefined
      }
      pied={
        <>
          <Button variante="fantome" onClick={() => onOpenChange(false)}>
            Annuler
          </Button>
          <Button
            variante="primaire"
            icone={<Send className="size-4" aria-hidden />}
            disabled={!apercu || apercu.erreurs.length > 0}
            action={envoyer}
          >
            Envoyer
          </Button>
        </>
      }
    >
      <div className="grid gap-4">
        <Field label="Commentaire" hint="Facultatif. Repris dans le message par {{commentaire}}.">
          {(i) => (
            <Textarea
              id={i}
              rows={3}
              maxLength={2000}
              value={commentaire}
              onChange={(e) => setCommentaire(e.target.value)}
            />
          )}
        </Field>
        <div className="grid gap-2">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <span className="text-ui font-medium text-fg">Aperçu du message</span>
            <Segmented<Vue>
              label="Forme de l’aperçu"
              valeur={vue}
              onChange={setVue}
              options={[
                { valeur: 'lisible', libelle: 'Lisible' },
                { valeur: 'requete', libelle: 'Requête' },
              ]}
            />
          </div>
          {erreurApercu !== null && (
            <ErreurChargement
              variante="ligne"
              erreur={erreurApercu}
              titre="Aperçu non calculé"
              onReessayer={() => setTick((t) => t + 1)}
            />
          )}
          {!apercu && erreurApercu === null && <SqueletteLignes lignes={6} />}
          {apercu && (
            <>
              {apercu.erreurs.length > 0 && (
                <Alerte ton="danger" icone={OctagonAlert}>
                  Le message ne peut pas partir : {apercu.erreurs[0].message} Prévenez un administrateur.
                </Alerte>
              )}
              <pre
                tabIndex={0}
                className="max-h-72 overflow-auto rounded-lg border border-line bg-surface-2/50 px-3 py-2.5 font-mono text-xs leading-relaxed break-words whitespace-pre-wrap text-fg"
              >
                {texte}
              </pre>
            </>
          )}
          <p className="text-xs text-fg-subtle">Inscrit au journal de la MEP. Rien d’autre ne change dans la MEP.</p>
        </div>
        {erreur && (
          <Alerte ton="danger" icone={TriangleAlert} role="alert">
            {erreur}
          </Alerte>
        )}
      </div>
    </Dialog>
  );
}
