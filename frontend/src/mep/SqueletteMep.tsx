import { SqueletteEnsemble } from './ensemble/Squelettes';
import { lireBacReplie } from './graphe/contexteGraphe';
import {
  EnChargement,
  Squelette,
  SqueletteChronologie,
  SqueletteFormulaire,
  SqueletteLignes,
  SqueletteTableau,
} from '../ui/Squelette';

/**
 * Cadre du graphe en chargement : barre d'outils et bandeau, à la hauteur du graphe ; avec `bac`, la colonne du bac à droite
 * (« Sections à placer », « Étapes à placer »), à sa largeur : rien ne se décale quand le plan paraît.
 */
export function SqueletteCadreGraphe({ bac = false }: { bac?: boolean }) {
  // Même place que le bac à venir : replié en fine colonne si l'utilisateur l'a replié.
  const bacReplie = bac && lireBacReplie();
  return (
    <div className="flex h-[clamp(520px,calc(100vh-280px),940px)] flex-col overflow-hidden rounded-xl border border-line bg-surface">
      <div className="flex h-12 shrink-0 items-center gap-2 border-b border-line bg-surface-2/60 px-3">
        <Squelette className="h-8 w-36 rounded-lg" />
        <Squelette className="h-8 w-44 rounded-lg" />
        <Squelette className="ml-auto h-8 w-28 rounded-lg" />
        <Squelette className="h-8 w-36 rounded-lg" />
      </div>
      <div className="flex h-9 shrink-0 items-center border-b border-line px-3">
        <Squelette className="h-3.5 w-64" />
      </div>
      {bacReplie && (
        <div className="flex min-h-0 flex-1 justify-end">
          <div className="grid w-11 content-start justify-items-center gap-2 border-l border-line bg-surface-2/40 py-2">
            <Squelette className="size-7 rounded-lg" />
            <Squelette className="h-24 w-3.5" />
          </div>
        </div>
      )}
      {bac && !bacReplie && (
        <div className="flex min-h-0 flex-1 justify-end">
          <div className="grid w-60 content-start gap-2 border-l border-line bg-surface-2/40 p-2.5 max-sm:w-48">
            <Squelette className="h-3.5 w-36" />
            <Squelette className="h-8 w-full rounded-lg" />
            <Squelette className="h-24 w-full rounded-xl" />
            <Squelette className="mt-2 h-3.5 w-32" />
            <Squelette className="h-20 w-full rounded-xl" />
          </div>
        </div>
      )}
    </div>
  );
}

/** Contenu d'un onglet en chargement, à la forme de l'onglet demandé (`?onglet=…`, `?plan=…`). */
export function SqueletteOnglet({ onglet }: { onglet: string | null }) {
  if (onglet === 'plan' || onglet === 'execution' || onglet === 'etapes' || onglet === 'retour')
    return (
      <div className="mx-auto w-full max-w-[1700px] px-6 py-6">
        <SqueletteCadreGraphe />
      </div>
    );
  if (onglet === 'ensemble') return <SqueletteEnsemble />;
  if (onglet === 'journal')
    return (
      <div className="mx-auto w-full max-w-4xl px-6 py-6">
        <Squelette className="mb-6 h-12 w-full rounded-lg" />
        <Squelette className="h-9 w-full rounded-lg" />
        <SqueletteChronologie evenements={8} className="mt-6" />
      </div>
    );
  if (onglet === 'recette') return <SqueletteTableau lignes={8} colonnes={4} className="mx-6 my-6" />;
  // Préparation : sommaire des sections à gauche, section ouverte à droite.
  return (
    <div className="grid gap-6 px-6 py-4 md:grid-cols-[220px_1fr]">
      <SqueletteLignes lignes={9} className="content-start gap-4 pt-2" />
      <div className="rounded-xl border border-line bg-surface p-5">
        <Squelette className="h-5 w-40" />
        <SqueletteFormulaire champs={5} className="mt-5" />
      </div>
    </div>
  );
}

/** Page d'une MEP en chargement : en-tête (identité, actions, onglets) et contenu de l'onglet demandé. */
export function SqueletteMep({ onglet }: { onglet: string | null }) {
  return (
    <EnChargement libelle="Chargement de la MEP…" className="flex min-w-0 flex-1 flex-col">
      <div className="border-b border-line bg-surface">
        <div className="flex flex-wrap items-center justify-between gap-x-6 gap-y-2 px-6 pt-3">
          <div className="grid gap-2">
            <Squelette className="h-7 w-80" />
            <Squelette className="ml-8 h-4 w-[30rem] max-w-full" />
          </div>
          <div className="flex gap-2">
            <Squelette className="h-8 w-28 rounded-lg" />
            <Squelette className="h-8 w-32 rounded-lg" />
            <Squelette className="h-8 w-24 rounded-lg" />
          </div>
        </div>
        <div className="mt-2 flex h-10 items-center gap-6 px-6">
          {[0, 1, 2, 3].map((i) => (
            <Squelette key={i} className="h-4 w-20" />
          ))}
        </div>
      </div>
      <SqueletteOnglet onglet={onglet} />
    </EnChargement>
  );
}
