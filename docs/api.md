# Contrat de l'API (jalon 1)

Toutes les routes sont sous `/api`, en JSON (camelCase), énumérations sérialisées en chaînes (`"Preparation"`, `"AFaire"`…). Dates : ISO 8601 (`DateTimeOffset` → `"2026-10-09T12:00:00+00:00"`, `DateOnly` → `"2026-10-09"`, `TimeOnly` → `"08:30:00"`). Identifiants : GUID.

Authentification par cookie (`gestion-mep.auth`, HttpOnly, SameSite=Lax). Toute route sauf `POST /api/auth/connexion` et `GET /api/sante` exige d'être connecté, sinon **401**.

## Erreurs

- **400** validation de saisie, **401** non connecté, **403** droit insuffisant, **404** introuvable.
- **409** conflit de version (verrou optimiste) : la ressource a changé depuis sa lecture.
- **422** refus par une règle métier.

Corps d'erreur : `{ "message": "texte en français pour l'utilisateur" }`.

## Énumérations

- `Phase` : `Preparation`, `Execution`
- `EtatEtape` : `AFaire`, `EnCours`, `Reussie`, `Echec`
- `Criticite` : `Faible`, `Moderee`, `Elevee`, `Bloquante`
- `StatutDisponibilite` : `PlanInvalide`, `EcheanceARenseigner`, `RetourArriere`, `FicheACompleter`, `InstructionsACompleter`, `Attend`, `Terminee`, `EnCours`, `ArretResolution`, `Disponible`

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
