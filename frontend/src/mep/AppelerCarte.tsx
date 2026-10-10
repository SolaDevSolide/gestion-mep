import { Phone, PhoneCall } from 'lucide-react';
import { Avatar } from '../ui/Avatar';
import type { Etape, Mep } from '../types';
import { BadgeDisponibilite } from './preparation/ResponsablesTechniquesSection';

/**
 * Encart « À appeler en cas de problème » du panneau d'une étape : les responsables techniques des systèmes de l'étape
 * (à défaut, tous), avec leur contact du jour J et leur disponibilité.
 */
export function AppelerCarte({ mep, etape }: { mep: Mep; etape: Etape }) {
  const duSysteme = mep.responsablesTechniques.filter((r) => r.systemeIds.some((s) => etape.systemeIds.includes(s)));
  const liste = duSysteme.length > 0 ? duSysteme : mep.responsablesTechniques;
  if (liste.length === 0) return null;
  const systeme = mep.systemes.filter((s) => etape.systemeIds.includes(s.id)).map((s) => s.nom).join(', ');

  return (
    <section aria-label="À appeler en cas de problème" className="rounded-lg border border-line bg-surface-2/50">
      <h3 className="flex items-center gap-1.5 border-b border-line px-3.5 py-2 text-xs font-semibold tracking-wide text-fg-subtle uppercase">
        <PhoneCall className="size-3.5" aria-hidden />À appeler en cas de problème
        {duSysteme.length > 0 && systeme && <span className="font-normal tracking-normal normal-case">· {systeme}</span>}
      </h3>
      <ul className="divide-y divide-line">
        {liste.map((r) => {
          const p = mep.participants.find((x) => x.id === r.participantId);
          if (!p) return null;
          const contact = r.contactJourJ || p.contact;
          return (
            <li key={r.participantId} className="flex items-start gap-3 px-3.5 py-2.5 text-sm">
              <Avatar nom={p.nom} />
              <div className="min-w-0 flex-1">
                <p className="flex flex-wrap items-center gap-2 font-medium">{p.nom}<BadgeDisponibilite valeur={r.disponibilite} /></p>
                <p className="flex items-center gap-1 text-[13px] text-fg-muted"><Phone className="size-3 shrink-0 text-fg-subtle" aria-hidden />{contact || 'Contact non renseigné'}</p>
                {r.perimetre.trim() && <p className="mt-0.5 text-xs whitespace-pre-line text-fg-subtle">{r.perimetre}</p>}
              </div>
            </li>
          );
        })}
      </ul>
    </section>
  );
}
