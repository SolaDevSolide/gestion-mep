import { EditeurBlocs, ZoneAuto } from '../../ui/EditeurBlocs';
import { Markdown } from '../../ui/Instructions';
import { Bloc } from './EspaceEtape';
import type { Fiche } from './PanneauEdition';

interface Props {
  fiche: Pick<Fiche, 'instructions' | 'resultatAttendu' | 'fin'>;
  onChange: (patch: Partial<Fiche>) => void;
}

/**
 * Colonne centrale de l'édition d'une étape : instructions exactes et résultat attendu (avec aperçu Markdown), qui sert
 * d'instruction de test pour un test unitaire.
 */
export function ColonneInstructions({ fiche: v, onChange }: Props) {
  return (
    <>
      <Bloc titre="Instructions exactes">
        <EditeurBlocs label="Instructions" valeur={v.instructions} onChange={(md) => onChange({ instructions: md })} />
      </Bloc>
      <Bloc titre="Résultat attendu">
        <p className="text-xs text-fg-subtle">
          {v.fin === 'TestUnitaire'
            ? 'Test unitaire : l’exécutant saisira le résultat constaté en terminant l’étape.'
            : 'Simple vérification : l’exécutant confirme le résultat, sans le saisir.'}
        </p>
        <ZoneAuto
          aria-label="Résultat attendu"
          value={v.resultatAttendu}
          onChange={(e) => onChange({ resultatAttendu: e.target.value })}
          placeholder="ex. `GET /sante` renvoie 200 avec la version 2.4.0."
          className="border border-line-strong bg-surface text-fg placeholder:text-fg-subtle"
        />
        {v.resultatAttendu.trim() && (
          <div className="rounded-md border border-dashed border-line bg-surface px-3 py-2">
            <Markdown source={v.resultatAttendu} />
          </div>
        )}
      </Bloc>
    </>
  );
}
