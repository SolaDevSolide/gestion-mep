import { DatabaseBackup } from 'lucide-react';
import { couleurSysteme, styleSysteme } from '../../couleursSysteme';
import { LIBELLE_CRITICITE } from '../../format';
import type { Criticite, Mep, TypePlan } from '../../types';
import { Checkbox } from '../../ui/Checkbox';
import { cn } from '../../ui/cn';
import { Field, Input, Select } from '../../ui/Input';
import { Segmented } from '../../ui/Segmented';
import { Bloc } from './EspaceEtape';
import type { Fiche } from './PanneauEdition';

interface Props {
  mep: Mep;
  plan: TypePlan;
  fiche: Fiche;
  onChange: (patch: Partial<Fiche>) => void;
}

/** Colonne gauche de l'édition d'une étape : systèmes, criticité, durée, et (déploiement) type et point de non-retour. */
export function ColonneFiche({ mep, plan, fiche: v, onChange }: Props) {
  const basculerSysteme = (id: string) =>
    onChange({
      systemeIds: v.systemeIds.includes(id)
        ? v.systemeIds.filter((x) => x !== id)
        : mep.systemes.filter((s) => s.id === id || v.systemeIds.includes(s.id)).map((s) => s.id),
    });

  return (
    <>
      <Bloc titre="Systèmes">
        <div className="flex flex-wrap gap-1.5">
          {mep.systemes.map((s) => {
            const actif = v.systemeIds.includes(s.id);
            return (
              <button
                key={s.id}
                type="button"
                aria-pressed={actif}
                onClick={() => basculerSysteme(s.id)}
                className={cn(
                  'inline-flex items-center gap-1.5 rounded-full border-[1.5px] px-2.5 py-0.5 text-[13px] font-semibold transition-colors',
                  !actif && 'border-line-strong text-fg-subtle hover:text-fg',
                )}
                style={actif ? styleSysteme(s.nom) : undefined}
              >
                <i className="size-2 rounded-full" style={{ background: couleurSysteme(s.nom) }} aria-hidden />
                {s.nom}
              </button>
            );
          })}
          {mep.systemes.length === 0 && <span className="text-sm text-fg-subtle">Aucun système dans la MEP.</span>}
        </div>
      </Bloc>
      <div className="grid grid-cols-2 gap-3">
        <Field label="Criticité">
          {(id) => (
            <Select
              id={id}
              value={v.criticite}
              onChange={(e) => onChange({ criticite: e.target.value as Criticite | '' })}
            >
              <option value="">—</option>
              {(Object.keys(LIBELLE_CRITICITE) as Criticite[]).map((c) => (
                <option key={c} value={c}>
                  {LIBELLE_CRITICITE[c]}
                </option>
              ))}
            </Select>
          )}
        </Field>
        <Field label="Durée (min)">
          {(id) => (
            <Input
              id={id}
              type="number"
              min={0}
              inputMode="numeric"
              placeholder="facultatif"
              value={v.duree}
              onChange={(e) => onChange({ duree: e.target.value })}
            />
          )}
        </Field>
      </div>
      {plan === 'Deploiement' && (
        <>
          <Bloc titre="Type">
            <Segmented
              label="Type d’étape"
              valeur={v.type}
              onChange={(t) => onChange({ type: t })}
              options={[
                { valeur: 'Standard', libelle: 'Standard' },
                {
                  valeur: 'Sauvegarde',
                  libelle: (
                    <>
                      <DatabaseBackup className="size-3.5" aria-hidden />
                      Sauvegarde
                    </>
                  ),
                },
              ]}
            />
          </Bloc>
          <div
            className="rounded-lg border-[1.5px] border-rollback-solid bg-danger-bg/60 px-3 py-2 text-danger-fg"
            title="Démarrer cette étape exigera l’accord du pilote ; ensuite, plus de retour arrière."
          >
            <Checkbox
              label={<span className="font-semibold">Point de non-retour</span>}
              checked={v.pointDeNonRetour}
              onChange={(c) => onChange({ pointDeNonRetour: c })}
            />
          </div>
        </>
      )}
    </>
  );
}
