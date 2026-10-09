import type { Mep } from '../types';

export function PlanCheck({ mep }: { mep: Mep }) {
  const erreurs = mep.erreursPlan;
  if (erreurs.length === 0) {
    return (
      <section className="plan plan-ok" aria-label="Vérifier le plan">
        <strong>Vérifier le plan :</strong> Plan complet
      </section>
    );
  }
  return (
    <section className="plan plan-ko" aria-label="Vérifier le plan">
      <strong>Vérifier le plan : {erreurs.length} point{erreurs.length > 1 ? 's' : ''} à corriger</strong>
      <ul>
        {erreurs.map((e, i) => <li key={i}>{e}</li>)}
      </ul>
    </section>
  );
}
