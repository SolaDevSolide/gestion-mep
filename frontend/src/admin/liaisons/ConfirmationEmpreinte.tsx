import { Check, CircleCheck, CircleX, Hourglass, Network, TriangleAlert } from 'lucide-react';
import { useState } from 'react';
import { api } from '../../api';
import { formatDateHeure } from '../../format';
import type { LiaisonNoosphere } from '../../types';
import { Alerte } from '../../ui/Alerte';
import { Button } from '../../ui/Button';
import { Card, CardHeader } from '../../ui/Card';
import { StatusPill } from '../../ui/StatusPill';
import { useToast } from '../../ui/Toast';
import { Capacites, Empreinte } from './communs';
import { etatPair } from './liaisons';

/**
 * Dernière étape de l'appairage (vue 3b) : comparer les quatre mots avec ceux de Noosphere, puis confirmer, ou annuler
 * (la liaison est coupée et ses clés effacées). À droite, l'état des deux côtés.
 */
export function ConfirmationEmpreinte({
  liaison: l,
  onChange,
}: {
  liaison: LiaisonNoosphere;
  onChange: (l: LiaisonNoosphere) => void;
}) {
  const toast = useToast();
  const [erreur, setErreur] = useState('');
  const pair = etatPair(l);

  async function confirmer() {
    setErreur('');
    try {
      const maj = await api.confirmerLiaison(l.id, l.empreinte);
      toast.succes(`Liaison avec ${maj.nomInstance} confirmée.`);
      onChange(maj);
    } catch (e) {
      setErreur(e instanceof Error ? e.message : 'Confirmation impossible.');
    }
  }

  async function annuler() {
    setErreur('');
    try {
      const maj = await api.couperLiaison(l.id);
      toast.succes('Appairage annulé : clés effacées.');
      onChange(maj);
    } catch (e) {
      setErreur(e instanceof Error ? e.message : 'Annulation impossible.');
    }
  }

  return (
    <div className="grid grid-cols-[minmax(0,1fr)] gap-5 lg:grid-cols-[minmax(0,1.75fr)_minmax(0,1fr)]">
      <Card>
        <CardHeader
          titre="Comparer l’empreinte"
          description="Dernière étape de l’appairage."
          action={
            <StatusPill ton="wait" icone={Hourglass} taille="sm">
              À confirmer ici
            </StatusPill>
          }
        />
        <div className="grid gap-5 p-5">
          <div className="flex items-start gap-3">
            <span className="flex size-9 shrink-0 items-center justify-center rounded-lg bg-accent-soft text-accent-text">
              <Network className="size-4.5" aria-hidden />
            </span>
            <div className="min-w-0 text-sm">
              <p className="font-semibold">{l.nomInstance}</p>
              <p className="text-ui text-fg-subtle [overflow-wrap:anywhere]">
                <span className="font-mono">{l.url}</span>
                {l.versionNoosphere && ` · Noosphere ${l.versionNoosphere}`} · a répondu le{' '}
                {formatDateHeure(l.appaireeLe)}
              </p>
            </div>
          </div>
          <p className="text-sm">
            Noosphere affiche quatre mots sur sa page Links. Lisez-les ensemble, à voix haute ou côte à côte.
          </p>
          <Empreinte valeur={l.empreinte} grand />
          <Alerte ton="warn" icone={TriangleAlert} titre="Si un seul mot diffère, ne confirmez pas.">
            Quelqu’un peut se trouver entre les deux applications. « Ils diffèrent » annule l’appairage et efface les
            clés.
          </Alerte>
          {erreur && (
            <Alerte ton="danger" icone={CircleX} role="alert">
              {erreur}
            </Alerte>
          )}
        </div>
        <div className="flex flex-wrap gap-3 rounded-b-xl border-t border-line bg-surface-2/50 px-5 py-3.5">
          <Button
            variante="danger-doux"
            className="h-auto min-h-9 max-w-full py-1.5 text-left whitespace-normal"
            icone={<CircleX className="size-4 shrink-0" aria-hidden />}
            action={annuler}
          >
            Ils diffèrent : annuler
          </Button>
          <Button
            variante="primaire"
            className="h-auto min-h-9 max-w-full py-1.5 text-left whitespace-normal"
            icone={<Check className="size-4 shrink-0" aria-hidden />}
            action={confirmer}
          >
            Les mots sont identiques : confirmer
          </Button>
        </div>
      </Card>

      <Card className="self-start">
        <CardHeader titre="État" />
        <dl className="grid gap-4 p-5 text-sm">
          <div className="grid grid-cols-[minmax(0,1fr)_minmax(0,1.1fr)] items-start gap-3">
            <dt className="text-fg-subtle">Ici</dt>
            <dd>
              <StatusPill ton="wait" icone={Hourglass} taille="sm">
                À confirmer
              </StatusPill>
            </dd>
          </div>
          <div className="grid grid-cols-[minmax(0,1fr)_minmax(0,1.1fr)] items-start gap-3">
            <dt className="text-fg-subtle">Côté Noosphere</dt>
            <dd>
              <StatusPill
                ton={pair.ton}
                icone={l.confirmeeParNoosphere ? CircleCheck : Hourglass}
                taille="sm"
                className="whitespace-normal"
              >
                {l.confirmeeParNoosphere ? 'Confirmée' : l.confirmeeParNoosphere === false ? 'À confirmer' : 'Inconnu'}
              </StatusPill>
            </dd>
          </div>
          <div className="grid grid-cols-[minmax(0,1fr)_minmax(0,1.1fr)] items-start gap-3">
            <dt className="text-fg-subtle">Échanges</dt>
            <dd>Aucun tant que les deux côtés n’ont pas confirmé.</dd>
          </div>
          <div className="grid grid-cols-[minmax(0,1fr)_minmax(0,1.1fr)] items-start gap-3">
            <dt className="text-fg-subtle">Autorisé par Noosphere</dt>
            <dd>
              <Capacites capacites={l.capacites} />
            </dd>
          </div>
        </dl>
      </Card>
    </div>
  );
}
