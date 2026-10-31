# Liaison avec Noosphere

[Noosphere](https://github.com/SolaDevSolide/noosphere) tient la cartographie du système d'information : applications, flux, domaines et équipes. Une **liaison** relie un serveur Gestion MEP à un serveur Noosphere :

- **Noosphere → Gestion MEP** : les applications choisies deviennent des systèmes du catalogue global (ou sont rattachées à un système existant). Leur nom et leur description restent gérés par Noosphere ; environnements, cibles et objets restent propres à Gestion MEP.
- **Gestion MEP → Noosphere** : chaque MEP qui touche un système suivi est annoncée à Noosphere (prévue, déplacée, changement de phase, clôture), qui l'affiche sur la fiche de l'application et sur les flux qui la touchent.

Seul ce que tout le monde voit dans Noosphere est partagé : ni fiches restreintes, ni notes protégées. Les objets impactés ne sont pas envoyés.

La liaison se gère dans la console d'administration (API `/api/admin/liaisons`, voir [API](API.md#liaison-avec-noosphere)) ou en ligne de commande (`gestion-mep liaison`, voir plus bas). L'onglet « Liaisons » de la console arrive avec sa maquette ; en attendant, tout se fait par l'API ou la ligne de commande.

## Appairer

1. Dans Noosphere, un administrateur crée un **code d'appairage** : 12 caractères affichés `XXXX-XXXX-XXXX` (par exemple `7K4Q-29XD-M3PA`), valable 15 minutes, pour un seul usage.
2. Dans Gestion MEP, un administrateur saisit l'adresse de Noosphere et ce code : `gestion-mep liaison appairer https://noosphere.entreprise.fr 7K4Q-29XD-M3PA`, ou `POST /api/admin/liaisons/appairer { url, code }`. Le code se saisit avec ou sans tirets ni espaces, en majuscules ou en minuscules.
3. Les deux serveurs échangent leurs clés publiques. Les deux écrans affichent la même **empreinte** de quatre mots, par exemple `orbit cedar 0712 lumen`.
4. Chaque administrateur vérifie que les deux empreintes sont identiques (par téléphone, en visio…) et **confirme** de son côté : `gestion-mep liaison confirmer <id> --empreinte "orbit cedar 0712 lumen"`, ou `POST /api/admin/liaisons/{id}/confirmer { empreinte }`. Une empreinte différente est refusée (422) et inscrite au journal d'administration (`liaison.empreinte-differente`) : ne confirmez pas, coupez la liaison et recommencez.

Un côté n'échange des données qu'après avoir confirmé : tant que Noosphere n'a pas confirmé, la liaison indique `confirmeeParNoosphere: false` et la lecture du catalogue attend. Avant confirmation, seuls l'état de la liaison (`/status`) et la coupure (`/unpair`) sont acceptés.

Erreurs à l'appairage : code inconnu ou expiré (404), déjà utilisé (409), incorrect (422 ; Noosphere bloque le code après 5 essais erronés), bloqué (422, demandez un nouveau code), adresse injoignable ou réponse invalide (502).

L'adresse de Gestion MEP envoyée à Noosphere est l'**adresse publique** (réglage `url-publique`), sinon celle de la requête. Le nom de cette instance, affiché par Noosphere, est le réglage `liaison-nom-instance` (`LIAISON_NOM_INSTANCE`, par défaut `gestion-mep`).

## Choisir les applications suivies

`GET /api/admin/liaisons/{id}/catalogue` liste les domaines et les applications de Noosphere, avec pour chacune : son domaine, son équipe, son type, son cycle de vie (`planned`, `live`, `retiring`, `retired`), le système qui la suit, et des **propositions** de systèmes existants au nom identique ou proche.

On suit les applications :

- **par domaine** : toutes les applications du domaine, et, avec le **suivi automatique** (actif par défaut), celles qui y apparaîtront ensuite (sauf les applications retirées) ;
- **une par une**, en indiquant pour chacune soit un système existant à **rattacher**, soit rien pour en **créer** un. Sans système indiqué, un système local au même nom (casse et accents ignorés) est rattaché ; sinon un système est créé (avec le suffixe « (2) » si le nom est déjà pris par un système lui-même suivi).

`PUT /api/admin/liaisons/{id}/suivi { domainesSuivis, applications: [{ applicationId, suivre, systemeId }] }` enregistre ces choix et synchronise aussitôt. Dans une même saisie, le choix fait pour une application passe avant son domaine : une application d'un domaine ajouté, rattachée à un système désigné ou écartée (`suivre: false`), ne reçoit pas d'abord un système créé par le domaine. Ne plus suivre une application rend son système local, sans le supprimer.

Les options de la liaison (`PUT /api/admin/liaisons/{id}/options`, ou `gestion-mep liaison options`) :

- **Envoyer les MEP** (`envoyerMeps`, actif par défaut) : coupé, plus rien ne part vers Noosphere ; rétabli, les MEP en cours sont renvoyées.
- **Suivi automatique** (`suiviAutomatique`) des nouvelles applications des domaines suivis.

## Synchronisation du catalogue

Le catalogue est relu :

- quand Noosphere envoie un **avis de changement** (une fiche partagée a changé) ;
- toutes les **15 minutes** ;
- à la demande : **Synchroniser maintenant** (`POST /api/admin/liaisons/{id}/synchroniser`, `gestion-mep liaison synchroniser`).

La lecture utilise l'`ETag` du catalogue : rien n'est relu ni réécrit quand rien n'a changé. Chaque synchronisation qui change quelque chose est inscrite au journal d'administration (`liaison.synchronisee`) avec les systèmes créés, rattachés, mis à jour, désactivés et réactivés.

Règles appliquées à un système suivi :

| Dans Noosphere | Dans Gestion MEP |
|---|---|
| Nom, description | Repris tels quels ; ils ne se modifient plus dans Gestion MEP (400 « géré par Noosphere »). Un nom déjà pris par un autre système est laissé tel quel, avec un avertissement. |
| Application retirée (`retired`) | Système **désactivé**, jamais supprimé. Il est réactivé si l'application revient. |
| Application supprimée ou devenue restreinte (absente du catalogue) | Système désactivé, marqué « absent de Noosphere ». |
| Domaine, équipe, cycle de vie, adresse de la fiche | Affichés avec le système (`source` dans `GET /api/catalogue/systemes`). |

Un système suivi ne se supprime pas (400) : arrêtez d'abord de le suivre. Environnements, catégories, objets et MEP du système ne sont jamais touchés par la synchronisation.

## MEP envoyées à Noosphere

Une MEP qui touche au moins un système suivi est envoyée à Noosphere quand elle est créée ou annoncée, quand sa date, son créneau, sa phase ou son issue change, et quand elle est supprimée. Les modèles ne sont jamais envoyés.

- **Regroupement** : la première modification fixe l'envoi 30 secondes plus tard ; les modifications suivantes s'y ajoutent. Un événement identique au dernier envoyé ne repart pas.
- **Nouvelles tentatives** : 30 s, 2 min, puis 10 min. Après ces essais, une ligne est inscrite au journal d'administration (`liaison.envoi-echec`) et le rapprochement des 15 minutes reprend l'envoi.
- **Rapprochement** : toutes les 15 minutes, et au rétablissement de « Envoyer les MEP », toutes les MEP qui touchent un système suivi sont comparées au dernier envoi (sauf les MEP terminées depuis plus de 90 jours jamais envoyées).

Chaque événement porte : l'identifiant de la MEP et son lien, le titre (« Outil VersionCible »), les applications suivies touchées, leurs environnements, la phase, l'issue, le créneau prévu et l'heure de mise à jour. Seuls les environnements des systèmes suivis sont envoyés.

| Gestion MEP | `phase` envoyée |
|---|---|
| Préparation | `preparation` |
| Préparation, MEP reportée | `postponed` |
| Exécution | `execution` |
| Retour arrière | `rollback` |
| Recette | `acceptance` |
| Clôturée | `closed` |
| Annulée, ou MEP supprimée | `cancelled` |

| Issue d'une MEP clôturée | `outcome` envoyé |
|---|---|
| Réussie | `success` |
| Réussie avec incidents (échecs d'étape ou de recette survenus) | `success-with-incidents` |
| Repliée (retour arrière) | `rolled-back` |
| Échec | `failed` |
| MEP non clôturée | `null` |

Créneau (`plannedStart`, `plannedEnd`, en UTC) : la date annoncée dans la section Communication (ou la date visée d'un report), avec les heures de début et de fin annoncées, dans le fuseau de la MEP ; une fin plus tôt que le début tombe le lendemain ; sans heure de fin, début + durée d'indisponibilité. Sans date annoncée, la date et l'heure de la MEP ; une date sans heure compte à minuit.

Une MEP qui ne touche plus aucun système suivi est envoyée une dernière fois avec une liste d'applications vide ; une MEP supprimée, avec la phase `cancelled`.

## Renouveler les clés

**Renouveler les clés** (`POST /api/admin/liaisons/{id}/renouveler`, `gestion-mep liaison renouveler <id>`) crée de nouvelles clés et les envoie à Noosphere dans une requête signée avec l'ancienne clé. L'empreinte change et les deux côtés affichent « Clés renouvelées le … » ; aucune nouvelle confirmation n'est demandée. L'ancienne clé reste acceptée 5 minutes, le temps que les requêtes en route arrivent. Noosphere peut renouveler les siennes de la même façon.

## Couper la liaison

L'un ou l'autre côté coupe la liaison (`POST /api/admin/liaisons/{id}/couper { confirmer: true }`, `gestion-mep liaison couper <id>`). Gestion MEP prévient Noosphere (sans attendre son accord), efface ses clés et ne garde que l'empreinte de la clé de Noosphere, pour lui répondre « liaison coupée ». Les systèmes suivis **restent** et deviennent locaux : leur nom et leur description redeviennent modifiables. Côté Noosphere, les MEP déjà reçues restent en historique.

Pour relier à nouveau les deux serveurs, recommencez l'appairage avec un nouveau code : une nouvelle liaison est créée.

## Journal des échanges

Chaque échange est conservé 90 jours : quand, sens (`entrant`, `sortant`), opération (nom de la route du protocole : `pair`, `status`, `catalog`, `notices`, `deployments`, `rekey`, `unpair`), résultat (`ok`, `echec`, `refuse`), code HTTP et détail. `GET /api/admin/liaisons/echanges` (`?liaison=`, `?refus=true`, `?avant=`, `?limite=`), `GET /api/admin/liaisons/{id}/echanges`, `gestion-mep liaison echanges [<id>] [--refus]`.

Les requêtes refusées y figurent aussi, avec leur raison :

| Raison | Réponse | Cause |
|---|---|---|
| `bad-signature` | 401 | Signature absente, mal formée ou fausse, composants signés inattendus, renouvellement de clés signé avec une autre clé que la clé en cours. |
| `bad-digest` | 401 | Le corps ne correspond pas à son `Content-Digest`. |
| `clock-skew` | 401 | Heure de la requête à plus de 5 minutes de l'horloge du serveur : vérifiez la synchronisation NTP des deux serveurs. |
| `replay` | 401 | Requête déjà reçue dans les 10 dernières minutes. |
| `unknown-key` | 401 | Clé inconnue, ou ancienne clé au-delà des 5 minutes de grâce. |
| `link-cut` | 401 | La liaison a été coupée de ce côté. |
| `link-not-confirmed` | 403 | L'empreinte n'a pas encore été confirmée de ce côté (seuls `/status` et `/unpair` passent avant). |
| `capability-missing` | 403 | La liaison n'a pas la capacité demandée (par exemple `notices:send` pour un avis). |
| `bad-body` | 400 | Corps qui n'est pas un JWE pour la clé de chiffrement de ce côté, ou pas le JSON attendu. |

Les réponses sont au format `application/problem+json`, `type` `https://noosphere.dev/problems/<raison>`, signées sauf pour `unknown-key` et `link-cut` (la clé du demandeur est alors inconnue ou effacée). Les requêtes sont aussi limitées à 120 par minute et par clé (429).

## Démarrer avec Noosphere (Docker Compose)

Quand les deux applications démarrent ensemble, Gestion MEP s'appaire au premier démarrage avec un code fixé à l'avance (16 caractères au moins, à usage unique) :

- côté Noosphere : `NOOSPHERE_LINK_BOOTSTRAP_CODE` ;
- côté Gestion MEP : `NOOSPHERE_URL` (adresse de Noosphere) et `NOOSPHERE_CODE_LIAISON` (le même code).

Gestion MEP essaie toutes les 30 secondes tant que Noosphere ne répond pas (dix fois au plus), puis ne réessaie plus : tant qu'une liaison existe avec cette adresse, rien n'est refait aux démarrages suivants. Un code de moins de 16 caractères est ignoré. Les administrateurs confirment toujours l'empreinte des deux côtés.

```yaml
# Exemple : docker compose up, puis confirmer l'empreinte dans les deux consoles
services:
  noosphere:
    image: ${NOOSPHERE_IMAGE:-noosphere:local}
    environment:
      NOOSPHERE_LINK_BOOTSTRAP_CODE: ${CODE_LIAISON}
      # … autres réglages de Noosphere (voir sa documentation)

  app:   # Gestion MEP, comme dans docker-compose.yml
    environment:
      NOOSPHERE_URL: http://noosphere:8080
      NOOSPHERE_CODE_LIAISON: ${CODE_LIAISON}
      LIAISON_NOM_INSTANCE: Gestion MEP recette
```

Avec, dans le fichier `.env` commun, un code d'au moins 16 caractères (par exemple tiré par `openssl rand -base64 18`) : `CODE_LIAISON=…`. Dans `docker-compose.yml`, ces variables arrivent à l'application par `Liaison__NoosphereUrl`, `Liaison__CodeAppairage` et `Liaison__NomInstance`.

`http://` n'est raisonnable qu'à l'intérieur d'un réseau Docker : entre deux serveurs, utilisez `https://`. Les requêtes de la liaison sont de toute façon signées et leurs données chiffrées.

## Réseau

Les appels vers Noosphere partent directement du serveur, sans passer par le réseau sortant des [API externes](API-externes.md) (réseaux internes autorisés, proxy) : Noosphere est le plus souvent sur le réseau interne. Ils ne suivent pas les redirections et abandonnent au bout de 15 secondes. Noosphere appelle Gestion MEP sous `/link/v1/…` (hors de `/api`) : le reverse proxy doit transmettre ce chemin.

## Sécurité

- Chaque côté crée, pour chaque liaison, une clé de signature (ECDSA P-256) et une clé de chiffrement (ECDH P-256). Les clés privées sont chiffrées au repos (Data Protection, mêmes clés que la session) et ne quittent jamais le serveur.
- Chaque requête et chaque réponse est signée (RFC 9421) ; chaque corps porteur de données est chiffré (JWE `ECDH-ES` + `A256GCM`).
- L'appairage est protégé par le code : la réponse de Noosphere porte un code d'authentification calculé avec lui.
- L'empreinte de quatre mots se calcule sur les quatre clés publiques : deux empreintes identiques garantissent que personne ne s'est glissé entre les deux serveurs.

## Mode démonstration

La démonstration contient une liaison fictive, active, avec `https://noosphere.demo.invalid` : les systèmes API et WEB y sont suivis (domaine « Commerce »), le catalogue contient des applications retirées, prévues et en cours de retrait, et le journal des échanges est rempli. Aucun appel réel ne part : synchroniser relit le catalogue fictif, les MEP « envoyées » sont seulement inscrites au journal des échanges.

## Ligne de commande

| Commande | Effet |
|---|---|
| `gestion-mep liaison appairer <url> <code>` | Appaire avec Noosphere et affiche l'empreinte. |
| `gestion-mep liaison liste` | Liaisons, état, empreinte, systèmes suivis. |
| `gestion-mep liaison confirmer <id> --empreinte "…"` | Confirme l'empreinte affichée par Noosphere. |
| `gestion-mep liaison synchroniser [<id>]` | Relit le catalogue maintenant. |
| `gestion-mep liaison options <id> [--envoyer-meps oui\|non] [--suivi-automatique oui\|non]` | Change les options. |
| `gestion-mep liaison renouveler <id>` | Renouvelle les clés. |
| `gestion-mep liaison couper <id> [--oui]` | Coupe la liaison. |
| `gestion-mep liaison echanges [<id>] [--refus] [--limite n]` | Journal des échanges. |

`<id>` : l'identifiant de la liaison (ses 8 premiers caractères suffisent) ; sans identifiant, `synchroniser` prend la seule liaison en service et `echanges` lit les échanges de toutes les liaisons. Référence complète : [Ligne de commande](Ligne-de-commande.md).
