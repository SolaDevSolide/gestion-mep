// Toutes les animations et captures, dans l'ordre de la documentation.
import cycleDeVie from './cycle-de-vie.mjs';
import demarrageRapide from './demarrage-rapide.mjs';
import preparer from './preparer.mjs';
import executer from './executer.mjs';
import suivre from './suivre.mjs';
import modelesDeTexte from './modeles-de-texte.mjs';
import administrer from './administrer.mjs';

export const SCENARIOS = [
  ...demarrageRapide,
  ...cycleDeVie,
  ...preparer,
  ...executer,
  ...suivre,
  ...modelesDeTexte,
  ...administrer,
];
