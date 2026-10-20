import { CopyPlus, FilePlus2, FolderOpen, Library, Trash2 } from 'lucide-react';
import type { ReactNode } from 'react';
import type { ModeleEtape } from '../../types';
import { Menu, MenuContent, MenuItem, MenuLabel, MenuSeparator, MenuTrigger } from '../../ui/Menu';

/** Choix proposés partout où l'on crée une étape : vierge, ou copie d'une étape modèle. */
export interface ChoixCreation {
  /** Étapes modèles les plus utilisées pour les systèmes de la MEP (5 au plus). */
  modeles: ModeleEtape[];
  onVierge: () => void;
  onModele: (m: ModeleEtape) => void;
  onParcourir: () => void;
}

/** Entrées « Étape vierge » et « Depuis un modèle » (les plus utilisés, puis la bibliothèque) d'un menu de création. */
export function EntreesCreation({ choix }: { choix: ChoixCreation }) {
  return (
    <>
      <MenuItem onSelect={choix.onVierge}>
        <FilePlus2 className="size-4 shrink-0 text-fg-subtle" aria-hidden />
        Étape vierge
      </MenuItem>
      <MenuSeparator />
      <MenuLabel>Depuis un modèle</MenuLabel>
      {choix.modeles.length === 0 && (
        <p className="px-2.5 py-1 text-ui text-fg-subtle">Aucune étape modèle pour les systèmes de la MEP.</p>
      )}
      {choix.modeles.map((m) => (
        <MenuItem key={m.id} onSelect={() => choix.onModele(m)}>
          <CopyPlus className="size-4 shrink-0 text-fg-subtle" aria-hidden />
          <span className="min-w-0 flex-1 truncate">{m.action}</span>
        </MenuItem>
      ))}
      <MenuItem onSelect={choix.onParcourir}>
        <Library className="size-4 shrink-0 text-fg-subtle" aria-hidden />
        Parcourir la bibliothèque…
      </MenuItem>
    </>
  );
}

/**
 * Menu ouvert à un point de l'écran (clic sur un lien, « + » d'une carte) : un déclencheur invisible posé au point, le
 * contenu au-dessus de tout (plein écran compris). Échap ou un clic ailleurs le ferme.
 */
export function MenuAuPoint({
  point,
  label,
  onFermer,
  children,
}: {
  point: { x: number; y: number };
  label: string;
  onFermer: () => void;
  children: ReactNode;
}) {
  return (
    <Menu
      open
      modal={false}
      onOpenChange={(o) => {
        if (!o) onFermer();
      }}
    >
      <MenuTrigger asChild>
        <span
          aria-hidden
          tabIndex={-1}
          className="pointer-events-none fixed size-px"
          style={{ left: point.x, top: point.y }}
        />
      </MenuTrigger>
      <MenuContent
        align="start"
        sideOffset={6}
        aria-label={label}
        // Le déclencheur est invisible et muet : le menu porte son propre nom.
        aria-labelledby={undefined}
        className="w-80 max-w-[calc(100vw-2rem)]"
      >
        {children}
      </MenuContent>
    </Menu>
  );
}

/** Lien proposé dans un menu (choix parmi des liens fusionnés). */
export interface ChoixLien {
  id: string;
  libelle: string;
}

/** Contenu du menu d'un lien : choisir parmi les liens qui passent là, ou insérer / supprimer le lien choisi. */
export function ContenuMenuLien({
  titre,
  candidats,
  onChoisirLien,
  onCandidat,
  creation,
  onSupprimer,
  note,
  onOuvrirSection,
}: {
  titre: string;
  /** Plusieurs liens passent au point cliqué : la personne choisit d'abord lequel. */
  candidats?: ChoixLien[];
  onChoisirLien?: (id: string) => void;
  /** Lien sous le pointeur ou le focus dans la liste (mis en avant dans le graphe) ; null en sortant de la liste. */
  onCandidat?: (id: string | null) => void;
  /** Insertion possible sur ce lien. */
  creation?: ChoixCreation;
  onSupprimer?: () => void;
  /** Lien non modifiable ici : pourquoi. */
  note?: string;
  /** Lien intérieur à une section : ouvrir sa vue d'édition. */
  onOuvrirSection?: () => void;
}) {
  if (candidats && candidats.length > 1)
    return (
      <>
        <MenuLabel>{titre}</MenuLabel>
        {candidats.map((c) => (
          <MenuItem
            key={c.id}
            // Le menu reste ouvert : il montre ensuite les actions du lien choisi.
            onSelect={(e) => {
              e.preventDefault();
              onChoisirLien?.(c.id);
            }}
            onFocus={() => onCandidat?.(c.id)}
            onBlur={() => onCandidat?.(null)}
          >
            <span className="min-w-0 flex-1 truncate font-mono text-ui">{c.libelle}</span>
          </MenuItem>
        ))}
      </>
    );
  return (
    <>
      <MenuLabel>{titre}</MenuLabel>
      {note && <p className="max-w-72 px-2.5 pb-1.5 text-ui text-fg-muted">{note}</p>}
      {onOuvrirSection && (
        <MenuItem onSelect={onOuvrirSection}>
          <FolderOpen className="size-4 shrink-0 text-fg-subtle" aria-hidden />
          Ouvrir la section pour la modifier
        </MenuItem>
      )}
      {creation && (
        <>
          <MenuSeparator />
          <MenuLabel>Insérer une étape sur ce lien</MenuLabel>
          <EntreesCreation choix={creation} />
        </>
      )}
      {onSupprimer && (
        <>
          <MenuSeparator />
          <MenuItem onSelect={onSupprimer} className="text-danger-fg">
            <Trash2 className="size-4 shrink-0" aria-hidden />
            Supprimer le lien
          </MenuItem>
        </>
      )}
    </>
  );
}
