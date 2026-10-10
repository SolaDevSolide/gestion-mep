import { Save } from 'lucide-react';
import { useContext } from 'react';
import { api } from '../../api';
import { AuthContext } from '../../auth';
import { depuisInputDateTime, formatDateHeure, versInputDateTime } from '../../format';
import { useDraft, useReportDirty } from '../../hooks';
import { useMep } from '../../mepContext';
import type { MepIdentiteSaisie } from '../../types';
import { AvisConflit } from '../../ui/AvisConflit';
import { Button } from '../../ui/Button';
import { ChampEnvironnement } from '../../ui/ChampEnvironnement';
import { Checkbox } from '../../ui/Checkbox';
import { MultiSelect } from '../../ui/Combobox';
import { Field, Input, Select, Textarea } from '../../ui/Input';
import { SectionCarte, vide } from './Formulaires';

export function IdentiteSection() {
  const { mep, run } = useMep();
  const source: Omit<MepIdentiteSaisie, 'version'> = {
    outil: mep.outil, versionActuelle: mep.versionActuelle, versionCible: mep.versionCible,
    objet: mep.objet, impacts: mep.impacts,
    fuseauHoraire: mep.fuseauHoraire, indisponibiliteMinutes: mep.indisponibiliteMinutes, piloteId: mep.piloteId,
    suppleantIds: mep.suppleantIds, echeanceRepli: mep.echeanceRepli,
    environnement: mep.environnement, bilanActive: mep.bilanActive,
  };
  const d = useDraft(source);
  useReportDirty(d.dirty);
  const v = d.value;

  const edition = mep.droits.peutEditerContenu;
  const peutEcheance = edition || mep.droits.estPilote;
  const lecture = !edition;
  // Pilote et suppléants : le pilote actuel, un suppléant ou un administrateur (tout éditeur tant qu'aucun pilote n'est désigné).
  const admin = useContext(AuthContext)?.utilisateur?.estAdmin ?? false;
  const pilotage = edition && (mep.piloteId === null || mep.droits.estPilote || admin);

  const personnes = (valeur: string | null, set: (x: string | null) => void) => (id: string) => (
    <Select id={id} value={valeur ?? ''} disabled={!pilotage} onChange={(e) => set(vide(e.target.value))}>
      <option value="">— choisir —</option>
      {mep.participants.map((p) => <option key={p.id} value={p.id}>{p.nom}</option>)}
    </Select>
  );

  async function enregistrer() {
    if (d.conflit) return;
    const ok = await run(() => api.modifierIdentite(mep.id, { ...v, version: mep.version }), 'Intervention enregistrée.');
    if (ok) d.reset();
  }

  return (
    <SectionCarte
      numero={1}
      titre="Intervention"
      pied={peutEcheance && (
        <>
          <Button variante="primaire" icone={<Save className="size-4" aria-hidden />} disabled={!d.dirty || d.conflit} onClick={() => void enregistrer()}>Enregistrer</Button>
          {d.dirty && <Button variante="fantome" onClick={d.reset}>Annuler les modifications</Button>}
          {!edition && <span className="text-xs text-fg-subtle">Seule l’échéance de retour arrière est modifiable après le passage en exécution.</span>}
        </>
      )}
    >
      <AvisConflit brouillon={d} className="mb-4" />
      <div className="grid gap-4 md:grid-cols-2">
        <Field label="Outil">{(id) => <Input id={id} value={v.outil} disabled={lecture} onChange={(e) => d.update({ outil: e.target.value })} />}</Field>
        <Field label="Objet">{(id) => <Input id={id} value={v.objet} disabled={lecture} onChange={(e) => d.update({ objet: e.target.value })} />}</Field>
        <Field label="Version actuelle">{(id) => <Input id={id} className="font-mono" value={v.versionActuelle} disabled={lecture} onChange={(e) => d.update({ versionActuelle: e.target.value })} />}</Field>
        <Field label="Version cible">{(id) => <Input id={id} className="font-mono" value={v.versionCible} disabled={lecture} onChange={(e) => d.update({ versionCible: e.target.value })} />}</Field>
        <div className="grid grid-cols-2 gap-4 text-sm" aria-label="Dates constatées">
          <p><span className="block text-[13px] font-medium">Démarrée le</span><span className="text-fg-muted">{mep.demarrageAutorise ? formatDateHeure(mep.demarrageAutorise.le) : "—"}</span></p>
          <p><span className="block text-[13px] font-medium">Terminée le</span><span className="text-fg-muted">{mep.cloture ? formatDateHeure(mep.cloture.le) : "—"}</span></p>
        </div>
        <Field label="Fuseau horaire">{(id) => <Input id={id} value={v.fuseauHoraire} disabled={lecture} onChange={(e) => d.update({ fuseauHoraire: e.target.value })} />}</Field>
        <Field label="Indisponibilité prévue (minutes)">
          {(id) => (
            <Input id={id} type="number" min={0} value={v.indisponibiliteMinutes ?? ''} disabled={lecture}
              onChange={(e) => d.update({ indisponibiliteMinutes: e.target.value === '' ? null : Number(e.target.value) })} />
          )}
        </Field>
        <Field label="Pilote">{personnes(v.piloteId, (x) => d.update({ piloteId: x, suppleantIds: v.suppleantIds.filter((s) => s !== x) }))}</Field>
        <Field label="Suppléants" hint={pilotage || lecture ? 'Ils ont les mêmes droits que le pilote.' : 'Seul le pilote, un suppléant ou un administrateur peut changer le pilote et les suppléants.'}>
          {(id) => (
            <MultiSelect
              id={id}
              options={mep.participants.filter((p) => p.id !== v.piloteId).map((p) => ({ valeur: p.id, libelle: p.nom, detail: p.role }))}
              valeurs={v.suppleantIds}
              onChange={(ids) => d.update({ suppleantIds: ids })}
              placeholder="Aucun suppléant"
              placeholderRecherche="Rechercher une personne…"
              vide="Aucune autre personne : ajoutez-en dans « Personnes »."
              disabled={!pilotage}
              libelleInconnu="Personne supprimée"
            />
          )}
        </Field>
        <Field label="Environnement" className="md:col-span-2">
          {(id) => <ChampEnvironnement id={id} valeur={v.environnement ?? ''} disabled={lecture} onChange={(e) => d.update({ environnement: e })} />}
        </Field>
        <Field label="Échéance de retour arrière (obligatoire)" className="md:col-span-2">
          {(id) => (
            <Input id={id} type="datetime-local" value={versInputDateTime(v.echeanceRepli)} disabled={!peutEcheance} className="md:max-w-xs"
              onChange={(e) => d.update({ echeanceRepli: depuisInputDateTime(e.target.value) })} />
          )}
        </Field>
        <Field label="Impacts attendus" className="md:col-span-2">
          {(id) => <Textarea id={id} rows={3} value={v.impacts} disabled={lecture} onChange={(e) => d.update({ impacts: e.target.value })} />}
        </Field>
        <div className="md:col-span-2">
          <Checkbox
            label="Bilan de clôture"
            checked={v.bilanActive ?? true}
            disabled={lecture}
            onChange={(c) => d.update({ bilanActive: c })}
          />
        </div>
      </div>
    </SectionCarte>
  );
}
