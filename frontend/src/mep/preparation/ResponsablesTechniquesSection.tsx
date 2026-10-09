import { Code2, Pencil, Phone, Plus, Trash2, UserCheck, UserX } from 'lucide-react';
import { useEffect, useState, type FormEvent } from 'react';
import { api } from '../../api';
import { useMep } from '../../mepContext';
import type { DisponibiliteJourJ, ResponsableTechnique } from '../../types';
import { Alerte } from '../../ui/Alerte';
import { Avatar } from '../../ui/Avatar';
import { BoutonIcone, Button } from '../../ui/Button';
import { MultiSelect } from '../../ui/Combobox';
import { ConfirmDialog, Dialog } from '../../ui/Dialog';
import { EmptyState } from '../../ui/EmptyState';
import { Field, Input, Select, Textarea } from '../../ui/Input';
import { Segmented } from '../../ui/Segmented';
import { Badge } from '../../ui/StatusPill';
import { Tooltip } from '../../ui/Tooltip';
import { SectionCarte } from './Formulaires';

/** Badge de disponibilité le jour J (réutilisé par l'encart « À appeler en cas de problème »). */
export function BadgeDisponibilite({ valeur }: { valeur: DisponibiliteJourJ }) {
  return valeur === 'Joignable'
    ? <Badge ton="ok"><UserCheck className="size-3" aria-hidden />Joignable</Badge>
    : <Badge ton="danger"><UserX className="size-3" aria-hidden />Absent</Badge>;
}

const VIDE: ResponsableTechnique = { participantId: '', systemeIds: [], perimetre: '', disponibilite: 'Joignable', contactJourJ: '' };

/** Section « Responsables techniques » : qui a développé quoi, et comment le joindre le jour J. */
export function ResponsablesTechniquesSection() {
  const { mep, run } = useMep();
  const edition = mep.droits.peutEditerContenu;
  const [forme, setForme] = useState<{ existant?: ResponsableTechnique } | null>(null);
  const [suppr, setSuppr] = useState<ResponsableTechnique | null>(null);
  const [brouillon, setBrouillon] = useState<ResponsableTechnique>(VIDE);
  const [envoi, setEnvoi] = useState(false);

  const nom = (id: string) => mep.participants.find((p) => p.id === id)?.nom ?? 'Participant inconnu';
  const dejaResponsables = new Set(mep.responsablesTechniques.map((r) => r.participantId));
  const candidats = mep.participants.filter((p) => !dejaResponsables.has(p.id) || p.id === forme?.existant?.participantId);

  useEffect(() => {
    if (forme) setBrouillon(forme.existant ?? { ...VIDE, participantId: candidats[0]?.id ?? '' });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [forme]);

  const enregistrer = (liste: ResponsableTechnique[], message: string) =>
    run(() => api.modifierResponsablesTechniques(mep.id, mep.version, liste), message);

  async function valider(e: FormEvent) {
    e.preventDefault();
    setEnvoi(true);
    try {
      const ex = forme?.existant;
      const nouveau = { ...brouillon, perimetre: brouillon.perimetre.trim(), contactJourJ: brouillon.contactJourJ.trim() };
      const liste = ex
        ? mep.responsablesTechniques.map((r) => (r.participantId === ex.participantId ? nouveau : r))
        : [...mep.responsablesTechniques, nouveau];
      if (await enregistrer(liste, ex ? 'Responsable technique modifié.' : 'Responsable technique ajouté.')) setForme(null);
    } finally {
      setEnvoi(false);
    }
  }

  const aucunCandidat = !forme?.existant && candidats.length === 0;

  return (
    <SectionCarte
      numero={3}
      titre="Responsables techniques"
      description="Les développeurs et sachants de cette MEP, distincts du pilote : ce qu’ils ont développé, leur disponibilité et leur contact le jour J."
      action={edition && <Button variante="primaire" taille="sm" icone={<Plus className="size-4" aria-hidden />} onClick={() => setForme({})}>Ajouter</Button>}
    >
      {mep.responsablesTechniques.length === 0 ? (
        <EmptyState icone={Code2} titre="Aucun responsable technique" description="Désignez au moins une personne à appeler si un problème survient pendant l’intervention." />
      ) : (
        <ul className="divide-y divide-line overflow-hidden rounded-lg border border-line">
          {mep.responsablesTechniques.map((r) => {
            const systemes = r.systemeIds.map((id) => mep.systemes.find((s) => s.id === id)?.nom).filter((n): n is string => !!n);
            const estPilote = r.participantId === mep.piloteId;
            return (
              <li key={r.participantId} className="flex flex-wrap items-start gap-x-4 gap-y-2 px-4 py-3">
                <div className="flex min-w-0 flex-1 basis-56 items-center gap-3">
                  <Avatar nom={nom(r.participantId)} />
                  <div className="min-w-0">
                    <p className="flex flex-wrap items-center gap-2 font-medium">
                      {nom(r.participantId)}
                      <BadgeDisponibilite valeur={r.disponibilite} />
                      {estPilote && <Badge ton="run">Pilote</Badge>}
                    </p>
                    <p className="flex items-center gap-1 text-xs text-fg-subtle"><Phone className="size-3" aria-hidden />{r.contactJourJ || 'Contact non renseigné'}</p>
                  </div>
                </div>
                <div className="min-w-0 flex-1 basis-56 space-y-1.5 text-sm text-fg-muted">
                  <p className="whitespace-pre-line">{r.perimetre || <span className="text-fg-subtle">Périmètre non précisé</span>}</p>
                  {systemes.length > 0 && (
                    <p className="flex flex-wrap gap-1" aria-label="Systèmes développés">{systemes.map((s) => <Badge key={s} ton="run">{s}</Badge>)}</p>
                  )}
                </div>
                {edition && (
                  <div className="flex shrink-0 items-center gap-1">
                    <Tooltip content="Modifier"><BoutonIcone label={`Modifier ${nom(r.participantId)}`} taille="sm" onClick={() => setForme({ existant: r })}><Pencil className="size-4" aria-hidden /></BoutonIcone></Tooltip>
                    <Tooltip content="Retirer"><BoutonIcone label={`Retirer ${nom(r.participantId)}`} taille="sm" onClick={() => setSuppr(r)}><Trash2 className="size-4" aria-hidden /></BoutonIcone></Tooltip>
                  </div>
                )}
              </li>
            );
          })}
        </ul>
      )}

      <Dialog
        open={forme !== null}
        onOpenChange={(o) => { if (!o) setForme(null); }}
        titre={forme?.existant ? 'Modifier le responsable technique' : 'Ajouter un responsable technique'}
        description="Un participant n’apparaît qu’une fois. Ajoutez d’abord la personne dans « Personnes » si elle n’y figure pas."
        pied={
          <>
            <Button variante="fantome" onClick={() => setForme(null)}>Annuler</Button>
            <Button type="submit" form="formulaire-rt" variante="primaire" chargement={envoi} disabled={!brouillon.participantId}>{forme?.existant ? 'Enregistrer' : 'Ajouter'}</Button>
          </>
        }
      >
        <form id="formulaire-rt" onSubmit={(e) => void valider(e)} className="grid gap-4 sm:grid-cols-2">
          {aucunCandidat && <div className="sm:col-span-2"><Alerte ton="wait" icone={UserX}>Tous les participants sont déjà responsables techniques.</Alerte></div>}
          <Field label="Participant *" className="sm:col-span-2">
            {(id) => (
              <Select id={id} value={brouillon.participantId} disabled={!!forme?.existant} onChange={(e) => setBrouillon({ ...brouillon, participantId: e.target.value })}>
                {!brouillon.participantId && <option value="">— choisir une personne —</option>}
                {candidats.map((p) => <option key={p.id} value={p.id}>{p.nom}{p.role ? ` (${p.role})` : ''}</option>)}
              </Select>
            )}
          </Field>
          <Field label="Systèmes développés" hint="Facultatif : ce que cette personne a développé." className="sm:col-span-2">
            {(id) => (
              <MultiSelect
                id={id}
                options={mep.systemes.map((s) => ({ valeur: s.id, libelle: s.nom, detail: s.cible }))}
                valeurs={brouillon.systemeIds}
                onChange={(systemeIds) => setBrouillon({ ...brouillon, systemeIds })}
                placeholder="Aucun système précisé"
                placeholderRecherche="Rechercher un système…"
                vide="Aucun système dans cette MEP."
              />
            )}
          </Field>
          <Field label="Périmètre" className="sm:col-span-2">
            {(id) => <Textarea id={id} rows={2} value={brouillon.perimetre} onChange={(e) => setBrouillon({ ...brouillon, perimetre: e.target.value })} placeholder="ex. API de facturation et ses migrations de base" />}
          </Field>
          <Field label="Disponibilité le jour J">
            {() => (
              <Segmented<DisponibiliteJourJ>
                label="Disponibilité le jour J"
                valeur={brouillon.disponibilite}
                onChange={(disponibilite) => setBrouillon({ ...brouillon, disponibilite })}
                options={[{ valeur: 'Joignable', libelle: 'Joignable' }, { valeur: 'Absent', libelle: 'Absent' }]}
              />
            )}
          </Field>
          <Field label="Contact le jour J">
            {(id) => <Input id={id} value={brouillon.contactJourJ} onChange={(e) => setBrouillon({ ...brouillon, contactJourJ: e.target.value })} placeholder="téléphone, astreinte, canal" />}
          </Field>
        </form>
      </Dialog>

      <ConfirmDialog
        open={suppr !== null}
        onOpenChange={(o) => { if (!o) setSuppr(null); }}
        titre={`Retirer ${suppr ? nom(suppr.participantId) : ''} des responsables techniques ?`}
        description="La personne reste dans « Personnes »."
        libelleConfirmer="Retirer"
        variante="danger"
        onConfirmer={() => suppr ? enregistrer(mep.responsablesTechniques.filter((r) => r.participantId !== suppr.participantId), 'Responsable technique retiré.') : false}
      />
    </SectionCarte>
  );
}
