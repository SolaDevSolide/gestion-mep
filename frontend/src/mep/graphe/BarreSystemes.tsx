import { useState } from 'react';
import { api } from '../../api';
import { useMep } from '../../mepContext';
import type { Systeme, SystemeCatalogue } from '../../types';
import { ConfirmDialog } from '../../ui/Dialog';
import { PuceSysteme } from './systemes';

/**
 * Barre « Systèmes impactés » au-dessus du graphe : puces colorées des systèmes de la MEP, ajout depuis le catalogue,
 * retrait refusé (avec message) si des étapes l'utilisent. Mêmes routes que la section Systèmes de la préparation.
 */
export function BarreSystemes({
  modifiable,
  catalogue,
  descriptions,
  onMessage,
}: {
  modifiable: boolean;
  catalogue: SystemeCatalogue[];
  descriptions: Map<string, string>;
  onMessage: (m: string) => void;
}) {
  const { mep, run } = useMep();
  const [retrait, setRetrait] = useState<Systeme | null>(null);
  const reste = catalogue.filter((c) => c.actif && !mep.systemes.some((s) => s.catalogueId === c.id));

  function retirer(s: Systeme) {
    const utilise = mep.etapes.filter((e) => e.systemeIds.includes(s.id)).map((e) => e.code);
    if (utilise.length > 0) {
      onMessage(`${s.nom} est utilisé par ${utilise.join(', ')} : retirez-le d’abord de ces étapes.`);
      return;
    }
    setRetrait(s);
  }

  return (
    <div
      className="flex flex-wrap items-center gap-2 rounded-xl border border-line bg-surface px-3.5 py-2.5 shadow-sm"
      role="group"
      aria-label="Systèmes impactés"
    >
      <span className="mr-1 text-[11px] font-semibold tracking-[0.06em] text-fg-subtle uppercase">
        Systèmes impactés
      </span>
      {mep.systemes.length === 0 && <span className="text-sm text-fg-subtle">Aucun système.</span>}
      {mep.systemes.map((s) => (
        <PuceSysteme
          key={s.id}
          nom={s.nom}
          description={descriptions.get(s.id)}
          onRetirer={modifiable ? () => retirer(s) : undefined}
        />
      ))}
      {modifiable && reste.length > 0 && (
        <select
          aria-label="Ajouter un système du catalogue"
          value=""
          onChange={(e) => {
            const c = reste.find((x) => x.id === e.target.value);
            if (c)
              void run(() => api.ajouterSystemeDepuisCatalogue(mep.id, c.id, ''), `Système ${c.nom} ajouté à la MEP.`);
          }}
          className="h-7 cursor-pointer rounded-full border-[1.5px] border-dashed border-line-strong bg-transparent px-2.5 text-[13px] text-accent-text hover:border-accent"
        >
          <option value="">+ Ajouter un système du catalogue…</option>
          {reste.map((c) => (
            <option key={c.id} value={c.id}>
              {c.nom}
              {c.description ? ` · ${c.description}` : ''}
            </option>
          ))}
        </select>
      )}
      <ConfirmDialog
        open={retrait !== null}
        onOpenChange={(o) => {
          if (!o) setRetrait(null);
        }}
        titre={`Retirer ${retrait?.nom ?? ''} de la MEP ?`}
        description="Sa fiche de sauvegarde et son rattachement aux responsables techniques seront aussi retirés."
        libelleConfirmer="Retirer le système"
        variante="danger"
        onConfirmer={() =>
          retrait
            ? run(() => api.supprimerSysteme(mep.id, retrait.id), `Système ${retrait.nom} retiré de la MEP.`)
            : false
        }
      />
    </div>
  );
}
