import { useEffect, useState } from 'react';
import { api } from '../api';
import { calculerCompteARebours, formatDateHeure } from '../format';
import { useMep } from '../mepContext';
import { BadgePhase } from './Badges';

export function useMaintenant(intervalleMs = 1000): number {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const t = window.setInterval(() => setNow(Date.now()), intervalleMs);
    return () => window.clearInterval(t);
  }, [intervalleMs]);
  return now;
}

export function CompteARebours({ echeance }: { echeance: string | null }) {
  const now = useMaintenant();
  const c = calculerCompteARebours(echeance, now);
  return (
    <div className={`rebours rebours-${c.niveau}`} role="timer" aria-live="off">
      <span className="rebours-label">Retour arrière avant</span>
      <span className="rebours-valeur">{c.texte}</span>
      {echeance && c.niveau !== 'depasse' && <span className="rebours-date">{formatDateHeure(echeance)}</span>}
    </div>
  );
}

export function Banniere() {
  const { mep, run } = useMep();
  const enExecution = mep.phase === 'Execution';
  const retourne = calculerCompteARebours(mep.echeanceRepli, Date.now()).niveau === 'depasse';

  async function basculer() {
    const cible = enExecution ? 'Preparation' : 'Execution';
    const question = enExecution
      ? 'Revenir en préparation ? Les états des étapes sont conservés.'
      : 'Passer en exécution ? Le plan sera figé pour tout le monde.';
    if (!window.confirm(question)) return;
    await run(() => api.changerPhase(mep.id, cible), enExecution ? 'Retour en préparation.' : 'MEP en exécution.');
  }

  return (
    <div className={`banniere${retourne ? ' banniere-alerte' : ''}`}>
      <div className="banniere-id">
        <div className="banniere-outil">
          {mep.outil || '(sans nom)'} <span className="banniere-version">{mep.versionCible ? `→ ${mep.versionCible}` : ''}</span>
        </div>
        <BadgePhase phase={mep.phase} />
      </div>
      <CompteARebours echeance={mep.echeanceRepli} />
      {mep.droits.estPilote && (
        <button type="button" className={`btn ${enExecution ? 'btn-secondaire' : 'btn-primaire'}`} onClick={() => void basculer()}>
          {enExecution ? 'Revenir en préparation' : 'Passer en exécution'}
        </button>
      )}
    </div>
  );
}
