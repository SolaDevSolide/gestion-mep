# Jalon 4.2 : retours de Hugo du 10/10/2026 (après test du jalon 4.1)

Branche locale `jalon-4-2-retours` (créée depuis `jalon-4-1-ergonomie`). **Ne pas pousser** : Hugo valide d'abord, puis on fusionne.

## A. Édition d'une étape : tout visible, sans défilement

Constat : le panneau à onglets (Fiche, Instructions, Liens, Résolutions, Pièces jointes) oblige à défiler ; « pas propre ». Les instructions elles-mêmes conviennent.

Attendu : un **espace d'édition** où tout est visible en même temps, sans barre de défilement du panneau (écran de référence 1366×768 et plus).
- Ouverture en surcouche large (≈ 92 % de la largeur, toute la hauteur utile), le graphe reste visible derrière / sur le côté et l'étape éditée y est mise en évidence ; Échap ou ✕ ferme (avec garde si modifications non enregistrées).
- Barre du haut : code, badges (Sauvegarde, point de non-retour), **action** en grand champ, « modifications non enregistrées », Enregistrer / Annuler / Fermer.
- Trois colonnes :
  - **gauche (étroite)** : systèmes (puces, plusieurs), criticité, durée estimée (facultative), type, point de non-retour ;
  - **centre (large)** : instructions (éditeur de blocs actuel, qu'on garde) puis résultat attendu ; c'est la seule zone qui peut grandir : si le contenu dépasse, **seule cette colonne** défile, pas l'écran ;
  - **droite** : liens (préalables / débloque, compacts), résolutions (sélecteur avec recherche, compact : résultats limités en nombre, « voir plus »), pièces jointes (liste compacte), supprimer l'étape en bas.
- Plus d'onglets dans l'édition. En exécution, même principe (colonnes : état + actions / instructions rendues / liens + résolutions + pièces jointes).
- Sous ~1100 px de large : empilement acceptable.

## B. Glisser-déposer fluide

Constat : la fiche glissée « snappe » sur une grille. Attendu : la fiche suit **exactement** le pointeur, de manière continue (pas d'aimantation, pas de saut), à tous les niveaux de zoom ; seul le **repère bleu** d'insertion indique où elle sera déposée (à garder tel quel).

## C. Moins de texte

Réduire ou supprimer les textes d'aide et descriptions verbeux (astuce sous la barre, « Comment faire », descriptions de sections, aides répétées). Garder : libellés, messages d'erreur, états vides courts. Une aide reste accessible via une petite icône « ? » si nécessaire.

## D. Préparation › Intervention : pas de date ni d'heures à saisir

La date de MEP, l'heure de début et l'heure de fin ne se saisissent plus : ce sont des informations **constatées** (historique).
- Début = moment du Go (`demarrageAutorise.le`), fin = clôture (`cloture.le`). Afficher en lecture seule « Démarrée le … », « Terminée le … » (ou « — ») dans l'en-tête / l'identité.
- Back : `dateMep`, `heureDebut`, `heureFin` ne sont plus exigés nulle part (`erreursPlan`, complétude) ; `PUT /meps/{id}` les ignore (champs conservés en base pour l'existant, plus modifiables). `MepResumeDto.dateMep` : renvoyer la date du Go si elle existe, sinon `null` ; la liste des MEP affiche « Démarrée le » ou « Créée le ».
- L'échéance de retour arrière, le fuseau, l'indisponibilité restent tels quels.

## E. Plus de responsable par étape

Le responsable d'une étape se décide au moment de la MEP, pas avant.
- Back : `ResponsableId` n'est plus exigé (`erreursPlan` : retirer « Responsable » des champs manquants et la règle « responsable relié à un compte ») ; `EtapeSaisie.responsableId` ignoré (ne plus le renvoyer dans le formulaire). **Démarrer** : tout participant relié à un compte, le pilote / suppléants, ou un admin peut démarrer une étape disponible ; il devient l'**exécutant** (`executantId`), journalisé « E0005 démarrée par Bob ». Terminer / résolutions : l'exécutant, ou le pilote / suppléants. **Prendre la main** reste pour une étape **en cours** (ou en échec) démarrée par quelqu'un d'autre (motif obligatoire, journal comme aujourd'hui). Les étapes existantes gardent leur `responsableId` en base (sans effet). Adapter les tests.
- Front : retirer le champ Responsable de l'édition, de la fiche (afficher l'exécutant une fois démarrée, sinon rien) ; filtre « À moi » en exécution = étapes dont je suis l'exécutant ; « Mes systèmes » reste.

## F. Durée estimée facultative

Vérifier qu'aucune règle ni pastille ne l'exige (back : `int?` ; front : pas d'indicateur « à compléter », affichage omis si vide, résumé « durée cumulée » qui ignore les vides et l'indique, ex. « 145 min (2 étapes sans durée) »).

## G. Résolutions types sur plusieurs systèmes

- Back : `ResolutionType.SystemeCatalogueId` → **`SystemeCatalogueIds: List<Guid>`** (uuid[]), migration `[ancien]` ou vide ; DTO `systemeCatalogueIds` ; saisie idem (ids inconnus → 400) ; `GET /resolutions?systemeCatalogueId=` : filtre « contient ». Démo : au moins une résolution sur deux systèmes (ex. « API + BDD : connexion refusée après bascule »).
- Front : sélection multiple des systèmes dans la création / modification de résolution ; sélecteur de résolutions de l'étape : une résolution correspond si **un** de ses systèmes est dans le filtre ; « même système » si intersection non vide avec les systèmes de l'étape ; affichage de toutes ses puces de systèmes.

## Tests

Back et front à jour et verts, `npx tsc --noEmit`, `npm run build`. `docs/api.md` : section « Jalon 4.2 ».
