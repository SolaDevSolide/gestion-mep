import {
  Check,
  CircleCheck,
  FlaskConical,
  Hourglass,
  KeyRound,
  Pencil,
  RefreshCw,
  TriangleAlert,
  Unplug,
} from 'lucide-react';
import { useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../../api';
import { compte, formatDate, formatDateHeure } from '../../format';
import type { CatalogueLiaison, LiaisonNoosphere } from '../../types';
import { Alerte } from '../../ui/Alerte';
import { Button } from '../../ui/Button';
import { Card, CardHeader } from '../../ui/Card';
import { Checkbox } from '../../ui/Checkbox';
import { Badge, StatusPill } from '../../ui/StatusPill';
import { useToast } from '../../ui/Toast';
import { heureCourte } from '../connexions/connexions';
import { Capacites, CarteLigneDeCommande, Empreinte, Ligne } from './communs';
import { JournalEchanges } from './JournalEchanges';
import { estRetiree, PERIODE_SYNCHRO_MINUTES } from './liaisons';

/**
 * Liaison confirmée ici (vues 3e et 3f) : fiche, interrupteurs, synchronisation, renouvellement des clés, coupure avec
 * confirmation dans la page, journal des échanges et résumé des applications suivies.
 */
export function LiaisonActive({
  liaison: l,
  catalogue,
  avertissement,
  onChange,
  onCoupee,
  onModifierSuivi,
  onRelireStatut,
}: {
  liaison: LiaisonNoosphere;
  catalogue: CatalogueLiaison | null;
  /** État de Noosphere non relu à l'ouverture (injoignable…). */
  avertissement: string | null;
  onChange: (l: LiaisonNoosphere, c?: CatalogueLiaison) => void;
  onCoupee: () => void;
  onModifierSuivi: () => void;
  onRelireStatut: () => Promise<void>;
}) {
  const toast = useToast();
  const [tick, setTick] = useState(0);
  const [coupure, setCoupure] = useState(false);
  const boutonCouper = useRef<HTMLButtonElement>(null);
  const confirmeeDesDeux = l.confirmeeParNoosphere === true;
  const domainesSuivis = (catalogue?.domaines ?? []).filter((d) => d.suivi);
  const prochaine = l.derniereSynchro
    ? new Date(new Date(l.derniereSynchro).getTime() + PERIODE_SYNCHRO_MINUTES * 60_000).toISOString()
    : null;

  async function option(s: { envoyerMeps?: boolean; suiviAutomatique?: boolean }) {
    try {
      const maj = await api.optionsLiaison(l.id, {
        envoyerMeps: s.envoyerMeps ?? null,
        suiviAutomatique: s.suiviAutomatique ?? null,
      });
      onChange(maj);
      setTick((t) => t + 1);
      if (s.envoyerMeps !== undefined)
        toast.succes(
          s.envoyerMeps
            ? 'Les MEP sont de nouveau envoyées à Noosphere.'
            : 'Plus aucune MEP n’est envoyée à Noosphere.',
        );
    } catch (e) {
      toast.erreur(e instanceof Error ? e.message : 'Option non enregistrée.');
    }
  }

  async function synchroniser() {
    try {
      const r = await api.synchroniserLiaison(l.id);
      const c = await api.catalogueLiaison(l.id);
      onChange(r.liaison, c);
      setTick((t) => t + 1);
      toast.succes(r.inchange ? 'Catalogue inchangé.' : `Catalogue relu : ${r.resume}`);
      if (r.avertissements.length > 0) toast.erreur(r.avertissements.join(' '));
    } catch (e) {
      setTick((t) => t + 1);
      toast.erreur(e instanceof Error ? e.message : 'Synchronisation impossible.');
    }
  }

  async function renouveler() {
    try {
      const maj = await api.renouvelerLiaison(l.id);
      onChange(maj);
      setTick((t) => t + 1);
      toast.succes(`Clés renouvelées : nouvelle empreinte ${maj.empreinte}.`);
    } catch (e) {
      toast.erreur(e instanceof Error ? e.message : 'Renouvellement impossible.');
    }
  }

  async function couper() {
    try {
      await api.couperLiaison(l.id);
      toast.succes(`Liaison avec ${l.nomInstance} coupée : les systèmes importés sont devenus locaux.`);
      onCoupee();
    } catch (e) {
      toast.erreur(e instanceof Error ? e.message : 'Coupure impossible.');
    }
  }

  return (
    <div className="grid grid-cols-[minmax(0,1fr)] gap-5">
      <Card>
        <CardHeader
          titre={l.nomInstance}
          description="Liaison avec Noosphere, la documentation du parc applicatif."
          action={
            <div className="flex flex-wrap gap-2">
              {l.demo && (
                <StatusPill ton="later" icone={FlaskConical} taille="sm">
                  Démonstration
                </StatusPill>
              )}
              <StatusPill ton="ok" icone={CircleCheck} taille="sm">
                Active
              </StatusPill>
              {confirmeeDesDeux ? (
                <StatusPill ton="ok" icone={Check} taille="sm">
                  Confirmée des deux côtés
                </StatusPill>
              ) : (
                <StatusPill ton="warn" icone={Hourglass} taille="sm">
                  En attente de Noosphere
                </StatusPill>
              )}
            </div>
          }
        />
        <div className="grid gap-4 p-5">
          {coupure && (
            <Alerte
              ton="danger"
              icone={Unplug}
              role="alert"
              className="animate-fade-in"
              titre={`Couper la liaison avec ${l.nomInstance} ?`}
              action={
                <div className="flex flex-wrap gap-2">
                  <Button
                    taille="sm"
                    variante="danger"
                    icone={<Unplug className="size-4" aria-hidden />}
                    action={couper}
                    autoFocus
                  >
                    Couper la liaison
                  </Button>
                  <Button
                    taille="sm"
                    onClick={() => {
                      setCoupure(false);
                      boutonCouper.current?.focus();
                    }}
                  >
                    Annuler
                  </Button>
                </div>
              }
            >
              Gestion MEP prévient Noosphere puis efface ses clés : plus rien n’est échangé.{' '}
              {l.systemesSuivis > 0
                ? `Les ${compte(l.systemesSuivis, 'système importé', 'systèmes importés')} restent dans le catalogue et deviennent locaux : leur nom et leur description redeviennent modifiables ici.`
                : 'Aucun système n’est suivi.'}{' '}
              Les MEP déjà envoyées restent dans Noosphere, sans lien.
            </Alerte>
          )}
          {!confirmeeDesDeux && (
            <Alerte
              ton="warn"
              icone={Hourglass}
              titre="En attente de confirmation dans Noosphere"
              action={
                <Button taille="sm" icone={<RefreshCw className="size-4" aria-hidden />} action={onRelireStatut}>
                  Relire l’état de Noosphere
                </Button>
              }
            >
              Confirmée ici. Un administrateur de Noosphere doit encore confirmer la même empreinte sur sa page Links ;
              d’ici là, rien n’est échangé.
            </Alerte>
          )}
          {avertissement && (
            <Alerte ton="warn" icone={TriangleAlert} titre="État de Noosphere non relu">
              {avertissement} L’état affiché est celui du dernier échange
              {l.dernierEchangeLe ? `, le ${formatDateHeure(l.dernierEchangeLe)}` : ''}.
            </Alerte>
          )}
          {l.derniereErreur && (
            <Alerte ton="warn" icone={TriangleAlert} titre="Dernière synchronisation en échec">
              {l.derniereErreur}
            </Alerte>
          )}
          {l.demo && (
            <p className="text-ui text-fg-subtle">
              Liaison fictive de la démonstration : aucun appel ne part vers Noosphere, les échanges sont seulement
              inscrits au journal.
            </p>
          )}
          <dl className="grid gap-3 text-sm">
            <Ligne libelle="Adresse">
              <span className="font-mono">{l.url}</span>
              {l.versionNoosphere && ` · Noosphere ${l.versionNoosphere}`}
            </Ligne>
            <Ligne libelle="Appairée">
              {formatDate(l.appaireeLe)}
              {l.appaireePar && ` par ${l.appaireePar}`}
            </Ligne>
            <Ligne libelle="Empreinte">
              <Empreinte valeur={l.empreinte} />
              {l.clesRenouveleesLe && (
                <span className="mt-1 block text-xs text-fg-subtle">
                  Clés renouvelées le {formatDate(l.clesRenouveleesLe)}.
                </span>
              )}
            </Ligne>
            <Ligne libelle="Dernière synchronisation">
              {l.derniereSynchro ? (
                <>
                  {formatDateHeure(l.derniereSynchro)} · prochaine vers {heureCourte(prochaine)} (toutes les{' '}
                  {PERIODE_SYNCHRO_MINUTES} minutes)
                </>
              ) : (
                <span className="text-fg-subtle">
                  Pas encore : le catalogue se lit une fois les deux côtés confirmés.
                </span>
              )}
            </Ligne>
            <Ligne libelle="Autorisé par Noosphere">
              <Capacites capacites={l.capacites} />
            </Ligne>
          </dl>
        </div>
        <div className="grid gap-3 border-t border-line px-5 py-4">
          <Checkbox
            checked={l.envoyerMeps}
            onChange={(v) => void option({ envoyerMeps: v })}
            label="Envoyer les MEP à Noosphere"
            description="Les MEP touchant un système suivi, dès qu’une date est prévue : changement de date, de systèmes, de phase ou de résultat, annulation."
          />
          <Checkbox
            checked={l.suiviAutomatique}
            onChange={(v) => void option({ suiviAutomatique: v })}
            label="Suivre automatiquement les nouvelles applications des domaines suivis"
            description={
              domainesSuivis.length > 0
                ? `${domainesSuivis.map((d) => d.nom).join(' et ')}.`
                : 'Aucun domaine suivi en entier pour l’instant.'
            }
          />
        </div>
        <div className="flex flex-wrap items-center gap-3 rounded-b-xl border-t border-line bg-surface-2/50 px-5 py-3.5">
          <Button icone={<RefreshCw className="size-4" aria-hidden />} action={synchroniser}>
            Synchroniser maintenant
          </Button>
          <Button icone={<KeyRound className="size-4" aria-hidden />} action={renouveler}>
            Renouveler les clés
          </Button>
          <Button
            ref={boutonCouper}
            variante="danger-doux"
            className="sm:ml-auto"
            icone={<Unplug className="size-4" aria-hidden />}
            aria-expanded={coupure}
            onClick={() => setCoupure(true)}
          >
            Couper la liaison
          </Button>
        </div>
      </Card>

      <div className="grid grid-cols-[minmax(0,1fr)] items-start gap-5 lg:grid-cols-[minmax(0,1.75fr)_minmax(0,1fr)]">
        <JournalEchanges liaisonId={l.id} tick={tick} />
        <div className="grid grid-cols-[minmax(0,1fr)] gap-5">
          <ResumeSuivi liaison={l} catalogue={catalogue} onModifier={onModifierSuivi} />
          <CarteLigneDeCommande
            commandes={['liaison synchroniser', 'liaison renouveler <id>', 'liaison couper <id> --oui']}
          />
        </div>
      </div>
    </div>
  );
}

/** Applications suivies, en résumé : domaines, choix une par une, systèmes, applications retirées. */
function ResumeSuivi({
  liaison: l,
  catalogue: c,
  onModifier,
}: {
  liaison: LiaisonNoosphere;
  catalogue: CatalogueLiaison | null;
  onModifier: () => void;
}) {
  const suivies = c?.applications.filter((a) => a.suivie) ?? [];
  const domaines = c?.domaines.filter((d) => d.suivi) ?? [];
  const parDomaine = suivies.filter((a) => a.domaineId && domaines.some((d) => d.id === a.domaineId));
  const unes = suivies.filter((a) => !parDomaine.includes(a));
  const retirees = suivies.filter((a) => estRetiree(a) || a.cycle.toLowerCase() === 'retiring');
  return (
    <Card>
      <CardHeader
        titre="Applications suivies"
        description={
          c?.luLe
            ? `${compte(suivies.length, 'application')}, dont ${parDomaine.length} par domaine.`
            : 'Catalogue de Noosphere pas encore lu.'
        }
        action={
          c?.luLe && (
            <Button taille="sm" icone={<Pencil className="size-4" aria-hidden />} onClick={onModifier}>
              Modifier
            </Button>
          )
        }
      />
      <dl className="grid gap-3 p-5 text-sm">
        {!c?.luLe ? (
          <p className="text-fg-subtle">
            Il se lit dès que les deux côtés ont confirmé l’empreinte ; choisissez ensuite les applications à suivre.
          </p>
        ) : (
          <>
            <div className="grid grid-cols-[minmax(0,6.5rem)_minmax(0,1fr)] gap-3">
              <dt className="text-fg-subtle">Domaines</dt>
              <dd className="flex flex-wrap gap-1">
                {domaines.length === 0 && <span className="text-fg-subtle">Aucun</span>}
                {domaines.map((d) => (
                  <Badge key={d.id}>
                    {d.nom} · {parDomaine.filter((a) => a.domaineId === d.id).length}
                  </Badge>
                ))}
              </dd>
            </div>
            <div className="grid grid-cols-[minmax(0,6.5rem)_minmax(0,1fr)] gap-3">
              <dt className="text-fg-subtle">Une par une</dt>
              <dd className="flex flex-wrap gap-1">
                {unes.length === 0 && <span className="text-fg-subtle">Aucune</span>}
                {unes.map((a) => (
                  <Badge key={a.id}>{a.nom}</Badge>
                ))}
              </dd>
            </div>
            <div className="grid grid-cols-[minmax(0,6.5rem)_minmax(0,1fr)] gap-3">
              <dt className="text-fg-subtle">Systèmes</dt>
              <dd>
                {compte(l.systemesSuivis, 'système géré', 'systèmes gérés')} par Noosphere ·{' '}
                <Link
                  to="/parametres/systemes?source=noosphere"
                  className="font-semibold text-accent-text hover:underline"
                >
                  Voir le catalogue
                </Link>
              </dd>
            </div>
            {retirees.length > 0 && (
              <div className="grid grid-cols-[minmax(0,6.5rem)_minmax(0,1fr)] gap-3">
                <dt className="text-fg-subtle">En retrait</dt>
                <dd>
                  {retirees.map((a) => a.nom).join(', ')} :{' '}
                  {retirees.length > 1 ? 'systèmes inactifs' : 'système inactif'} une fois retiré
                  {retirees.length > 1 ? 's' : ''}, jamais supprimé{retirees.length > 1 ? 's' : ''}.
                </dd>
              </div>
            )}
          </>
        )}
      </dl>
    </Card>
  );
}
