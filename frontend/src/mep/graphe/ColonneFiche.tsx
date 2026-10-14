import { DatabaseBackup } from 'lucide-react';
import { Check } from 'lucide-react';
import { couleurSysteme, styleSysteme } from '../../couleursSysteme';
import {
  basculerEnvironnement,
  environnementsEtape,
  environnementsParDefaut,
  memeEnvironnement,
} from '../../environnements';
import { LIBELLE_CRITICITE, LIBELLE_FIN } from '../../format';
import type { Criticite, Mep, TypePlan } from '../../types';
import { Checkbox } from '../../ui/Checkbox';
import { cn } from '../../ui/cn';
import { Field, Input, Select, Textarea } from '../../ui/Input';
import { Segmented } from '../../ui/Segmented';
import { Bloc } from './EspaceEtape';
import { estSimplifiee } from '../typeMep';
import type { Fiche } from './PanneauEdition';

interface Props {
  mep: Mep;
  plan: TypePlan;
  fiche: Fiche;
  onChange: (patch: Partial<Fiche>) => void;
}

/**
 * Colonne gauche de l'édition d'une étape : systèmes et environnements (une ligne par système de la MEP : on active le
 * système, puis on coche ses environnements parmi ceux choisis dans Préparation › Systèmes), criticité, durée, fin (test unitaire ou simple vérification) et,
 * pour le déploiement, type (avec l'emplacement et la méthode de récupération d'une sauvegarde) et point de non-retour.
 */
export function ColonneFiche({ mep, plan, fiche: v, onChange }: Props) {
  const basculerSysteme = (id: string) => {
    if (v.systemeIds.includes(id)) {
      onChange({
        systemeIds: v.systemeIds.filter((x) => x !== id),
        environnements: v.environnements.filter((e) => e.systemeId !== id),
      });
      return;
    }
    const systeme = mep.systemes.find((s) => s.id === id);
    onChange({
      systemeIds: mep.systemes.filter((s) => s.id === id || v.systemeIds.includes(s.id)).map((s) => s.id),
      // Un système qui n'a qu'un environnement dans la MEP le coche d'office.
      environnements: [...v.environnements, ...(systeme ? environnementsParDefaut(systeme) : [])],
    });
  };
  /** Cocher un environnement active aussi son système. */
  const basculerEnv = (systemeId: string, nom: string) =>
    onChange({
      systemeIds: v.systemeIds.includes(systemeId)
        ? v.systemeIds
        : mep.systemes.filter((s) => s.id === systemeId || v.systemeIds.includes(s.id)).map((s) => s.id),
      environnements: basculerEnvironnement(v.environnements, systemeId, nom),
    });

  return (
    <>
      <Bloc titre="Systèmes et environnements">
        {mep.systemes.length === 0 ? (
          <span className="text-sm text-fg-subtle">Aucun système dans la MEP.</span>
        ) : (
          <>
            <ul className="divide-y divide-line rounded-lg border border-line">
              {mep.systemes.map((s) => {
                const actif = v.systemeIds.includes(s.id);
                // Environnements de la MEP pour ce système, puis ceux que l'étape garde alors qu'ils en ont été retirés.
                const retires = environnementsEtape(v, s).filter((e) => e.retire);
                const options = [
                  ...s.environnements.map((e) => ({ nom: e.nom, production: e.production, retire: false })),
                  ...retires,
                ];
                return (
                  <li key={s.id} className="flex flex-wrap items-center gap-1.5 px-2.5 py-2">
                    <button
                      type="button"
                      aria-pressed={actif}
                      onClick={() => basculerSysteme(s.id)}
                      className={cn(
                        'inline-flex min-w-20 items-center gap-1.5 rounded-full border-[1.5px] px-2.5 py-0.5 text-[13px] font-semibold transition-colors',
                        !actif && 'border-line-strong text-fg-subtle hover:text-fg',
                      )}
                      style={actif ? styleSysteme(s.nom) : undefined}
                    >
                      <i className="size-2 rounded-full" style={{ background: couleurSysteme(s.nom) }} aria-hidden />
                      {s.nom}
                    </button>
                    {options.map((e) => {
                      const coche =
                        actif && v.environnements.some((x) => x.systemeId === s.id && memeEnvironnement(x.nom, e.nom));
                      return (
                        <button
                          key={e.nom}
                          type="button"
                          aria-pressed={coche}
                          aria-label={`${s.nom} : ${e.nom}${e.retire ? ' (n’est plus choisi pour ce système)' : ''}`}
                          onClick={() => basculerEnv(s.id, e.nom)}
                          className={cn(
                            'inline-flex items-center gap-1 rounded-md border px-1.5 py-0.5 text-xs font-medium transition-colors',
                            e.retire
                              ? 'border-danger-bd bg-danger-bg text-danger-fg line-through'
                              : coche
                                ? e.production
                                  ? 'border-warn-bd bg-warn-bg text-warn-fg'
                                  : 'border-accent bg-accent-soft text-accent-text'
                                : 'border-line text-fg-subtle hover:border-line-strong hover:text-fg',
                          )}
                        >
                          {coche && <Check className="size-3" aria-hidden />}
                          {e.nom}
                        </button>
                      );
                    })}
                    {s.environnements.length === 0 && (
                      <span className="text-xs text-fg-subtle">aucun environnement choisi pour ce système</span>
                    )}
                  </li>
                );
              })}
            </ul>
            <p className="mt-1.5 text-xs text-fg-subtle">
              Proposés : les environnements choisis pour chaque système dans Préparation › Systèmes.
            </p>
          </>
        )}
      </Bloc>
      {/* MEP simplifiée : ni criticité ni durée. */}
      {!estSimplifiee(mep) && (
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
      )}
      <Bloc titre="Fin de l’étape">
        <Segmented
          label="Fin de l’étape"
          valeur={v.fin}
          onChange={(fin) => onChange({ fin })}
          options={[
            { valeur: 'TestUnitaire', libelle: LIBELLE_FIN.TestUnitaire },
            { valeur: 'Verification', libelle: LIBELLE_FIN.Verification },
          ]}
        />
      </Bloc>
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
          {v.type === 'Sauvegarde' && (
            <div className="grid gap-3">
              <Field label="Emplacement de la sauvegarde *">
                {(id) => (
                  <Input
                    id={id}
                    value={v.sauvegardeEmplacement}
                    onChange={(e) => onChange({ sauvegardeEmplacement: e.target.value })}
                    placeholder="ex. /sauvegardes/clients.sql"
                  />
                )}
              </Field>
              <Field label="Méthode de récupération *">
                {(id) => (
                  <Textarea
                    id={id}
                    rows={2}
                    value={v.sauvegardeMethodeRecuperation}
                    onChange={(e) => onChange({ sauvegardeMethodeRecuperation: e.target.value })}
                    placeholder="ex. psql -d clients < clients.sql"
                  />
                )}
              </Field>
            </div>
          )}
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
