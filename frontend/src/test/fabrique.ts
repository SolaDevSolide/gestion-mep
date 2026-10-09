import type { Etape, ExecutionResolution, Mep, StatutDisponibilite } from '../types';

export function etape(p: Partial<Etape> = {}, statut: StatutDisponibilite = 'Disponible'): Etape {
  return {
    id: 'e1', plan: 'Deploiement', code: 'E0001', ordre: 1, action: 'Redémarrer le service', systemeId: 's1', responsableId: 'p1',
    criticite: 'Moderee', etat: 'AFaire', instructions: 'systemctl restart api', resultatAttendu: 'Service actif',
    preuve: '', debutReel: null, finReelle: null, revision: 1, executantId: null, dependances: [], resolutionIds: [],
    type: 'Standard', pointDeNonRetour: false, nonRetourAutorise: null,
    disponibilite: { statut, libelle: statut }, ...p,
  };
}

export function mep(p: Partial<Mep> = {}): Mep {
  return {
    id: 'm1', version: 1, outil: 'Portail', versionActuelle: '1.0', versionCible: '2.0', objet: 'Mise à jour', impacts: '',
    dateMep: '2026-10-09', heureDebut: '20:00:00', heureFin: '23:00:00', fuseauHoraire: 'Europe/Paris', indisponibiliteMinutes: 30,
    piloteId: 'p3', suppleantIds: [], environnement: 'Production', estModele: false, approbationsMinimum: 1, approbations: [], goNoGo: [],
    testsRecette: [], bilanActive: true, bilan: null, nonRetourFranchi: null, piecesJointes: [], phase: 'Execution', issue: null, retourArriere: null, cloture: null,
    planRepli: { quandDeclencher: '', dureeTesteeMinutes: null, versionARestaurer: '', donneesDepuisSauvegarde: '', siRepliEchoue: '' },
    echeanceRepli: '2026-10-09T23:30:00Z',
    sauvegarde: { mode: 'AvantMep', structure: true, donnees: true, configuration: false, responsableId: null, realiseeLe: null, procedure: '', copies: '', restaurationTestee: false, dateTestRestauration: null, preuveTestRestauration: '' },
    procedureRelue: null, demarrageAutorise: null,
    participants: [
      { id: 'p1', nom: 'Claire', role: 'Opérateur', contact: '', utilisateurId: 'u1', utilisateurNom: 'Claire' },
      { id: 'p2', nom: 'Bob', role: 'Opérateur', contact: '', utilisateurId: 'u2', utilisateurNom: 'Bob' },
      { id: 'p3', nom: 'Alice', role: 'Pilote', contact: '', utilisateurId: 'u3', utilisateurNom: 'Alice' },
    ],
    systemes: [{ id: 's1', nom: 'API', cible: 'srv-api-01', impact: '', catalogueId: null }],
    livrables: [], etapes: [], resolutions: [], erreursPlan: [],
    droits: { estPilote: false, estApprobateur: false, peutModifierPlan: false, participantId: 'p1' },
    ...p,
  };
}

export function resolution(p: Partial<ExecutionResolution> = {}): ExecutionResolution {
  return {
    id: 'r1', etapeId: 'e1', resolutionTypeId: 't1', titre: 'API ne répond plus', symptomes: 'Erreur 502', diagnostic: 'curl -i http://localhost/sante',
    corrections: [
      { ordre: 1, instruction: 'systemctl restart api', resultatAttendu: 'Service actif', resultat: null, observation: '', par: null, le: null },
      { ordre: 2, instruction: 'systemctl status api', resultatAttendu: 'active (running)', resultat: null, observation: '', par: null, le: null },
    ],
    verificationInstruction: 'curl -i http://localhost/sante', verificationAttendu: 'HTTP 200',
    verificationResultat: null, verificationObservation: '', statut: 'EnCours', demarreePar: 'Claire', demarreeLe: '2026-10-09T21:00:00Z', termineeLe: null,
    ...p,
  };
}
