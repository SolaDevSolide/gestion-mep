import { CircleCheck, GraduationCap, Link2, Link2Off, OctagonAlert, Plus, Save, Trash2 } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { api } from '../api';
import { useAuth } from '../auth';
import { useAsync, useDraft, useReportDirty } from '../hooks';
import { useReferentiel } from '../referentiels';
import type { Expertise } from '../types';
import { Alerte } from '../ui/Alerte';
import { Apparition } from '../ui/Apparition';
import { ErreurChargement } from '../ui/ErreurChargement';
import { Squelette, SqueletteFormulaire } from '../ui/Squelette';
import { BoutonIcone, Button } from '../ui/Button';
import { Card, CardHeader } from '../ui/Card';
import { ConfirmDialog } from '../ui/Dialog';
import { EmptyState } from '../ui/EmptyState';
import { Field, Input, Select } from '../ui/Input';
import { useToast } from '../ui/Toast';
import { FormulaireMotDePasse } from './FormulaireMotDePasse';

/** Retour de la connexion unique : le serveur renvoie vers /profil?sso=CODE. */
const RETOURS_SSO: Record<string, string> = {
  'deja-lie': 'Ce compte Microsoft est déjà lié à un autre compte.',
  'sso-session': 'La demande de liaison a expiré, recommencez.',
  'sso-injoignable': 'Le fournisseur de connexion est injoignable depuis le serveur. Réessayez plus tard.',
  'sso-echec': 'La liaison a échoué. Réessayez ou contactez un administrateur.',
  'compte-desactive': 'Ce compte est désactivé. Contactez un administrateur.',
};

interface Brouillon {
  nomAffiche: string;
  role: string;
  contact: string;
  expertises: Expertise[];
}

/** « Mon profil » : nom affiché, rôle, contact et expertises (systèmes du catalogue) du compte connecté. */
export default function ProfilPage() {
  const { utilisateur, majUtilisateur } = useAuth();
  const toast = useToast();
  const catalogueLu = useReferentiel('catalogue');
  const catalogue = catalogueLu.donnees;
  const source: Brouillon = {
    nomAffiche: utilisateur?.nomAffiche ?? '',
    role: utilisateur?.role ?? '',
    contact: utilisateur?.contact ?? '',
    expertises: utilisateur?.expertises ?? [],
  };
  const d = useDraft(source);
  useReportDirty(d.dirty, 'Mon profil', d);
  const v = d.value;
  const [envoi, setEnvoi] = useState(false);
  const [erreurSauvegarde, setErreurSauvegarde] = useState('');

  const [params, setParams] = useSearchParams();
  // Code lu une fois à l'arrivée, puis retiré de l'adresse pour qu'un rafraîchissement ne le rejoue pas.
  const [codeSso] = useState(() => params.get('sso'));
  useEffect(() => {
    if (params.has('sso')) {
      const suite = new URLSearchParams(params);
      suite.delete('sso');
      setParams(suite, { replace: true });
    }
  }, [params, setParams]);
  // Si l'état de la connexion unique ne se charge pas, la carte reste simplement masquée (la liaison est facultative).
  const sso = useAsync(() => api.ssoEtat(), []);
  // La page (carte de la connexion unique comprise) apparaît d'un bloc une fois le catalogue et l'état SSO lus.
  const [revele, setRevele] = useState(false);
  if (!revele && catalogueLu.pret && (sso.donnees !== null || sso.echec !== null)) setRevele(true);
  const etatSso = sso.donnees;
  const [liaison, setLiaison] = useState(false);
  const [delier, setDelier] = useState(false);

  async function lier() {
    setLiaison(true);
    try {
      const { url } = await api.ssoLier();
      window.location.assign(url);
    } catch (e) {
      toast.erreur(e instanceof Error ? e.message : 'Liaison impossible.');
      setLiaison(false);
    }
  }

  async function delierMoi(): Promise<boolean> {
    try {
      await api.ssoDelierMoi();
      sso.recharger();
      toast.succes('Compte délié.');
      return true;
    } catch (e) {
      toast.erreur(e instanceof Error ? e.message : 'Dissociation impossible.');
      return false;
    }
  }

  const systemes = catalogue ?? [];
  // Catalogue non chargé : pas de « Système inconnu » trompeur.
  const nomSysteme = (id: string) =>
    systemes.find((s) => s.id === id)?.nom ??
    (catalogue === null ? 'Système (catalogue non chargé)' : 'Système inconnu');
  const libres = systemes.filter((s) => s.actif && !v.expertises.some((e) => e.systemeCatalogueId === s.id));
  const incomplet = !v.nomAffiche.trim() || v.expertises.some((e) => !e.systemeCatalogueId);

  const changerExpertise = (i: number, patch: Partial<Expertise>) =>
    d.update({ expertises: v.expertises.map((e, k) => (k === i ? { ...e, ...patch } : e)) });

  async function enregistrer() {
    setEnvoi(true);
    setErreurSauvegarde('');
    try {
      const u = await api.modifierMonProfil({
        nomAffiche: v.nomAffiche.trim(),
        role: v.role.trim(),
        contact: v.contact.trim(),
        expertises: v.expertises.map((e) => ({ systemeCatalogueId: e.systemeCatalogueId, detail: e.detail.trim() })),
      });
      majUtilisateur(u);
      d.reset();
      toast.succes('Profil enregistré.');
    } catch (e) {
      setErreurSauvegarde(e instanceof Error ? e.message : 'Enregistrement impossible.');
    } finally {
      setEnvoi(false);
    }
  }

  return (
    <main className="mx-auto w-full max-w-3xl px-6 py-8">
      <h1 className="text-2xl font-semibold tracking-tight">Mon profil</h1>
      <p className="mt-1 text-sm text-fg-subtle">
        Pré-remplit votre fiche dans les MEP et vous suggère comme responsable technique.
      </p>

      <Apparition pret={revele} libelle="Chargement du profil…" squelette={<SqueletteProfil />}>
        {erreurSauvegarde && (
          <Alerte ton="danger" icone={OctagonAlert} role="alert" className="mt-6">
            {erreurSauvegarde}
          </Alerte>
        )}

        {codeSso === 'lie' && (
          <Alerte ton="ok" icone={CircleCheck} className="mt-6">
            Compte Microsoft lié.
          </Alerte>
        )}
        {codeSso && codeSso !== 'lie' && (
          <Alerte ton="danger" icone={OctagonAlert} role="alert" className="mt-6">
            {RETOURS_SSO[codeSso] ?? 'La liaison a échoué. Réessayez ou contactez un administrateur.'}
          </Alerte>
        )}

        <Card className="mt-6">
          <CardHeader titre="Identité" description={`Compte : ${utilisateur?.login ?? ''}`} />
          <div className="grid gap-4 px-5 py-5 md:grid-cols-2">
            <Field label="Nom affiché *" className="md:col-span-2">
              {(id) => (
                <Input id={id} value={v.nomAffiche} onChange={(e) => d.update({ nomAffiche: e.target.value })} />
              )}
            </Field>
            <Field label="Rôle" hint="ex. Développeur API, DBA, Pilote.">
              {(id) => <Input id={id} value={v.role} onChange={(e) => d.update({ role: e.target.value })} />}
            </Field>
            <Field label="Contact" hint="e-mail ou téléphone.">
              {(id) => <Input id={id} value={v.contact} onChange={(e) => d.update({ contact: e.target.value })} />}
            </Field>
          </div>
        </Card>

        <Card className="mt-6">
          <CardHeader
            titre="Expertises"
            description="Systèmes que vous connaissez."
            action={
              <Button
                taille="sm"
                icone={<Plus className="size-4" aria-hidden />}
                disabled={libres.length === 0 || catalogue === null}
                onClick={() =>
                  d.update({ expertises: [...v.expertises, { systemeCatalogueId: libres[0].id, detail: '' }] })
                }
              >
                Ajouter une expertise
              </Button>
            }
          />
          <div className="px-5 py-5">
            {catalogueLu.erreur != null && (
              <ErreurChargement
                variante="ligne"
                className="mb-3"
                erreur={catalogueLu.erreur}
                titre="Catalogue des systèmes non chargé"
                complement="Les expertises ne peuvent pas être modifiées pour l’instant."
                onReessayer={catalogueLu.recharger}
                enCours={catalogueLu.enCours}
              />
            )}
            {v.expertises.length === 0 ? (
              <EmptyState icone={GraduationCap} titre="Aucune expertise" description="Ajoutez vos systèmes." />
            ) : (
              <ul className="space-y-3">
                {v.expertises.map((e, i) => (
                  <li
                    key={i}
                    className="grid gap-3 rounded-lg border border-line p-3 md:grid-cols-[14rem_minmax(0,1fr)_auto] md:items-end"
                  >
                    <Field label="Système">
                      {(id) => (
                        <Select
                          id={id}
                          disabled={catalogue === null}
                          value={e.systemeCatalogueId}
                          onChange={(ev) => changerExpertise(i, { systemeCatalogueId: ev.target.value })}
                        >
                          <option value={e.systemeCatalogueId}>{nomSysteme(e.systemeCatalogueId)}</option>
                          {libres.map((s) => (
                            <option key={s.id} value={s.id}>
                              {s.nom}
                            </option>
                          ))}
                        </Select>
                      )}
                    </Field>
                    <Field label="Détail">
                      {(id) => (
                        <Input
                          id={id}
                          value={e.detail}
                          placeholder="ex. Expert de la zone facturation"
                          onChange={(ev) => changerExpertise(i, { detail: ev.target.value })}
                        />
                      )}
                    </Field>
                    <BoutonIcone
                      label={`Supprimer l’expertise ${nomSysteme(e.systemeCatalogueId)}`}
                      onClick={() => d.update({ expertises: v.expertises.filter((_, k) => k !== i) })}
                    >
                      <Trash2 className="size-4" aria-hidden />
                    </BoutonIcone>
                  </li>
                ))}
              </ul>
            )}
          </div>
          <div className="flex flex-wrap items-center gap-3 border-t border-line bg-surface-2/50 px-5 py-3">
            <Button
              variante="primaire"
              icone={<Save className="size-4" aria-hidden />}
              disabled={!d.dirty || incomplet}
              chargement={envoi}
              onClick={() => void enregistrer()}
            >
              Enregistrer
            </Button>
            {d.dirty && (
              <Button variante="fantome" onClick={d.reset}>
                Annuler les modifications
              </Button>
            )}
          </div>
        </Card>

        {etatSso && (etatSso.disponible || etatSso.identifiant !== null) && (
          <Card className="apparition mt-6">
            <CardHeader
              titre="Connexion unique"
              description={
                etatSso.identifiant === null
                  ? 'Liez votre compte pour vous connecter sans saisir de mot de passe.'
                  : 'Votre compte est lié : vous pouvez vous connecter sans mot de passe.'
              }
            />
            <div className="flex flex-wrap items-center justify-between gap-3 px-5 py-5">
              {etatSso.identifiant === null ? (
                <>
                  <p className="text-sm text-fg-muted">Vous serez redirigé vers le fournisseur pour vous identifier.</p>
                  <Button
                    variante="primaire"
                    icone={<Link2 className="size-4" aria-hidden />}
                    chargement={liaison}
                    onClick={() => void lier()}
                  >
                    Lier mon compte
                  </Button>
                </>
              ) : (
                <>
                  <p className="min-w-0 text-sm">
                    Lié à <span className="font-mono font-semibold break-all">{etatSso.identifiant}</span>
                  </p>
                  {etatSso.peutDelier && (
                    <Button icone={<Link2Off className="size-4" aria-hidden />} onClick={() => setDelier(true)}>
                      Délier
                    </Button>
                  )}
                </>
              )}
            </div>
          </Card>
        )}
        <ConfirmDialog
          open={delier}
          onOpenChange={setDelier}
          titre="Délier votre compte ?"
          description="Vous ne pourrez plus vous connecter par la connexion unique ; votre mot de passe reste valable."
          libelleConfirmer="Délier"
          onConfirmer={delierMoi}
        />

        <Card className="mt-6">
          <CardHeader
            titre="Mot de passe"
            description="Choisissez un mot de passe personnel d’au moins 10 caractères."
          />
          <div className="max-w-md px-5 py-5">
            <FormulaireMotDePasse
              libelleAncien="Mot de passe actuel"
              onChange={() => toast.succes('Mot de passe modifié.')}
            />
          </div>
        </Card>
      </Apparition>
    </main>
  );
}

/** Silhouette des cartes du profil (identité, expertises), le temps de lire le catalogue et l'état de la connexion. */
function SqueletteProfil() {
  return (
    <div className="mt-6 grid gap-6">
      <div className="rounded-xl border border-line bg-surface p-5">
        <Squelette className="h-5 w-32" />
        <SqueletteFormulaire champs={3} className="mt-5" />
      </div>
      <div className="rounded-xl border border-line bg-surface p-5">
        <Squelette className="h-5 w-32" />
        <Squelette className="mt-5 h-16 w-full" />
      </div>
    </div>
  );
}
