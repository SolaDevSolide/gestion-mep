# Contrat de l'API (jalon 1)

Toutes les routes sont sous `/api`, en JSON (camelCase), énumérations sérialisées en chaînes (`"Preparation"`, `"AFaire"`…). Dates : ISO 8601 (`DateTimeOffset` → `"2026-10-09T12:00:00+00:00"`, `DateOnly` → `"2026-10-09"`, `TimeOnly` → `"08:30:00"`). Identifiants : GUID.

Authentification par cookie (`gestion-mep.auth`, HttpOnly, SameSite=Lax). Toute route sauf `POST /api/auth/connexion` et `GET /api/sante` exige d'être connecté, sinon **401**.

## Erreurs

- **400** validation de saisie, **401** non connecté, **403** droit insuffisant, **404** introuvable.
- **409** conflit de version (verrou optimiste) : la ressource a changé depuis sa lecture.
- **422** refus par une règle métier.

Corps d'erreur : `{ "message": "texte en français pour l'utilisateur" }`.

## Énumérations

Liste à jour (jalon 3) ; le détail de chaque ajout est dans la section du jalon concerné.

- `Phase` : `Preparation`, `Execution`, `RetourArriere`, `Cloturee`, `Recette` (ordre de stockage ; cycle de vie : Preparation → Execution → Recette → Cloturee, ou → RetourArriere → Cloturee)
- `EtatEtape` : `AFaire`, `EnCours`, `Reussie`, `Echec`
- `Criticite` : `Faible`, `Moderee`, `Elevee`, `Bloquante`
- `StatutDisponibilite` : `PlanInvalide`, `EcheanceARenseigner`, `RetourArriere`, `FicheACompleter`, `InstructionsACompleter`, `Attend`, `Terminee`, `EnCours`, `ArretResolution`, `ResolutionEnCours`, `SiRetourArriere`, `Disponible`
- `IssueMep` : `Reussie`, `Repliee`
- `TypePlan` : `Deploiement`, `RetourArriere`
- `ResultatControle` : `Conforme`, `Echec`
- `StatutResolution` : `EnCours`, `Reussie`, `Echouee`
- `ModeSauvegarde` : `AvantMep`, `PendantMep` (jalon 3)
- `TypeEtape` : `Standard`, `Sauvegarde` (jalon 3)
- `DecisionApprobation` : `Approuvee`, `Refusee` (jalon 3)

## Authentification

| Méthode | Route | Corps | Réponse |
|---|---|---|---|
| POST | `/auth/connexion` | `{ login, motDePasse }` | 200 `UtilisateurDto` ; 401 si identifiants faux |
| POST | `/auth/deconnexion` | — | 204 |
| GET | `/auth/moi` | — | 200 `UtilisateurDto` |
| GET | `/utilisateurs` | — | 200 `UtilisateurDto[]` (pour relier un participant à un compte) |
| POST | `/utilisateurs` | `{ login, nomAffiche, motDePasse, estAdmin }` | 201 `UtilisateurDto` ; réservé aux admins (403) |

`UtilisateurDto` : `{ id, login, nomAffiche, estAdmin }`

## MEP

| Méthode | Route | Corps | Réponse |
|---|---|---|---|
| GET | `/meps` | — | 200 `MepResumeDto[]` (plus récentes d'abord) |
| POST | `/meps` | `{ outil, objet }` | 201 `MepDto`. Le créateur est ajouté comme participant relié à son compte et devient pilote. |
| GET | `/meps/{id}` | — | 200 `MepDto` |
| PUT | `/meps/{id}` | `MepIdentiteSaisie` | 200 `MepDto` ; 409 si `version` périmée |
| PUT | `/meps/{id}/sauvegarde` | `SauvegardeSaisie` | 200 `MepDto` ; 409 si `version` périmée. Autorisé dans les deux phases. |
| DELETE | `/meps/{id}` | — | 204 ; seulement en préparation, par le pilote ou un admin |
| GET | `/meps/{id}/journal` | — | 200 `EvenementDto[]` (plus récents d'abord) |
| GET | `/meps/{id}/flux` | — | `text/event-stream` : un événement `maj` avec `data: {"version": n}` à chaque modification de la MEP ; commentaire `: ping` toutes les 20 s |

`MepResumeDto` : `{ id, outil, versionCible, objet, dateMep, phase, echeanceRepli, pilote (string|null), nbEtapes, nbReussies, nbEchecs }`

`MepIdentiteSaisie` (toutes les clés présentes) : `{ version, outil, versionActuelle, versionCible, objet, impacts, dateMep, heureDebut, heureFin, fuseauHoraire, indisponibiliteMinutes, piloteId, suppleantId, echeanceRepli }`. Modifiable seulement en préparation, sauf `echeanceRepli` modifiable par le pilote dans les deux phases.

`SauvegardeSaisie` : `{ version, structure, donnees, configuration, responsableId, realiseeLe, procedure, copies, restaurationTestee, dateTestRestauration, preuveTestRestauration }`

`MepDto` :
```json
{
  "id": "…", "version": 3,
  "outil": "", "versionActuelle": "", "versionCible": "", "objet": "", "impacts": "",
  "dateMep": "2026-10-09", "heureDebut": "20:00:00", "heureFin": "23:00:00",
  "fuseauHoraire": "Europe/Paris", "indisponibiliteMinutes": 30,
  "piloteId": "participant id", "suppleantId": null,
  "phase": "Preparation", "echeanceRepli": "…",
  "sauvegarde": { "structure": true, "donnees": true, "configuration": false, "responsableId": null, "realiseeLe": null,
                  "procedure": "", "copies": "", "restaurationTestee": false, "dateTestRestauration": null, "preuveTestRestauration": "" },
  "procedureRelue": { "par": "Bob", "le": "…" } ,
  "demarrageAutorise": null,
  "participants": [ { "id", "nom", "role", "contact", "utilisateurId", "utilisateurNom" } ],
  "systemes": [ { "id", "nom", "cible", "impact" } ],
  "livrables": [ { "id", "objet", "version", "emplacement", "systemeId" } ],
  "etapes": [ EtapeDto ],
  "erreursPlan": [ "E0003 : compléter Instructions exactes." ],
  "droits": { "estPilote": true, "peutModifierPlan": true, "participantId": "id du participant relié à l'utilisateur courant ou null" }
}
```
`procedureRelue` / `demarrageAutorise` : `null` ou `{ par: string, le: DateTimeOffset }`.
`peutModifierPlan` = phase Preparation.

`EtapeDto` : `{ id, code, ordre, action, systemeId, responsableId, criticite, etat, instructions, resultatAttendu, preuve, debutReel, finReelle, revision, dependances: [id], disponibilite: { statut, libelle } }` — étapes triées par `ordre`.

`EvenementDto` : `{ id, quand, auteur, type, detail }`

## Participants, systèmes, livrables (préparation uniquement, sinon 422)

| Méthode | Route | Corps |
|---|---|---|
| POST | `/meps/{id}/participants` | `{ nom, role, contact, utilisateurId }` |
| PUT | `/meps/{id}/participants/{pid}` | idem |
| DELETE | `/meps/{id}/participants/{pid}` | — (422 si pilote, suppléant ou responsable d'une étape) |
| POST / PUT / DELETE | `/meps/{id}/systemes[/{sid}]` | `{ nom, cible, impact }` (DELETE : 422 si utilisé par une étape) |
| POST / PUT / DELETE | `/meps/{id}/livrables[/{lid}]` | `{ objet, version, emplacement, systemeId }` |

Toutes renvoient 200 `MepDto` (DELETE aussi).

## Étapes

| Méthode | Route | Corps | Notes |
|---|---|---|---|
| POST | `/meps/{id}/etapes` | `{}` ou `EtapeSaisie` sans `revision` | crée l'étape avec le prochain code, criticité Modérée, état À faire |
| PUT | `/meps/{id}/etapes/{eid}` | `EtapeSaisie` | 409 si `revision` périmée ; 422 si cycle ou hors préparation. `revision` incrémentée. |
| DELETE | `/meps/{id}/etapes/{eid}` | — | 422 si une autre étape en dépend |
| POST | `/meps/{id}/etapes/{eid}/deplacer` | `{ sens: "haut" \| "bas" }` | change l'ordre d'affichage |

`EtapeSaisie` : `{ revision, action, systemeId, responsableId, criticite, instructions, resultatAttendu, dependances: [id] }`

### Exécution (phase Execution)

| Méthode | Route | Corps | Qui |
|---|---|---|---|
| POST | `/meps/{id}/etapes/{eid}/demarrer` | `{ revision }` | responsable de l'étape |
| POST | `/meps/{id}/etapes/{eid}/terminer` | `{ revision, resultat: "Reussie"\|"Echec", preuve, resultatVerifie }` | responsable |
| POST | `/meps/{id}/etapes/{eid}/autoriser-reprise` | `{ revision }` | pilote / suppléant |

Toutes renvoient 200 `MepDto`.

## Validations et phase

| Méthode | Route | Corps | Qui |
|---|---|---|---|
| POST | `/meps/{id}/procedure-relue` | `{ relue: true\|false }` | tout participant relié à un compte, en préparation |
| POST | `/meps/{id}/phase` | `{ phase: "Execution"\|"Preparation" }` | pilote / suppléant ; 422 avec la liste des anomalies si le plan est incomplet |

## Divers

- `GET /api/sante` → 200 `{ "statut": "ok" }` (sonde Docker).
- Chaque modification écrit un événement dans le journal (type : `mep.creee`, `mep.modifiee`, `sauvegarde.modifiee`, `participant.*`, `systeme.*`, `livrable.*`, `etape.creee|modifiee|supprimee|deplacee|demarree|reussie|echec|reprise`, `procedure.relue`, `phase.execution`, `phase.preparation`) et notifie le flux SSE.

## Précisions d'implémentation

- **Codes** : une règle métier du domaine (`Regles`) renvoie toujours 422, y compris « seul le responsable peut démarrer » ou « seul le pilote peut passer en exécution ». 403 est réservé aux droits propres à l'API : création d'un compte hors admin, suppression d'une MEP par un non-pilote, modification de l'échéance de retour arrière par un non-pilote, validation de la relecture par quelqu'un qui n'est pas participant relié. Corps JSON absent, mal formé ou enum inconnue : 400 `{message}`. Route `/api/...` inconnue : 404 JSON (jamais `index.html`).
- **Verrous** : `version` (MEP) et `revision` (étape) sont obligatoires là où le contrat les prévoit (400 si absents, 409 si périmés). `Mep.Version` est en plus un jeton de concurrence EF : deux écritures simultanées donnent aussi 409. Toute modification de la MEP ou de ses enfants (participants, systèmes, livrables, étapes, phase, relecture, sauvegarde) incrémente `version` ; un `PUT /meps/{id}` qui ne change rien n'incrémente rien.
- **Réponses des étapes** : `POST /etapes`, `PUT`, `DELETE` et `deplacer` renvoient 200 `MepDto` (comme les autres routes de modification ; la création n'est pas un 201). `deplacer` en bout de liste est sans effet (200). Les révisions ne changent pas lors d'un déplacement.
- **`PUT /meps/{id}`** : en exécution, seule `echeanceRepli` peut différer de l'état actuel, sinon 422 ; les autres clés doivent donc être renvoyées inchangées. Changer l'échéance exige pilote, suppléant ou admin (403). `piloteId`/`suppleantId` doivent être des participants de la MEP (400). `outil` est obligatoire (400). Les chaînes sont rognées (`trim`).
- **`POST /etapes`** : corps vide, `{}` ou `EtapeSaisie` ; les champs absents restent vides, criticité Modérée par défaut. `PUT` remplace tous les champs de la fiche : une clé absente efface la valeur (`criticite` absente → null, « Fiche à compléter »).
- **Suppressions** : un participant désigné responsable de la sauvegarde ne peut pas non plus être supprimé (422). Supprimer un système détache les livrables qui y étaient rattachés (`systemeId` → null).
- **`POST /utilisateurs`** : le login est mis en minuscules (la connexion est insensible à la casse) ; mot de passe d'au moins 8 caractères ; login déjà pris → 400. `GET /auth/moi` renvoie 401 si le compte a été supprimé.
- **`POST /meps/{id}/procedure-relue`** : `relue: true` enregistre l'utilisateur courant et l'heure ; `false` efface la validation. Modifier le plan ensuite ne la réinitialise pas.
- **Flux SSE** : la connexion envoie d'abord le commentaire `: connecté`, puis `event: maj` / `data: {"version":n}` ; si plusieurs modifications arrivent pendant qu'un client lit, seule la dernière version est livrée. Le client doit se reconnecter (EventSource le fait seul) et relire la MEP après coupure. La suppression d'une MEP n'émet pas d'événement. Diffusion en mémoire : une seule instance du serveur.
- **Dates** : les `DateTimeOffset` reçus sont convertis en UTC ; ils sont renvoyés avec `+00:00`.
- **Journal** : détails en français, par exemple « E0002 démarrée », « E0002 réussie : API démo OK », « E0005 en échec : … », « Participant ajouté : Dave », « MEP modifiée : objet, pilote ».

---

# Jalon 2 : ajouts et changements

Le domaine (`backend/src/GestionMep.Domain`) porte toutes les règles ; l'API ne fait qu'appeler `Regles` (422 sur `RegleMetierException`).

## Énumérations nouvelles ou étendues

- `Phase` : `Preparation`, `Execution`, `RetourArriere`, `Cloturee`
- `IssueMep` : `Reussie`, `Repliee`
- `TypePlan` : `Deploiement`, `RetourArriere`
- `ResultatControle` : `Conforme`, `Echec`
- `StatutResolution` : `EnCours`, `Reussie`, `Echouee`
- `StatutDisponibilite` : ajout de `ResolutionEnCours` et `SiRetourArriere`

## MepDto : champs ajoutés

```json
{
  "issue": null,
  "retourArriere": null,            // ou { "le": "…", "par": "Alice" | null (automatique), "motif": "Échéance de retour arrière atteinte." }
  "cloture": null,                  // ou { "le": "…", "par": "Alice" }
  "planRepli": { "quandDeclencher": "", "dureeTesteeMinutes": 30, "versionARestaurer": "", "donneesDepuisSauvegarde": "", "siRepliEchoue": "" },
  "systemes": [ { "id", "nom", "cible", "impact", "catalogueId" } ],
  "etapes": [ EtapeDto ],           // les deux plans ; filtrer sur `plan`
  "resolutions": [ ExecutionResolutionDto ],
  "droits": { "estPilote", "peutModifierPlan", "participantId" }
}
```

`EtapeDto` gagne : `plan` (`TypePlan`), `executantId` (participant ayant pris la main, ou null), `resolutionIds: [id]` (résolutions types liées). `responsableId` reste le responsable prévu ; l'exécutant effectif est `executantId ?? responsableId`.

`ExecutionResolutionDto` : `{ id, etapeId, resolutionTypeId, titre, symptomes, diagnostic, corrections: [ { ordre, instruction, resultatAttendu, resultat (ResultatControle|null), observation, par (string|null), le } ], verificationInstruction, verificationAttendu, verificationResultat, verificationObservation, statut, demarreePar (string), demarreeLe, termineeLe }`.

## Plan de retour arrière

- `PUT /meps/{id}/plan-repli` `{ version, quandDeclencher, dureeTesteeMinutes, versionARestaurer, donneesDepuisSauvegarde, siRepliEchoue }` → 200 `MepDto`. Préparation uniquement (422 sinon). Journal `repli.modifie`.
- Étapes de retour arrière : mêmes routes que les étapes. `POST /meps/{id}/etapes` accepte `plan` (`"Deploiement"` par défaut, ou `"RetourArriere"` → code `R0001`…). Les dépendances doivent appartenir au même plan (422). `deplacer` agit à l'intérieur du plan.
- `POST /meps/{id}/retour-arriere` `{ motif }` → pilote/suppléant, en exécution. Journal `repli.declenche`.
- `POST /meps/{id}/cloturer` `{}` → pilote/suppléant. En exécution : tout le déploiement réussi → `issue: Reussie`. En retour arrière : toutes les étapes de repli réussies → `issue: Repliee`. Journal `mep.cloturee`.
- **Échéance automatique** : un service d'arrière-plan vérifie chaque minute les MEP en exécution (`Regles.AppliquerEcheance`) et toute lecture/écriture d'une MEP l'applique aussi avant de répondre ; le basculement est journalisé (`repli.declenche`, auteur « Système ») et notifié par SSE.
- `phase` (`POST /meps/{id}/phase`) reste limité à `Execution` ↔ `Preparation`.

## Prendre la main

- `POST /meps/{id}/etapes/{eid}/prendre-la-main` `{ revision }` → tout utilisateur connecté. S'il n'est pas encore participant de la MEP, un participant est créé pour lui (nom = nom affiché, rôle « Prise en main », relié à son compte). Journal `etape.prise-en-main` : « Bob a pris la main sur E0003 (prévu : Claire) ».
- `demarrer`, `terminer` et les résolutions exigent d'être l'exécutant effectif (422 sinon, avec un message qui invite à prendre la main).

## Catalogue global (Paramètres)

Lecture : tout utilisateur connecté. Écriture : admin (403 sinon).

| Méthode | Route | Corps |
|---|---|---|
| GET | `/catalogue/systemes` | → `SystemeCatalogueDto[]` `{ id, nom, description, cible, actif }` (triés par nom) |
| POST / PUT | `/catalogue/systemes[/{id}]` | `{ nom, description, cible, actif }` → `SystemeCatalogueDto` ; nom unique (400) |
| DELETE | `/catalogue/systemes/{id}` | 204 ; s'il est utilisé par une MEP, il est seulement désactivé (`actif: false`) |
| POST | `/meps/{id}/systemes/depuis-catalogue` | `{ catalogueId, impact }` → `MepDto` ; copie nom et cible, renseigne `catalogueId` ; préparation uniquement ; 422 si déjà présent dans la MEP |

## Résolutions types (Paramètres)

Lecture : tout utilisateur connecté. Création et modification : tout utilisateur connecté (le développeur en crée en préparant son étape). Suppression : admin.

| Méthode | Route | Corps |
|---|---|---|
| GET | `/resolutions` | `?systemeCatalogueId=` facultatif → `ResolutionTypeDto[]` (actives, triées par titre) |
| GET | `/resolutions/{id}` | → `ResolutionTypeDto` |
| POST | `/resolutions` | `ResolutionTypeSaisie` → 201 `ResolutionTypeDto` ; 422 avec la liste de `Regles.ErreursResolutionType` |
| PUT | `/resolutions/{id}` | `ResolutionTypeSaisie` + `version` → 200 ; 409 si version périmée. Les résolutions déjà démarrées gardent leur copie. |
| DELETE | `/resolutions/{id}` | 204 ; désactivation si liée à une étape |

`ResolutionTypeDto` : `{ id, titre, symptomes, diagnostic, systemeCatalogueId, corrections: [ { ordre, instruction, resultatAttendu } ], verificationInstruction, verificationAttendu, actif, version, modifieeLe }`.
`ResolutionTypeSaisie` : mêmes champs sans `id`, `actif`, `modifieeLe`.

`EtapeSaisie` gagne `resolutionIds: [id]` (résolutions existantes et actives, 400 sinon).

## Exécution d'une résolution

| Méthode | Route | Corps |
|---|---|---|
| POST | `/meps/{id}/etapes/{eid}/resolutions` | `{ resolutionTypeId, revision }` → `MepDto` (journal `resolution.demarree`) |
| POST | `/meps/{id}/resolutions/{rid}/corrections/{ordre}` | `{ resultat: "Conforme"\|"Echec", observation }` → `MepDto` (journal `resolution.correction`) |
| POST | `/meps/{id}/resolutions/{rid}/verification` | `{ resultat, observation }` → `MepDto` (journal `resolution.reussie` ou `resolution.echouee` + `repli.declenche`) |

Une correction ou une vérification en échec termine la résolution et **déclenche automatiquement le retour arrière**. Une vérification conforme remet l'étape « À faire » : son exécutant peut la redémarrer.

## Précisions d'implémentation (jalon 2)

- **Stockage** : les énumérations restent stockées en entiers (comme au jalon 1, sans perte à la migration `Jalon2` : les étapes existantes prennent `Plan = Deploiement`) ; l'API les sérialise en chaînes. Les corrections des résolutions (types et exécutions) sont stockées en `jsonb`, `Etape.ResolutionIds` en `uuid[]`.
- **Corps d'erreur 422 d'une résolution type** : `{ "message": "<anomalies jointes>", "erreurs": ["Donner un titre à la résolution.", …] }`. Le champ `erreurs` est facultatif et n'existe que sur ces refus.
- **Ordre des étapes** : `ordre` est propre à chaque plan (1, 2, 3… pour le déploiement et, séparément, pour le retour arrière). `etapes` est renvoyé trié par plan (déploiement d'abord) puis par `ordre`.
- **`MepResumeDto`** : `nbEtapes`, `nbReussies` et `nbEchecs` ne comptent que les étapes de déploiement.
- **Échéance automatique** : appliquée à chaque `Charger` d'une MEP (toutes les routes `/meps/{id}/…` sauf `journal` et `flux`) et par la surveillance d'arrière-plan (`Surveillance:Activer`, vrai par défaut). `GET /meps` (liste) n'applique pas l'échéance : la phase peut y rester périmée jusqu'à une minute. La bascule incrémente `version`.
- **Journal** : `repli.declenche` manuel : « Retour arrière déclenché par Alice : motif » ; automatique (échéance ou résolution en échec), auteur « Système » : « Retour arrière déclenché automatiquement : motif ». Une résolution en échec écrit, dans l'ordre : `resolution.correction` (si échec d'une correction), `resolution.echouee`, `repli.declenche`. `resolution.correction` : « Correction 1 conforme[ : observation] » ou « Correction 1 en échec : observation ».
- **Prise de main** : l'utilisateur déjà responsable de l'étape qui « reprend la main » remet `executantId` à null (journal « … a repris la main sur E0003 »). Un participant exécutant d'une étape ne peut pas être supprimé (422). Le participant créé automatiquement n'est ajouté que si la prise de main aboutit.
- **`PUT /etapes/{eid}`** : `resolutionIds` absent efface les liens (comme `dependances`). Une résolution désactivée déjà liée à l'étape peut y rester ; on ne peut pas en lier une nouvelle désactivée ou inconnue (400). `plan` est ignoré sur un `PUT` (le plan d'une étape ne change pas).
- **Résolutions types** : `GET /resolutions?systemeCatalogueId=` filtre sur l'égalité exacte (les résolutions sans système n'apparaissent que sans filtre) et ne renvoie que les actives ; `GET /resolutions/{id}` renvoie aussi une désactivée. Les corrections sont triées par `ordre` reçu puis renumérotées 1..n. `version` démarre à 1 ; un `DELETE` qui désactive incrémente `version`. Supprimer une étape supprime aussi les exécutions de résolution qui s'y rattachent.
- **Catalogue** : nom unique sans tenir compte de la casse (400). `DELETE` désactive aussi un système référencé par une résolution type. Ajouter à une MEP un système désactivé : 422. Renommer un système du catalogue ne modifie pas les systèmes déjà copiés dans les MEP.
- **Amorçage** : sur une base du jalon 1 déjà amorcée (utilisateurs présents) avec `Demo:Activer`, un catalogue et des résolutions types vides sont créés seuls ; les MEP existantes ne sont pas touchées (leurs systèmes n'ont donc pas de `catalogueId` et leurs étapes pas de `resolutionIds`).


---

# Jalon 3 : ajouts et changements

Spécification : `conception/jalon3-spec.md`. Comme avant, toute règle du domaine (`Regles`) refusée donne **422** `{ message }` (texte français à afficher tel quel) ; 403 reste réservé aux droits propres à l'API (admin, déposant d'une pièce jointe).

## Changements incompatibles (à reprendre côté front)

| Avant (jalon 2) | Jalon 3 |
|---|---|
| `MepDto.suppleantId` (participant ou null) | **`suppleantIds: [participantId]`** (toujours un tableau, éventuellement vide) |
| `MepIdentiteSaisie.suppleantId` | **`suppleantIds: [participantId]`** (absent ou `null` : inchangés) |
| `POST /meps/{id}/phase { phase: "Execution" }` | décision **Go** : exige `criteresCoches` (tous les critères actifs) et les approbations ; voir « Go / no-go » |
| `POST /meps/{id}/cloturer {}` depuis `Execution` | refusé (422 « Passer d'abord en recette… ») : la clôture réussie se fait depuis `Recette` ; corps = bilan (obligatoire si `bilanActive`) |
| `POST /meps/{id}/retour-arriere` en `Execution` seulement | aussi en `Recette` ; refusé une fois le point de non-retour franchi |
| `GET /meps` : uniquement des MEP | renvoie aussi les **modèles** (`estModele: true`) ; filtre `?modele=true|false` |
| Échéance automatique : pas de bascule si tout le déploiement est réussi | bascule en `Execution` **et** `Recette` dès l'échéance atteinte, tant que la MEP n'est pas clôturée, **sauf point de non-retour franchi** |
| `erreursPlan` | peut contenir les anomalies d'approbation et de recette (voir plus bas) |

## Énumérations ajoutées

- `Phase` : ajout de **`Recette`** (en fin d'énumération : valeur stockée 4, les valeurs existantes ne bougent pas).
- `ModeSauvegarde` : `AvantMep` (défaut), `PendantMep`.
- `TypeEtape` : `Standard` (défaut), `Sauvegarde`.
- `DecisionApprobation` : `Approuvee`, `Refusee`.

## MepDto : champs ajoutés ou modifiés

```json
{
  "suppleantIds": ["participant id"],              // remplace suppleantId
  "environnement": "Production",                   // texte libre (suggestions : Recette, Préproduction, Production)
  "estModele": false,
  "approbationsMinimum": 1,                        // paramètre global au moment de la lecture
  "approbations": [ ApprobationDto ],
  "goNoGo": [ { "libelle": "Sauvegarde disponible", "par": "Alice", "le": "…" } ],   // critères cochés au Go (copie figée) ; [] avant le Go
  "testsRecette": [ TestRecetteDto ],              // triés par ordre
  "bilanActive": true,
  "bilan": null,                                   // ou BilanDto après la clôture
  "nonRetourFranchi": null,                        // ou le code de l'étape point de non-retour démarrée, ex. "E0004"
  "piecesJointes": [ PieceJointeDto ],             // toutes les pièces de la MEP (MEP, étapes, résolutions exécutées), par date de dépôt
  "sauvegarde": { "mode": "PendantMep", "structure": true, … },
  "droits": { "estPilote", "peutModifierPlan", "participantId", "estApprobateur": true }
}
```

- `ApprobationDto` : `{ id, utilisateurId, nom, decision: DecisionApprobation|null, commentaire, le: DateTimeOffset|null }`. `decision: null` = en attente. `nom` = nom affiché du compte.
- `TestRecetteDto` : `{ id, ordre, libelle, resultatAttendu, responsableId (participant|null), resultat: ResultatControle|null, observation, par: string|null, le: DateTimeOffset|null }`.
- `BilanDto` : `{ ceQuiAMarche, incidents, actions, redigePar: string|null, le }`.
- `PieceJointeDto` : `{ id, mepId, etapeId, resolutionTypeId, executionResolutionId, nomFichier, typeContenu, taille (octets), sha256 (hex minuscule), deposeParId, deposePar (nom|null), deposeLe }`. Un seul des rattachements est renseigné, plus `mepId` pour les pièces d'une étape ou d'une résolution exécutée.
- `droits.estPilote` : vrai pour le pilote **et tous les suppléants**. `droits.estApprobateur` : l'utilisateur courant figure dans `approbations`.

`EtapeDto` gagne : `type` (`TypeEtape`), `pointDeNonRetour` (bool), `nonRetourAutorise` (`null` ou `{ par, le }` : accord du pilote).

`MepResumeDto` gagne : `environnement`, `estModele`.

## Identité, suppléants, environnement, bilan

`PUT /meps/{id}` (`MepIdentiteSaisie`) : `{ version, outil, versionActuelle, versionCible, objet, impacts, dateMep, heureDebut, heureFin, fuseauHoraire, indisponibiliteMinutes, piloteId, suppleantIds, echeanceRepli, environnement?, bilanActive? }`.

- `suppleantIds` : participants de la MEP (400 sinon). 422 « Alice est le pilote : il ne peut pas être aussi suppléant. » ; 422 « Bob est désigné deux fois comme suppléant. ». Absent/`null` : inchangés.
- `environnement` (texte, rogné) et `bilanActive` (bool) : facultatifs, absents/`null` = inchangés. Comme les autres champs d'identité, modifiables seulement en préparation.
- Désigner comme pilote un participant dont le compte est approbateur : 422 (« … est approbateur : le retirer des approbateurs avant d'en faire le pilote. »).
- Journal `mep.modifiee` : noms des champs, dont `suppléants`, `environnement`, `bilan de clôture`.
- Un participant suppléant ou responsable d'un test de recette ne peut pas être supprimé (422).

## Sauvegarde avant ou pendant la MEP

- `PUT /meps/{id}/sauvegarde` : `SauvegardeSaisie` gagne `mode` (`ModeSauvegarde`, facultatif : absent/`null` = inchangé). Changer le mode n'est possible qu'en préparation (422 sinon) ; les autres champs restent modifiables dans toutes les phases.
- `EtapeSaisie` (POST/PUT étape) gagne `type` (`TypeEtape`, absent = `Standard`) et `pointDeNonRetour` (bool, absent = false). Sur une étape du plan `RetourArriere`, `type: "Sauvegarde"` ou `pointDeNonRetour: true` → 422 « Le plan de retour arrière n'accepte ni étape de sauvegarde ni point de non-retour. ». Modifier `pointDeNonRetour` efface l'accord du pilote déjà donné.
- `erreursPlan` en mode `PendantMep` sans étape de déploiement de type `Sauvegarde` : « Sauvegarde pendant la MEP : indiquer l'étape de déploiement de type Sauvegarde. ». En mode `PendantMep`, l'étape qui fait la sauvegarde est celle de `type: "Sauvegarde"` (affichage « La sauvegarde est faite par l'étape E000x »).

## Circuit d'approbation

| Méthode | Route | Corps | Qui / règles |
|---|---|---|---|
| PUT | `/meps/{id}/approbateurs` | `{ version, utilisateurIds: [compte] }` | tout connecté, en préparation (422 sinon). Comptes existants (400). Doublons ignorés. Le compte du pilote est refusé (422 « Alice est le pilote : il ne peut pas approuver sa propre MEP. »). Les approbateurs déjà désignés gardent leur entrée ; **toutes** les décisions sont remises à zéro. Journal `approbateurs.modifies` « Approbateurs : Claire, Bob » (« aucun » si vide). → 200 `MepDto` |
| POST | `/meps/{id}/approbation` | `{ version, decision: "Approuvee"\|"Refusee", commentaire }` | l'approbateur désigné seulement (422 sinon), jamais le compte du pilote ; en préparation ; seulement si `erreursPlan` ne contient plus que des anomalies d'approbation (sinon 422 « Plan incomplet, approbation impossible : … »). `Refusee` exige un commentaire (422 « Expliquer le refus dans le commentaire. »). 409 si `version` périmée, 400 si `decision` absente. Journal `approbation.approuvee` « MEP approuvée par Claire[ : commentaire] » ou `approbation.refusee` « MEP refusée par Claire : commentaire ». → 200 `MepDto` |

- **Remise à zéro** : en préparation, toute modification du contenu (`PUT /meps/{id}`, `PUT /sauvegarde`, participants, systèmes y compris `depuis-catalogue`, livrables, étapes : création, modification, dépendances, suppression, déplacement, `PUT /plan-repli`, tests de recette, approbateurs) remet toutes les décisions à `null`. Si au moins une décision existait, le journal reçoit en plus `approbations.reinitialisees` « Approbations à refaire : la MEP a été modifiée » (même version que la modification). Ne remettent pas à zéro : relecture de la procédure, no-go, pièces jointes, actions d'exécution. Hors préparation, les décisions restent comme trace.
- `erreursPlan` (anomalies d'approbation, en fin de liste) : « Il faut N approbation(s). » tant que moins de N approbations `Approuvee` (N = `approbationsMinimum`, 0 = aucune exigée) ; « Claire a refusé : commentaire » pour chaque refus ; « Alice ne peut pas approuver : c'est le pilote. » si un approbateur est devenu le compte du pilote.

## Go / no-go

| Méthode | Route | Corps | Notes |
|---|---|---|---|
| POST | `/meps/{id}/phase` | `{ phase: "Execution", criteresCoches: [critereId] }` | **Go**. Pilote ou suppléant. Refus 422 dans l'ordre : modèle (« Un modèle ne passe pas en exécution : le dupliquer pour créer une MEP. »), phase, « Plan incomplet : … » (approbations comprises), critère actif non coché (« Go impossible : cocher « Supervision en place ». »). Les critères inactifs et les ids inconnus sont ignorés. Enregistre `goNoGo` (libellés des critères actifs, par ordre). Journal `gonogo.go` « Go (critère 1, critère 2…) » puis `phase.execution` « Plan passé en exécution ». |
| POST | `/meps/{id}/no-go` | `{ motif }` | Pilote ou suppléant, en préparation, motif obligatoire (422 « Indiquer le motif du no-go. »). La MEP reste en préparation (version incrémentée). Journal `gonogo.nogo` « No-go : motif ». → 200 `MepDto` |

`POST /meps/{id}/phase { phase: "Preparation" }` (retour en préparation depuis l'exécution) est inchangé ; `goNoGo` est conservé et remplacé au Go suivant.

## Point de non-retour

- `POST /meps/{id}/etapes/{eid}/autoriser-non-retour` `{ revision }` → pilote ou suppléant, phase `Execution`, étape `pointDeNonRetour` pas encore démarrée et pas encore autorisée (422 sinon). Renseigne `nonRetourAutorise`, incrémente `revision`. Journal `etape.non-retour-autorise` « Passage du point de non-retour E0004 autorisé ». → 200 `MepDto`.
- `demarrer` sur une étape point de non-retour sans accord : 422 « Point de non-retour : l'accord du pilote est requis. ».
- **Franchi** dès qu'une étape point de non-retour a été démarrée (même si elle est ensuite reprise) ; `nonRetourFranchi` donne son code. Dès lors :
  - l'échéance ne s'applique plus au déploiement (`disponibilite` n'affiche plus « Échéance à renseigner » / « RETOUR ARRIÈRE », `terminer` en réussite, `autoriser-reprise` et les résolutions restent possibles après l'échéance) ;
  - aucun retour arrière : ni automatique (échéance), ni manuel (`POST /retour-arriere` → 422 « Point de non-retour franchi (E0004) : corriger en avant. ») ;
  - une résolution en échec laisse l'étape en échec, sans retour arrière (journal `resolution.echouee` seul, sans `repli.declenche`) ; le pilote peut `autoriser-reprise`.

## Recette en production

Préparation (préparation uniquement, sinon 422 ; chaque écriture remet les approbations à zéro) — toutes → 200 `MepDto` :

| Méthode | Route | Corps |
|---|---|---|
| POST | `/meps/{id}/tests-recette` | `{ libelle, resultatAttendu, responsableId }` (ajouté en fin, `ordre` = max + 1). Journal `recette.test-ajoute` |
| PUT | `/meps/{id}/tests-recette/{tid}` | idem (remplace les trois champs). Journal `recette.test-modifie` |
| DELETE | `/meps/{id}/tests-recette/{tid}` | — ; les `ordre` sont renumérotés 1..n. Journal `recette.test-supprime` |
| POST | `/meps/{id}/tests-recette/{tid}/deplacer` | `{ sens: "haut"\|"bas" }` (en bout de liste : sans effet). Journal `recette.test-deplace` |

`responsableId` : participant de la MEP (400 sinon) ou `null`. `erreursPlan` : « Ajouter au moins un test de recette. », « Test de recette 2 : compléter Libellé, Résultat attendu, Responsable. », « Test de recette 2 : le responsable Eve n'est relié à aucun compte. ».

Déroulé :

| Méthode | Route | Corps | Qui / règles |
|---|---|---|---|
| POST | `/meps/{id}/phase` | `{ phase: "Recette" }` | pilote ou suppléant, depuis `Execution`, tout le déploiement réussi (422 « E0005 n'est pas réussie : recette impossible. »). Journal `phase.recette` « MEP passée en recette ». |
| POST | `/meps/{id}/tests-recette/{tid}/resultat` | `{ resultat: "Conforme"\|"Echec", observation }` | responsable du test (compte relié au participant) ou pilote/suppléant ; phase `Recette` ; observation obligatoire ; un test déjà validé est refusé (le faire refaire). Journal `recette.test` « Test de recette 1 conforme : obs » / « Test de recette 1 en échec : obs ». |
| POST | `/meps/{id}/tests-recette/{tid}/refaire` | — | pilote ou suppléant, phase `Recette`, test déjà validé : remet `resultat`, `observation`, `par`, `le` à vide. Journal `recette.refaire` « Test de recette 2 à refaire ». |

En `Recette` : les étapes de déploiement sont « Terminée » et n'acceptent plus d'action (422 « La MEP est en recette : le déploiement est terminé. ») ; l'échéance s'applique (retour arrière automatique si atteinte, sauf point de non-retour franchi) ; le pilote peut `POST /retour-arriere`. La surveillance d'arrière-plan couvre `Execution` et `Recette`.

## Clôture et bilan

`POST /meps/{id}/cloturer` `{ ceQuiAMarche, incidents, actions }` (corps facultatif ; chaînes rognées) → pilote ou suppléant :

- depuis `Recette` : tous les tests `Conforme` (422 « Test de recette 2 « Appeler l'API de test » non conforme : clôture impossible. ») → `issue: Reussie` ;
- depuis `RetourArriere` : inchangé → `issue: Repliee` ;
- depuis `Execution` : 422 « Passer d'abord en recette : la clôture réussie se fait après les tests de recette. » ;
- si `bilanActive` : `ceQuiAMarche` et `incidents` obligatoires pour les deux issues (422 « Bilan : décrire ce qui a marché et les incidents (« Rien à signaler » accepté). ») ; `actions` facultatif. Le bilan est enregistré (`bilan.redigePar` = auteur de la clôture). Si `bilanActive` est faux, un bilan fourni est quand même enregistré.

Journal `mep.cloturee` inchangé.

## Modèles et duplication

`POST /meps/{id}/dupliquer` `{ environnement?, commeModele: bool }` → tout utilisateur connecté → **201** `MepDto` de la nouvelle MEP (en-tête `Location: /api/meps/{nouvelId}`, `version: 1`).

- Copie profonde en préparation, nouveaux identifiants, références remappées (pilote, suppléants, responsables, systèmes des étapes et livrables, dépendances, responsables des tests), **codes d'étapes conservés**. Le pilote et les suppléants sont ceux de la source (l'utilisateur qui duplique n'est pas ajouté).
- Copiés : champs d'identité (sauf `dateMep` et `echeanceRepli`), participants, systèmes (avec `catalogueId`), livrables, étapes (état `AFaire`, preuve vide, dates nulles, `executantId` et `nonRetourAutorise` nuls, `revision` 0, `type`, `pointDeNonRetour`, `resolutionIds`), plan de repli, configuration de sauvegarde (`mode`, cases, responsable, procédure, copies ; sans `realiseeLe`, `restaurationTestee` = false, sans date ni preuve de test), tests de recette (résultats vides), approbateurs (décisions vides), `bilanActive`.
- Non copiés : `echeanceRepli`, `dateMep`, procédure relue, démarrage autorisé, `goNoGo`, `bilan`, résolutions exécutées, pièces jointes.
- `environnement` absent/`null` : celui de la source ; sinon la valeur rognée. `estModele` = `commeModele`.
- Journal de la nouvelle MEP : `mep.creee` « Créée par duplication du modèle « Outil » » ou « … de la MEP « Outil » ».
- Un modèle ne passe jamais en exécution (422, voir Go). Il reste modifiable en préparation comme une MEP.

## Pièces jointes

Dépôt en `multipart/form-data`, champ **`fichier`** (à défaut, le premier fichier du formulaire). Taille max `Stockage:TailleMaxMo` (20 Mo par défaut) → **413** `{ message: "Fichier trop volumineux : 20 Mo maximum." }`. Fichier vide, absent ou corps non multipart → 400. Le nom d'origine est assaini (sans chemin ni caractères de contrôle ou `<>:"|?*`, 200 caractères max, « fichier » s'il est vide) et sert seulement à l'affichage et au téléchargement ; le contenu est stocké sous l'Id. Le `typeContenu` est celui envoyé par le navigateur (`application/octet-stream` à défaut).

| Méthode | Route | Réponse | Règles |
|---|---|---|---|
| GET | `/meps/{id}/pieces-jointes` | 200 `PieceJointeDto[]` | toutes les pièces de la MEP (identiques à `MepDto.piecesJointes`) |
| POST | `/meps/{id}/pieces-jointes` | 200 `MepDto` | tout connecté ; MEP non clôturée (422 « MEP clôturée : ses pièces jointes ne sont plus modifiables. ») |
| POST | `/meps/{id}/etapes/{eid}/pieces-jointes` | 200 `MepDto` | idem ; `etapeId` renseigné |
| POST | `/meps/{id}/resolutions/{rid}/pieces-jointes` | 200 `MepDto` | idem ; `executionResolutionId` renseigné |
| DELETE | `/meps/{id}/pieces-jointes/{pid}` | 200 `MepDto` | déposant, pilote ou suppléant (403 sinon) ; MEP non clôturée (422) |
| GET | `/resolutions/{id}/pieces-jointes` | 200 `PieceJointeDto[]` | |
| POST | `/resolutions/{id}/pieces-jointes` | 201 `PieceJointeDto` | tout connecté (comme l'édition des résolutions types) ; 404 si résolution inconnue |
| DELETE | `/resolutions/{id}/pieces-jointes/{pid}` | 204 | admin (403) |
| GET | `/pieces-jointes/{pid}/contenu` | 200 contenu | tout connecté ; `Content-Disposition: attachment; filename=…; filename*=UTF-8''…` (nom d'origine assaini), `Content-Type` = `typeContenu`, `X-Content-Type-Options: nosniff` ; 404 si inconnue |

- Journal (pièces d'une MEP) : `piece.ajoutee` « Pièce jointe ajoutée[ à E0002 | à la résolution « Titre »] : nom » et `piece.supprimee` « Pièce jointe supprimée[ à E0002 | à une résolution] : nom ». Ces écritures incrémentent `version` et notifient le flux SSE, sans remettre les approbations à zéro.
- Supprimer une MEP, une étape ou une résolution type (suppression réelle) supprime aussi ses pièces jointes et leurs fichiers.
- Déploiement : volume docker `pieces-jointes` monté sur `/data/pieces-jointes` (variables `Stockage__Dossier`, `Stockage__TailleMaxMo`, `.env` : `PIECES_TAILLE_MAX_MO`).

## Paramètres (admin)

Lecture : tout connecté. Écriture : admin (403 sinon).

| Méthode | Route | Corps | Réponse |
|---|---|---|---|
| GET | `/parametres` | — | 200 `{ approbationsMinimum }` |
| PUT | `/parametres` | `{ approbationsMinimum }` (entier ≥ 0, 400 sinon) | 200 `{ approbationsMinimum }` |
| GET | `/criteres-go-no-go` | — | 200 `CritereGoNoGoDto[]` `{ id, libelle, ordre, actif }`, actifs et inactifs, triés par `ordre` puis libellé |
| POST | `/criteres-go-no-go` | `{ libelle, ordre?, actif? }` | 201 `CritereGoNoGoDto` ; libellé obligatoire (400) ; `ordre` absent = max + 1 ; `actif` absent = true |
| PUT | `/criteres-go-no-go/{id}` | `{ libelle, ordre?, actif? }` | 200 ; `ordre`/`actif` absents = inchangés |
| DELETE | `/criteres-go-no-go/{id}` | — | 204 (suppression réelle : les MEP gardent leur copie figée dans `goNoGo`) |

Seuls les critères `actif` sont à cocher dans le dialogue Go / no-go.

## Journal : nouveaux types

`approbateurs.modifies`, `approbation.approuvee`, `approbation.refusee`, `approbations.reinitialisees`, `gonogo.go`, `gonogo.nogo`, `etape.non-retour-autorise`, `phase.recette`, `recette.test-ajoute`, `recette.test-modifie`, `recette.test-supprime`, `recette.test-deplace`, `recette.test`, `recette.refaire`, `piece.ajoutee`, `piece.supprimee` (et `mep.creee` pour une duplication).

## Précisions d'implémentation (jalon 3)

- **Stockage** : `SuppleantIds` en `uuid[]` (la migration `Jalon3` reprend l'ancien `SuppleantId`) ; `Approbations`, `GoNoGo`, `TestsRecette` en `jsonb`, `Bilan` en `jsonb` nullable ; `ParametresGlobaux` = table à une ligne (`Id = 1`, `ApprobationsMinimum = 1` créée par la migration). Les MEP existantes passent `bilanActive: true`, `environnement: ""`, sans approbateur ni test de recette (elles devront en avoir un pour le Go ; une MEP déjà en exécution sans test peut être clôturée depuis la recette).
- **Démo** : comptes inchangés. MEP de démonstration : environnement « Production », Bob suppléant, Claire approbatrice (décision en attente : `erreursPlan` = `["Il faut 1 approbation(s)."]`), sauvegarde `PendantMep` faite par E0001 (`type: Sauvegarde`), E0004 point de non-retour, tests « Ouvrir la page d'accueil fictive » (Claire) et « Appeler l'API de test » (Bob), bilan actif. Modèle « Modèle : déploiement API standard » (copie de la démo, environnement vide). Critères : « Équipe présente et joignable », « Sauvegarde disponible », « Utilisateurs prévenus de l'interruption », « Supervision en place ». `approbationsMinimum = 1`. Sur une base déjà amorcée, seuls les critères de démonstration manquants sont ajoutés.
- **Approbateur = compte** (pas un participant) : il n'a pas besoin d'être participant de la MEP. Son nom est figé dans l'approbation à la désignation (`nom` renvoie le nom affiché actuel s'il existe).
- **Droits** : les suppléants ont tous les droits du pilote (Go, no-go, recette, clôture, retour arrière, reprise, point de non-retour, suppression de pièces jointes). Le compte du pilote principal seul est exclu des approbateurs.

# Jalon 3.1 : ajouts et changements

Le **pilote** est la personne responsable de la mise en production ; les développeurs deviennent des **responsables techniques**, distincts du pilote. Objectif : qu'une MEP se passe bien même quand le développeur n'est pas disponible.

## Changements incompatibles (à reprendre côté front)

| Avant | Maintenant |
|---|---|
| `POST /meps` faisait du créateur le pilote. | Le créateur est ajouté comme **participant relié à son compte** (rôle « Responsable technique ») et comme **responsable technique** (`Joignable`). `piloteId` vaut `null` : le pilote se choisit ensuite (`erreursPlan` contient « Renseigner le pilote. » tant que ce n'est pas fait). |
| `POST /meps/{id}/etapes/{eid}/prendre-la-main` `{ revision }`. | `{ revision, motif }` : le **motif est obligatoire** (voir plus bas). |
| Tout utilisateur connecté pouvait modifier le contenu d'une MEP en préparation. | **403** pour qui n'est ni responsable technique, ni pilote, ni suppléant, ni administrateur (voir « Droits d'édition »). |
| `POST /meps/{id}/phase` `{ phase: "Preparation" }` réussissait tant qu'aucune étape n'était en cours. | **422** dès que le point de non-retour est franchi (voir plus bas). |
| `POST /meps/{id}/dupliquer` : `environnement: ""` donnait une MEP sans environnement ; la copie gardait le titre de l'original ; le journal disait « Créée par duplication … ». | `environnement` vide ou absent = **reprendre celui de la source** ; l'outil de la copie est « *outil* (copie) » ; journal « MEP créée par duplication de « … » » ou « MEP créée depuis le modèle « … » ». |
| `DELETE /meps/{id}` : pilote ou administrateur. | Pilote, **créateur** ou administrateur (le créateur n'est plus pilote d'office). |

## MepDto : champs ajoutés

| Champ | Type | Précisions |
|---|---|---|
| `responsablesTechniques` | `ResponsableTechniqueDto[]` | Liste vide pour les MEP existantes (la migration `Jalon31` ajoute la colonne `jsonb` avec `'[]'`). |
| `avertissements` | `string[]` | Avertissements **non bloquants** de la vérification du plan, séparés de `erreursPlan` (voir plus bas). |
| `droits.estResponsableTechnique` | `bool` | Le compte courant est relié à l'un des responsables techniques. |
| `droits.peutEditerContenu` | `bool` | MEP en préparation **et** compte autorisé à modifier le contenu. `droits.peutModifierPlan` garde son sens (la MEP est en préparation). |

`ResponsableTechniqueDto` :

```json
{
  "participantId": "guid",
  "systemeIds": ["guid"],
  "perimetre": "texte libre",
  "disponibilite": "Joignable | Absent",
  "contactJourJ": "texte libre"
}
```

`systemeIds` est facultatif (ce que la personne a développé : systèmes de la MEP). Un participant n'apparaît qu'une seule fois.

## Responsables techniques

`PUT /meps/{id}/responsables-techniques` `{ version, responsables: [{ participantId, systemeIds?, perimetre?, disponibilite, contactJourJ? }] }` → **remplace toute la liste**. Droits d'édition requis, MEP en préparation. → 200 `MepDto`.

- 400 : `participantId` ou `disponibilite` manquants ; 409 : `version` périmée ; 403 : voir « Droits d'édition ».
- 422 : participant en double (« Bob est déjà responsable technique : un participant n'apparaît qu'une fois. »), participant ou système inconnu de la MEP, MEP hors préparation.
- Journal `responsables-techniques.modifies` : « Responsables techniques : Bob, Claire » (ou « aucun »). Comme toute modification de contenu, cela remet les approbations à zéro.
- Un participant responsable technique ne peut pas être supprimé de la MEP (422 « … est responsable technique : le retirer des responsables techniques d'abord. »). Supprimer un système le retire des `systemeIds` concernés.
- **Duplication** : les responsables techniques sont copiés (identifiants de participants et de systèmes remappés). Celui qui duplique, s'il n'est pas déjà autorisé à éditer la copie, y devient participant et responsable technique.
- **Démo** : Bob responsable technique de l'API (joignable, contact « Fictif — poste B »), Claire responsable technique du WEB (joignable, « Fictif — poste C »). Alice reste pilote : aucun avertissement.

## Droits d'édition du contenu (403)

Les comptes suivants peuvent modifier le contenu d'une MEP : **responsables techniques**, **pilote**, **suppléants** et **administrateurs**. Tout autre compte reçoit :

```
403 { "message": "Seuls les responsables techniques, le pilote, ses suppléants ou un administrateur peuvent modifier cette MEP." }
```

Routes concernées (le contrôle est fait avant les autres règles : phase, version, saisie) : `PUT /meps/{id}`, `PUT /meps/{id}/sauvegarde`, `PUT /meps/{id}/plan-repli`, `PUT /meps/{id}/responsables-techniques`, `PUT /meps/{id}/approbateurs`, participants, systèmes (y compris `depuis-catalogue`), livrables, étapes (création, modification, suppression, déplacement) et tests de recette (création, modification, suppression, déplacement).

**Exceptions inchangées** : la décision d'approbation (l'approbateur désigné), « procédure relue » (n'importe quel participant relié à un compte), les pièces jointes, et toutes les actions d'exécution (démarrer, terminer, prendre la main, résolutions, résultats de recette…) qui ont leurs propres règles. Les modifications réservées au pilote (échéance de retour arrière, Go, no-go, clôture…) sont inchangées.

## Avertissements de la vérification du plan

La vérification du plan renvoie désormais **deux listes** : `erreursPlan` (anomalies, qui bloquent le Go) et `avertissements` (non bloquants, jamais comptés dans `erreursPlan`, n'empêchent ni l'approbation ni le Go). Ordre :

1. Aucun responsable technique : « Aucun responsable technique renseigné. » (seul avertissement dans ce cas).
2. Pilote également responsable technique : « Alice est à la fois responsable technique et pilote : prévoir un suppléant qui connaît le plan. »
3. Pour chaque responsable technique **Absent**, pour chaque étape (déploiement ou retour arrière) dont il est le responsable **ou** rattachée à l'un de ses systèmes, si l'étape n'a pas à la fois des instructions, un résultat attendu et au moins une résolution type : « E0002 : Bob sera absent, ajouter une résolution type pour que quelqu'un d'autre puisse la reprendre. »

## Prendre la main : motif obligatoire

`POST /meps/{id}/etapes/{eid}/prendre-la-main` `{ revision, motif }`. Un motif vide ou composé d'espaces est refusé : **422** « Indiquer pourquoi vous prenez la main. ». Journal `etape.prise-en-main` : « Alice a pris la main sur E0002 (prévu : Bob) : *motif* » (ou « Alice a repris la main sur E0002 : *motif* » quand le preneur est le responsable prévu).

## Revenir en préparation

`POST /meps/{id}/phase` `{ phase: "Preparation" }` est refusé (**422**) une fois le point de non-retour franchi : « Le point de non-retour E0004 est franchi : on ne peut plus revenir en préparation, tout problème se corrige en avant. » (`nonRetourFranchi` du `MepDto` indique l'étape ; le front désactive le bouton).

## Duplication, modèles

- Outil de la copie : « *outil* (copie) » ; pour une MEP créée **depuis un modèle**, l'outil du modèle sans le préfixe « Modèle : » (pas de suffixe). Un modèle créé par duplication est « *outil* (copie) ».
- `environnement` vide/absent : celui de la source.
- Journal `mep.creee` : « MEP créée depuis le modèle « Modèle : … » » ou « MEP créée par duplication de « … » ».

## Journal : nouveaux types

`responsables-techniques.modifies`. Le détail de `etape.prise-en-main` contient maintenant le motif.

## Précisions d'implémentation (jalon 3.1)

- **Stockage** : `Meps.ResponsablesTechniques` en `jsonb` (`disponibilite` stockée en entier : 0 = Joignable, 1 = Absent). Migration `Jalon31` : colonne `NOT NULL DEFAULT '[]'` ; les MEP existantes n'ont aucun responsable technique (avertissement « Aucun responsable technique renseigné. »).
- **Énumération** : `DisponibiliteJourJ` = `Joignable | Absent` (texte dans l'API, comme les autres énumérations).
- **Front (indicateurs de complétude)** : alignés sur `erreursPlan` ; un champ facultatif (heures, indisponibilité, restauration testée) ne rend plus une section orange. La section « Responsables techniques » est orange tant qu'il n'y en a aucun ou qu'un avertissement subsiste. Les sections de la préparation sont renumérotées (Responsables techniques = 3, Pièces jointes = 9).
