# Calendrier et périodes de gel

Le calendrier place chaque MEP sur son créneau, avec les périodes de gel décidées par les administrateurs (clôture comptable, fêtes de fin d'année…). Une MEP prévue pendant un gel est signalée dès la préparation. Son Go est refusé tant qu'un approbateur n'a pas accordé de dérogation. Le calendrier signale aussi les MEP qui touchent un même système au même moment. Il s'exporte en iCalendar (`.ics`) ou s'ouvre dans un agenda par une adresse d'abonnement personnelle.

## À l'écran

### Le calendrier des MEP

Dans la liste des MEP, l'affichage **Calendrier** (à côté de Cartes et Tableau) place les MEP sur leur créneau. Les filtres de la liste s'appliquent (recherche, système, environnement, équipe, personne, état, type) ; la période, elle, est celle de la vue. L'affichage choisi est retenu par le navigateur ; la vue et le jour affichés sont dans l'adresse de la page (`?vue=semaine&jour=2026-11-27`).

- **Mois** : une case par jour. Chaque MEP y figure avec son heure de début, la couleur de sa phase, `PROD` si elle touche la production, un triangle en cas de conflit et un flocon pendant un gel. Les jours de gel sont hachurés, avec le nom de la période. Un clic sur un jour affiche ses MEP sous la grille. Sur téléphone, les cases ne montrent que des points de couleur : touchez un jour pour voir sa liste.
- **Semaine** : une colonne par jour, de 6 h à minuit (plus tôt ou plus tard si une MEP le demande). Les MEP qui se chevauchent sont côte à côte ; celles en conflit sont entourées de rouge. Sur téléphone, la semaine se lit jour par jour.
- **Liste** : les MEP du mois, jour par jour, avec horaire, systèmes, pilote, phase, gels et conflits.

Les heures sont celles du fuseau de l'instance (`Europe/Paris`), indiqué sous le calendrier avec la légende des couleurs. « Aujourd'hui » revient au jour courant ; les flèches passent au mois ou à la semaine d'avant ou d'après.

Dans l'affichage Cartes, la carte d'une MEP porte aussi ses signalements : « Gel : Clôture comptable · Go bloqué sans dérogation » (ou « dérogation demandée », « dérogation accordée ») et « Conflit sur BDD avec Paie 3.1 (21:00 → 23:00) ». Plusieurs conflits se résument en « Conflit avec 2 MEP sur BDD, WEB », le détail au survol.

### Exporter ou s'abonner

- **Exporter .ics** télécharge les MEP de la période affichée, filtres compris, et les périodes de gel. Le fichier s'importe dans n'importe quel agenda, mais ne se met pas à jour.
- **S'abonner** ouvre l'adresse d'abonnement personnelle. Choisissez ce qu'elle contient : les filtres actuels de la liste ou toutes les MEP que vous pouvez voir, avec ou sans les périodes de gel. **Créer mon adresse** l'affiche, une seule fois : copiez-la dans votre agenda (Outlook : « Ajouter un calendrier › À partir d'Internet » ; Thunderbird : « Nouvel agenda › Sur le réseau »). Ensuite, la fenêtre indique seulement sa date de création et sa dernière lecture par un agenda. **Régénérer l'adresse** en crée une nouvelle et coupe l'ancienne ; **Révoquer** la coupe sans la remplacer. Les filtres et le choix des gels font partie de l'adresse : en changer ne demande pas de nouvelle adresse, seulement de recopier celle qui est affichée à la création.

### Paramètres › Périodes de gel

Tout le monde voit la page ; seuls les administrateurs créent, modifient et suppriment. Le tableau (une fiche par période sur téléphone) donne pour chaque période :

- quand elle a lieu (« Ponctuelle » ou « Récurrente », en clair : « Chaque fin de mois, comme du 26/11 00:00 au 01/12 00:00 ») ;
- les systèmes et les environnements concernés ;
- sa prochaine occurrence dans l'année et le nombre de MEP qu'elle touche.

**Nouvelle période** ou **Modifier** ouvre le formulaire :

- le nom et le motif ;
- ponctuelle ou récurrente (chaque fin de mois, chaque mois, chaque semaine, chaque année), avec un « jusqu'au » facultatif ;
- le début et la fin de la première occurrence, la fin étant exclue ;
- les systèmes concernés : tous, ou une sélection du catalogue ;
- les environnements concernés : tout environnement marqué « production », ou des environnements nommés.

Le formulaire rappelle les prochaines occurrences de la période enregistrée. Les dérogations sont décidées par les approbateurs désignés de chaque MEP.

### Sur la fiche d'une MEP

Pendant la préparation, si le créneau de la MEP touche un gel, l'en-tête porte la pastille **Période de gel** et l'onglet Préparation affiche un encadré avec le créneau, la période, son motif et la date à laquelle le gel prend fin. Le pilote y trouve **Demander une dérogation** et **Choisir une autre date** (qui ouvre la section Intervention). Un conflit avec une autre MEP est rappelé dans un encadré orange, avec un lien vers elle ; rien n'est bloqué.

Dans la fenêtre **Go / no-go**, un Go qui tomberait dans un gel sans dérogation accordée est grisé (« Bloqué : période de gel « Clôture comptable » »). La fenêtre explique pourquoi et permet de demander la dérogation sur place, avec son motif. La demande apparaît ensuite sur la fiche, « en attente de la décision d'un approbateur » ; le pilote peut la retirer.

### Décider d'une dérogation

Un approbateur désigné de la MEP voit les demandes qui l'attendent en haut de **Suivi en direct** : la MEP et son créneau, le pilote, la période, le motif et le plan (nombre d'étapes, retour arrière prévu ou non). Il peut aussi décider depuis la fiche de la MEP. **Accorder la dérogation** (commentaire facultatif) rend le Go possible ; **Refuser** demande un commentaire. La décision s'affiche sur la fiche avec son auteur et son heure ; la dérogation vaut pour cette occurrence du gel seulement.

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

Une période de gel est définie par un administrateur (Paramètres › Périodes de gel, API ou ligne de commande). Chaque création, modification ou suppression est inscrite au journal d'administration (`gel.cree`, `gel.modifie` avec l'ancienne et la nouvelle valeur, `gel.supprime`).

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

Toutes les routes exigent une session ou un [jeton d'accès](Jetons-d-accès.md), sauf le flux d'abonnement. Avec un jeton : lecture du calendrier et des périodes de gel avec `meps:lecture`, dérogations avec `meps:ecriture`, administration des périodes de gel avec `admin` ; la gestion de l'adresse d'abonnement (`/api/calendrier/abonnement`) est fermée aux jetons.

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
