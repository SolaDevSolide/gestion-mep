import type { HTMLAttributes, ReactNode } from 'react';
import { cn } from './cn';

export function Card({ className, ...rest }: HTMLAttributes<HTMLDivElement>) {
  return <div className={cn('rounded-xl border border-line bg-surface shadow-sm', className)} {...rest} />;
}

export function CardHeader({
  titre,
  description,
  action,
  className,
}: {
  titre: ReactNode;
  description?: ReactNode;
  action?: ReactNode;
  className?: string;
}) {
  return (
    <div
      className={cn(
        'flex flex-wrap items-start justify-between gap-x-4 gap-y-3 border-b border-line px-5 py-4',
        className,
      )}
    >
      {/* Sur un écran étroit, l'action passe sous le titre au lieu de l'écraser. */}
      <div className="min-w-0 flex-1 basis-56">
        <h2 className="text-md font-semibold tracking-tight text-fg">{titre}</h2>
        {description && <p className="mt-0.5 text-ui text-fg-subtle">{description}</p>}
      </div>
      {action}
    </div>
  );
}
