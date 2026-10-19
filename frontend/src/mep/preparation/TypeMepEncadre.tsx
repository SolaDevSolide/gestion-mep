import { ArrowDownRight, ArrowUpRight, Feather, Rocket, TriangleAlert } from 'lucide-react';
import { useState } from 'react';
import { api } from '../../api';
import { useMep } from '../../mepContext';
import type { Mep, TypeMep } from '../../types';
import { Alerte } from '../../ui/Alerte';
import { Button } from '../../ui/Button';
import { Checkbox } from '../../ui/Checkbox';
import { ConfirmDialog } from '../../ui/Dialog';
import { estSimplifiee } from '../typeMep';

const pluriel = (n: number, mot: string) => `${n} ${mot}${n > 1 ? 's' : ''}`;

/** Ce que le passage en MEP simplifiée effacera (même décompte que le serveur). */
export function effacementsSimplification(mep: Pick<Mep, 'etapes' | 'versionActuelle' | 'versionCible'>) {
  return {
    durees: mep.etapes.filter((e) => e.dureeEstimeeMinutes !== null).length,
    criticites: mep.etapes.filter((e) => e.criticite !== null).length,
    resolutions: mep.etapes.reduce((n, e) => n + e.resolutionIds.length, 0),
    versions: [mep.versionActuelle.trim(), mep.versionCible.trim()].filter(Boolean),
  };
}

interface Props {
  /** Pilote, suppléant ou administrateur (ou tout éditeur sans pilote), en préparation. */
  peutChanger: boolean;
  /** Modifications d'Intervention non enregistrées : à enregistrer ou annuler avant de changer de type. */
  brouillonModifie: boolean;
}

/**
 * Type de la MEP en tête d'Intervention. MEP simplifiée : encadré qui l'explique, avec « Passer en MEP complète » (tout est
 * conservé). MEP complète : « Passer en MEP simplifiée », destructif, derrière une confirmation explicite.
 */
export function TypeMepEncadre({ peutChanger, brouillonModifie }: Props) {
  const { mep, run } = useMep();
  const simplifiee = estSimplifiee(mep);
  const [confirmer, setConfirmer] = useState(false);
  const [compris, setCompris] = useState(false);
  // Une MEP complète n'affiche l'encadré qu'à qui peut la simplifier : l'écran reste celui d'avant pour les autres.
  if (!simplifiee && !peutChanger) return null;

  const autres = mep.suppleantIds.length;
  const etapesDeploiement = mep.etapes.filter((e) => e.plan === 'Deploiement').length;
  const eff = effacementsSimplification(mep);
  const changer = (type: TypeMep) =>
    run(
      () => api.changerTypeMep(mep.id, { version: mep.version, type }),
      type === 'Complete' ? 'La MEP est maintenant complète.' : 'La MEP est maintenant simplifiée.',
    );

  return (
    <div className="flex flex-wrap items-center gap-x-4 gap-y-2 rounded-lg border border-line bg-surface-2/50 p-4 md:col-span-2">
      {simplifiee ? (
        <Feather className="size-5 shrink-0 text-fg-subtle" aria-hidden />
      ) : (
        <Rocket className="size-5 shrink-0 text-fg-subtle" aria-hidden />
      )}
      <div className="min-w-0 flex-1 basis-72">
        <p className="text-sm font-semibold">{simplifiee ? 'MEP simplifiée' : 'MEP complète'}</p>
        <p className="text-ui text-fg-muted">
          {simplifiee
            ? 'Un pilote, les autres personnes sont suppléantes. Pas de version actuelle ni cible ; étapes sans durée, sans criticité ni résolutions liées : le jour J, toutes les résolutions des systèmes de l’étape sont proposées.'
            : 'Versions, suppléants choisis, durée, criticité et résolutions liées à chaque étape. Pour une petite intervention, la MEP peut passer en simplifiée.'}
        </p>
        {peutChanger && brouillonModifie && (
          <p className="mt-1 text-xs text-fg-subtle">
            Enregistrez ou annulez d’abord les modifications d’Intervention.
          </p>
        )}
      </div>
      {peutChanger && (
        <Button
          taille="sm"
          disabled={brouillonModifie}
          icone={
            simplifiee ? (
              <ArrowUpRight className="size-4" aria-hidden />
            ) : (
              <ArrowDownRight className="size-4" aria-hidden />
            )
          }
          onClick={() => {
            setCompris(false);
            setConfirmer(true);
          }}
        >
          {simplifiee ? 'Passer en MEP complète' : 'Passer en MEP simplifiée'}
        </Button>
      )}
      {simplifiee ? (
        <ConfirmDialog
          open={confirmer}
          onOpenChange={setConfirmer}
          titre="Passer en MEP complète ?"
          description="Tout ce qui est déjà saisi est conservé."
          libelleConfirmer="Passer en MEP complète"
          onConfirmer={() => changer('Complete')}
        >
          <ul className="list-disc space-y-1.5 pl-5 text-sm text-fg-muted">
            <li>Version actuelle et version cible apparaissent dans Intervention, à renseigner.</li>
            <li>
              {autres > 1
                ? `Les ${autres} autres personnes restent suppléantes ; le pilote peut ensuite en retirer dans « Suppléants ».`
                : autres === 1
                  ? 'L’autre personne reste suppléante ; le pilote peut ensuite la retirer dans « Suppléants ».'
                  : 'Les suppléants se choisissent ensuite dans « Suppléants ».'}
            </li>
            <li>
              Chaque étape retrouve durée, criticité et résolutions liées ; « Vérifier le plan » réclamera la criticité
              {etapesDeploiement > 0
                ? ` des ${pluriel(etapesDeploiement, 'étape')} de déploiement`
                : ' des étapes'}{' '}
              avant le Go.
            </li>
            <li>Le jour J, seules les résolutions liées à l’étape en échec seront proposées.</li>
            <li>Le changement est inscrit au journal de la MEP.</li>
          </ul>
        </ConfirmDialog>
      ) : (
        <ConfirmDialog
          open={confirmer}
          onOpenChange={setConfirmer}
          titre="Passer en MEP simplifiée ?"
          description="Ce passage supprime définitivement des informations déjà saisies."
          libelleConfirmer="Supprimer et passer en simplifiée"
          variante="danger"
          desactive={!compris}
          taille="md"
          onConfirmer={() => changer('Simplifiee')}
        >
          <div className="space-y-3">
            <Alerte ton="danger" icone={TriangleAlert} titre="Seront supprimés, sans retour possible :">
              <ul className="mt-1 list-disc space-y-1 pl-5">
                <li>
                  les durées estimées des étapes{eff.durees > 0 ? ` (${pluriel(eff.durees, 'étape')})` : ' (aucune)'} ;
                </li>
                <li>
                  les criticités des étapes{eff.criticites > 0 ? ` (${pluriel(eff.criticites, 'étape')})` : ' (aucune)'}{' '}
                  ;
                </li>
                <li>
                  les résolutions liées aux étapes
                  {eff.resolutions > 0 ? ` (${pluriel(eff.resolutions, 'lien')})` : ' (aucune)'} ;
                </li>
                <li>
                  les versions actuelle et cible
                  {eff.versions.length > 0 ? ` (${eff.versions.join(' → ')})` : ' (non renseignées)'} ;
                </li>
                <li>
                  le choix des suppléants : toutes les personnes de la MEP sauf le pilote deviennent suppléantes, avec
                  les mêmes droits que lui.
                </li>
              </ul>
            </Alerte>
            <p className="text-sm text-fg-muted">
              Repasser ensuite en MEP complète ne les fait pas revenir. Le reste (instructions, tests unitaires,
              recettes, retour arrière) est conservé ; le changement est inscrit au journal de la MEP.
            </p>
            <Checkbox
              label="J’ai compris que ces informations seront supprimées."
              checked={compris}
              onChange={setCompris}
            />
          </div>
        </ConfirmDialog>
      )}
    </div>
  );
}
