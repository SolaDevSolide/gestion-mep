import { FileText, Flag, Save, ShieldAlert } from 'lucide-react';
import { useMemo } from 'react';
import { api } from '../api';
import { useDraft, useReportDirty } from '../hooks';
import { useMep } from '../mepContext';
import type { PlanRepli } from '../types';
import { Alerte } from '../ui/Alerte';
import { AvisConflit } from '../ui/AvisConflit';
import { Button } from '../ui/Button';
import { Card, CardHeader } from '../ui/Card';
import { Field, Input, Textarea } from '../ui/Input';
import { BoutonDeclencherRepli } from './ActionsPilote';
import { EtapesEditeur } from './EtapesEditeur';
import { ExecutionVue } from './ExecutionVue';

function FichePlan() {
  const { mep, run } = useMep();
  const d = useDraft<PlanRepli>(mep.planRepli);
  useReportDirty(d.dirty);
  const v = d.value;
  const edition = mep.droits.peutEditerContenu;

  async function enregistrer() {
    if (d.conflit) return;
    const ok = await run(() => api.modifierPlanRepli(mep.id, { ...v, version: mep.version }), 'Plan de retour arrière enregistré.');
    if (ok) d.reset();
  }

  return (
    <Card>
      <CardHeader
        titre={<span className="flex items-center gap-2"><FileText className="size-4 text-accent-text" aria-hidden />Fiche du plan de retour arrière</span>}
        description="Les critères et repères à connaître avant de décider de revenir en arrière."
      />
      <div className="grid gap-4 p-5 md:grid-cols-2">
        <Field label="Quand déclencher le retour arrière ?" className="md:col-span-2">
          {(id) => <Textarea id={id} rows={2} disabled={!edition} value={v.quandDeclencher} onChange={(e) => d.update({ quandDeclencher: e.target.value })} placeholder="ex. Échec d’une étape bloquante sans résolution, ou échéance dépassée." />}
        </Field>
        <Field label="Durée testée (minutes)">
          {(id) => <Input id={id} type="number" min={0} disabled={!edition} value={v.dureeTesteeMinutes ?? ''} onChange={(e) => d.update({ dureeTesteeMinutes: e.target.value === '' ? null : Number(e.target.value) })} />}
        </Field>
        <Field label="Version ou copies à restaurer">
          {(id) => <Input id={id} className="font-mono" disabled={!edition} value={v.versionARestaurer} onChange={(e) => d.update({ versionARestaurer: e.target.value })} />}
        </Field>
        <Field label="Données créées depuis la sauvegarde" className="md:col-span-2">
          {(id) => <Textarea id={id} rows={2} disabled={!edition} value={v.donneesDepuisSauvegarde} onChange={(e) => d.update({ donneesDepuisSauvegarde: e.target.value })} placeholder="Que devient ce qui a été saisi pendant l’intervention ?" />}
        </Field>
        <Field label="Si le retour arrière échoue" className="md:col-span-2">
          {(id) => <Textarea id={id} rows={2} disabled={!edition} value={v.siRepliEchoue} onChange={(e) => d.update({ siRepliEchoue: e.target.value })} placeholder="Plan B, personne à joindre…" />}
        </Field>
      </div>
      <AvisConflit brouillon={d} className="mx-5 mb-4" />
      {edition && (
        <div className="flex items-center gap-3 border-t border-line bg-surface-2/50 px-5 py-3">
          <Button variante="primaire" icone={<Save className="size-4" aria-hidden />} disabled={!d.dirty || d.conflit} onClick={() => void enregistrer()}>Enregistrer la fiche</Button>
          {d.dirty && <Button variante="fantome" onClick={d.reset}>Annuler les modifications</Button>}
        </div>
      )}
    </Card>
  );
}

function FicheLecture() {
  const { mep } = useMep();
  const p = mep.planRepli;
  const lignes = useMemo(() => [
    ['Quand déclencher', p.quandDeclencher],
    ['Durée testée', p.dureeTesteeMinutes !== null ? `${p.dureeTesteeMinutes} min` : ''],
    ['Version ou copies à restaurer', p.versionARestaurer],
    ['Données créées depuis la sauvegarde', p.donneesDepuisSauvegarde],
    ['Si le retour arrière échoue', p.siRepliEchoue],
  ], [p]);
  return (
    <Card>
      <CardHeader titre={<span className="flex items-center gap-2"><FileText className="size-4 text-accent-text" aria-hidden />Fiche du plan de retour arrière</span>} />
      <dl className="grid gap-x-8 gap-y-3 p-5 md:grid-cols-2">
        {lignes.map(([k, val]) => (
          <div key={k} className={k === 'Quand déclencher' ? 'md:col-span-2' : undefined}>
            <dt className="text-xs font-semibold tracking-wide text-fg-subtle uppercase">{k}</dt>
            <dd className="mt-0.5 text-sm whitespace-pre-line">{val || <span className="text-fg-subtle">Non renseigné</span>}</dd>
          </div>
        ))}
      </dl>
    </Card>
  );
}

export default function RetourArriereTab() {
  const { mep } = useMep();
  const prep = mep.phase === 'Preparation';

  return (
    <div className="mx-auto w-full max-w-[1700px] space-y-6 px-6 py-6">
      {prep ? <FichePlan /> : <FicheLecture />}

      {mep.nonRetourFranchi && mep.phase !== 'RetourArriere' && (
        <Alerte ton="warn" icone={Flag} titre="Retour arrière impossible : point de non-retour franchi" role="status">
          L’étape {mep.nonRetourFranchi} a été démarrée. Il n’est plus possible de revenir en arrière, ni automatiquement à l’échéance, ni manuellement : tout problème se corrige en avant. Les étapes ci-dessous ne seront jamais exécutées.
        </Alerte>
      )}

      {(mep.phase === 'Execution' || mep.phase === 'Recette') && mep.droits.estPilote && !mep.nonRetourFranchi && (
        <Alerte ton="rollback" icone={ShieldAlert} titre="Décision du pilote" action={<BoutonDeclencherRepli taille="md" />}>
          Les étapes ci-dessous sont en attente. Elles ne seront exécutables qu’après le déclenchement du retour arrière (possible aussi pendant la recette).
        </Alerte>
      )}

      <section aria-labelledby="titre-etapes-repli">
        <h2 id="titre-etapes-repli" className="mb-3 text-base font-semibold tracking-tight">Étapes de retour arrière</h2>
        {prep ? <EtapesEditeur plan="RetourArriere" /> : <ExecutionVue plan="RetourArriere" />}
      </section>
    </div>
  );
}
