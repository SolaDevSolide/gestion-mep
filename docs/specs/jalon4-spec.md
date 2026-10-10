# Jalon 4 : spécification

Retours de Hugo du 10/10/2026, maquette validée : `docs/maquettes/jalon4-etapes.html` (ouvrir dans un navigateur ; elle est interactive).
Périmètre : préparation (comptes, participants, systèmes, responsables techniques, sauvegarde, recette), éditeur d'étapes en graphe, exécution des étapes et des résolutions, création d'une fiche de résolution depuis une étape.

Principes inchangés : toute règle métier est dans `GestionMep.Domain` (`Regles`), refus = **422** `{ message }` en français ; 403 réservé aux droits propres à l'API ; codes d'étape stables (`E0001`, `R0001`, jamais renumérotés) ; toute écriture journalise et notifie le flux SSE ; en préparation, toute modification du contenu remet les approbations à zéro (comme aujourd'hui).

---

## 1. Textes à corriger (front)

- Préparation › Validation, sous « Démarrage autorisé » : « Se coche automatiquement au Go, donné par le pilote ou un suppléant. » + bouton lien **« Donner le Go → »** (ouvre le dialogue Go / no-go) visible pour pilote / suppléant en préparation.
- Champ Suppléants : aide = « Ils ont les mêmes droits que le pilote. » (retirer « Le pilote ne peut pas être suppléant », la règle reste).
- Section Responsables techniques : retirer « distincts du pilote » (le pilote peut être responsable technique). Description : « Les développeurs et sachants de cette MEP : ce qu'ils ont développé, leur disponibilité et leur contact le jour J. »

## 2. Comptes : rôle, contact, expertises

Modèle `Utilisateur` : ajouter `Role` (string), `Contact` (string), `Expertises: List<Expertise>` (jsonb) avec `Expertise { SystemeCatalogueId: Guid, Detail: string }` (Detail = texte libre, ex. « Expert de la zone facturation »).

API :
- `UtilisateurDto` devient `{ id, login, nomAffiche, estAdmin, role, contact, expertises: [{ systemeCatalogueId, detail }] }` (toutes les routes qui le renvoient : `/auth/moi`, `/utilisateurs`).
- `POST /utilisateurs` (admin) accepte en plus `role`, `contact`.
- **`PUT /utilisateurs/moi/profil`** `{ nomAffiche?, role, contact, expertises: [{ systemeCatalogueId, detail }] }` → 200 `UtilisateurDto`. Chacun gère son propre profil (réponse de Hugo : « chacun peut gérer ses expertises »). Système du catalogue inconnu → 400 ; doublon de système → 400 ; textes rognés.
- **`PUT /utilisateurs/{id}/profil`** même corps → admin uniquement (403 sinon), pour corriger le profil d'un autre.

Front :
- Nouvelle page **« Mon profil »** (menu utilisateur et barre latérale) : nom affiché, rôle, contact, expertises (liste : système du catalogue + détail, ajout / suppression).
- Ajout d'une personne à une MEP (Préparation › Personnes) : le champ **Compte** passe en premier ; choisir un compte **pré-remplit** nom, rôle et contact depuis le profil (modifiables ensuite).
- Démo : Alice « Pilote / DBA » expertise BDD ; Bob « Développeur API » expertises API et MDM ; Claire « Développeuse front » expertise WEB ; contacts fictifs.

## 3. Préparation : ordre et suggestions

- La section **Systèmes** passe **avant** **Responsables techniques** (navigation de la préparation et ordre des cartes).
- Section Responsables techniques : bloc **« Suggestions d'après les systèmes de la MEP »** = comptes dont une expertise porte sur un `catalogueId` d'un système de la MEP, pas encore responsables techniques. Chaque suggestion montre nom, systèmes concernés, détail de l'expertise (au clic ou en sous-texte), et un bouton **Ajouter** (crée le participant si besoin, pré-rempli comme en 2, puis l'ajoute comme responsable technique avec ces systèmes et `perimetre` = détail).

## 4. Sauvegarde par système

Modèle : `Systeme` (système d'une MEP) gagne `Sauvegarde: SauvegardeSysteme` (jsonb ou colonnes, au choix) :
```
SauvegardeSysteme { Statut: StatutSauvegarde (ARenseigner | Requise | NonRequise) = ARenseigner,
                    Moment: ModeSauvegarde (AvantMep | PendantMep) = AvantMep,
                    Contenu: string   // quoi et comment (markdown) si Requise
                    Justification: string } // obligatoire si NonRequise
```
API : `SystemeDto` gagne `sauvegarde: { statut, moment, contenu, justification }`. **`PUT /meps/{id}/systemes/{sid}/sauvegarde`** `{ version, statut, moment, contenu, justification }` → 200 `MepDto`, préparation uniquement, droits d'édition du contenu ; journal `sauvegarde.systeme` « Sauvegarde BDD : requise (avant la MEP) » / « … : non requise (justification) ». Remet les approbations à zéro.

`erreursPlan` (remplace les anciennes règles « mode de sauvegarde » qui portaient sur toute la MEP) :
- « Sauvegarde : indiquer si BDD doit être sauvegardé. » pour chaque système `ARenseigner` ;
- « Sauvegarde de BDD non requise : justifier. » si `NonRequise` sans justification ;
- « Sauvegarde de BDD pendant la MEP : ajouter une étape de déploiement de type Sauvegarde sur ce système. » si `Requise` + `PendantMep` et aucune étape `Type = Sauvegarde` du plan de déploiement n'a ce système dans `systemeIds`.
`avertissements` : « Aucun système n'est sauvegardé. » si au moins un système et tous `NonRequise`.

La fiche Sauvegarde globale existante garde : responsable, réalisée le, copies, restauration testée (date, preuve). Son champ `mode` et les cases structure / données / configuration ne sont plus affichés (les garder en base pour compatibilité, ne plus les valider). Migration : systèmes existants → `ARenseigner` ; si l'ancien mode était `PendantMep`, mettre `Moment = PendantMep`.

Front : la section Sauvegarde affiche **un tableau de tous les systèmes de la MEP** ; par ligne : système (couleur), choix **Requise / Non requise**, puis selon le cas : moment (avant / pendant la MEP) + contenu (markdown), ou justification (obligatoire). Bandeau d'avertissement si aucun système n'est sauvegardé. Puis les champs globaux restants.

## 5. Recette : onglet à part, fiches par système

Modèle : remplacer `TestsRecette` par `FichesRecette: List<FicheRecette>` (jsonb) :
```
FicheRecette { Id, Ordre, SystemeId: Guid? (système de la MEP), Titre,
               Etapes: List<EtapeTest> }
EtapeTest { Id, Ordre, Instructions: string (markdown, voir §7), ResultatAttendu: string (markdown),
            Resultat: ResultatControle?, Observation, ParId, Le }
```
Pas de responsable (réponse C) : en exécution, le contact affiché est celui des **responsables techniques du système** de la fiche.

API (remplace `/tests-recette/*`, à supprimer) :
- `POST /meps/{id}/fiches-recette` `{ systemeId, titre }` → 200 `MepDto` (fiche vide ajoutée en fin).
- `PUT /meps/{id}/fiches-recette/{fid}` `{ systemeId, titre, etapes: [{ id?, instructions, resultatAttendu }] }` → remplace la fiche (les `id` connus gardent leur identité, ordre = ordre reçu).
- `DELETE /meps/{id}/fiches-recette/{fid}`.
- Préparation uniquement (422 sinon), droits d'édition du contenu, remettent les approbations à zéro. Journal `recette.fiche-ajoutee|modifiee|supprimee`.
- `POST /meps/{id}/fiches-recette/{fid}/etapes/{tid}/resultat` `{ resultat, observation }` : phase `Recette`, observation obligatoire, tout participant relié à un compte ou pilote / suppléant / admin ; étape déjà validée → 422 (« la faire refaire ») ; journal `recette.test` « Recette « Tables clients » étape 2 conforme : obs ».
- `POST /meps/{id}/fiches-recette/{fid}/refaire` (toute la fiche) et `…/etapes/{tid}/refaire` : pilote / suppléant, phase `Recette`.
- `MepDto.fichesRecette: [{ id, ordre, systemeId, titre, etapes: [{ id, ordre, instructions, resultatAttendu, resultat, observation, par, le }] }]` ; retirer `testsRecette`.

Règles :
- `erreursPlan` : « Ajouter au moins une fiche de recette. » ; « Recette « X » : ajouter au moins une étape de test. » ; « Recette « X », étape 2 : compléter les instructions et le résultat attendu. » ; « Recette « X » : indiquer un titre. ».
- `avertissements` : « Aucune recette pour le système WEB. » pour chaque système de la MEP sans fiche.
- Clôture depuis `Recette` : toutes les étapes de test `Conforme`, sinon 422 « Recette « X », étape 2 non conforme : clôture impossible. ».
- Duplication : les fiches sont copiées (nouveaux ids, résultats vides, `systemeId` remappé).
- Migration : les anciens tests deviennent une fiche « Recette » (`systemeId` null), une étape par test (`Libelle` → instructions, `ResultatAttendu` → résultat attendu, résultat / observation conservés).

Front : nouvel onglet **Recette** toujours visible, placé **après Retour arrière** et avant Journal (ordre : Préparation, Étapes, Exécution, Retour arrière, Recette, Journal). Retirer la section Recette de la Préparation. L'onglet présente les **systèmes de la MEP** (sections colorées) ; dans chacun, les fiches de recette (« + Ajouter une recette ») ; une fiche = titre + liste d'étapes de test (instructions avec l'éditeur de blocs §7, résultat attendu). En préparation : édition. En phase Recette : déroulé, chaque étape Conforme / Échec + observation, contact = responsables techniques du système, bouton Refaire pour le pilote. Une section « Sans système » pour les fiches sans `systemeId`.

## 6. Étapes : graphe par lignes

Modèle `Etape` :
- `SystemeIds: List<Guid>` (uuid[]) **remplace** `SystemeId` (migration : `[SystemeId]` ou vide). Chaque id = système de la MEP (400 sinon).
- `Ligne: int` (≥ 0) et `Ordre` = position dans la ligne (0, 1, 2…), par plan.
- `DureeEstimeeMinutes: int?`.
- `Dependances` reste la liste explicite des liens. **Règle** : chaque préalable est dans le même plan **et sur une ligne strictement inférieure** (422 « Un lien descend toujours : E0004 doit être sur une ligne au-dessus de E0003. »).

Liens par défaut (domaine, `Regles.RelierParDefaut`) : une étape attend **toutes** les étapes de la ligne au-dessus et est attendue par **toutes** celles de la ligne en dessous. Quand une étape quitte sa ligne, ses anciens suivants qui n'ont plus aucun préalable sont rattachés à toute la ligne au-dessus d'eux. Les lignes vides sont supprimées (renumérotation des `Ligne` sans trou). Les liens particuliers (ajoutés à la main) restent possibles.

API :
- `EtapeDto` : `systemeIds` (remplace `systemeId`), `ligne`, `ordre` (position dans la ligne), `dureeEstimeeMinutes`.
- `EtapeSaisie` : `systemeIds`, `dureeEstimeeMinutes`, et pour **`POST /etapes`** : `ligne?` (absent = nouvelle ligne en bas), `position?` (absent = en fin de ligne). À la création, si `dependances` est absent, **liens par défaut**. `PUT` ne change ni ligne ni position (il garde `dependances` reçues, validées par la règle des lignes).
- **`POST /meps/{id}/etapes/{eid}/placer`** `{ ligne, position, liensParDefaut = true }` → 200 `MepDto`. `ligne` peut valoir le nombre de lignes (= nouvelle ligne en bas). Même ligne : simple réordonnancement, liens inchangés. Autre ligne : liens par défaut si `liensParDefaut`, sinon suppression des liens devenus invalides. Préparation, droits d'édition. Journal `etape.deplacee` « E0005 placée ligne 2 ». Pas de `revision` exigée (la position n'est pas dans la fiche).
- `POST …/deplacer` (haut / bas) : à supprimer.
- `GET /meps/{id}` : `etapes` triées par plan, ligne, ordre.
- Migration : `Ligne` calculée pour l'existant = profondeur dans le graphe des dépendances (0 si aucun préalable, sinon 1 + max des préalables), `Ordre` = rang dans la ligne selon l'ancien ordre. Le faire en C# au démarrage (`Amorcage`) pour les étapes marquées non placées (ex. `Ligne = -1` posé par la migration), car c'est récursif.
- Duplication : copie `SystemeIds`, `Ligne`, `Ordre`, `DureeEstimeeMinutes`.
- Démo (nouvelle base) : reprendre l'exemple de la maquette (lignes : sauvegardes BDD / API / WEB ; migration BDD / déploiement API (API + LDAP) / publication WEB ; bascule des données (API + BDD + MDM, point de non-retour) ; bascule du portail ; contrôle final), systèmes de la MEP BDD, API, WEB, LDAP, MDM (catalogue démo à compléter : LDAP « Annuaire LDAP », MDM « MDM Phoenix », BATCH « Traitements nocturnes »), états d'exécution comme aujourd'hui (préparation). Garder les comptes alice / bob / claire, mot de passe demo. Instructions au format §7 avec du texte et des blocs de code.

## 7. Instructions : blocs Texte / Code (sans changement de schéma)

Les champs texte d'instructions restent des **chaînes markdown** : `Etape.Instructions`, `Etape.ResultatAttendu`, `ResolutionType.Symptomes`, `Diagnostic`, `Correction.Instruction`, `VerificationInstruction`, `EtapeTest.Instructions`, `EtapeTest.ResultatAttendu`.
Un bloc **Code** = bloc markdown délimité :
````
```
commande exacte
```
````
Le front découpe la chaîne en blocs (texte entre les blocs délimités = blocs Texte) et la recompose à l'enregistrement. Back : aucune contrainte de format.

Front :
- Composant `EditeurBlocs` : liste de blocs ; chaque bloc a un interrupteur **Texte | Code**, monter / descendre / supprimer, « + Bloc texte », « + Bloc code » ; bloc Texte = zone de saisie + **aperçu markdown** ; bloc Code = zone en police à chasse fixe.
- Composant `Instructions` (lecture, exécution) : rendu markdown sûr (`marked` + `dompurify`, dépendances npm à ajouter) ; chaque bloc de code rendu avec un bouton **Copier** (repli : sélection du texte si le presse-papiers est refusé). Cases à cocher markdown affichées.
- Utilisé pour : instructions et résultat attendu des étapes (déploiement et retour arrière), symptômes / diagnostic / corrections / vérification des résolutions types (éditeur et déroulé), instructions / résultat des étapes de test de recette.

## 8. Front : éditeur de graphe (onglets Étapes, Exécution, Retour arrière)

Reprendre la maquette `docs/maquettes/jalon4-etapes.html` (comportements à respecter) dans un composant React réutilisable `GrapheEtapes` (props : plan, mode `edition | execution`) :
- Lignes empilées de haut en bas, centrées ; fiches côte à côte dans une ligne. **Pas** de libellé de ligne à gauche.
- Fiche : bandeau des **systèmes** (couleur par système, partagé si plusieurs, nom + description si un seul), code, criticité (préparation) ou état (exécution : Réussie, En cours, Échec, Disponible, En attente), action, avatar + responsable (ou exécutant ayant pris la main), durée estimée, badges Sauvegarde / nb de résolutions. Point de non-retour : cadre rouge épais + bandeau « ⛔ POINT DE NON-RETOUR » + **barrière** rouge pointillée pleine largeur sous la ligne avec étiquette « ⛔ E0007 : après ce point, plus de retour arrière ».
- Liens dessinés en SVG **derrière** les fiches et l'étiquette de barrière. **Jonctions** : les liens de plusieurs étapes vers le même ensemble d'étapes se rejoignent sur une barre horizontale puis repartent (traits droits, coudes arrondis, une flèche par cible, point sur la barre à chaque arrivée) ; barres décalées si plusieurs entre les mêmes lignes. En exécution, liens verts quand les sources sont réussies.
- Préparation :
  - **glisser-déposer** une fiche sur une autre ligne ou entre deux fiches (repère d'insertion, ligne survolée en pointillés) → `placer` ;
  - **ronds** en haut et en bas de chaque fiche : tirer le rond du bas vers la fiche qui doit attendre (ou celui du haut vers une étape au-dessus) crée un lien particulier ; trait pointillé qui suit la souris ; cible entourée en vert si possible, en rouge sinon ;
  - cliquer sur un lien / une jonction le sélectionne : le panneau liste les liens (× pour un seul, bouton pour tous, touche Suppr) ;
  - **blocs fantômes** : « + Étape » au bout de chaque ligne (positionné à droite, hors centrage) et « + Nouvelle ligne » en bas → création + ouverture de la fiche ;
  - accessible au clavier : fiche focalisable, Entrée ouvre le panneau ; actions équivalentes au glisser-déposer dans le panneau (liste « Préalables » avec ajout / retrait) ;
  - barre **« Systèmes impactés »** au-dessus du graphe (puces colorées des systèmes de la MEP, ajout depuis le catalogue, retrait refusé avec message si des étapes l'utilisent ; mêmes routes que la section Systèmes).
- Panneau de droite (préparation) : code + type (Standard / Sauvegarde), action, **systèmes (plusieurs possibles, puces)**, responsable, criticité, durée estimée, **instructions (EditeurBlocs)**, résultat attendu (markdown + aperçu), préalables / débloque (listes avec ×, listes déroulantes d'ajout), **résolutions types** (cases ; celles du même système en premier, marquées « même système ») + bouton **« + Créer une nouvelle fiche de résolution »**, pièces jointes, case point de non-retour, supprimer.
- Exécution : mêmes fiches sans ronds ni fantômes ; panneau : état, systèmes, prévu / a pris la main (+ Prendre la main avec motif, existant), préalables avec leur état, **instructions rendues (markdown, Copier sur le code)**, résultat attendu rendu, accord du pilote pour le point de non-retour, Démarrer / preuve / Réussie / Échec, « Problème connu ? » → déroulé de résolution (existant, rendu avec `Instructions`).
- Retour arrière : même composant sur le plan `RetourArriere`, cadre rouge.
- Remplacer les vues existantes (EtapesEditeur, GrapheExecution, ListeExecution, ExecutionVue…) par ce composant ; supprimer le code mort.

## 9. Front : créer une fiche de résolution depuis une étape

- Le bouton « + Créer une nouvelle fiche de résolution » ouvre la **page de création** `/parametres/resolutions/nouvelle?mep={id}&etape={eid}` (la page existe aussi sans paramètres depuis Paramètres › Résolutions types).
- Formulaire : titre, système (catalogue, pré-sélectionné sur le premier système de l'étape), symptômes (markdown), diagnostic (EditeurBlocs), corrections ordonnées (chacune EditeurBlocs + résultat attendu, ajout / monter / descendre / supprimer), vérification finale (EditeurBlocs + résultat attendu). À droite : **aperçu en direct** tel qu'en exécution.
- « Enregistrer et lier à E0006 » : `POST /resolutions`, puis `PUT` de l'étape avec la résolution ajoutée à `resolutionIds`, puis retour à l'onglet Étapes avec l'étape sélectionnée. « Annuler » / fil d'Ariane « ← Retour à E0006 ».

## 10. Tests et documentation

- Back : tests du domaine (liens par défaut, placer, règle des lignes, migration des lignes, sauvegarde par système, fiches de recette, clôture) et de l'API (profil, placer, sauvegarde, fiches de recette, suppression des anciennes routes). Mettre à jour les tests existants touchés (`systemeId`, `tests-recette`, `deplacer`).
- Front : tests vitest du découpage / recomposition des blocs, du calcul des jonctions, des suggestions de responsables techniques, et du panneau d'étape (rendu markdown + Copier).
- `docs/api.md` : section « Jalon 4 : ajouts et changements » (changements incompatibles en tableau, comme pour le jalon 3).
- Guide de test : non concerné dans ce jalon.
