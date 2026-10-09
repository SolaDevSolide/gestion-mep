import { useEffect, useState } from 'react';
import { api } from '../../api';
import { useDraft, useReportDirty } from '../../hooks';
import { useMep } from '../../mepContext';
import type { Livrable, LivrableSaisie, Participant, ParticipantSaisie, Systeme, SystemeSaisie, Utilisateur } from '../../types';
import { Section, vide } from '../Champ';

interface LigneProps<T> {
  source: T;
  edition: boolean;
  onSave: (v: T) => Promise<boolean>;
  onDelete?: () => Promise<boolean>;
  rendu: (v: T, maj: (p: Partial<T>) => void, edition: boolean) => React.ReactNode;
  ajout?: boolean;
}

function Ligne<T>({ source, edition, onSave, onDelete, rendu, ajout }: LigneProps<T>) {
  const d = useDraft(source);
  useReportDirty(d.dirty);
  return (
    <tr className={d.dirty ? 'ligne-modifiee' : undefined}>
      {rendu(d.value, d.update, edition)}
      {edition && (
        <td className="cellule-actions">
          <button type="button" className="btn btn-petit btn-primaire" disabled={!d.dirty} onClick={async () => { if (await onSave(d.value)) d.reset(); }}>
            {ajout ? 'Ajouter' : 'Enregistrer'}
          </button>
          {onDelete && <button type="button" className="btn btn-petit btn-danger" onClick={() => void onDelete()}>Supprimer</button>}
        </td>
      )}
    </tr>
  );
}

const Texte = ({ valeur, maj, edition, label }: { valeur: string; maj: (s: string) => void; edition: boolean; label: string }) => (
  <td><input aria-label={label} value={valeur} disabled={!edition} onChange={(e) => maj(e.target.value)} /></td>
);

export function TableauxSection() {
  const { mep, run } = useMep();
  const edition = mep.droits.peutModifierPlan;
  const [utilisateurs, setUtilisateurs] = useState<Utilisateur[] | null>(null);
  const [nouvelleCle, setNouvelleCle] = useState(0);

  useEffect(() => {
    if (edition) api.utilisateurs().then(setUtilisateurs).catch(() => setUtilisateurs([]));
  }, [edition]);

  const participantVide: ParticipantSaisie = { nom: '', role: '', contact: '', utilisateurId: null };
  const systemeVide: SystemeSaisie = { nom: '', cible: '', impact: '' };

  const ligneParticipant = (v: ParticipantSaisie, maj: (p: Partial<ParticipantSaisie>) => void, ed: boolean, nomCompte?: string | null) => (
    <>
      <Texte label="Nom" valeur={v.nom} edition={ed} maj={(s) => maj({ nom: s })} />
      <Texte label="Rôle" valeur={v.role} edition={ed} maj={(s) => maj({ role: s })} />
      <Texte label="Contact" valeur={v.contact} edition={ed} maj={(s) => maj({ contact: s })} />
      <td>
        {ed ? (
          <select aria-label="Compte relié" value={v.utilisateurId ?? ''} onChange={(e) => maj({ utilisateurId: vide(e.target.value) })}>
            <option value="">— aucun compte —</option>
            {(utilisateurs ?? []).map((u) => <option key={u.id} value={u.id}>{u.nomAffiche}</option>)}
            {v.utilisateurId && !(utilisateurs ?? []).some((u) => u.id === v.utilisateurId) && <option value={v.utilisateurId}>{nomCompte ?? 'Compte'}</option>}
          </select>
        ) : (nomCompte ?? '—')}
      </td>
    </>
  );

  const ligneSysteme = (v: SystemeSaisie, maj: (p: Partial<SystemeSaisie>) => void, ed: boolean) => (
    <>
      <Texte label="Système" valeur={v.nom} edition={ed} maj={(s) => maj({ nom: s })} />
      <Texte label="Cible" valeur={v.cible} edition={ed} maj={(s) => maj({ cible: s })} />
      <Texte label="Impact" valeur={v.impact} edition={ed} maj={(s) => maj({ impact: s })} />
    </>
  );

  return (
    <Section numero={2} titre="Personnes et systèmes">
      <h3>Personnes</h3>
      <div className="tableau-conteneur">
        <table className="tableau tableau-edit">
          <thead><tr><th>Nom</th><th>Rôle</th><th>Contact</th><th>Compte relié</th>{edition && <th />}</tr></thead>
          <tbody>
            {mep.participants.map((p: Participant) => (
              <Ligne<ParticipantSaisie>
                key={p.id}
                source={{ nom: p.nom, role: p.role, contact: p.contact, utilisateurId: p.utilisateurId }}
                edition={edition}
                rendu={(v, maj, ed) => ligneParticipant(v, maj, ed, p.utilisateurNom)}
                onSave={(v) => run(() => api.modifierParticipant(mep.id, p.id, v), 'Personne enregistrée.')}
                onDelete={() => run(() => api.supprimerParticipant(mep.id, p.id), 'Personne supprimée.')}
              />
            ))}
            {edition && (
              <Ligne<ParticipantSaisie>
                key={`np${nouvelleCle}`} ajout source={participantVide} edition
                rendu={(v, maj, ed) => ligneParticipant(v, maj, ed)}
                onSave={async (v) => { const ok = await run(() => api.ajouterParticipant(mep.id, v), 'Personne ajoutée.'); if (ok) setNouvelleCle((k) => k + 1); return ok; }}
              />
            )}
          </tbody>
        </table>
      </div>

      <h3>Systèmes</h3>
      <div className="tableau-conteneur">
        <table className="tableau tableau-edit">
          <thead><tr><th>Système</th><th>Cible</th><th>Impact</th>{edition && <th />}</tr></thead>
          <tbody>
            {mep.systemes.map((s: Systeme) => (
              <Ligne<SystemeSaisie>
                key={s.id}
                source={{ nom: s.nom, cible: s.cible, impact: s.impact }}
                edition={edition}
                rendu={ligneSysteme}
                onSave={(v) => run(() => api.modifierSysteme(mep.id, s.id, v), 'Système enregistré.')}
                onDelete={() => run(() => api.supprimerSysteme(mep.id, s.id), 'Système supprimé.')}
              />
            ))}
            {edition && (
              <Ligne<SystemeSaisie>
                key={`ns${nouvelleCle}`} ajout source={systemeVide} edition rendu={ligneSysteme}
                onSave={async (v) => { const ok = await run(() => api.ajouterSysteme(mep.id, v), 'Système ajouté.'); if (ok) setNouvelleCle((k) => k + 1); return ok; }}
              />
            )}
          </tbody>
        </table>
      </div>
      {!edition && mep.participants.length === 0 && mep.systemes.length === 0 && <p className="muted">Rien à afficher.</p>}
    </Section>
  );
}

export function LivrablesSection() {
  const { mep, run } = useMep();
  const edition = mep.droits.peutModifierPlan;
  const [cle, setCle] = useState(0);
  const vide0: LivrableSaisie = { objet: '', version: '', emplacement: '', systemeId: null };

  const rendu = (v: LivrableSaisie, maj: (p: Partial<LivrableSaisie>) => void, ed: boolean) => (
    <>
      <Texte label="Objet" valeur={v.objet} edition={ed} maj={(s) => maj({ objet: s })} />
      <Texte label="Version" valeur={v.version} edition={ed} maj={(s) => maj({ version: s })} />
      <Texte label="Emplacement" valeur={v.emplacement} edition={ed} maj={(s) => maj({ emplacement: s })} />
      <td>
        <select aria-label="Système" value={v.systemeId ?? ''} disabled={!ed} onChange={(e) => maj({ systemeId: vide(e.target.value) })}>
          <option value="">— aucun —</option>
          {mep.systemes.map((s) => <option key={s.id} value={s.id}>{s.nom}</option>)}
        </select>
      </td>
    </>
  );

  return (
    <Section numero={3} titre="Livrables">
      <div className="tableau-conteneur">
        <table className="tableau tableau-edit">
          <thead><tr><th>Objet</th><th>Version</th><th>Emplacement</th><th>Système</th>{edition && <th />}</tr></thead>
          <tbody>
            {mep.livrables.map((l: Livrable) => (
              <Ligne<LivrableSaisie>
                key={l.id}
                source={{ objet: l.objet, version: l.version, emplacement: l.emplacement, systemeId: l.systemeId }}
                edition={edition} rendu={rendu}
                onSave={(v) => run(() => api.modifierLivrable(mep.id, l.id, v), 'Livrable enregistré.')}
                onDelete={() => run(() => api.supprimerLivrable(mep.id, l.id), 'Livrable supprimé.')}
              />
            ))}
            {edition && (
              <Ligne<LivrableSaisie>
                key={`nl${cle}`} ajout source={vide0} edition rendu={rendu}
                onSave={async (v) => { const ok = await run(() => api.ajouterLivrable(mep.id, v), 'Livrable ajouté.'); if (ok) setCle((k) => k + 1); return ok; }}
              />
            )}
          </tbody>
        </table>
      </div>
      {!edition && mep.livrables.length === 0 && <p className="muted">Aucun livrable.</p>}
    </Section>
  );
}
