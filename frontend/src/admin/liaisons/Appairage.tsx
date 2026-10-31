import { ArrowDownLeft, ArrowUpRight, Link2, Lock, OctagonAlert, Unlink } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import { api } from '../../api';
import { formatDateHeure } from '../../format';
import type { LiaisonNoosphere } from '../../types';
import { Alerte } from '../../ui/Alerte';
import { Button } from '../../ui/Button';
import { Card, CardHeader } from '../../ui/Card';
import { Field, Input } from '../../ui/Input';
import { StatusPill } from '../../ui/StatusPill';
import { CarteLigneDeCommande } from './communs';

/**
 * Aucune liaison en service (vue 3a) : adresse de Noosphere et code d'appairage, ce qui sera échangé, la ligne de
 * commande, le démarrage commun avec Docker Compose et, s'il y en a, les liaisons coupées.
 */
export function Appairage({
  coupees,
  onAppairee,
}: {
  coupees: LiaisonNoosphere[];
  onAppairee: (l: LiaisonNoosphere) => void;
}) {
  const [url, setUrl] = useState('');
  const [code, setCode] = useState('');
  const [erreur, setErreur] = useState('');
  const [envoi, setEnvoi] = useState(false);
  const valide = url.trim().length > 0 && code.trim().length > 0;

  async function appairer(e?: FormEvent) {
    e?.preventDefault();
    if (!valide || envoi) return;
    setEnvoi(true);
    setErreur('');
    try {
      onAppairee(await api.appairerLiaison({ url: url.trim(), code: code.trim() }));
    } catch (x) {
      setErreur(x instanceof Error ? x.message : 'Appairage impossible.');
    } finally {
      setEnvoi(false);
    }
  }

  return (
    <div className="grid grid-cols-[minmax(0,1fr)] gap-5">
      <Card>
        <CardHeader
          titre="Liaison avec Noosphere"
          description="Noosphere documente le parc applicatif. Relié, le catalogue des systèmes suit ses applications, et les MEP apparaissent sur leurs fiches."
          action={
            <StatusPill ton="wait" icone={Unlink} taille="sm">
              Aucune liaison
            </StatusPill>
          }
        />
        <form onSubmit={(e) => void appairer(e)} noValidate>
          <div className="grid gap-5 p-5 lg:grid-cols-[minmax(0,1.15fr)_minmax(0,1fr)]">
            <div className="grid content-start gap-4">
              {erreur && (
                <Alerte ton="danger" icone={OctagonAlert} role="alert" titre="Appairage impossible">
                  {erreur}
                </Alerte>
              )}
              <Field label="Adresse de Noosphere" hint="L’adresse que vous utilisez pour ouvrir Noosphere.">
                {(id) => (
                  <Input
                    id={id}
                    type="url"
                    inputMode="url"
                    autoComplete="url"
                    spellCheck={false}
                    placeholder="https://noosphere.entreprise.fr"
                    value={url}
                    onChange={(e) => setUrl(e.target.value)}
                  />
                )}
              </Field>
              <Field
                label="Code d’appairage"
                hint="Créé par un administrateur de Noosphere (Links, « Pair an application »). Usage unique, valable 15 minutes."
              >
                {(id) => (
                  <Input
                    id={id}
                    className="font-mono tracking-wider uppercase"
                    autoComplete="off"
                    spellCheck={false}
                    placeholder="XXXX-XXXX-XXXX"
                    value={code}
                    onChange={(e) => setCode(e.target.value)}
                  />
                )}
              </Field>
            </div>
            <aside
              aria-labelledby="echange-titre"
              className="self-start rounded-lg border border-line bg-surface-2/60 p-4 text-sm"
            >
              <h3 id="echange-titre" className="text-2xs font-semibold tracking-wider text-fg-subtle uppercase">
                Ce qui sera échangé
              </h3>
              <ul className="mt-3 grid gap-3">
                <li className="flex gap-2.5">
                  <ArrowDownLeft className="mt-0.5 size-4 shrink-0 text-ok-text" aria-hidden />
                  <span>
                    <strong className="font-semibold">Reçu de Noosphere</strong> : applications, flux, domaines et
                    équipes visibles de tous. Les applications suivies deviennent des systèmes du catalogue.
                  </span>
                </li>
                <li className="flex gap-2.5">
                  <ArrowUpRight className="mt-0.5 size-4 shrink-0 text-accent-text" aria-hidden />
                  <span>
                    <strong className="font-semibold">Envoyé à Noosphere</strong> : les MEP des systèmes suivis (titre,
                    dates, phase, résultat, lien), affichées sur leurs fiches.
                  </span>
                </li>
                <li className="flex gap-2.5">
                  <Lock className="mt-0.5 size-4 shrink-0 text-fg-subtle" aria-hidden />
                  <span>
                    <strong className="font-semibold">Jamais</strong> : fiches restreintes, notes protégées, objets de
                    données. Environnements, cibles et objets des systèmes restent ici.
                  </span>
                </li>
              </ul>
            </aside>
          </div>
          <div className="flex flex-wrap items-center gap-x-4 gap-y-2 rounded-b-xl border-t border-line bg-surface-2/50 px-5 py-3.5">
            <Button
              type="submit"
              variante="primaire"
              icone={<Link2 className="size-4" aria-hidden />}
              chargement={envoi}
              disabled={!valide}
            >
              Appairer
            </Button>
            <p className="min-w-0 flex-1 basis-64 text-ui text-fg-subtle">
              Les deux serveurs échangent leurs clés publiques, puis chacun affiche la même empreinte de quatre mots.
            </p>
          </div>
        </form>
      </Card>

      <div className="grid grid-cols-[minmax(0,1fr)] gap-5 lg:grid-cols-2">
        <CarteLigneDeCommande
          description="Les mêmes étapes depuis le serveur."
          commandes={[
            'liaison appairer https://noosphere.entreprise.fr <code>',
            'liaison confirmer <id> --empreinte "<quatre mots>"',
            'liaison liste',
          ]}
        />
        <Card>
          <CardHeader titre="Démarrés ensemble" description="Avec Docker Compose." />
          <div className="grid gap-3 p-5 text-sm">
            <p>
              Avec <code className="font-mono text-ui">NOOSPHERE_URL</code> et{' '}
              <code className="font-mono text-ui">NOOSPHERE_CODE_LIAISON</code> (16 caractères au moins), Gestion MEP
              s’appaire seul au premier démarrage.
            </p>
            <p className="text-fg-subtle">Un administrateur confirme quand même l’empreinte, ici et dans Noosphere.</p>
          </div>
        </Card>
      </div>

      {coupees.length > 0 && (
        <Card>
          <CardHeader
            titre="Liaisons coupées"
            description="Leurs systèmes sont restés dans le catalogue, devenus locaux. Pour relier à nouveau, appairez avec un nouveau code."
          />
          <ul className="divide-y divide-line">
            {coupees.map((l) => (
              <li key={l.id} className="grid gap-1 px-5 py-3 text-sm sm:grid-cols-[minmax(0,1fr)_auto] sm:gap-4">
                <span className="min-w-0">
                  <span className="font-medium">{l.nomInstance}</span>
                  <span className="block truncate font-mono text-xs text-fg-subtle">{l.url}</span>
                </span>
                <span className="text-ui text-fg-subtle">
                  Coupée le {formatDateHeure(l.coupeeLe)}
                  {l.coupeePar && ` par ${l.coupeePar}`}
                </span>
              </li>
            ))}
          </ul>
        </Card>
      )}
    </div>
  );
}
