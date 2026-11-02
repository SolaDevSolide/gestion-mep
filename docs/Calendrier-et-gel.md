# Calendrier et périodes de gel

Le calendrier place chaque MEP sur son créneau, avec les périodes de gel décidées par les administrateurs (clôture comptable, fêtes de fin d'année…). Une MEP prévue pendant un gel est signalée dès la préparation. Son Go est refusé tant qu'un approbateur n'a pas accordé de dérogation. Le calendrier signale aussi les MEP qui touchent un même système au même moment. Il s'exporte en iCalendar (`.ics`) ou s'ouvre dans un agenda par une adresse d'abonnement personnelle.

## Le créneau d'une MEP

Le créneau est calculé par l'application à chaque enregistrement de la MEP. Il est gardé en base, en UTC, pour que le calendrier se lise vite quel que soit le nombre de MEP.

| Phase | Créneau |
|---|---|
| Préparation | Date et heures annoncées (Communication), sinon date et heures de la section Intervention. Sans heure de début : la journée entière, de minuit à minuit dans le fuseau de la MEP. Une MEP reportée avec une date visée prend cette date. |
| Exécution, recette, retour arrière | Du Go à Go + durée prévue (heure de fin moins heure de début). Une MEP lancée s'étend au moins jusqu'à maintenant (`enCours`). |
| Clôturée | Du Go à la clôture. |
| Annulée | Le créneau prévu, sinon du Go à l'annulation. |

Une MEP sans date prévue n'a pas de créneau : elle n'apparaît pas dans le calendrier, et `{{gel}}` est alors vide. Les modèles de MEP n'y figurent jamais.

## Fuseaux horaires

- Tous les instants sont **enregistrés et renvoyés en UTC** (`2026-11-28T17:00:00Z`).
- Les dates et heures d'une MEP se lisent dans **son** fuseau (Intervention, `Europe/Paris` par défaut). Une journée entière va de minuit à minuit dans ce fuseau, même un jour de changement d'heure (23 ou 25 heures).
- Le **fuseau de l'instance** est `Europe/Paris`. Il sert par défaut :
  - pour les jours `du` et `au` du calendrier (paramètre `fuseau` pour en choisir un autre) ;
  - pour les heures saisies d'une période de gel (champ `fuseauHoraire` de la période) ;
  - pour les exports, où il est indiqué en `X-WR-TIMEZONE`.
- Une période de gel répétée se répète **dans son fuseau**. « Chaque lundi de 8h à 18h » reste de 8h à 18h, heure de Paris, en hiver comme en été ; seuls les instants UTC changent.
- Le fichier `.ics` donne les heures en UTC (suffixe `Z`) et les journées entières en dates locales (`VALUE=DATE`). Chaque agenda les affiche ensuite dans le fuseau de la personne.

## Périodes de gel

Une période de gel est définie par un administrateur (console, API ou ligne de commande). Chaque création, modification ou suppression est inscrite au journal d'administration (`gel.cree`, `gel.modifie` avec l'ancienne et la nouvelle valeur, `gel.supprime`).

| Champ | Sens |
|---|---|
| Nom | Unique, sans tenir compte de la casse ni des accents (400 sinon). |
| Début, fin | Première occurrence. La fin est exclue : un gel « du 1er au 3 décembre » finit le 3 à minuit. 400 si la fin n'est pas après le début. |
| Motif | Affiché aux pilotes et dans le message de refus du Go. |
| Systèmes | Systèmes du catalogue concernés. Vide : **tous les systèmes**, y compris les systèmes hors catalogue. |
| Environnements | Environnements concernés (`Production`, `Recette`…). Vide : tous. |
| Production seulement | Seulement les MEP qui touchent un environnement de production. |
| Répétition | `Aucune`, `Hebdomadaire`, `Mensuelle` (même jour du mois, ramené au dernier jour pour un mois plus court), `FinDeMois` (même écart avec le dernier jour du mois : du 28 novembre 18h au 2 décembre 8h devient du 29 décembre 18h au 2 janvier 8h), `Annuelle`. |
| Fin de répétition | Dernier jour local où une occurrence peut commencer. Vide : sans fin. |
| Fuseau horaire | Fuseau des répétitions (défaut : celui de l'instance). |

Une MEP est **concernée** par une occurrence quand son créneau la chevauche, qu'elle touche un des systèmes (ou que la période les vise tous) et, si la période les précise, un des environnements ou la production.

### Exemple : la clôture comptable

```
gestion-mep periodes-gel creer "Clôture comptable" --debut "2026-11-28 18:00" --fin "2026-12-02 08:00" \
  --repetition fin-de-mois --systeme BDD --environnement Production --motif "Arrêté des comptes"
```

## Dérogations et Go

Pendant la préparation, la fiche de la MEP (`calendrier.gels`) liste les occurrences de gel qui touchent son créneau. `calendrier.gelsAuGo` liste celles qu'un Go donné maintenant toucherait, de maintenant à maintenant + durée prévue, sans dérogation accordée. Tant que `gelsAuGo` n'est pas vide, le Go est **refusé** (422, type `urn:gestion-mep:probleme:go-pendant-un-gel`, voir [API](API.md#codes-de-réponse)).

1. Le **pilote ou un suppléant** demande une dérogation pour l'occurrence, avec un **motif obligatoire**. Une seule demande à la fois par occurrence.
2. Un **approbateur désigné** de la MEP l'accorde ou la refuse (commentaire obligatoire pour un refus). Les règles sont celles de l'approbation du plan ([Droits par rôle](Droits.md)) : ni le pilote, ni un suppléant, ni un responsable technique ne peuvent décider, et jamais la personne qui l'a demandée, même avec un autre compte relié au sien. Être administrateur ne donne aucun droit de plus.
3. Le Go passe. Le journal de la MEP inscrit, pour chaque gel franchi, la dérogation qui l'a permis (`gel.go`).

Tout est tracé au journal de la MEP : `gel.derogation.demandee`, `gel.derogation.accordee`, `gel.derogation.refusee`, `gel.derogation.retiree`. Le pilote ou un suppléant peut retirer une demande en attente ou accordée tant que la MEP est en préparation. Une dérogation vaut pour **une occurrence** : un gel répété demande une dérogation par mois (ou par semaine…) franchi.

La variable `{{gel}}` des [modèles de texte](Modèles-de-texte.md) reprend ces informations. Elle vaut par exemple « « Clôture comptable » du 28/11/2026 18h00 au 02/12/2026 08h00 (Europe/Paris) : dérogation accordée par Claire Durand », et « aucune » hors gel.

## Conflits

Deux MEP sont **en conflit** quand leurs créneaux se chevauchent et qu'elles touchent un même système : même système du catalogue, ou même nom de système hors catalogue (sans casse ni accents). Un conflit est un **signalement, jamais un blocage**. Il est donné dans le calendrier (`conflits` de chaque MEP) et sur la fiche de la MEP (`calendrier.conflits`), avec les systèmes en commun. Les MEP annulées et les modèles n'en créent pas.

## API

Toutes les routes exigent une session, sauf le flux d'abonnement.

| Route | Rôle |
|---|---|
| `GET /api/calendrier` | MEP dont le créneau touche la plage `du`, `au` (jours inclus, AAAA-MM-JJ, dans `fuseau` ; défaut : le mois en cours ; 366 jours au plus). Filtres de la [recherche des MEP](API.md#recherche-des-mep) : `recherche`, `systeme`, `environnement`, `equipe`, `personne`, `role` (dont `pilote`), `etat`, `type`. Pagination comme la liste des MEP : `limite` (100 par défaut, 500 au plus), `apres` (curseur), `suivant` dans la réponse. Réponse `{ meps, gels, total, suivant, du, au, fuseauHoraire }` : MEP par début, avec `phase`, `etat` et `toucheProduction` pour les couleurs, `systemes`, `equipeIds`, `pilote`, leurs `gels` et leurs `conflits` ; `gels` : occurrences des périodes de gel de la plage. |
| `GET /api/calendrier/ics` | Même plage et mêmes filtres, en fichier `text/calendar` ; `gels=0` pour ne pas exporter les périodes de gel. |
| `GET /api/calendrier/abonnement` | État de l'abonnement du compte : `{ actif, creeLe, dernierAcces, url }` (`url` toujours null ici). |
| `POST /api/calendrier/abonnement` | Crée ou régénère l'adresse d'abonnement et la renvoie dans `url`, **une seule fois**. L'ancienne adresse cesse aussitôt de répondre. |
| `DELETE /api/calendrier/abonnement` | Révoque l'adresse (204). |
| `GET /api/calendrier/abonnement/{secret}.ics` | Flux de l'abonnement, sans session : MEP de 90 jours avant à un an après aujourd'hui, avec les mêmes filtres possibles dans l'adresse. 404 pour un secret inconnu ou révoqué, ou un compte désactivé. |
| `GET /api/periodes-gel` | Périodes de gel, pour toute personne connectée. |
| `POST /api/admin/periodes-gel`, `PUT …/{id}`, `DELETE …/{id}` | Création (201), modification (`version` obligatoire, 409 si périmée), suppression (204) : administrateurs. |
| `POST /api/meps/{id}/derogations-gel` | `{ periodeGelId, occurrence, motif }` : demande une dérogation (`occurrence` : début de l'occurrence visée ; absent : la première qui concerne la MEP). Renvoie la MEP. |
| `POST /api/meps/{id}/derogations-gel/{did}/decision` | `{ accorder, commentaire }` : décision d'un approbateur. Renvoie la MEP. |
| `POST /api/meps/{id}/derogations-gel/{did}/retirer` | Retire la demande. Renvoie la MEP. |

La fiche d'une MEP (`GET /api/meps/{id}`) porte `calendrier` : `{ debut, fin, journeeEntiere, prevu, enCours, gels, gelsAuGo, conflits, derogations }`. Chaque dérogation indique `peutDecider` pour la personne qui lit.

## Export et abonnement iCalendar

Le fichier suit la norme RFC 5545 : lignes terminées par CRLF et repliées à 75 octets, textes échappés, `DTSTAMP` sur chaque événement. Chaque MEP a un identifiant stable (`UID: mep-<id>@gestion-mep`) : un agenda qui relit le flux met à jour l'événement au lieu d'en créer un autre. Le statut suit la phase : `TENTATIVE` en préparation, `CANCELLED` pour une MEP annulée, `CONFIRMED` sinon. Les périodes de gel sont des événements « disponibles » (`TRANSP:TRANSPARENT`, catégorie « Période de gel »), un par occurrence.

**Abonnement.** Chaque personne peut créer une adresse d'abonnement à coller dans son agenda (Outlook, Google Agenda, Thunderbird…). L'adresse contient un secret aléatoire de 256 bits, impossible à deviner. Le serveur n'en garde que l'empreinte SHA-256 : l'adresse n'est montrée qu'à sa création. Elle donne accès au calendrier de toutes les MEP, comme une session ; la garder pour soi. En cas de fuite, la **régénérer** (l'ancienne cesse de répondre) ou la **révoquer**. Les deux sont inscrits au journal d'administration (`calendrier.abonnement`). Un compte désactivé perd son flux. L'agenda relit le flux toutes les heures environ (`REFRESH-INTERVAL`) ; le serveur autorise 5 minutes de cache.

## Ligne de commande

```
gestion-mep periodes-gel lister [--json]
gestion-mep periodes-gel creer "<nom>" --debut "AAAA-MM-JJ HH:MM" --fin "AAAA-MM-JJ HH:MM" [--motif …] [--systeme <SYSTÈME>]…
    [--environnement <nom>]… [--production] [--repetition aucune|hebdomadaire|mensuelle|fin-de-mois|annuelle] [--jusqu-au AAAA-MM-JJ] [--fuseau <IANA>]
gestion-mep periodes-gel supprimer <période> [--oui]
gestion-mep calendrier exporter [--du AAAA-MM-JJ] [--au AAAA-MM-JJ] [--fuseau <IANA>] [--systeme …] [--etat …] [--equipe …] [--sans-gels] [--sortie fichier.ics]
```

Les heures saisies se lisent dans `--fuseau` (défaut : fuseau de l'instance) et sont enregistrées en UTC. `<période>` : nom, identifiant ou début d'identifiant. Codes de retour habituels : 1 saisie refusée, 2 option inconnue, 4 confirmation absente (`--oui`), 5 introuvable. Détail dans [Ligne de commande](Ligne-de-commande.md).
