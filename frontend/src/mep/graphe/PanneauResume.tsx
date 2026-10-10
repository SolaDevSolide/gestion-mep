import type { ReactNode } from 'react';
import { construireLignes } from '../../graphe';
import type { Etape, TypePlan } from '../../types';
import { Card } from '../../ui/Card';

/** Intertitre de section d'un panneau (petites capitales, comme la maquette). */
export function TitrePanneau({ children, id }: { children: ReactNode; id?: string }) {
  return <h3 id={id} className="text-[11px] font-semibold tracking-[0.06em] text-fg-subtle uppercase">{children}</h3>;
}

/** Panneau sans sélection : résumé du plan et, en préparation, mode d'emploi du graphe. */
export function PanneauResume({ plan, etapes, aide }: { plan: TypePlan; etapes: Etape[]; aide: boolean }) {
  const lignes = construireLignes(etapes).length;
  const total = etapes.reduce((t, e) => t + (e.dureeEstimeeMinutes ?? 0), 0);
  return (
    <Card className="grid gap-4 p-4">
      <div className="grid gap-1">
        <TitrePanneau>{plan === 'Deploiement' ? 'Plan de déploiement' : 'Plan de retour arrière'}</TitrePanneau>
        <p className="text-sm text-fg-muted tnum">
          {etapes.length} étape{etapes.length > 1 ? 's' : ''} · {lignes} ligne{lignes > 1 ? 's' : ''} · {total} min de travail cumulé
        </p>
      </div>
      {aide ? (
        <div className="grid gap-1.5">
          <TitrePanneau>Comment faire</TitrePanneau>
          <ol className="grid list-decimal gap-1.5 pl-4.5 text-[13px]">
            <li><b>Systèmes</b> : ajoutez ceux de la MEP en haut ; une étape peut en toucher plusieurs.</li>
            <li><b>Ajouter</b> : « + Étape » au bout d’une ligne, ou « + Nouvelle ligne » en bas.</li>
            <li><b>Déplacer</b> : glisser une fiche ; ses liens sont recréés (elle attend la ligne au-dessus).</li>
            <li><b>Lien particulier</b> : tirer le rond du bas d’une fiche vers celle qui doit attendre (ou depuis le panneau, « Préalables »).</li>
            <li><b>Instructions</b> : des blocs Texte (markdown) ou Code (copiables en exécution).</li>
            <li><b>Clavier</b> : Tab pour aller de fiche en fiche, Entrée pour ouvrir le panneau, Échap pour le fermer.</li>
          </ol>
        </div>
      ) : (
        <p className="text-[13px] text-fg-subtle">Cliquez sur une étape (ou Entrée sur une fiche) pour afficher son détail.</p>
      )}
    </Card>
  );
}
