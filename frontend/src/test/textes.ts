// Données de test des modèles de texte (catalogue des variables, valeurs d'une MEP, modèles d'une équipe).
import type { Equipe, ModeleTexte, VariablesMep, VariableTexte } from '../types';

export const variablesCatalogue: VariableTexte[] = [
  { cle: 'application', libelle: 'Application', groupe: 'MEP', description: 'Outil concerné.', liste: false },
  { cle: 'date', libelle: 'Date de la MEP', groupe: 'Date et heure', description: 'Date annoncée.', liste: false },
  {
    cle: 'indisponibilite',
    libelle: 'Indisponibilité prévue',
    groupe: 'Date et heure',
    description: 'Durée.',
    liste: false,
  },
  {
    cle: 'reference_demande',
    libelle: 'Référence de la demande',
    groupe: 'Communication',
    description: 'Numéro du ticket.',
    liste: false,
  },
  { cle: 'benefices', libelle: 'Bénéfices et nouveautés', groupe: 'Communication', description: 'Liste.', liste: true },
  { cle: 'signature', libelle: 'Signature', groupe: 'Équipe', description: 'Signature.', liste: false },
];

export const valeursMep: VariablesMep = {
  equipeId: 'eq1',
  equipeProposeeId: 'eq1',
  valeurs: [
    { cle: 'application', texte: 'Portail', liste: null },
    { cle: 'date', texte: 'mardi 20 octobre 2026', liste: null },
    { cle: 'indisponibilite', texte: null, liste: null },
    { cle: 'reference_demande', texte: 'CHG-1', liste: null },
    { cle: 'benefices', texte: null, liste: ['Plus rapide', 'Plus simple'] },
    { cle: 'signature', texte: 'L’équipe Exploitation', liste: null },
  ],
};

export const equipesTest: Equipe[] = [
  {
    id: 'eq1',
    nom: 'Exploitation',
    description: '',
    membreIds: ['u3'],
    creeeLe: '2026-10-01T08:00:00Z',
    contactSupport: 'support@exemple.invalid',
    signature: '',
  },
  {
    id: 'eq2',
    nom: 'Infrastructure',
    description: '',
    membreIds: [],
    creeeLe: '2026-10-01T08:00:00Z',
    contactSupport: '',
    signature: '',
  },
];

export function modeleTexte(p: Partial<ModeleTexte> = {}): ModeleTexte {
  return {
    id: 'mt1',
    equipeId: 'eq1',
    nom: 'Annonce aux utilisateurs',
    usage: 'Annonce',
    objet: '[{{application}}] MEP le {{date}}',
    corps: 'Bonjour,\n\n**Ce qui change**\n{{benefices}}\n\nIndisponibilité : {{indisponibilite}}\n\n{{signature}}',
    fourni: true,
    modifie: false,
    version: 1,
    modifieLe: '2026-10-01T08:00:00Z',
    modifieParId: null,
    modifiePar: null,
    peutModifier: true,
    ...p,
  };
}
