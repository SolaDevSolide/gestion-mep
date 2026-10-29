# Ligne de commande

La commande `gestion-mep` administre et exploite Gestion MEP sans navigateur. Tout ce qui se règle dans la console d'administration se règle aussi en ligne de commande. Les opérations d'exploitation y sont aussi : sauvegardes, migrations, mises à jour, santé et pièces jointes. Pour les MEP, la commande sait les consulter, les exporter, les importer, les dupliquer, les annuler, les supprimer et confier leur pilotage en secours. Elle travaille directement sur la base de données, avec les mêmes règles que l'application. Ses modifications sont inscrites dans les journaux avec l'auteur « Ligne de commande ».

Cette page décrit les conventions communes, puis donne la référence de chaque commande. Les usages courants sont expliqués dans [Exploiter](Exploiter.md#commandes-dadministration).

## Lancer une commande

| Installation | Commande |
|---|---|
| Docker, application en marche | `docker compose exec app gestion-mep <commande>` (ajouter `-T` pour rediriger l'entrée ou la sortie) |
| Docker, application arrêtée | `docker compose run --rm app <commande>` (sans le préfixe `gestion-mep`) |
| Linux sans Docker | `sudo gestion-mep <commande>` |
| Windows sans Docker | `gestion-mep <commande>` dans une invite de commandes administrateur |

## Conventions

**Structure.** Une commande s'écrit `gestion-mep <groupe> <action> [arguments] [options]`, par exemple `gestion-mep comptes creer jdupont --admin`. Groupes et actions sont en français, sans accents. Quelques commandes n'ont pas d'action (`migrer`, `version`, `sante`). Les anciens noms restent acceptés : `sauvegarder` (`sauvegardes creer`), `importer` (`sauvegardes importer`), `journal` (`journal lister`), `reglage` (`reglages`) et `--version` (`version`).

**Aide.** `gestion-mep aide` liste toutes les commandes. `gestion-mep <groupe> --help` aide sur un groupe. `gestion-mep <groupe> <action> --help` (ou `-h`) donne l'usage, les options, la sortie `--json`, des exemples et les codes de retour. Une commande ou une option mal tapée est refusée avec une suggestion (« vouliez-vous dire « gestion-mep meps lister » ? »).

**Désigner un élément.** Un compte se désigne par son login, une équipe ou un système par son nom (majuscules indifférentes) ou son identifiant. Une MEP, une pièce jointe ou un élément de bibliothèque se désigne par son identifiant. Les 8 premiers caractères suffisent : c'est la colonne `ID` des listes.

**Sorties.** Les données demandées (listes, fiches, identifiant créé) vont sur la sortie standard. Les erreurs, avertissements et questions vont sur la sortie d'erreur. Les comptes rendus (« Compte créé… ») vont sur la sortie standard. On peut donc enchaîner :

```bash
ID=$(gestion-mep meps dupliquer 7c41d2aa --pour jdupont --quiet)   # l'identifiant seul
```

| Option commune | Effet |
|---|---|
| `--json` | Sortie JSON, décrite ci-dessous |
| `--quiet`, `-q` | Seulement les données demandées et les erreurs : ni compte rendu, ni avertissement |
| `--verbose`, `-v` | Détails sur la sortie d'erreur : fichier de configuration lu, base utilisée (mot de passe masqué), durée, pile d'une erreur inattendue |
| `--non-interactif` | Ne pose jamais de question ; une confirmation manquante échoue (code 4). Implicite hors terminal et avec `--json` |
| `--config <fichier>` | Fichier de configuration à lire (installation sans Docker), au lieu de celui de `GESTION_MEP_CONFIG` |
| `--help`, `-h` | Aide de la commande |

Les options communes se placent n'importe où dans la commande. `--quiet` et `--verbose` ne vont pas ensemble.

### Codes de retour

| Code | Signification | Exemple |
|---|---|---|
| `0` | Succès, y compris quand il n'y avait rien à faire | Compte déjà désactivé |
| `1` | Échec : règle de l'application, saisie refusée, contrôle en échec | Dernier administrateur, archive abîmée, fichier de pièce jointe manquant |
| `2` | Commande mal formée : commande, option ou argument inconnu ou manquant | `gestion-mep meps lsiter` |
| `3` | Base injoignable, ou configuration absente ou illisible | PostgreSQL arrêté, `--config` vers un fichier absent |
| `4` | Confirmation requise : opération destructive lancée sans `--oui` hors terminal ; rien n'a été modifié | `gestion-mep meps supprimer 3f2a9c1e --motif "…"` dans un script |
| `5` | Élément introuvable | Login, MEP, archive ou système inconnu |

### Sortie JSON

Avec `--json`, la sortie standard contient un seul document JSON. Les champs sont en `camelCase` et les énumérations en texte. Les dates sont au format ISO 8601, en UTC.

- Les commandes de consultation (`lister`, `afficher`, `journal`, `expertises`, `config afficher`…) écrivent la donnée elle-même, avec la même forme que l'API quand elle existe (par exemple `meps afficher --json` : comme `GET /api/meps/{id}`).
- Les autres commandes écrivent une enveloppe stable :

  ```json
  {
    "code": 0,
    "messages": ["Compte « jdupont » créé."],
    "avertissements": [],
    "resultat": { "compte": { "login": "jdupont", "…": "…" }, "motDePasseProvisoire": "…" }
  }
  ```

  `resultat` décrit ce qui a été créé ou modifié (voir « Sortie `--json` » dans l'aide de chaque commande). Il est `null` quand il n'y a rien à décrire.
- En cas d'erreur, la sortie standard reste vide. La sortie d'erreur contient `{ "code": 5, "erreur": "Compte « zorro » introuvable (gestion-mep comptes lister).", "details": null }`. `details` liste les anomalies d'une saisie refusée.

Les noms de champs ne changent pas d'une version à l'autre ; des champs peuvent s'ajouter.

### Confirmation des opérations destructives

Les opérations qui suppriment ou remplacent des données demandent une confirmation : `meps annuler`, `meps supprimer`, `sauvegardes purger`, `sauvegardes restaurer` et `demo reinitialiser`. Au terminal, la commande explique ce qu'elle va faire et demande de taper « oui ». Dans un script, ou avec `--non-interactif`, elle s'arrête avec le code 4 sans rien modifier. `--oui` confirme d'avance. Les refus de l'application passent avant la question (par exemple, une MEP qui a eu un Go ne se supprime pas).

Les commandes qui suivent les règles de l'application sans perte de données ne demandent rien. C'est le cas des suppressions d'éléments de bibliothèque et de systèmes : un système utilisé est désactivé au lieu d'être supprimé.

### Relancer sans risque

Relancer une commande dont l'effet est déjà là ne change rien et réussit : `comptes desactiver` un compte déjà désactivé, `equipes ajouter` un membre déjà présent, `meps annuler` une MEP déjà annulée, `sauvegardes purger` quand il n'y a rien à supprimer, `modeles-texte retablir` un texte d'origine, `sso delier` un compte non relié. Les créations (`comptes creer`, `systemes ajouter`…) refusent un doublon avec le code 1.

### Configuration lue

La commande lit la configuration dans le même ordre que l'application. Elle voit donc exactement ce que voit le service. Du moins prioritaire au plus prioritaire :

1. valeurs par défaut de l'application ;
2. variables d'environnement (`ConnectionStrings__Mep`, `Stockage__Dossier`…, celles du fichier `.env` avec Docker) ;
3. fichier de configuration de l'installation sans Docker (`/etc/gestion-mep/gestion-mep.conf`, `C:\ProgramData\GestionMep\gestion-mep.conf`), ou celui de `GESTION_MEP_CONFIG`, ou celui de l'option `--config` ;
4. réglages modifiés depuis la console ou par `reglages definir`, enregistrés dans la base.

L'option `--config` remplace donc le fichier, pas les réglages enregistrés dans la base. `gestion-mep config afficher` montre la configuration lue, secrets masqués. `--verbose` indique le fichier lu.

### Secrets

Aucun secret ne se passe en argument : il resterait dans l'historique du shell et dans la liste des processus. Le secret de la connexion unique se lit sur l'entrée standard (`sso configurer --secret-stdin < secret.txt`), ou se saisit sans écho au terminal. Les mots de passe et secrets ne sont jamais affichés : `config afficher` écrit « défini (masqué) », et `--verbose` masque le mot de passe de la base. Seuls les mots de passe provisoires (`comptes creer`, `comptes reinitialiser-mdp`) sont affichés, une seule fois. Ils sont aussi dans `resultat` avec `--json`, même avec `--quiet` : les transmettre par un canal sûr.

### Complétion

`gestion-mep completion <shell>` écrit le script de complétion des groupes, actions et options :

```bash
sudo gestion-mep completion bash > /etc/bash_completion.d/gestion-mep         # bash (posé par le script d'installation Linux)
gestion-mep completion zsh > "${fpath[1]}/_gestion-mep"                         # zsh
gestion-mep completion powershell | Out-String | Invoke-Expression             # PowerShell (à ajouter à $PROFILE)
```

Avec Docker, la commande s'exécute dans le conteneur : la complétion sert surtout aux installations sans Docker.

## Ce qui reste dans l'application

Certaines opérations restent dans l'application, parce qu'elles sont signées par une personne ou qu'elles demandent l'éditeur :

- la préparation et l'exécution d'une MEP : saisie des étapes, des sections et de la recette ; approbations ; go / no-go ; démarrage et résultat des étapes ; retour arrière ; clôture. Ces gestes engagent leur auteur et apparaissent sous son nom dans le journal de la MEP ;
- la création d'une MEP vide : `meps dupliquer` (depuis un modèle) et `meps importer` créent une MEP déjà remplie ;
- le report et la reprise d'une MEP, décidés par son pilote ;
- la rédaction des éléments de bibliothèque (étapes modèles, modèles de recette, modèles de texte), qui se fait dans l'éditeur avec son aperçu. La ligne de commande les consulte, les supprime et rétablit les modèles de texte fournis ;
- le dépôt et la suppression d'une pièce jointe. Ces gestes sont signés par une personne et soumis aux quotas. La ligne de commande mesure la place occupée, liste les pièces, en extrait les fichiers et contrôle le dossier ;
- le suivi en direct (tableau de bord des systèmes, notifications).

L'installation elle-même se fait par les scripts d'installation (voir [Installer](Installer.md) et [Installer sans Docker](Installer-sans-Docker.md)).

## Référence des commandes

<!-- reference-cli:debut (généré par « gestion-mep aide --markdown », ne pas modifier à la main) -->

### Comptes

Un compte se désigne par son login.

#### `comptes lister`

```
gestion-mep comptes lister
```

Liste les comptes : login, administrateur, état, nom.

Sortie `--json` : tableau des comptes, comme GET /api/admin/utilisateurs.

```bash
gestion-mep comptes lister
gestion-mep comptes lister --json
```

#### `comptes afficher`

```
gestion-mep comptes afficher <login>
```

Détail d'un compte : rôle, contact, équipes, comptes reliés, expertises.

Sortie `--json` : le compte, comme GET /api/admin/utilisateurs.

```bash
gestion-mep comptes afficher jdupont
```

#### `comptes creer`

```
gestion-mep comptes creer <login> [--nom "Nom"] [--admin] [--role "Rôle"] [--contact "Contact"]
```

Crée un compte avec un mot de passe provisoire, à changer à la première connexion. Le mot de passe provisoire n'est affiché qu'une fois (même avec --quiet) : le transmettre par un canal sûr.

| Option | Effet |
|---|---|
| `--nom "Nom"` | Nom affiché (défaut : le login) |
| `--admin` | Rôle administrateur |
| `--role "Rôle"` | Rôle (DBA, développeur…) |
| `--contact "Contact"` | Téléphone, courriel… |

Sortie `--json` : enveloppe { code, messages, avertissements, resultat } ; resultat : { compte, motDePasseProvisoire }.

```bash
gestion-mep comptes creer jdupont --nom "Jean Dupont" --role DBA --contact "poste 1234" --admin
```

#### `comptes modifier`

```
gestion-mep comptes modifier <login> [options]
```

Modifie un compte : seules les options données changent.

| Option | Effet |
|---|---|
| `--nom "Nom"` | Nom affiché (ne peut pas imiter le nom ou le login d'un autre compte) |
| `--admin oui\|non` | Rôle administrateur (le dernier administrateur actif le garde) |
| `--role "Rôle"` | Rôle |
| `--contact "Contact"` | Contact |
| `--ajouter-expertise <SYSTÈME>` | Ajoute (ou met à jour) une expertise sur un système du catalogue |
| `--detail "texte"` | Détail de l'expertise ajoutée juste avant |
| `--retirer-expertise <SYSTÈME>` | Retire une expertise |

Sortie `--json` : enveloppe { code, messages, avertissements, resultat } ; resultat : le compte modifié.

```bash
gestion-mep comptes modifier jdupont --nom "J. Dupont" --admin non
gestion-mep comptes modifier jdupont --ajouter-expertise BDD --detail "Base clients" --retirer-expertise API
```

#### `comptes expertises`

```
gestion-mep comptes expertises <login>
```

Expertises du compte.

Sortie `--json` : tableau { systemeCatalogueId, systeme, detail }.

```bash
gestion-mep comptes expertises jdupont
```

#### `comptes reinitialiser-mdp`

```
gestion-mep comptes reinitialiser-mdp <login>
```

Nouveau mot de passe provisoire (à changer à la connexion) ; ferme les sessions du compte. Administrateur perdu ou bloqué : comptes reinitialiser-mdp <login>, puis comptes reactiver <login> s'il était désactivé.

Sortie `--json` : enveloppe { code, messages, avertissements, resultat } ; resultat : { login, motDePasseProvisoire }.

```bash
gestion-mep comptes reinitialiser-mdp admin
gestion-mep comptes reinitialiser-mdp jdupont --json   (pour un script : resultat.motDePasseProvisoire)
```

#### `comptes desactiver`

```
gestion-mep comptes desactiver <login>
```

Désactive le compte et ferme ses sessions (sans effet s'il l'est déjà).

Sortie `--json` : enveloppe { code, messages, avertissements, resultat }.

```bash
gestion-mep comptes desactiver jdupont
```

#### `comptes reactiver`

```
gestion-mep comptes reactiver <login>
```

Réactive le compte (sans effet s'il est déjà actif).

Sortie `--json` : enveloppe { code, messages, avertissements, resultat }.

```bash
gestion-mep comptes reactiver jdupont
```

#### `comptes relier`

```
gestion-mep comptes relier <login> <login-principal> [--desactiver]
```

Relie deux comptes d'une même personne : elle s'affiche sous le nom du compte principal, rien n'est fusionné ni supprimé.

| Option | Effet |
|---|---|
| `--desactiver` | Désactive aussi le compte relié |

Sortie `--json` : enveloppe { code, messages, avertissements, resultat }.

```bash
gestion-mep comptes relier jean.dupont@entreprise.fr jdupont --desactiver
```

#### `comptes delier`

```
gestion-mep comptes delier <login>
```

Défait la liaison : le compte reprend son propre nom.

Sortie `--json` : enveloppe { code, messages, avertissements, resultat }.

```bash
gestion-mep comptes delier jean.dupont@entreprise.fr
```

### Équipes

Une équipe se désigne par son nom (guillemets s'il contient des espaces) ou son identifiant ; une personne peut appartenir à plusieurs équipes.

#### `equipes lister`

```
gestion-mep equipes lister
```

Équipes et nombre de membres.

Sortie `--json` : tableau des équipes, comme GET /api/equipes.

```bash
gestion-mep equipes lister
```

#### `equipes afficher`

```
gestion-mep equipes afficher <équipe>
```

Description, informations des modèles de texte et membres d'une équipe.

Sortie `--json` : { equipe, membres }.

```bash
gestion-mep equipes afficher "Développement web"
```

#### `equipes creer`

```
gestion-mep equipes creer "<nom>" [--description "…"] [--contact-support "…"] [--signature "…"]
```

Crée une équipe.

| Option | Effet |
|---|---|
| `--description "…"` | Description |
| `--contact-support "…"` | Contact support repris par les modèles de texte |
| `--signature "…"` | Signature des modèles de texte (\n pour un retour à la ligne) |

Sortie `--json` : enveloppe { code, messages, avertissements, resultat } ; resultat : l'équipe.

```bash
gestion-mep equipes creer "Exploitation" --description "Équipe d'astreinte"
```

#### `equipes modifier`

```
gestion-mep equipes modifier <équipe> [--nom "…"] [--description "…"] [--contact-support "…"] [--signature "…"]
```

Modifie une équipe : seules les options données changent.

| Option | Effet |
|---|---|
| `--nom "…"` | Nouveau nom |
| `--description "…"` | Description |
| `--contact-support "…"` | Contact support des modèles de texte (page Modèles de texte) |
| `--signature "…"` | Signature des modèles de texte (\n pour un retour à la ligne) |

Sortie `--json` : enveloppe { code, messages, avertissements, resultat } ; resultat : l'équipe.

```bash
gestion-mep equipes modifier Exploitation --nom "Exploitation N2"
gestion-mep equipes modifier Exploitation --contact-support "astreinte@entreprise.fr" --signature "L'équipe Exploitation\nPoste 1234"
```

#### `equipes ajouter`

```
gestion-mep equipes ajouter <équipe> <login> [<login>…]
```

Ajoute des comptes à l'équipe (déjà membres : sans effet).

Sortie `--json` : enveloppe { code, messages, avertissements, resultat }.

```bash
gestion-mep equipes ajouter Exploitation jdupont mmartin
```

#### `equipes retirer`

```
gestion-mep equipes retirer <équipe> <login> [<login>…]
```

Retire des comptes de l'équipe (non membres : sans effet).

Sortie `--json` : enveloppe { code, messages, avertissements, resultat }.

```bash
gestion-mep equipes retirer Exploitation mmartin
```

#### `equipes supprimer`

```
gestion-mep equipes supprimer <équipe>
```

Supprime l'équipe (comptes et MEP inchangés).

Sortie `--json` : enveloppe { code, messages, avertissements, resultat }.

```bash
gestion-mep equipes supprimer "Exploitation N2"
```

### Connexion unique (Microsoft Entra ID ou OpenID Connect)

#### `sso afficher`

```
gestion-mep sso afficher
```

Réglages de la connexion unique, ce qui manque, expiration du secret.

Sortie `--json` : réglages, comme GET /api/admin/sso (jamais le secret).

```bash
gestion-mep sso afficher
```

#### `sso configurer`

```
gestion-mep sso configurer [options]
```

Modifie les réglages : seules les options données changent. Le secret ne se passe jamais en argument (il resterait dans l'historique du shell et la liste des processus) : --secret-stdin.

| Option | Effet |
|---|---|
| `--annuaire <id>` | Microsoft : ID de l'annuaire (locataire), GUID ou domaine |
| `--emetteur <url>` | Autre fournisseur OpenID Connect : adresse https de l'émetteur |
| `--client <id>` | ID d'application (client) |
| `--secret-stdin` | Lit le secret client sur l'entrée standard (ou le demande au terminal, sans l'afficher) |
| `--expiration <AAAA-MM-JJ\|aucune>` | Date d'expiration du secret, pour être prévenu à temps |
| `--role-admin <valeur\|aucun>` | Rôle d'application du fournisseur qui donne le rôle administrateur |
| `--creation-auto oui\|non` | Crée le compte à la première connexion (défaut oui) |
| `--libelle "texte"` | Texte du bouton de la page de connexion |

Sortie `--json` : enveloppe { code, messages, avertissements, resultat }.

```bash
gestion-mep sso configurer --annuaire entreprise.onmicrosoft.com --client 0f1e2d3c-… --expiration 2027-06-30
gestion-mep sso configurer --secret-stdin < secret.txt
```

#### `sso tester`

```
gestion-mep sso tester
```

Vérifie que le fournisseur reconnaît l'application et le secret (code 1 si un contrôle échoue).

Sortie `--json` : enveloppe { code, messages, avertissements, resultat } ; resultat : tableau { libelle, ok, detail }.

```bash
gestion-mep sso tester
```

#### `sso activer`

```
gestion-mep sso activer
```

Affiche le bouton de connexion unique (sans redémarrage).

Sortie `--json` : enveloppe { code, messages, avertissements, resultat }.

```bash
gestion-mep sso activer
```

#### `sso desactiver`

```
gestion-mep sso desactiver
```

Retire le bouton de connexion unique ; les liaisons sont conservées.

Sortie `--json` : enveloppe { code, messages, avertissements, resultat }.

```bash
gestion-mep sso desactiver
```

#### `sso comptes`

```
gestion-mep sso comptes
```

Comptes reliés à un compte du fournisseur.

Sortie `--json` : tableau des liaisons.

```bash
gestion-mep sso comptes
```

#### `sso delier`

```
gestion-mep sso delier <login>
```

Supprime la liaison d'un compte avec le fournisseur.

Sortie `--json` : enveloppe { code, messages, avertissements, resultat }.

```bash
gestion-mep sso delier jdupont
```

### Réglages du serveur, modifiables sans redémarrage

Ancien nom accepté : « reglage ».

#### `reglages lister`

```
gestion-mep reglages lister
```

Réglages : valeur, origine (console, fichier, défaut), alertes.

Sortie `--json` : { reglages, approbationsMinimum }.

```bash
gestion-mep reglages lister
```

#### `reglages definir`

```
gestion-mep reglages definir <nom> <valeur>
```

Modifie un réglage ; appliqué sous 10 secondes, sans redémarrage (remplace la valeur du fichier). Le nom est celui de la variable du fichier .env, en minuscules avec des tirets (URL_PUBLIQUE → url-publique) ; approbations-minimum : approbations exigées avant le Go. Définir la valeur qu'il a déjà ne change rien.

Sortie `--json` : enveloppe { code, messages, avertissements, resultat } ; resultat : le réglage.

```bash
gestion-mep reglages definir url-publique https://mep.entreprise.fr
gestion-mep reglages definir lien-signalement ""      (valeur vide : lien masqué)
gestion-mep reglages definir approbations-minimum 2
```

#### `reglages retablir`

```
gestion-mep reglages retablir <nom>
```

Revient à la valeur du fichier de configuration (ou à la valeur par défaut).

Sortie `--json` : enveloppe { code, messages, avertissements, resultat } ; resultat : le réglage.

```bash
gestion-mep reglages retablir Stockage:QuotaMepMo
```

### Critères go / no-go

Un critère se désigne par son n° dans « criteres lister » ou par son identifiant.

#### `criteres lister`

```
gestion-mep criteres lister
```

Critères go / no-go, numérotés (n°).

Sortie `--json` : tableau des critères.

```bash
gestion-mep criteres lister
```

#### `criteres ajouter`

```
gestion-mep criteres ajouter "<libellé>" [--ordre n] [--actif oui|non]
```

Ajoute un critère.

| Option | Effet |
|---|---|
| `--ordre n` | Position dans la liste |
| `--actif oui\|non` | Proposé dans les MEP (défaut oui) |

Sortie `--json` : enveloppe { code, messages, avertissements, resultat } ; resultat : le critère.

```bash
gestion-mep criteres ajouter "Astreinte prévenue" --ordre 5
```

#### `criteres modifier`

```
gestion-mep criteres modifier <n°|id> [--libelle "…"] [--ordre n] [--actif oui|non]
```

Modifie un critère.

| Option | Effet |
|---|---|
| `--libelle "…"` | Libellé |
| `--ordre n` | Position |
| `--actif oui\|non` | Proposé dans les MEP |

Sortie `--json` : enveloppe { code, messages, avertissements, resultat } ; resultat : le critère.

```bash
gestion-mep criteres modifier 3 --libelle "Astreinte prévenue et joignable" --actif non
```

#### `criteres supprimer`

```
gestion-mep criteres supprimer <n°|id>
```

Supprime un critère (les MEP gardent leur copie).

Sortie `--json` : enveloppe { code, messages, avertissements, resultat }.

```bash
gestion-mep criteres supprimer 3
```

### Charte visuelle (couleurs de l'interface et des exports Excel)

Ordre : charte de la console > fichier charte.json > charte par défaut.

#### `charte afficher`

```
gestion-mep charte afficher
```

Charte en vigueur, origine (console, fichier, défaut), thème sombre et lisibilité.

Sortie `--json` : charte, comme GET /api/admin/charte.

```bash
gestion-mep charte afficher
```

#### `charte exporter`

```
gestion-mep charte exporter [fichier.json|-]
```

Charte en vigueur au format du fichier charte.json (défaut : sortie standard).

Sortie `--json` : enveloppe { code, messages, avertissements, resultat }.

```bash
docker compose exec -T app gestion-mep charte exporter > charte.json
```

#### `charte importer`

```
gestion-mep charte importer <fichier.json|-> [--essai]
```

Remplace la charte de la console (le fichier charte.json n'est plus lu).

| Option | Effet |
|---|---|
| `--essai` | Contrôle le fichier sans rien enregistrer |

Sortie `--json` : enveloppe { code, messages, avertissements, resultat }.

```bash
docker compose exec -T app gestion-mep charte importer - --essai < charte.json
```

#### `charte verifier`

```
gestion-mep charte verifier [fichier.json|-]
```

Valide un fichier, ou la charte en vigueur et le fichier charte.json : format et lisibilité (code 1 si invalide).

Sortie `--json` : enveloppe { code, messages, avertissements, resultat }.

```bash
gestion-mep charte verifier
gestion-mep charte verifier /tmp/charte.json
```

#### `charte definir`

```
gestion-mep charte definir <rôle> <RRGGBB>
```

Change une couleur : primaire, secondaire, tertiaire (ou « primaire » pour la lier), succes, avertissement, erreur, info. Dans un shell, « # » commence un commentaire : écrire la couleur sans « # » ou entre guillemets.

Sortie `--json` : enveloppe { code, messages, avertissements, resultat }.

```bash
gestion-mep charte definir primaire 0F766E
gestion-mep charte definir tertiaire primaire
```

#### `charte retablir`

```
gestion-mep charte retablir
```

Efface la charte de la console : retour au fichier charte.json, sinon à la charte par défaut.

Sortie `--json` : enveloppe { code, messages, avertissements, resultat }.

```bash
gestion-mep charte retablir
```

### Journal d'administration

#### `journal lister`

```
gestion-mep journal lister [--depuis AAAA-MM-JJ] [--limite n] [--type <type>]
```

Journal d'administration, du plus récent au plus ancien (200 événements par défaut).

| Option | Effet |
|---|---|
| `--depuis AAAA-MM-JJ` | Événements depuis ce jour (UTC) |
| `--limite n` | Nombre d'événements (200 par défaut) |
| `--type <type>` | Type d'événement (compte.cree…) ou famille (compte : tous les compte.…) |

Sortie `--json` : tableau { id, quand, auteur, auteurLogin, type, detail }. Autre nom : `gestion-mep journal`.

```bash
gestion-mep journal --depuis 2026-10-01 --limite 500
gestion-mep journal --type sauvegarde --json
```

### Catalogue des systèmes

Un système se désigne par son nom (sans tenir compte des majuscules) ou son identifiant.

#### `systemes lister`

```
gestion-mep systemes lister
```

Systèmes, environnements (* production) et nombre d'objets.

Sortie `--json` : tableau des systèmes.

```bash
gestion-mep systemes lister
```

#### `systemes afficher`

```
gestion-mep systemes afficher <SYSTÈME>
```

Fiche : environnements, catégories et objets.

Sortie `--json` : { systeme, objets }.

```bash
gestion-mep systemes afficher BDD
```

#### `systemes historique`

```
gestion-mep systemes historique <SYSTÈME> [--excel fichier.xlsx]
```

MEP qui ont touché le système : jour, état, environnements, échecs d'étape.

| Option | Effet |
|---|---|
| `--excel fichier.xlsx` | Écrit le classeur de l'onglet « Historique des MEP » de la fiche |

Sortie `--json` : historique, comme GET /api/catalogue/systemes/{id}/historique.

```bash
gestion-mep systemes historique BDD --environnement Production
```

#### `systemes ajouter`

```
gestion-mep systemes ajouter <nom> [--description "…"] [--cible "…"] [--actif oui|non]
```

Ajoute un système au catalogue.

| Option | Effet |
|---|---|
| `--description "…"` | Description |
| `--cible "…"` | Serveur ou adresse |
| `--actif oui\|non` | Proposé dans les MEP (défaut oui) |

Sortie `--json` : enveloppe { code, messages, avertissements, resultat } ; resultat : le système.

```bash
gestion-mep systemes ajouter CRM --description "Relation client" --cible crm01.entreprise.fr
```

#### `systemes modifier`

```
gestion-mep systemes modifier <SYSTÈME> [--nom "…"] [--description "…"] [--cible "…"] [--actif oui|non]
```

Modifie un système.

| Option | Effet |
|---|---|
| `--nom "…"` | Nouveau nom |
| `--description "…"` | Description |
| `--cible "…"` | Serveur ou adresse |
| `--actif oui\|non` | Proposé dans les MEP |

Sortie `--json` : enveloppe { code, messages, avertissements, resultat } ; resultat : le système.

```bash
gestion-mep systemes modifier crm --nom CRM2 --actif non
```

#### `systemes supprimer`

```
gestion-mep systemes supprimer <SYSTÈME>
```

Supprime un système, ou le désactive s'il est utilisé (MEP, résolution type).

Sortie `--json` : enveloppe { code, messages, avertissements, resultat }.

```bash
gestion-mep systemes supprimer CRM2
```

#### `systemes environnement`

```
gestion-mep systemes environnement <SYSTÈME> <nom> [--cible "…"] [--production oui|non] [--retirer]
```

Ajoute, modifie ou retire un environnement de la fiche.

| Option | Effet |
|---|---|
| `--cible "…"` | Serveur ou adresse de l'environnement |
| `--production oui\|non` | Environnement de production |
| `--retirer` | Retire l'environnement |

Sortie `--json` : enveloppe { code, messages, avertissements, resultat }.

```bash
gestion-mep systemes environnement BDD Préproduction --cible pg-preprod
gestion-mep systemes environnement BDD Production --production oui
```

#### `systemes categorie`

```
gestion-mep systemes categorie <SYSTÈME> "<catégorie>" [--renommer "…"] [--retirer]
```

Ajoute, renomme ou retire (avec ses objets) une catégorie de la fiche.

| Option | Effet |
|---|---|
| `--renommer "…"` | Nouveau nom de la catégorie |
| `--retirer` | Retire la catégorie et ses objets |

Sortie `--json` : enveloppe { code, messages, avertissements, resultat }.

```bash
gestion-mep systemes categorie ERP Tables
gestion-mep systemes categorie ERP Tables --renommer "Tables SQL"
```

#### `systemes objet`

```
gestion-mep systemes objet <SYSTÈME> "<catégorie>" "<nom>" [--type "…"] [--libelle "…"] [--precision "…"] [--retirer]
```

Ajoute, modifie ou retire un objet de la fiche.

| Option | Effet |
|---|---|
| `--type "…"` | Type (Table, Service…) |
| `--libelle "…"` | Libellé |
| `--precision "…"` | Précision |
| `--retirer` | Retire l'objet |

Sortie `--json` : enveloppe { code, messages, avertissements, resultat }.

```bash
gestion-mep systemes objet BDD Tables clients --type Table --libelle "Fiches clients"
```

### Catégories d'objets proposées par défaut à chaque système

#### `categories lister`

```
gestion-mep categories lister
```

Catégories d'objets proposées par défaut à chaque système.

Sortie `--json` : tableau de noms.

```bash
gestion-mep categories lister
```

#### `categories ajouter`

```
gestion-mep categories ajouter "<nom>"
```

Ajoute une catégorie par défaut.

Sortie `--json` : enveloppe { code, messages, avertissements, resultat }.

```bash
gestion-mep categories ajouter Licences
```

#### `categories retirer`

```
gestion-mep categories retirer "<nom>"
```

Retire une catégorie par défaut (les systèmes qui l'utilisent la gardent).

Sortie `--json` : enveloppe { code, messages, avertissements, resultat }.

```bash
gestion-mep categories retirer Licences
```

### Résolutions types

Une résolution se désigne par son identifiant (8 premiers caractères suffisent) ou son titre exact.

#### `resolutions lister`

```
gestion-mep resolutions lister [--systeme <SYSTÈME>] [--inactives]
```

Résolutions types.

| Option | Effet |
|---|---|
| `--systeme <SYSTÈME>` | Seulement celles de ce système |
| `--inactives` | Aussi les fiches désactivées |

Sortie `--json` : tableau, comme GET /api/resolutions.

```bash
gestion-mep resolutions lister
```

#### `resolutions afficher`

```
gestion-mep resolutions afficher <id|titre>
```

Fiche complète : symptômes, diagnostic, corrections, vérification.

Sortie `--json` : la résolution, comme GET /api/resolutions/{id}.

```bash
gestion-mep resolutions afficher 5c2e81d0
gestion-mep resolutions afficher "LDAP : compte de service verrouillé"
```

#### `resolutions supprimer`

```
gestion-mep resolutions supprimer <id|titre>
```

Supprime, ou désactive si elle a déjà servi.

Sortie `--json` : enveloppe { code, messages, avertissements, resultat }.

```bash
gestion-mep resolutions supprimer 5c2e81d0
```

#### `resolutions exporter`

```
gestion-mep resolutions exporter [fichier.json|-]
```

Résolutions actives en JSON, systèmes désignés par leur nom (défaut : sortie standard).

Sortie `--json` : enveloppe { code, messages, avertissements, resultat }.

```bash
docker compose exec -T app gestion-mep resolutions exporter > resolutions.json
```

#### `resolutions importer`

```
gestion-mep resolutions importer <fichier.json|-> [--remplacer|--ignorer-existantes] [--essai]
```

Crée les résolutions d'un export (code 1 si une fiche est refusée).

| Option | Effet |
|---|---|
| `--remplacer` | Une fiche du même titre est remplacée (nouvelle version, historique gardé) |
| `--ignorer-existantes` | Une fiche du même titre est ignorée (par défaut : refusée) |
| `--essai` | Contrôle sans rien enregistrer |

Sortie `--json` : tableau { titre, resultat (creee, remplacee, ignoree, refusee), motif }.

```bash
docker compose exec -T app gestion-mep resolutions importer - --essai < resolutions.json
```

### Étapes modèles (bibliothèque)

Une étape modèle se désigne par son identifiant (8 premiers caractères suffisent).

#### `etapes-modeles lister`

```
gestion-mep etapes-modeles lister [--systeme <SYSTÈME>]
```

Étapes modèles, les plus utilisées d'abord.

| Option | Effet |
|---|---|
| `--systeme <SYSTÈME>` | Seulement celles qui touchent ce système |

Sortie `--json` : tableau, comme GET /api/etapes-modeles.

```bash
gestion-mep etapes-modeles lister --systeme BDD
```

#### `etapes-modeles afficher`

```
gestion-mep etapes-modeles afficher <id>
```

Détail d'une étape modèle.

Sortie `--json` : l'étape modèle, comme GET /api/etapes-modeles/{id}.

```bash
gestion-mep etapes-modeles afficher c324d884
```

#### `etapes-modeles supprimer`

```
gestion-mep etapes-modeles supprimer <id>
```

Supprime une étape modèle (les étapes déjà insérées dans des MEP ne changent pas).

Sortie `--json` : enveloppe { code, messages, avertissements, resultat }.

```bash
gestion-mep etapes-modeles supprimer c324d884
```

### Modèles de recette (bibliothèque)

Un modèle se désigne par son identifiant (8 premiers caractères suffisent) ou son titre exact.

#### `modeles-recette lister`

```
gestion-mep modeles-recette lister [--systeme <SYSTÈME>]
```

Modèles de recette, les plus utilisés d'abord.

| Option | Effet |
|---|---|
| `--systeme <SYSTÈME>` | Seulement ceux de ce système |

Sortie `--json` : tableau, comme GET /api/modeles-recette.

```bash
gestion-mep modeles-recette lister --systeme API
```

#### `modeles-recette afficher`

```
gestion-mep modeles-recette afficher <id|titre>
```

Détail d'un modèle de recette : étapes de test.

Sortie `--json` : le modèle, comme GET /api/modeles-recette/{id}.

```bash
gestion-mep modeles-recette afficher "Santé de l'API"
```

#### `modeles-recette supprimer`

```
gestion-mep modeles-recette supprimer <id|titre>
```

Supprime un modèle de recette (les fiches déjà insérées ne changent pas).

Sortie `--json` : enveloppe { code, messages, avertissements, resultat }.

```bash
gestion-mep modeles-recette supprimer 55bd291b
```

### Modèles de texte des équipes (annonce, rappel, demande de MEP…)

Un modèle se désigne par son identifiant (8 premiers caractères suffisent).

#### `modeles-texte lister`

```
gestion-mep modeles-texte lister [--equipe <équipe>]
```

Modèles de texte des équipes.

| Option | Effet |
|---|---|
| `--equipe <équipe>` | Seulement ceux de cette équipe |

Sortie `--json` : tableau { id, equipe, nom, usage, fourni, version, modifieLe }.

```bash
gestion-mep modeles-texte lister --equipe "Développement web"
```

#### `modeles-texte afficher`

```
gestion-mep modeles-texte afficher <id>
```

Objet et corps d'un modèle de texte.

Sortie `--json` : le modèle { id, equipe, nom, usage, objet, corps, fourni, version, modifieLe }.

```bash
gestion-mep modeles-texte afficher b02d490e
```

#### `modeles-texte supprimer`

```
gestion-mep modeles-texte supprimer <id>
```

Supprime un modèle de texte (un modèle fourni ne se supprime pas : le rétablir).

Sortie `--json` : enveloppe { code, messages, avertissements, resultat }.

```bash
gestion-mep modeles-texte supprimer 7e1f0c42
```

#### `modeles-texte retablir`

```
gestion-mep modeles-texte retablir <id>
```

Rétablit le texte d'origine d'un modèle fourni.

Sortie `--json` : enveloppe { code, messages, avertissements, resultat }.

```bash
gestion-mep modeles-texte retablir b02d490e
```

#### `modeles-texte variables`

```
gestion-mep modeles-texte variables
```

Variables utilisables dans les modèles ({{mep.outil}}…).

Sortie `--json` : tableau { cle, libelle, groupe, description, liste }.

```bash
gestion-mep modeles-texte variables
```

### MEP et modèles de MEP

Une MEP se désigne par son identifiant (8 premiers caractères suffisent, colonne ID de « meps lister »).

#### `meps lister`

```
gestion-mep meps lister [filtres] [--modeles] [--excel fichier.xlsx]
```

MEP (ou modèles), plus récentes d'abord, avec les filtres de la page d'accueil.

| Option | Effet |
|---|---|
| `--recherche "…"` | Outil, version, objet, personnes, systèmes (sans accents ni casse) |
| `--systeme <SYSTÈME,…>` | Systèmes (nom du catalogue, ou nom saisi à la main dans les MEP) |
| `--environnement <env,…>` | Environnements, sur ces systèmes |
| `--echec` | Un échec d'étape survenu (sur les systèmes filtrés) |
| `--equipe <équipe,…>` | Une personne de l'équipe y participe |
| `--personne <login>` | Personne (login, nom ou identifiant ; comptes reliés compris) |
| `--role <rôle,…>` | Avec --personne : pilote, suppleant, responsable, executant, approbateur |
| `--etat <état,…>` | preparation, reportee, execution, recette, retour-arriere, reussie, incidents, repliee, echec, annulee |
| `--phase <phase>` | preparation, execution, recette, retour-arriere, cloturee, annulee |
| `--du AAAA-MM-JJ` | Jour du Go (à défaut, de création) à partir de |
| `--au AAAA-MM-JJ` | Jusqu'à ce jour inclus |
| `--type complete\|simplifiee` | Type de MEP |
| `--modeles` | Les modèles au lieu des MEP |
| `--excel fichier.xlsx` | Écrit la liste filtrée et ses échecs en classeur Excel |

Sortie `--json` : tableau, comme GET /api/meps.

```bash
gestion-mep meps lister --phase execution
gestion-mep meps lister --systeme BDD --environnement Production --echec
gestion-mep meps lister --personne alice --role pilote,executant --du 2026-01-01
gestion-mep meps lister --etat incidents,repliee --excel /tmp/meps.xlsx
```

#### `meps afficher`

```
gestion-mep meps afficher <id>
```

Résumé d'une MEP : identité, phase, personnes, systèmes, avancement.

Sortie `--json` : la MEP complète, comme GET /api/meps/{id}.

```bash
gestion-mep meps afficher 3f2a9c1e
gestion-mep meps afficher 3f2a9c1e --json
```

#### `meps journal`

```
gestion-mep meps journal <id> [--type <famille>] [--recherche "…"] [--limite n] [--apres <n° d'événement>]
```

Journal d'une MEP, du plus récent au plus ancien.

| Option | Effet |
|---|---|
| `--type <famille>` | Famille de l'onglet Journal (etape, phase, approbation…) ou type exact |
| `--recherche "…"` | Texte cherché dans le détail, l'auteur ou le login |
| `--limite n` | Nombre d'événements (1 à 500) |
| `--apres <n°>` | Seulement les événements plus récents que ce n° (suivre une MEP depuis un script) |

Sortie `--json` : tableau { id, quand, auteur, auteurLogin, type, detail, etapeId }.

```bash
gestion-mep meps journal 3f2a9c1e --limite 20
gestion-mep meps journal 3f2a9c1e --apres 1520 --json
```

#### `meps exporter`

```
gestion-mep meps exporter <id> [fichier.json|-]
```

MEP complète, suivi des tests unitaires et journal, en JSON (archive, audit, importation). Le fichier contient aussi le contenu réimportable de la MEP (« meps importer »). Les fichiers des pièces jointes restent dans les sauvegardes.

Sortie `--json` : enveloppe { code, messages, avertissements, resultat }.

```bash
docker compose exec -T app gestion-mep meps exporter 3f2a9c1e > mep-3f2a9c1e.json
```

#### `meps excel`

```
gestion-mep meps excel <id> <vue> [fichier.xlsx]
```

Une vue en classeur Excel : preparation, etapes, retour-arriere, recette, objets, journal, rapport. Sans fichier, le nom proposé par l'application, dans le dossier courant. « rapport » : rapport de fin d'une MEP clôturée.

Sortie `--json` : enveloppe { code, messages, avertissements, resultat }.

```bash
gestion-mep meps excel 3f2a9c1e etapes /tmp/etapes.xlsx
```

#### `meps importer`

```
gestion-mep meps importer <fichier.json|-> --pour <login> [--modele] [--essai]
```

Crée une MEP en préparation (ou un modèle) depuis un fichier de « meps exporter », comme une duplication. Comme une duplication : plan, étapes, sections, systèmes, personnes, recette et communication sont repris, avec de nouveaux identifiants ; ni dates, ni résultats, ni décisions d'approbation, ni journal, ni pièces jointes. Venant d'un autre serveur, les comptes sont retrouvés par leur login et les systèmes du catalogue par leur nom ; ce qui manque reste saisi à la main (personne sans compte, système hors catalogue) et est signalé.

| Option | Effet |
|---|---|
| `--pour <login>` | Compte qui reçoit la MEP : il en devient participant et responsable technique (créateur) |
| `--modele` | Crée un modèle au lieu d'une MEP |
| `--essai` | Contrôle le fichier et affiche les correspondances sans rien créer |

Sortie `--json` : enveloppe { code, messages, avertissements, resultat } ; resultat : { id, outil, estModele }.

```bash
docker compose exec -T app gestion-mep meps importer - --pour jdupont --modele < modele-paie.json
```

#### `meps dupliquer`

```
gestion-mep meps dupliquer <id> --pour <login> [--modele]
```

Crée une MEP en préparation depuis un modèle ou une autre MEP (ou un modèle depuis une MEP). Même copie que le bouton Dupliquer : ni dates, ni résultats, ni décisions d'approbation ; une MEP créée depuis un modèle reprend son outil sans « Modèle : ».

| Option | Effet |
|---|---|
| `--pour <login>` | Compte qui reçoit la copie : il en devient participant et responsable technique |
| `--modele` | Crée un modèle au lieu d'une MEP |

Sortie `--json` : enveloppe { code, messages, avertissements, resultat } ; resultat : { id, outil, estModele }.

```bash
gestion-mep meps dupliquer 7c41d2aa --pour jdupont      (MEP mensuelle depuis son modèle)
```

#### `meps annuler`

```
gestion-mep meps annuler <id> --motif "…" [--etat-production "…"] [--oui]
```

Annule une MEP en préparation (administrateur) ; elle reste consultable. Une MEP s'annule depuis la préparation : une MEP en cours revient d'abord en préparation dans l'application.

| Option | Effet |
|---|---|
| `--motif "…"` | Motif de l'annulation (obligatoire) |
| `--etat-production "…"` | État laissé en production (obligatoire après un Go) |
| `--oui` | Confirme l'opération sans question (obligatoire hors terminal) |

Sortie `--json` : enveloppe { code, messages, avertissements, resultat }.

```bash
gestion-mep meps annuler 3f2a9c1e --motif "Doublon de la MEP 7c41d2aa" --oui
```

#### `meps supprimer`

```
gestion-mep meps supprimer <id> --motif "…" [--oui]
```

Supprime définitivement une MEP jamais lancée, ou un modèle, avec ses pièces jointes. Une MEP qui a eu un Go ne se supprime pas : l'annuler pour garder sa trace.

| Option | Effet |
|---|---|
| `--motif "…"` | Motif, inscrit au journal d'administration (obligatoire) |
| `--oui` | Confirme l'opération sans question (obligatoire hors terminal) |

Sortie `--json` : enveloppe { code, messages, avertissements, resultat }.

```bash
gestion-mep meps supprimer 3f2a9c1e --motif "Créée par erreur" --oui
```

#### `meps piloter`

```
gestion-mep meps piloter <id> <login> --motif "…" [--ancien-pilote-suppleant oui|non]
```

Pilotage de secours après le Go : confie le pilotage à un compte (le pilote est injoignable).

| Option | Effet |
|---|---|
| `--motif "…"` | Motif (obligatoire), inscrit au journal de la MEP et au journal d'administration |
| `--ancien-pilote-suppleant oui\|non` | L'ancien pilote reste suppléant (défaut oui) |

Sortie `--json` : enveloppe { code, messages, avertissements, resultat }.

```bash
gestion-mep meps piloter 3f2a9c1e bmartin --motif "Pilote malade, relève de l'astreinte"
```

### Pièces jointes

#### `pieces-jointes utilisation`

```
gestion-mep pieces-jointes utilisation [--limite n]
```

Place occupée : total, par MEP, résolutions types, plafonds et espace disque.

| Option | Effet |
|---|---|
| `--limite n` | Nombre de MEP détaillées (10 par défaut) |

Sortie `--json` : { dossier, total, nombre, resolutions, meps, plafonds, disque }.

```bash
gestion-mep pieces-jointes utilisation --limite 20
```

#### `pieces-jointes lister`

```
gestion-mep pieces-jointes lister <id de MEP>
```

Pièces jointes d'une MEP : nom, taille, date, auteur, étape.

Sortie `--json` : tableau, comme GET /api/meps/{id}/pieces-jointes.

```bash
gestion-mep pieces-jointes lister 3f2a9c1e
```

#### `pieces-jointes extraire`

```
gestion-mep pieces-jointes extraire <id> [fichier|-]
```

Copie le fichier d'une pièce jointe (défaut : son nom, dans le dossier courant).

Sortie `--json` : enveloppe { code, messages, avertissements, resultat }.

```bash
docker compose exec -T app gestion-mep pieces-jointes extraire 9b0c4f11 - > preuve.png
```

#### `pieces-jointes verifier`

```
gestion-mep pieces-jointes verifier [--isoler-orphelins]
```

Contrôle que chaque pièce jointe a son fichier, au contenu intact (empreinte SHA-256) ; code 1 sinon ; signale les fichiers orphelins.

| Option | Effet |
|---|---|
| `--isoler-orphelins` | Déplace les fichiers sans pièce jointe (plus d'une heure) dans le sous-dossier « orphelins », comme au démarrage |

Sortie `--json` : enveloppe { code, messages, avertissements, resultat } ; resultat : { pieces, manquantes, abimees, orphelins, orphelinsIsoles }.

```bash
gestion-mep pieces-jointes verifier
gestion-mep pieces-jointes verifier --isoler-orphelins
```

### Sauvegardes et restauration

#### `sauvegardes creer`

```
gestion-mep sauvegardes creer [fichier.tar.gz]
```

Archive la base et les pièces jointes (défaut : dossier des sauvegardes).

Sortie `--json` : enveloppe { code, messages, avertissements, resultat } ; resultat : { fichier, taille, lignes, pieces }. Autre nom : `gestion-mep sauvegarder`.

```bash
gestion-mep sauvegarder
gestion-mep sauvegardes creer /data/sauvegardes/avant-intervention.tar.gz
```

#### `sauvegardes lister`

```
gestion-mep sauvegardes lister
```

Archives du dossier des sauvegardes.

Sortie `--json` : tableau { nom, chemin, taille, modifieeLe, lisible, application, migration, lignes, pieces }.

```bash
gestion-mep sauvegardes lister
```

#### `sauvegardes verifier`

```
gestion-mep sauvegardes verifier <archive>
```

Contrôle une archive sans rien restaurer : contenu, empreintes des pièces jointes, version (code 1 si invalide).

Sortie `--json` : enveloppe { code, messages, avertissements, resultat } ; resultat : { archive, application, migration, lignes, pieces, problemes }.

```bash
gestion-mep sauvegardes verifier sauvegarde-20261029-020000.tar.gz
```

#### `sauvegardes purger`

```
gestion-mep sauvegardes purger --garder n [--essai] [--oui]
```

Supprime les archives les plus anciennes du dossier des sauvegardes, en gardant les n plus récentes.

| Option | Effet |
|---|---|
| `--garder n` | Nombre d'archives à garder (1 au moins) |
| `--essai` | Affiche ce qui serait supprimé, sans rien supprimer |
| `--oui` | Confirme l'opération sans question (obligatoire hors terminal) |

Sortie `--json` : enveloppe { code, messages, avertissements, resultat } ; resultat : { supprimees, gardees }.

```bash
gestion-mep sauvegardes purger --garder 14 --oui      (après la sauvegarde de la nuit)
```

#### `sauvegardes restaurer`

```
gestion-mep sauvegardes restaurer <archive> [--oui]
```

Remplace base et pièces jointes par l'archive (application arrêtée). L'archive se donne par son nom dans le dossier des sauvegardes ou par son chemin. Les données actuelles sont d'abord sauvegardées (avant-import-…). Refusée si l'application tourne encore (autre connexion à la base).

| Option | Effet |
|---|---|
| `--oui` | Confirme l'opération sans question (obligatoire hors terminal) |

Sortie `--json` : enveloppe { code, messages, avertissements, resultat }.

```bash
sudo systemctl stop gestion-mep && sudo gestion-mep sauvegardes restaurer sauvegarde-20261029-093511.tar.gz --oui
```

#### `sauvegardes importer`

```
gestion-mep sauvegardes importer <fichier.tar.gz>
```

Comme « sauvegardes restaurer », sans confirmation ni contrôle d'arrêt (déménagement scripté).

Sortie `--json` : enveloppe { code, messages, avertissements, resultat }. Autre nom : `gestion-mep importer`.

```bash
docker compose run --rm -v "$PWD/sauvegardes:/import:ro" app importer /import/sauvegarde-AAAAMMJJ-HHMMSS.tar.gz
```

### Configuration lue au démarrage

#### `config afficher`

```
gestion-mep config afficher
```

Configuration lue au démarrage (secrets masqués).

Sortie `--json` : { demarrage, reglages, connexionUnique, erreurBase }.

```bash
gestion-mep config afficher
```

#### `config verifier`

```
gestion-mep config verifier
```

Vérifie base, migrations, dossiers, comptes administrateurs, mots de passe d'exemple (code 1 si problème).

Sortie `--json` : enveloppe { code, messages, avertissements, resultat } ; resultat : tableau { niveau (ok, attention, erreur), message }.

```bash
gestion-mep config verifier
```

### Mode démonstration (DEMO=true uniquement)

#### `demo reinitialiser`

```
gestion-mep demo reinitialiser [--oui]
```

Remet la base dans l'état initial de la démonstration ; refusée hors démonstration. Comptes, MEP, catalogue, résolutions, critères, paramètres, réglages et journaux sont recréés ; les pièces jointes sont supprimées ; les sessions ouvertes sont fermées.

| Option | Effet |
|---|---|
| `--oui` | Confirme l'opération sans question (obligatoire hors terminal) |

Sortie `--json` : { meps, fichiersSupprimes }.

```bash
docker compose exec app gestion-mep demo reinitialiser --oui
```

### Base de données

#### `migrer`

```
gestion-mep migrer
```

Applique les migrations de la base sans démarrer l'application (sauvegarde préalable, contrôles).

Sortie `--json` : enveloppe { code, messages, avertissements, resultat }.

```bash
gestion-mep migrer
```

### Mise à jour de l'application

#### `mettre-a-jour`

```
gestion-mep mettre-a-jour [--oui] [--version x.y.z]
```

Dernière version publiée ; avec --oui, demande son installation au service de mise à jour.

| Option | Effet |
|---|---|
| `--oui` | Demande l'installation (refusée pendant une MEP en cours) |
| `--version x.y.z` | Autre version plus récente (registre interne) |

Sortie `--json` : état, comme GET /api/admin/mises-a-jour.

```bash
gestion-mep mettre-a-jour
```

### Santé de l'application en marche

#### `sante`

```
gestion-mep sante [--prete] [--url <adresse>]
```

Interroge l'application en marche : vivante, ou prête à servir (base, schéma, échéances). Code 0 si l'application répond (et est prête avec --prete), 1 sinon.

| Option | Effet |
|---|---|
| `--prete` | Disponibilité complète (/api/sante/prete), comme le HEALTHCHECK de l'image |
| `--url <adresse>` | Adresse de l'application (défaut : adresse locale d'écoute, ou variable SANTE_URL) |

Sortie `--json` : { url, statut, http, detail }.

```bash
gestion-mep sante --prete
gestion-mep sante --prete --url https://mep.entreprise.fr --json
```

### Version

#### `version`

```
gestion-mep version
```

Version de l'application.

Sortie `--json` : { version, complete, installation }. Autre nom : `gestion-mep --version`.

```bash
gestion-mep version
gestion-mep --version --json
```

### Complétion des commandes dans le shell

#### `completion`

```
gestion-mep completion <bash|zsh|powershell>
```

Script de complétion des commandes pour le shell.

Sortie `--json` : enveloppe { code, messages, avertissements, resultat }.

```bash
sudo gestion-mep completion bash > /etc/bash_completion.d/gestion-mep
gestion-mep completion zsh > "${fpath[1]}/_gestion-mep"
gestion-mep completion powershell | Out-String | Invoke-Expression      (dans $PROFILE)
```

### Aide

#### `aide`

```
gestion-mep aide [groupe] [action] [--markdown]
```

Liste des commandes, ou aide d'un groupe ou d'une commande.

| Option | Effet |
|---|---|
| `--markdown` | Référence complète au format Markdown (documentation) |

Sortie `--json` : enveloppe { code, messages, avertissements, resultat }.

```bash
gestion-mep aide comptes
gestion-mep comptes creer --help
```

<!-- reference-cli:fin -->
