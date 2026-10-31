import { Send } from 'lucide-react';
import { LienNoosphere } from '../admin/liaisons/communs';
import { formatDateHeure } from '../format';
import { api } from '../api';
import { useAsync } from '../hooks';
import type { EnvoiMepNoosphere, Mep } from '../types';
import { Alerte } from '../ui/Alerte';
import { Popover, PopoverContent, PopoverTrigger } from '../ui/Popover';
import { cn } from '../ui/cn';

/** Envois de la MEP à Noosphere : relus quand sa phase, ses systèmes ou ses dates changent. */
export function useEnvoisNoosphere(mep: Mep) {
  const cle = [
    mep.id,
    mep.phase,
    mep.dateMep,
    mep.systemes.map((s) => s.catalogueId ?? '').join(','),
    mep.annulation?.le ?? '',
  ].join('|');
  // Promise.resolve : une erreur (ou un client sans cette route) laisse simplement la marque absente.
  return useAsync(
    () =>
      mep.estModele
        ? Promise.resolve<EnvoiMepNoosphere[]>([])
        : Promise.resolve().then(() => api.envoisNoosphereMep(mep.id)),
    [cle],
  );
}

/** Phrase d'état d'un envoi : envoyée, en attente, sans date, envoi coupé. */
export function etatEnvoi(e: EnvoiMepNoosphere): { libelle: string; detail: string; envoyee: boolean } {
  const apps = e.applications.map((a) => a.systeme).join(' et ');
  if (e.envoyeeLe)
    return {
      libelle: 'Envoyée à Noosphere',
      detail: `${apps ? `${apps} ${e.applications.length > 1 ? 'sont suivis' : 'est suivi'} par Noosphere : cette MEP apparaît sur ${e.applications.length > 1 ? 'leurs fiches' : 'sa fiche'}. ` : ''}Chaque changement de date, de systèmes, de phase ou de résultat y est envoyé 30 secondes après, une annulation aussi.`,
      envoyee: true,
    };
  if (!e.envoiActif)
    return {
      libelle: 'Non envoyée à Noosphere',
      detail: `${apps} ${e.applications.length > 1 ? 'sont suivis' : 'est suivi'} par Noosphere, mais l’envoi des MEP est coupé pour cette liaison.`,
      envoyee: false,
    };
  if (!e.datePrevue)
    return {
      libelle: 'Envoyée à Noosphere dès qu’une date est prévue',
      detail: `${apps} ${e.applications.length > 1 ? 'sont suivis' : 'est suivi'} par Noosphere : la MEP y sera annoncée dès qu’elle aura une date.`,
      envoyee: false,
    };
  return {
    libelle: 'Envoi à Noosphere en cours',
    detail: `${apps} ${e.applications.length > 1 ? 'sont suivis' : 'est suivi'} par Noosphere : la MEP y part dans les 30 secondes.`,
    envoyee: false,
  };
}

/** Marque de l'en-tête d'une MEP : « Envoyée à Noosphere », avec le détail au clic. Rien tant que rien n'est connu. */
export function MarqueNoosphere({ mep }: { mep: Mep }) {
  const { donnees } = useEnvoisNoosphere(mep);
  const envoi = donnees?.[0];
  if (!envoi) return null;
  const e = etatEnvoi(envoi);
  return (
    <Popover>
      <PopoverTrigger asChild>
        <button
          type="button"
          className={cn(
            'inline-flex animate-fade-in items-center gap-1.5 rounded-md font-medium hover:underline',
            e.envoyee ? 'text-accent-text' : 'text-fg-subtle',
          )}
        >
          <Send className="size-3.5" aria-hidden />
          {e.envoyee ? 'Envoyée à Noosphere' : e.libelle}
        </button>
      </PopoverTrigger>
      <PopoverContent className="w-80 max-w-[calc(100vw-2rem)] p-0">
        <DetailEnvoi envoi={envoi} />
      </PopoverContent>
    </Popover>
  );
}

/** Détail d'un envoi : phrase d'état, dernier envoi et liens vers les fiches de Noosphere. */
export function DetailEnvoi({ envoi, className }: { envoi: EnvoiMepNoosphere; className?: string }) {
  const e = etatEnvoi(envoi);
  return (
    <Alerte ton="run" icone={Send} titre={e.libelle} className={cn('border-0', className)}>
      {e.detail}
      {envoi.envoyeeLe && (
        <span className="mt-1.5 block text-xs">
          Dernier envoi : {formatDateHeure(envoi.envoyeeLe)}
          {envoi.demo ? ' (démonstration : rien ne part)' : ''}.
        </span>
      )}
      {envoi.applications.length > 0 && (
        <span className="mt-2 flex flex-wrap gap-x-4 gap-y-1">
          {envoi.applications.map((a) => (
            <LienNoosphere key={a.systemeId} url={a.url}>
              Voir {a.systeme} dans Noosphere
            </LienNoosphere>
          ))}
        </span>
      )}
    </Alerte>
  );
}
