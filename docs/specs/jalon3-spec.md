# Jalon 3 : spécification (déroulé d'une MEP)

Validé par Hugo le 2026-10-09 (questionnaire de cadrage : 1A 2A 3A 4A 5B 6A 7A 9A 10A 11B 13A).
Tout le texte visible est en français. On garde le style du code existant (Regles.cs : règles pures qui lèvent RegleMetierException ; l'API mappe en 422).

## 1. Pilote et suppléants (corrige un bug)
- `Mep.SuppleantId` devient `Mep.SuppleantIds : List<Guid>` (uuid[]), migration qui reprend l'ancienne valeur.
- Règle : le pilote ne peut pas être suppléant ; pas de doublon. Refus 422 avec message clair.
- Les suppléants ont les mêmes droits que le pilote (`EstPilote` inclut tous les suppléants).

## 2. Sauvegarde avant ou pendant la MEP
- `Sauvegarde.Mode : ModeSauvegarde { AvantMep, PendantMep }` (défaut AvantMep).
- `Etape.Type : TypeEtape { Standard, Sauvegarde }` (déploiement uniquement ; refus sur le plan de retour arrière).
- `ErreursPlan` : en mode PendantMep, au moins une étape de déploiement de type Sauvegarde ; en mode AvantMep, rien de nouveau.
- Front : choix du mode dans la fiche Sauvegarde ; en PendantMep la fiche montre « La sauvegarde est faite par l'étape E000x » et masque Réalisée le / Responsable ; badge « Sauvegarde » sur l'étape dans la liste et l'arbre.

## 3. Circuit d'approbation
- Paramètre global `ParametresGlobaux.ApprobationsMinimum` (int, défaut 1, 0 = pas d'approbation). Table à une ligne, éditable par un admin.
- `Mep.Approbations : List<Approbation { Id, UtilisateurId, Decision : DecisionApprobation? (Approuvee, Refusee), Commentaire, Le }>` : approbateurs choisis parmi les comptes en préparation.
- Règles : un approbateur ne peut pas être le pilote (compte du pilote) ; seul l'approbateur désigné décide ; décision possible seulement en préparation et quand `ErreursPlan` ne contient que des anomalies d'approbation ; un refus exige un commentaire.
- Toute modification du contenu de la MEP en préparation (champs, participants, systèmes, livrables, étapes, dépendances, plan de repli, sauvegarde, recette, approbateurs) remet les décisions à null via `Regles.ReinitialiserApprobations(mep)` (renvoie vrai si au moins une décision effacée → entrée de journal « Approbations à refaire : la MEP a été modifiée »).
- `ErreursPlan` : « Il faut N approbation(s) » si moins de N approuvées ; « X a refusé : commentaire » pour chaque refus.

## 4. Go / no-go
- Catalogue global `CritereGoNoGo { Id, Libelle, Ordre, Actif }` (Paramètres). Démo : « Équipe présente et joignable », « Sauvegarde disponible », « Utilisateurs prévenus de l'interruption », « Supervision en place ».
- « Passer en exécution » devient la décision Go : `PasserEnExecution(mep, user, criteresCoches : IReadOnlyCollection<Guid>, criteres actifs, now)` exige que tous les critères actifs soient cochés, enregistre `Mep.GoNoGo : List<CritereValide { Libelle, ParId, Le }>` (copie figée).
- No-go : `Regles.NoGo(mep, user, motif)` (pilote, préparation, motif obligatoire) : la MEP reste en préparation, journal « No-go : motif ».
- Front : dialogue Go / no-go avec la liste à cocher, bouton Go désactivé tant que tout n'est pas coché, bouton No-go avec motif.

## 5. Point de non-retour
- `Etape.PointDeNonRetour : bool` (déploiement uniquement), `Etape.NonRetourAutoriseParId / NonRetourAutoriseLe`.
- `Regles.AutoriserNonRetour(mep, etape, user, now)` : pilote, phase exécution. `Demarrer` sur une étape point de non-retour refuse sans cette autorisation (« Point de non-retour : l'accord du pilote est requis »).
- `Regles.NonRetourFranchi(mep)` = une étape point de non-retour a été démarrée (Etat != AFaire). Dès lors : l'échéance ne s'applique plus au déploiement (ni Disponibilite, ni Terminer, ni AutoriserReprise, ni DemarrerResolution), pas de retour arrière automatique ni manuel (refus « Point de non-retour franchi (E000x) : corriger en avant »), et un échec de résolution laisse l'étape en échec sans déclencher le retour arrière (le pilote peut autoriser une reprise).
- Front : badge « Point de non-retour » ; avant démarrage, encart d'alerte + bouton pilote « Autoriser le passage » ; bandeau « Point de non-retour franchi » dans l'en-tête ; onglet Retour arrière l'indique.

## 6. Recette en production
- `Phase.Recette` ajoutée à la FIN de l'enum (ne pas décaler les valeurs stockées ; vérifier le mode de stockage).
- `Mep.TestsRecette : List<TestRecette { Id, Ordre, Libelle, ResultatAttendu, ResponsableId (participant), Resultat : ResultatControle?, Observation, ParId, Le }>` édités en préparation. `ErreursPlan` : au moins un test de recette, chacun avec libellé, résultat attendu et responsable relié à un compte.
- `Regles.PasserEnRecette(mep, user, now)` : pilote, phase exécution, tout le déploiement réussi → Phase.Recette.
- `Regles.ValiderTestRecette(mep, test, resultat, observation, user, now)` : responsable du test ou pilote ; observation obligatoire ; échec possible ; `Regles.RefaireTestRecette` (pilote) remet à null.
- En recette, l'échéance s'applique (retour arrière auto si atteinte avant clôture, sauf point de non-retour franchi) et le pilote peut déclencher le retour arrière (motif). `AppliquerEcheance` et `DeclencherRetourArriere` acceptent Execution et Recette.
- `Cloturer` en issue Réussie : seulement depuis Recette, tous les tests Conformes. Depuis Execution : refus « Passer d'abord en recette ».
- Front : onglet « Recette » visible à partir de la phase Recette (et en préparation pour l'édition dans Préparation), avec Conforme / Échec par test.

## 7. Bilan de clôture
- `Mep.BilanActive : bool` (défaut true, modifiable en préparation).
- `Mep.Bilan { CeQuiAMarche, Incidents, Actions, RedigeParId, Le }`. `Cloturer(mep, user, now, bilan?)` : si BilanActive, CeQuiAMarche et Incidents obligatoires (« Rien à signaler » accepté) ; s'applique aux deux issues.
- Front : dialogue de clôture avec les champs du bilan ; bilan affiché en tête d'une MEP clôturée.

## 8. Modèles, duplication, environnement
- `Mep.Environnement : string` (texte, suggestions Recette / Préproduction / Production), `Mep.EstModele : bool`.
- `POST /api/meps/{id}/dupliquer { environnement?, commeModele: bool }` : copie profonde en préparation (nouveaux Id, dépendances et références remappées, codes conservés) : champs, participants, systèmes, livrables, étapes (état AFaire, preuve vide, dates nulles, exécutant nul, autorisation non-retour nulle), plan de repli, configuration de sauvegarde (sans réalisation ni preuve de test), tests de recette (résultats vides), approbateurs (décisions vides), BilanActive. Échéance, date de MEP, procédure relue, go/no-go, bilan et résolutions exécutées NON copiés. Journal sur la nouvelle MEP : « Créée par duplication de … ».
- Un modèle ne peut pas passer en exécution (refus). Liste : filtre Modèles / MEP, bouton « Nouvelle MEP depuis un modèle », bouton Dupliquer sur une MEP (choix de l'environnement).
- Démo : un modèle « Modèle : déploiement API standard » construit à partir de la démo.

## 9. Pièces jointes
- `PieceJointe { Id, MepId?, EtapeId?, ResolutionTypeId?, ExecutionResolutionId?, NomFichier, TypeContenu, Taille, Sha256, DeposeParId, DeposeLe }`, contenu sur disque dans `Stockage:Dossier` (défaut `/data/pieces-jointes`, volume docker `pieces-jointes` dans docker-compose). Taille max `Stockage:TailleMaxMo` (défaut 20).
- Endpoints : upload multipart sur une MEP / une étape / une résolution type, liste, téléchargement (Content-Disposition, nom d'origine), suppression (déposant ou pilote ; résolution type : admin). Refus après clôture pour les pièces d'une MEP. Journal à l'ajout et à la suppression. Nom de fichier assaini, jamais utilisé comme chemin (stocker sous l'Id).
- Front : zone de dépôt (glisser-déposer + bouton) et liste dans le panneau d'étape, la fiche MEP et l'éditeur de résolution type.

## 10. Démo
- Comptes alice / bob / claire inchangés. Claire est l'approbatrice (décision en attente). Bob suppléant, et ajouter Claire comme 2e suppléante n'est PAS fait (elle approuve).
- E0001 de type Sauvegarde, mode PendantMep. E0004 point de non-retour. Deux tests de recette (« Ouvrir la page d'accueil fictive » Claire, « Appeler l'API de test » Bob). Bilan actif. Environnement « Production ». Critères go/no-go de démo. ApprobationsMinimum = 1.
