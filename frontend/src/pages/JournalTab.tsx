import { useEffect, useState } from 'react';
import { api } from '../api';
import { formatDateHeure } from '../format';
import type { Evenement } from '../types';

export default function JournalTab({ mepId, tick }: { mepId: string; tick: number }) {
  const [evenements, setEvenements] = useState<Evenement[] | null>(null);
  const [erreur, setErreur] = useState('');

  useEffect(() => {
    api.journal(mepId).then((e) => { setEvenements(e); setErreur(''); }).catch((e: Error) => setErreur(e.message));
  }, [mepId, tick]);

  if (erreur) return <p className="alerte alerte-erreur" role="alert">{erreur}</p>;
  if (!evenements) return <p className="chargement">Chargement…</p>;
  if (evenements.length === 0) return <p className="muted">Aucun événement.</p>;
  return (
    <ol className="journal">
      {evenements.map((e) => (
        <li key={e.id}>
          <time>{formatDateHeure(e.quand)}</time>
          <strong>{e.auteur}</strong>
          <span className="journal-type">{e.type}</span>
          <span>{e.detail}</span>
        </li>
      ))}
    </ol>
  );
}
