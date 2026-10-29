import { useEffect, useState } from 'react';
import { api } from '../../api';
import { LIBELLE_CRITICITE } from '../../format';
import type {
  CatalogueConnexions,
  ConnexionApiSaisie,
  Criticite,
  Equipe,
  EstimationConnexion,
  NiveauRisque,
  SystemeCatalogue,
  TypeMep,
} from '../../types';
import { Card } from '../../ui/Card';
import { Checkbox } from '../../ui/Checkbox';
import { Field, Input } from '../../ui/Input';
import { Badge } from '../../ui/StatusPill';
import type { Brouillon } from './brouillon';
import { Pastilles, TitreCarte } from './ChampsConnexion';

const RISQUES: { valeur: NiveauRisque; libelle: string }[] = [
  { valeur: 'Faible', libelle: 'Faible' },
  { valeur: 'Modere', libelle: 'Modéré' },
  { valeur: 'Eleve', libelle: 'Élevé' },
];
const CRITICITES: Criticite[] = ['Faible', 'Moderee', 'Elevee', 'Bloquante'];
const TYPES: { valeur: TypeMep; libelle: string }[] = [
  { valeur: 'Complete', libelle: 'Complètes' },
  { valeur: 'Simplifiee', libelle: 'Simplifiées' },
];

/** « a », « a et b », « a, b et c ». */
export function enumerer(xs: string[], et = 'et'): string {
  if (xs.length <= 1) return xs.join('');
  return `${xs.slice(0, -1).join(', ')} ${et} ${xs[xs.length - 1]}`;
}

const minuscule = (s: string) => (s ? s[0].toLowerCase() + s.slice(1) : s);

/** Phrase qui résume la règle, pour la relire sans décoder les cases. */
export function resumeRegle(
  b: Pick<Brouillon, 'declencheurs' | 'filtres' | 'envoiManuel'>,
  catalogue: CatalogueConnexions,
  equipes: Equipe[],
  systemes: SystemeCatalogue[],
): string {
  const f = b.filtres;
  const libelles = new Map(catalogue.declencheurs.map((d) => [d.cle, d.libelle]));
  const sujets: string[] = [];
  const nomsEquipes = f.equipeIds.map((id) => equipes.find((e) => e.id === id)?.nom ?? 'équipe supprimée');
  if (nomsEquipes.length) sujets.push(`de l’équipe ${enumerer(nomsEquipes, 'ou')}`);
  const nomsSystemes = f.systemeCatalogueIds.map((id) => systemes.find((s) => s.id === id)?.nom ?? 'système supprimé');
  if (nomsSystemes.length) sujets.push(`qui touchent ${enumerer(nomsSystemes, 'ou')}`);
  if (f.environnements.length) sujets.push(`sur ${enumerer(f.environnements, 'ou')}`);
  if (f.niveauxRisque.length)
    sujets.push(
      `de risque ${enumerer(
        f.niveauxRisque.map((r) => minuscule(RISQUES.find((x) => x.valeur === r)?.libelle ?? r)),
        'ou',
      )}`,
    );
  if (f.typesMep.length === 1) sujets.push(f.typesMep[0] === 'Complete' ? 'complètes' : 'simplifiées');
  const pour = `les MEP${sujets.length ? ` ${sujets.join(', ')}` : ''}`;
  const moments = b.declencheurs.map((d) => {
    const l = minuscule(libelles.get(d) ?? d);
    if (d.startsWith('etape.') && f.criticitesEtape.length)
      return `${l} (criticité ${enumerer(
        f.criticitesEtape.map((c) => LIBELLE_CRITICITE[c].toLowerCase()),
        'ou',
      )})`;
    return l;
  });
  const auto = moments.length ? `Appelée pour ${pour} : ${enumerer(moments)}.` : 'Aucun envoi automatique.';
  return b.envoiManuel ? `${auto} Envoi possible depuis la MEP.` : auto;
}

/** Quand appeler : déclencheurs, filtres, envoi depuis une MEP (carte 4 de l'éditeur). */
export function CarteQuand({
  b,
  maj,
  catalogue,
  equipes,
  systemes,
  saisie,
}: {
  b: Brouillon;
  maj: (p: Partial<Brouillon>) => void;
  catalogue: CatalogueConnexions;
  equipes: Equipe[];
  systemes: SystemeCatalogue[];
  saisie: ConnexionApiSaisie;
}) {
  const groupes = [...new Set(catalogue.declencheurs.map((d) => d.groupe))];
  const environnements = [
    ...new Set([...systemes.flatMap((s) => s.environnements.map((e) => e.nom)), ...b.filtres.environnements]),
  ].sort((a, b) => a.localeCompare(b, 'fr'));
  const estimation = useEstimation(saisie, b.declencheurs.length > 0);
  const f = b.filtres;

  return (
    <Card className="grid gap-5 p-5">
      <TitreCarte numero={4} titre="Quand appeler" />
      <div className="grid gap-4">
        {groupes.map((g) => (
          <fieldset key={g}>
            <legend className="text-2xs font-semibold tracking-wider text-fg-subtle uppercase">{g}</legend>
            <div className="mt-2 grid gap-2 sm:grid-cols-2">
              {catalogue.declencheurs
                .filter((d) => d.groupe === g)
                .map((d) => (
                  <Checkbox
                    key={d.cle}
                    checked={b.declencheurs.includes(d.cle)}
                    onChange={(v) =>
                      maj({
                        declencheurs: v
                          ? catalogue.declencheurs
                              .map((x) => x.cle)
                              .filter((c) => c === d.cle || b.declencheurs.includes(c))
                          : b.declencheurs.filter((c) => c !== d.cle),
                      })
                    }
                    label={
                      <span className="inline-flex flex-wrap items-center gap-1.5">
                        {d.libelle}
                        {d.frequent && <Badge ton="warn">fréquent</Badge>}
                      </span>
                    }
                  />
                ))}
            </div>
          </fieldset>
        ))}
        {b.declencheurs.some((d) => catalogue.declencheurs.find((x) => x.cle === d)?.frequent) && (
          <p className="text-xs text-fg-subtle">
            « fréquent » : une MEP de 14 étapes donne 28 appels pour « démarrée » et « réussie ». Leurs envois réussis
            ne sont pas inscrits au journal de la MEP.
          </p>
        )}
      </div>

      <div className="grid gap-4 rounded-lg border border-line p-4">
        <p className="text-ui font-medium text-fg">
          Seulement pour les MEP… <span className="font-normal text-fg-subtle">(rien de coché : toutes)</span>
        </p>
        <Pastilles
          label="De l’équipe"
          options={equipes.map((e) => ({ valeur: e.id, libelle: e.nom }))}
          valeurs={f.equipeIds}
          onChange={(v) => maj({ filtres: { ...f, equipeIds: v } })}
        />
        <Pastilles
          label="Qui touchent les systèmes"
          options={systemes.map((s) => ({ valeur: s.id, libelle: s.nom }))}
          valeurs={f.systemeCatalogueIds}
          onChange={(v) => maj({ filtres: { ...f, systemeCatalogueIds: v } })}
        />
        <Pastilles
          label="Sur les environnements"
          options={environnements.map((e) => ({ valeur: e, libelle: e }))}
          valeurs={f.environnements}
          onChange={(v) => maj({ filtres: { ...f, environnements: v } })}
          hint="Mêmes noms que la recherche des MEP."
        />
        <Pastilles
          label="De niveau de risque"
          options={RISQUES}
          valeurs={f.niveauxRisque}
          onChange={(v) => maj({ filtres: { ...f, niveauxRisque: v } })}
        />
        <Pastilles
          label="Étapes de criticité"
          options={CRITICITES.map((c) => ({ valeur: c, libelle: LIBELLE_CRITICITE[c] }))}
          valeurs={f.criticitesEtape}
          onChange={(v) => maj({ filtres: { ...f, criticitesEtape: v } })}
          hint="Pour les événements d’étape seulement."
        />
        <Pastilles
          label="Type de MEP"
          options={TYPES}
          valeurs={f.typesMep}
          onChange={(v) => maj({ filtres: { ...f, typesMep: v } })}
        />
      </div>

      <p className="rounded-lg bg-surface-2/60 px-4 py-3 text-sm text-fg" role="status">
        {resumeRegle(b, catalogue, equipes, systemes)}
        {estimation && b.declencheurs.length > 0 && (
          <span className="text-fg-muted">
            {' '}
            Sur les {estimation.jours} derniers jours, ces règles auraient donné {estimation.appels} appel
            {estimation.appels > 1 ? 's' : ''} ({estimation.meps} MEP).
          </span>
        )}
      </p>

      <div className="grid gap-3 border-t border-line pt-4">
        <Checkbox
          checked={b.envoiManuel}
          onChange={(v) => maj({ envoiManuel: v })}
          label="Envoi depuis une MEP"
          description="Le pilote, ses suppléants et les administrateurs envoient le message quand ils le décident, depuis « ⋯ Plus d’actions ». {{evenement}} vaut alors « Point d’étape » et {{commentaire}} reprend ce qu’ils saisissent."
        />
        {b.envoiManuel && (
          <Field label="Libellé dans la MEP *" className="md:max-w-md" hint="Apparaît tel quel dans le menu de la MEP.">
            {(i) => (
              <Input
                id={i}
                value={b.libelleEnvoiManuel}
                maxLength={80}
                placeholder="Prévenir le canal Exploitation"
                onChange={(e) => maj({ libelleEnvoiManuel: e.target.value })}
              />
            )}
          </Field>
        )}
      </div>
    </Card>
  );
}

/** Estimation sur 30 jours, recalculée une demi-seconde après le dernier changement ; null si indisponible. */
function useEstimation(saisie: ConnexionApiSaisie, utile: boolean): EstimationConnexion | null {
  const [estimation, setEstimation] = useState<EstimationConnexion | null>(null);
  const cle = JSON.stringify({ d: saisie.declencheurs, f: saisie.filtres });
  useEffect(() => {
    if (!utile) return;
    let annule = false;
    const t = window.setTimeout(() => {
      api
        .estimerConnexion(saisie)
        .then((e) => {
          if (!annule) setEstimation(e);
        })
        .catch(() => {
          if (!annule) setEstimation(null);
        });
    }, 500);
    return () => {
      annule = true;
      window.clearTimeout(t);
    };
    // La saisie complète part, mais seuls les déclencheurs et les filtres changent l'estimation.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [cle, utile]);
  return estimation;
}
