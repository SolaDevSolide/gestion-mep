import { Phone, PhoneCall } from 'lucide-react';
import type { ReactNode } from 'react';
import { Avatar } from '../ui/Avatar';
import type { Etape, Mep } from '../types';
import { BadgeDisponibilite } from './preparation/ResponsablesTechniquesSection';

function Personne({
  nom,
  contact,
  detail,
  badge,
}: {
  nom: string;
  contact: string;
  detail?: string;
  badge?: ReactNode;
}) {
  return (
    <li className="flex items-start gap-2.5 px-3 py-2 text-sm">
      <Avatar nom={nom} taille="sm" />
      <div className="min-w-0 flex-1">
        <p className="flex flex-wrap items-center gap-1.5 font-medium">
          {nom}
          {badge}
        </p>
        <p className="flex items-center gap-1 text-[13px] text-fg-muted">
          <Phone className="size-3 shrink-0 text-fg-subtle" aria-hidden />
          {contact || 'Contact non renseigné'}
        </p>
        {detail && <p className="mt-0.5 text-xs whitespace-pre-line text-fg-subtle">{detail}</p>}
      </div>
    </li>
  );
}

/**
 * « À appeler en cas de problème » : les responsables techniques des systèmes de l'étape (à défaut, tous), avec leur
 * contact du jour J et leur disponibilité, puis le pilote et les suppléants.
 */
export function AppelerCarte({ mep, etape }: { mep: Mep; etape: Etape }) {
  const duSysteme = mep.responsablesTechniques.filter((r) => r.systemeIds.some((s) => etape.systemeIds.includes(s)));
  const liste = duSysteme.length > 0 ? duSysteme : mep.responsablesTechniques;
  const pilotage = [mep.piloteId, ...mep.suppleantIds]
    .map((id, i) => ({ p: mep.participants.find((x) => x.id === id), role: i === 0 ? 'Pilote' : 'Suppléant' }))
    .filter((x): x is { p: NonNullable<typeof x.p>; role: string } => !!x.p);
  if (liste.length === 0 && pilotage.length === 0) return null;
  const systeme = mep.systemes
    .filter((s) => etape.systemeIds.includes(s.id))
    .map((s) => s.nom)
    .join(', ');

  return (
    <section aria-label="À appeler en cas de problème" className="rounded-lg border border-line bg-surface-2/50">
      <h3 className="flex items-center gap-1.5 border-b border-line px-3 py-2 text-[11px] font-semibold tracking-[0.06em] text-fg-subtle uppercase">
        <PhoneCall className="size-3.5" aria-hidden />À appeler en cas de problème
      </h3>
      {liste.length > 0 && (
        <section aria-label="Responsables techniques">
          <p className="px-3 pt-2 text-xs font-medium text-fg-muted">
            Responsables techniques{duSysteme.length > 0 && systeme ? ` · ${systeme}` : ''}
          </p>
          <ul className="divide-y divide-line">
            {liste.map((r) => {
              const p = mep.participants.find((x) => x.id === r.participantId);
              if (!p) return null;
              return (
                <Personne
                  key={r.participantId}
                  nom={p.nom}
                  contact={r.contactJourJ || p.contact}
                  detail={r.perimetre.trim() || undefined}
                  badge={<BadgeDisponibilite valeur={r.disponibilite} />}
                />
              );
            })}
          </ul>
        </section>
      )}
      {pilotage.length > 0 && (
        <section aria-label="Pilotage" className={liste.length > 0 ? 'border-t border-line' : undefined}>
          <p className="px-3 pt-2 text-xs font-medium text-fg-muted">Pilotage</p>
          <ul className="divide-y divide-line">
            {pilotage.map(({ p, role }) => (
              <Personne
                key={p.id}
                nom={p.nom}
                contact={p.contact}
                badge={<span className="text-xs font-normal text-fg-subtle">{role}</span>}
              />
            ))}
          </ul>
        </section>
      )}
    </section>
  );
}
