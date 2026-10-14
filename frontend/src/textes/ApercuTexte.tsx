import { Fragment } from 'react';
import { cn } from '../ui/cn';
import { blocs, enTexteSimple, texteManque, type Libelles, type Ligne, type Morceau } from './modelesTexte';

function Morceaux({ morceaux, libelles }: { morceaux: Morceau[]; libelles: Libelles }) {
  return (
    <>
      {morceaux.map((m, i) => {
        const contenu =
          m.type === 'manque' ? (
            <span className="rounded border border-dashed border-warn-bd bg-warn-bg px-1 text-[13px] text-warn-fg">
              {texteManque(m.cle, libelles)}
            </span>
          ) : (
            m.texte
          );
        return m.gras ? (
          <strong key={i} className="font-semibold">
            {contenu}
          </strong>
        ) : (
          <Fragment key={i}>{contenu}</Fragment>
        );
      })}
    </>
  );
}

/** Aperçu d'un texte rempli : mis en forme (gras, listes à puces) ou texte simple, tel qu'il sera copié. */
export function ApercuTexte({
  lignes,
  libelles,
  simple,
  className,
}: {
  lignes: Ligne[];
  libelles: Libelles;
  simple?: boolean;
  className?: string;
}) {
  if (simple)
    return (
      <pre className={cn('font-mono text-[13px] leading-relaxed whitespace-pre-wrap text-fg', className)}>
        {enTexteSimple(lignes, libelles)}
      </pre>
    );
  return (
    <div className={cn('space-y-3 text-sm leading-relaxed text-fg', className)}>
      {blocs(lignes).map((b, i) =>
        b.type === 'liste' ? (
          <ul key={i} className="list-disc space-y-0.5 pl-6">
            {b.elements.map((l, j) => (
              <li key={j}>
                <Morceaux morceaux={l.morceaux} libelles={libelles} />
              </li>
            ))}
          </ul>
        ) : (
          <p key={i}>
            {b.lignes.map((l, j) => (
              <Fragment key={j}>
                {j > 0 && <br />}
                <Morceaux morceaux={l.morceaux} libelles={libelles} />
              </Fragment>
            ))}
          </p>
        ),
      )}
    </div>
  );
}
