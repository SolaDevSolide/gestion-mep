import { GraduationCap, OctagonAlert, Plus, Save, Trash2 } from 'lucide-react';
import { useState } from 'react';
import { api } from '../api';
import { useAuth } from '../auth';
import { useAsync, useDraft, useReportDirty } from '../hooks';
import type { Expertise, SystemeCatalogue } from '../types';
import { Alerte } from '../ui/Alerte';
import { BoutonIcone, Button } from '../ui/Button';
import { Card, CardHeader } from '../ui/Card';
import { EmptyState } from '../ui/EmptyState';
import { Field, Input, Select } from '../ui/Input';
import { useToast } from '../ui/Toast';

interface Brouillon { nomAffiche: string; role: string; contact: string; expertises: Expertise[] }

/** « Mon profil » : nom affiché, rôle, contact et expertises (systèmes du catalogue) du compte connecté. */
export default function ProfilPage() {
  const { utilisateur, majUtilisateur } = useAuth();
  const toast = useToast();
  const { donnees: catalogue, erreur } = useAsync<SystemeCatalogue[]>(() => api.catalogueSystemes(), []);
  const source: Brouillon = {
    nomAffiche: utilisateur?.nomAffiche ?? '', role: utilisateur?.role ?? '', contact: utilisateur?.contact ?? '', expertises: utilisateur?.expertises ?? [],
  };
  const d = useDraft(source);
  useReportDirty(d.dirty);
  const v = d.value;
  const [envoi, setEnvoi] = useState(false);
  const [erreurSauvegarde, setErreurSauvegarde] = useState('');

  const systemes = catalogue ?? [];
  const nomSysteme = (id: string) => systemes.find((s) => s.id === id)?.nom ?? 'Système inconnu';
  const libres = systemes.filter((s) => s.actif && !v.expertises.some((e) => e.systemeCatalogueId === s.id));
  const incomplet = !v.nomAffiche.trim() || v.expertises.some((e) => !e.systemeCatalogueId);

  const changerExpertise = (i: number, patch: Partial<Expertise>) =>
    d.update({ expertises: v.expertises.map((e, k) => (k === i ? { ...e, ...patch } : e)) });

  async function enregistrer() {
    setEnvoi(true);
    setErreurSauvegarde('');
    try {
      const u = await api.modifierMonProfil({
        nomAffiche: v.nomAffiche.trim(), role: v.role.trim(), contact: v.contact.trim(),
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
      <p className="mt-1 text-sm text-fg-subtle">Ces informations pré-remplissent votre fiche quand on vous ajoute à une MEP, et permettent de vous suggérer comme responsable technique des systèmes que vous connaissez.</p>

      {(erreur || erreurSauvegarde) && <Alerte ton="danger" icone={OctagonAlert} role="alert" className="mt-6">{erreurSauvegarde || erreur}</Alerte>}

      <Card className="mt-6">
        <CardHeader titre="Identité" description={`Compte : ${utilisateur?.login ?? ''}`} />
        <div className="grid gap-4 px-5 py-5 md:grid-cols-2">
          <Field label="Nom affiché *" className="md:col-span-2">
            {(id) => <Input id={id} value={v.nomAffiche} onChange={(e) => d.update({ nomAffiche: e.target.value })} />}
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
          description="Les systèmes du catalogue que vous connaissez, avec un mot sur votre rôle (« Expert de la zone facturation »)."
          action={<Button taille="sm" icone={<Plus className="size-4" aria-hidden />} disabled={libres.length === 0} onClick={() => d.update({ expertises: [...v.expertises, { systemeCatalogueId: libres[0].id, detail: '' }] })}>Ajouter une expertise</Button>}
        />
        <div className="px-5 py-5">
          {v.expertises.length === 0 ? (
            <EmptyState icone={GraduationCap} titre="Aucune expertise" description="Ajoutez les systèmes que vous connaissez pour être suggéré comme responsable technique." />
          ) : (
            <ul className="space-y-3">
              {v.expertises.map((e, i) => (
                <li key={i} className="grid gap-3 rounded-lg border border-line p-3 md:grid-cols-[14rem_minmax(0,1fr)_auto] md:items-end">
                  <Field label="Système">
                    {(id) => (
                      <Select id={id} value={e.systemeCatalogueId} onChange={(ev) => changerExpertise(i, { systemeCatalogueId: ev.target.value })}>
                        <option value={e.systemeCatalogueId}>{nomSysteme(e.systemeCatalogueId)}</option>
                        {libres.map((s) => <option key={s.id} value={s.id}>{s.nom}</option>)}
                      </Select>
                    )}
                  </Field>
                  <Field label="Détail">
                    {(id) => <Input id={id} value={e.detail} placeholder="ex. Expert de la zone facturation" onChange={(ev) => changerExpertise(i, { detail: ev.target.value })} />}
                  </Field>
                  <BoutonIcone label={`Supprimer l’expertise ${nomSysteme(e.systemeCatalogueId)}`} onClick={() => d.update({ expertises: v.expertises.filter((_, k) => k !== i) })}><Trash2 className="size-4" aria-hidden /></BoutonIcone>
                </li>
              ))}
            </ul>
          )}
        </div>
        <div className="flex flex-wrap items-center gap-3 border-t border-line bg-surface-2/50 px-5 py-3">
          <Button variante="primaire" icone={<Save className="size-4" aria-hidden />} disabled={!d.dirty || incomplet} chargement={envoi} onClick={() => void enregistrer()}>Enregistrer</Button>
          {d.dirty && <Button variante="fantome" onClick={d.reset}>Annuler les modifications</Button>}
        </div>
      </Card>
    </main>
  );
}
