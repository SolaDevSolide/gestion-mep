import { useState } from 'react';
import { LIBELLE_CRITICITE, ressembleCommande } from '../format';
import type { Etape, Participant, Systeme } from '../types';
import { BadgeDisponibilite, BadgeEtat } from './Badges';

export interface ActionsEtape {
  demarrer: (e: Etape) => Promise<unknown> | void;
  terminer: (e: Etape, resultat: 'Reussie' | 'Echec', preuve: string) => Promise<unknown> | void;
  autoriserReprise: (e: Etape) => Promise<unknown> | void;
}

interface Props {
  etape: Etape;
  systeme?: Systeme;
  responsable?: Participant;
  etapes: Etape[];
  participantId: string | null;
  estPilote: boolean;
  actions: ActionsEtape;
  ouverteParDefaut?: boolean;
}

export function EtapeCarte({ etape, systeme, responsable, etapes, participantId, estPilote, actions, ouverteParDefaut = false }: Props) {
  const [ouverte, setOuverte] = useState(ouverteParDefaut);
  const [preuve, setPreuve] = useState(etape.preuve ?? '');
  const [confirmation, setConfirmation] = useState(false);
  const [occupe, setOccupe] = useState(false);

  const statut = etape.disponibilite.statut;
  const jeSuisResponsable = participantId !== null && etape.responsableId === participantId;
  const peutDemarrer = statut === 'Disponible' && jeSuisResponsable;
  const peutTerminer = etape.etat === 'EnCours' && jeSuisResponsable;
  const peutReprise = etape.etat === 'Echec' && estPilote;
  const commande = ressembleCommande(etape.instructions);
  const prerequis = etape.dependances
    .map((id) => etapes.find((x) => x.id === id))
    .filter((x): x is Etape => !!x);

  async function agir(fn: () => Promise<unknown> | void) {
    setOccupe(true);
    try { await fn(); } finally { setOccupe(false); }
  }

  return (
    <article className={`etape-carte dispo-bord-${statut}`} data-code={etape.code}>
      <button type="button" className="etape-entete" aria-expanded={ouverte} onClick={() => setOuverte((o) => !o)}>
        <span className="etape-code">{etape.code}</span>
        <span className="etape-titre">{etape.action || '(sans libellé)'}</span>
        <span className="etape-meta">
          {systeme?.nom ?? '—'} · {responsable?.nom ?? 'Sans responsable'}
          {jeSuisResponsable && <strong className="moi"> (moi)</strong>}
        </span>
        <BadgeEtat etat={etape.etat} />
        <BadgeDisponibilite dispo={etape.disponibilite} grand />
      </button>

      {ouverte && (
        <div className="etape-corps">
          <p className="petit muted">Criticité : {LIBELLE_CRITICITE[etape.criticite]}
            {prerequis.length > 0 && <> · Après : {prerequis.map((p) => p.code).join(', ')}</>}
          </p>

          <h4>Instructions exactes</h4>
          {etape.instructions.trim() ? (
            <pre className={`instructions${commande ? ' instructions-commande' : ''}`}>{etape.instructions}</pre>
          ) : <p className="muted">Aucune instruction renseignée.</p>}

          <h4>Résultat attendu</h4>
          <div className="attendu">{etape.resultatAttendu.trim() || 'Non renseigné.'}</div>

          {(peutTerminer || etape.preuve) && (
            <label className="champ">
              <span>Preuve</span>
              <textarea
                rows={3}
                value={preuve}
                disabled={!peutTerminer}
                placeholder="Collez ici la sortie, la capture ou le constat qui prouve le résultat."
                onChange={(e) => setPreuve(e.target.value)}
              />
            </label>
          )}

          {etape.etat === 'Echec' && (
            <p className="alerte alerte-erreur gros-message" role="alert">
              Arrêter, prévenir le pilote, ne rien improviser.
            </p>
          )}

          <div className="actions actions-grosses">
            {peutDemarrer && (
              <button type="button" className="btn btn-primaire btn-gros" disabled={occupe} onClick={() => void agir(() => actions.demarrer(etape))}>
                Démarrer
              </button>
            )}
            {peutTerminer && (
              <>
                <button type="button" className="btn btn-succes btn-gros" disabled={occupe} onClick={() => setConfirmation(true)}>
                  Réussie
                </button>
                <button type="button" className="btn btn-danger btn-gros" disabled={occupe} onClick={() => void agir(() => actions.terminer(etape, 'Echec', preuve))}>
                  Échec
                </button>
              </>
            )}
            {peutReprise && (
              <button type="button" className="btn btn-secondaire btn-gros" disabled={occupe} onClick={() => void agir(() => actions.autoriserReprise(etape))}>
                Autoriser la reprise
              </button>
            )}
          </div>
        </div>
      )}

      {confirmation && (
        <div className="modale-fond" role="presentation">
          <div className="modale" role="dialog" aria-modal="true" aria-label="Confirmer la réussite">
            <h3>Confirmer {etape.code}</h3>
            <p>Résultat attendu :</p>
            <div className="attendu">{etape.resultatAttendu || 'Non renseigné.'}</div>
            <div className="actions">
              <button
                type="button"
                className="btn btn-succes btn-gros"
                disabled={occupe}
                onClick={() => void agir(async () => { const ok = await actions.terminer(etape, 'Reussie', preuve); if (ok !== false) setConfirmation(false); })}
              >
                J’ai vérifié et obtenu le résultat attendu
              </button>
              <button type="button" className="btn btn-secondaire" onClick={() => setConfirmation(false)}>Annuler</button>
            </div>
          </div>
        </div>
      )}
    </article>
  );
}
