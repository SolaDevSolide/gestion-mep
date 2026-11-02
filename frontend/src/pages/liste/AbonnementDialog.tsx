// Abonnement au calendrier (#129) : une adresse secrète par personne, à coller dans son agenda. Le serveur n'en garde que
// l'empreinte : l'adresse n'est montrée qu'à sa création ou sa régénération ; la régénérer coupe l'ancienne.
import { Info, KeyRound, OctagonAlert, RefreshCw, Trash2 } from 'lucide-react';
import { useEffect, useId, useState } from 'react';
import { api } from '../../api';
import { formatDateHeure } from '../../format';
import { estVide, urlAbonnementFiltre, type FiltreMeps } from '../../rechercheMeps';
import type { AbonnementCalendrier } from '../../types';
import { Alerte } from '../../ui/Alerte';
import { Button } from '../../ui/Button';
import { Checkbox } from '../../ui/Checkbox';
import { CodeBlock } from '../../ui/CodeBlock';
import { ConfirmDialog, Dialog } from '../../ui/Dialog';
import { ErreurChargement } from '../../ui/ErreurChargement';
import { EnChargement, Squelette } from '../../ui/Squelette';
import { useToast } from '../../ui/Toast';
import { cn } from '../../ui/cn';

/** Adresse affichée : celle du serveur, avec les filtres choisis et sans les gels si on n'en veut pas. */
export function adresseAbonnement(url: string, f: FiltreMeps | null, avecGels: boolean): string {
  const base = f ? urlAbonnementFiltre(url, f) : url;
  return avecGels ? base : `${base}${base.includes('?') ? '&' : '?'}gels=0`;
}

function Choix({
  nom,
  coche,
  onChange,
  titre,
  detail,
  disabled,
}: {
  nom: string;
  coche: boolean;
  onChange: () => void;
  titre: string;
  detail?: string;
  disabled?: boolean;
}) {
  const id = useId();
  return (
    <label
      htmlFor={id}
      className={cn(
        'flex cursor-pointer items-start gap-2.5 rounded-lg border px-3 py-2.5 has-[:focus-visible]:ring-2 has-[:focus-visible]:ring-accent',
        coche ? 'border-accent bg-accent-soft' : 'border-line',
        disabled && 'cursor-not-allowed opacity-60',
      )}
    >
      <input
        id={id}
        type="radio"
        name={nom}
        checked={coche}
        disabled={disabled}
        onChange={onChange}
        className="mt-0.5 size-4 shrink-0 accent-[var(--accent)]"
      />
      <span className="text-sm">
        <span className="font-medium">{titre}</span>
        {detail && <span className="block text-xs text-fg-subtle">{detail}</span>}
      </span>
    </label>
  );
}

export function AbonnementDialog({
  open,
  onOpenChange,
  f,
}: {
  open: boolean;
  onOpenChange: (o: boolean) => void;
  /** Filtres de la liste en cours. */
  f: FiltreMeps;
}) {
  const toast = useToast();
  const nom = useId();
  const [etat, setEtat] = useState<AbonnementCalendrier | null>(null);
  const [echec, setEchec] = useState<unknown>(null);
  const [essai, setEssai] = useState(0);
  const [avecFiltres, setAvecFiltres] = useState(true);
  const [avecGels, setAvecGels] = useState(true);
  const [envoi, setEnvoi] = useState(false);
  const [confirmer, setConfirmer] = useState<'regenerer' | 'revoquer' | null>(null);
  const sansFiltre = estVide({ ...f, du: '', au: '', phase: '', echec: false });

  useEffect(() => {
    if (!open) return;
    let annule = false;
    setEtat(null);
    setEchec(null);
    api
      .abonnementCalendrier()
      .then((e) => {
        if (!annule) setEtat(e);
      })
      .catch((e: unknown) => {
        if (!annule) setEchec(e ?? new Error('Chargement impossible.'));
      });
    return () => {
      annule = true;
    };
  }, [open, essai]);

  async function regenerer() {
    setEnvoi(true);
    try {
      setEtat(await api.regenererAbonnement());
      toast.succes(etat?.actif ? 'Nouvelle adresse créée : l’ancienne ne répond plus.' : 'Adresse d’abonnement créée.');
      return true;
    } catch (e) {
      toast.erreur(e instanceof Error ? e.message : 'Création impossible.');
      return false;
    } finally {
      setEnvoi(false);
    }
  }

  async function revoquer() {
    try {
      await api.revoquerAbonnement();
      setEtat({ actif: false, creeLe: null, dernierAcces: null, url: null });
      toast.succes('Adresse révoquée : votre agenda ne reçoit plus les MEP.');
      return true;
    } catch (e) {
      toast.erreur(e instanceof Error ? e.message : 'Révocation impossible.');
      return false;
    }
  }

  const filtres = avecFiltres && !sansFiltre;
  return (
    <>
      <Dialog
        open={open}
        onOpenChange={onOpenChange}
        titre="S’abonner au calendrier"
        description="Ajoutez les MEP à Outlook, Thunderbird ou l’agenda de votre téléphone. Il se met à jour tout seul (toutes les heures environ, selon l’agenda)."
        pied={
          <>
            <span className="mr-auto text-xs text-fg-subtle">
              Export ponctuel : « Exporter .ics » télécharge la vue affichée.
            </span>
            <Button onClick={() => onOpenChange(false)}>Fermer</Button>
          </>
        }
      >
        <div className="space-y-4">
          <fieldset className="space-y-2">
            <legend className="mb-1.5 text-ui font-medium">Ce que contient l’abonnement</legend>
            <Choix
              nom={nom}
              coche={filtres}
              disabled={sansFiltre}
              onChange={() => setAvecFiltres(true)}
              titre="Les filtres actuels"
              detail={
                sansFiltre
                  ? 'Aucun filtre n’est choisi dans la liste.'
                  : 'Système, environnement, équipe, personne, état et recherche de la liste.'
              }
            />
            <Choix
              nom={nom}
              coche={!filtres}
              onChange={() => setAvecFiltres(false)}
              titre="Toutes les MEP que je peux voir"
            />
          </fieldset>
          <Checkbox
            checked={avecGels}
            onChange={setAvecGels}
            label="Ajouter les périodes de gel"
            description="Chacune devient un événement « Gel : … » qui n’occupe pas l’agenda."
          />

          {echec !== null && (
            <ErreurChargement
              variante="ligne"
              erreur={echec}
              titre="État de l’abonnement non chargé"
              onReessayer={() => setEssai((n) => n + 1)}
            />
          )}
          {etat === null && echec === null && (
            <EnChargement libelle="Chargement de l’abonnement…" className="space-y-2">
              <Squelette className="h-4 w-40" />
              <Squelette className="h-10 w-full" />
            </EnChargement>
          )}
          {etat !== null && (
            <section aria-label="Adresse d’abonnement personnelle" className="apparition space-y-2.5">
              <h3 className="text-ui font-medium">Adresse secrète personnelle</h3>
              {etat.url ? (
                <>
                  <CodeBlock
                    code={adresseAbonnement(etat.url, filtres ? f : null, avecGels)}
                    titre="adresse d’abonnement"
                  />
                  <Alerte ton="warn" icone={KeyRound} role="status">
                    Copiez-la maintenant : elle ne sera plus affichée. Quiconque l’a voit ces MEP (titre, horaires,
                    systèmes, pilote, lien vers la fiche), avec vos droits de lecture. Rien du plan ni des commentaires.
                  </Alerte>
                </>
              ) : etat.actif ? (
                <p className="text-sm text-fg-muted">
                  Créée le {formatDateHeure(etat.creeLe)} ·{' '}
                  {etat.dernierAcces ? `lue par un agenda le ${formatDateHeure(etat.dernierAcces)}` : 'pas encore lue'}.
                  Elle n’est montrée qu’à sa création : régénérez-la pour en obtenir une nouvelle.
                </p>
              ) : (
                <p className="text-sm text-fg-muted">
                  Vous n’avez pas d’adresse d’abonnement. Elle est personnelle et donne vos droits de lecture.
                </p>
              )}
              <div className="flex flex-wrap justify-end gap-2">
                {etat.actif ? (
                  <>
                    <Button
                      taille="sm"
                      variante="danger-doux"
                      icone={<Trash2 className="size-4" aria-hidden />}
                      onClick={() => setConfirmer('revoquer')}
                    >
                      Révoquer
                    </Button>
                    <Button
                      taille="sm"
                      icone={<RefreshCw className="size-4" aria-hidden />}
                      onClick={() => setConfirmer('regenerer')}
                    >
                      Régénérer l’adresse
                    </Button>
                  </>
                ) : (
                  <Button
                    taille="sm"
                    variante="primaire"
                    icone={<KeyRound className="size-4" aria-hidden />}
                    chargement={envoi}
                    onClick={() => void regenerer()}
                  >
                    Créer mon adresse
                  </Button>
                )}
              </div>
            </section>
          )}
          <Alerte ton="wait" icone={Info}>
            Les filtres et les périodes de gel se règlent dans l’adresse : changer de choix ici ne demande pas de
            nouvelle adresse, seulement de recopier celle affichée.
          </Alerte>
        </div>
      </Dialog>
      <ConfirmDialog
        open={confirmer !== null}
        onOpenChange={(o) => {
          if (!o) setConfirmer(null);
        }}
        titre={confirmer === 'revoquer' ? 'Révoquer l’adresse d’abonnement ?' : 'Régénérer l’adresse d’abonnement ?'}
        description={
          confirmer === 'revoquer'
            ? 'Votre agenda ne recevra plus les MEP. Vous pourrez créer une autre adresse ensuite.'
            : 'L’adresse actuelle cesse aussitôt de répondre : il faudra coller la nouvelle dans votre agenda.'
        }
        libelleConfirmer={confirmer === 'revoquer' ? 'Révoquer' : 'Régénérer'}
        variante="danger"
        onConfirmer={confirmer === 'revoquer' ? revoquer : regenerer}
      >
        <Alerte ton="warn" icone={OctagonAlert}>
          Faites-le si l’adresse a pu être vue par quelqu’un d’autre.
        </Alerte>
      </ConfirmDialog>
    </>
  );
}
