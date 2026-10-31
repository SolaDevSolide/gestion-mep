import { ExternalLink, Link2 } from 'lucide-react';
import type { ReactNode } from 'react';
import { commande, useInstallation } from '../../installation';
import { Card, CardHeader } from '../../ui/Card';
import { cn } from '../../ui/cn';
import { CodeBlock } from '../../ui/CodeBlock';
import { Badge } from '../../ui/StatusPill';
import { libelleCapacite, motsEmpreinte } from './liaisons';

/** Adresse fictive de la démonstration (`*.demo.invalid`) : aucun lien réel à ouvrir. */
export function estAdresseDemo(url: string | null | undefined): boolean {
  if (!url) return false;
  try {
    return new URL(url).hostname.endsWith('.demo.invalid');
  } catch {
    return false;
  }
}

/**
 * Lien vers une fiche de Noosphere, dans un nouvel onglet. En démonstration (Noosphere fictif), le lien est montré mais
 * inactif, avec sa raison, plutôt que d'ouvrir une adresse qui n'existe pas.
 */
export function LienNoosphere({
  url,
  children,
  className,
  icone = true,
}: {
  url: string;
  children: ReactNode;
  className?: string;
  icone?: boolean;
}) {
  const contenu = (
    <>
      {icone && <ExternalLink className="size-3.5 shrink-0" aria-hidden />}
      {children}
    </>
  );
  if (!url || estAdresseDemo(url))
    return (
      <span
        className={cn('inline-flex items-center gap-1.5 text-fg-subtle', className)}
        title="Démonstration : Noosphere fictif, aucune fiche à ouvrir"
        aria-disabled="true"
      >
        {contenu}
      </span>
    );
  return (
    <a
      href={url}
      target="_blank"
      rel="noopener noreferrer"
      className={cn('inline-flex items-center gap-1.5 font-medium text-accent-text hover:underline', className)}
    >
      {contenu}
    </a>
  );
}

/** Les quatre éléments de l'empreinte, en pavés (`grand` : écran de confirmation). */
export function Empreinte({ valeur, grand }: { valeur: string; grand?: boolean }) {
  return (
    <span
      className={cn('flex flex-wrap gap-1.5', grand && 'justify-center gap-2.5 sm:gap-3')}
      aria-label={`Empreinte : ${valeur}`}
    >
      {motsEmpreinte(valeur).map((m, i) => (
        <span
          // Les quatre éléments sont fixes et dans l'ordre : la position les identifie.
          // oxlint-disable-next-line react/no-array-index-key
          key={i}
          aria-hidden
          className={cn(
            'rounded-md border font-mono font-medium',
            grand
              ? 'border-accent/40 bg-accent-soft px-3.5 py-2 text-xl text-accent-text sm:px-4 sm:text-2xl'
              : 'border-line bg-surface-2 px-2 py-0.5 text-ui text-fg',
          )}
        >
          {m}
        </span>
      ))}
    </span>
  );
}

/** Capacités accordées par Noosphere. */
export function Capacites({ capacites }: { capacites: string[] }) {
  if (capacites.length === 0) return <span className="text-fg-subtle">Aucune</span>;
  return (
    <span className="flex flex-wrap gap-1">
      {capacites.map((c) => (
        <Badge key={c}>{libelleCapacite(c)}</Badge>
      ))}
    </span>
  );
}

/** Ligne libellé / valeur des fiches de liaison. */
export function Ligne({ libelle, children }: { libelle: string; children: ReactNode }) {
  return (
    <div className="grid gap-0.5 sm:grid-cols-[12rem_minmax(0,1fr)] sm:gap-4">
      <dt className="text-fg-subtle">{libelle}</dt>
      <dd className="min-w-0 [overflow-wrap:anywhere]">{children}</dd>
    </div>
  );
}

/** Commandes équivalentes, à taper sur le serveur. */
export function CarteLigneDeCommande({ commandes, description }: { commandes: string[]; description?: string }) {
  const mode = useInstallation();
  return (
    <Card>
      <CardHeader titre="En ligne de commande" description={description} />
      <div className="p-5">
        <CodeBlock titre="Commandes" code={commandes.map((c) => commande(mode, c)).join('\n')} />
      </div>
    </Card>
  );
}

/** Marque d'un système suivi depuis Noosphere : « Géré par Noosphere » (`court` : « Noosphere », dans les tableaux). */
export function BadgeNoosphere({ court, className }: { court?: boolean; className?: string }) {
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1 rounded-md border border-accent/30 bg-accent-soft px-1.5 py-0.5 text-2xs leading-4 font-medium whitespace-nowrap text-accent-text',
        className,
      )}
      title={court ? 'Géré par Noosphere : nom et description y sont tenus' : undefined}
    >
      <Link2 className="size-3" aria-hidden />
      {court ? 'Noosphere' : 'Géré par Noosphere'}
    </span>
  );
}

/** Raison d'un système suivi inactif, d'après sa source (« retirée dans Noosphere »). */
export function raisonInactif(source: { cycle: string; absente: boolean } | null | undefined): string | null {
  if (!source) return null;
  if (source.absente) return 'absente de Noosphere';
  if (source.cycle.toLowerCase() === 'retired') return 'retirée dans Noosphere';
  return null;
}
