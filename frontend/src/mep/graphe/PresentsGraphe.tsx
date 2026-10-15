import { Hand, Lock } from 'lucide-react';
import { useContext, useMemo } from 'react';
import { prenom, useAutresPresents } from '../../presence';
import type { Presence, Verrou } from '../../types';
import { Avatar } from '../../ui/Avatar';
import { cn } from '../../ui/cn';
import { VerrousContext } from './verrouEtape';

/**
 * Où chacun se trouve dans le plan, en direct : les clés `ici:etape:{id}` (fiche d'une étape ouverte, même en lecture) et
 * `ici:section:{id}` (vue d'édition d'une section) disent où est la personne ; `etape:{id}` et `section:{id}` disent qu'elle
 * y modifie. Le détenteur du verrou d'une étape est marqué d'un cadenas, ou d'une main s'il a pris la main à quelqu'un.
 */
export const cleIciEtape = (id: string) => `ici:etape:${id}`;
export const cleIciSection = (id: string) => `ici:section:${id}`;

const surEtape = (p: Presence, id: string) => p.elements.some((e) => e === cleIciEtape(id) || e === `etape:${id}`);

interface Present {
  utilisateurId: string;
  nom: string;
  verrou: Verrou | null;
}

/** Personnes sur l'étape (présence et détenteur du verrou, sans cet onglet), détenteur en premier. */
function usePresentsEtape(etapeId: string): Present[] {
  const autres = useAutresPresents();
  const { session, verrous } = useContext(VerrousContext);
  return useMemo(() => {
    const verrou = verrous?.find((v) => v.etapeId === etapeId && v.session !== session) ?? null;
    const liste: Present[] = autres
      .filter((p) => surEtape(p, etapeId))
      .map((p) => ({ utilisateurId: p.utilisateurId, nom: p.nom, verrou: null }));
    if (verrou) {
      const i = liste.findIndex((p) => p.utilisateurId === verrou.utilisateurId);
      if (i >= 0) liste.splice(i, 1);
      liste.unshift({ utilisateurId: verrou.utilisateurId, nom: verrou.nom, verrou });
    }
    return liste;
  }, [autres, verrous, session, etapeId]);
}

function titrePresent(p: Present, lieu: string): string {
  if (p.verrou?.reprisDeNom) return `${p.nom} a pris la main ${lieu} à ${prenom(p.verrou.reprisDeNom)}`;
  if (p.verrou) return `${p.nom} modifie ${lieu}`;
  return `${p.nom} est ${lieu}`;
}

/** Pile d'avatars posée sur le coin haut droit d'une carte ou d'une section. */
function Pile({ presents, lieu, className }: { presents: Present[]; lieu: string; className?: string }) {
  if (presents.length === 0) return null;
  const visibles = presents.slice(0, 3);
  return (
    <span
      role="group"
      aria-label={presents.map((p) => titrePresent(p, lieu)).join(' ; ')}
      data-presents={presents.map((p) => prenom(p.nom)).join(',')}
      className={cn('pointer-events-auto absolute z-[5] flex -space-x-1.5', className)}
    >
      {visibles.map((p) => (
        <span key={p.utilisateurId} className="relative">
          <Avatar nom={p.nom} taille="xs" className="ring-2 ring-surface" titre={titrePresent(p, lieu)} />
          {p.verrou && (
            <span
              aria-hidden
              className={cn(
                'absolute -right-1 -bottom-1 flex size-3.5 items-center justify-center rounded-full ring-1 ring-surface',
                p.verrou.reprisDeNom ? 'bg-warn-solid text-white' : 'bg-surface text-fg',
              )}
            >
              {p.verrou.reprisDeNom ? <Hand className="size-2.5" /> : <Lock className="size-2.5" />}
            </span>
          )}
        </span>
      ))}
      {presents.length > visibles.length && (
        <span className="inline-flex size-5 items-center justify-center rounded-full bg-surface-2 text-[9px] font-semibold text-fg-muted ring-2 ring-surface">
          +{presents.length - visibles.length}
        </span>
      )}
    </span>
  );
}

/** Qui est sur cette étape (fiche ouverte), et qui la tient. */
export function PresentsEtape({ etapeId, code }: { etapeId: string; code: string }) {
  const presents = usePresentsEtape(etapeId);
  return <Pile presents={presents} lieu={`sur ${code}`} className="-top-2.5 -right-2" />;
}

/** Qui est dans cette section : sa vue d'édition, ou une de ses étapes. */
export function PresentsSection({
  sectionId,
  titre,
  etapeIds,
  className = '-top-3 right-3',
}: {
  sectionId: string;
  titre: string;
  etapeIds: readonly string[];
  className?: string;
}) {
  const presents = usePresentsSection(sectionId, etapeIds);
  return <Pile presents={presents} lieu={`dans la section « ${titre} »`} className={className} />;
}

export function usePresentsSection(sectionId: string, etapeIds: readonly string[]): Present[] {
  const autres = useAutresPresents();
  const { session, verrous } = useContext(VerrousContext);
  return useMemo(() => {
    const ids = new Set(etapeIds);
    const cles = new Set([cleIciSection(sectionId), `section:${sectionId}`]);
    const liste = autres
      .filter((p) => p.elements.some((e) => cles.has(e)) || etapeIds.some((id) => surEtape(p, id)))
      .map<Present>((p) => ({ utilisateurId: p.utilisateurId, nom: p.nom, verrou: null }));
    for (const v of verrous ?? []) {
      if (!ids.has(v.etapeId) || v.session === session) continue;
      const p = liste.find((x) => x.utilisateurId === v.utilisateurId);
      if (p) p.verrou = v;
      else liste.push({ utilisateurId: v.utilisateurId, nom: v.nom, verrou: v });
    }
    return liste;
  }, [autres, verrous, session, sectionId, etapeIds]);
}
