# Connexion Microsoft (SSO)

La connexion unique est **facultative**. Sans elle, l'application fonctionne avec ses comptes locaux. Une fois activée, la page de connexion affiche un bouton « Se connecter avec Microsoft » à côté du formulaire habituel : le mot de passe local reste toujours possible, par exemple pour un compte de secours.

Elle se règle **depuis la console d'administration** (onglet « Connexion unique ») **ou en ligne de commande** (`gestion-mep sso`), sans modifier `.env` ni redémarrer. Microsoft Entra ID est préréglé ; tout autre fournisseur OpenID Connect (Okta, Google Workspace, Keycloak, ADFS récent…) se règle de la même façon.

## Ce qu'il faut

- Un administrateur de l'annuaire Microsoft Entra de l'entreprise, pour inscrire l'application (une seule fois).
- L'application servie en **HTTPS** derrière le [proxy inverse](Installer.md#proxy-inverse-https). Microsoft n'accepte une adresse de retour en HTTP que pour `localhost`.
- Un accès sortant du serveur vers `https://login.microsoftonline.com` (directement ou par le proxy sortant de l'entreprise).

## 1. Inscrire l'application dans Microsoft Entra

L'adresse de retour exacte est affichée dans la console (onglet « Connexion unique ») et par `gestion-mep sso afficher`. Elle a la forme :

```text
https://mep.entreprise.fr/connexion-sso/retour
```

Si l'adresse affichée n'est pas l'adresse publique (proxy qui réécrit l'hôte), définir `URL_PUBLIQUE=https://mep.entreprise.fr` dans `.env` (voir [Installer](Installer.md#configuration)).

Dans le portail Entra (entra.microsoft.com) :

1. **Inscriptions d'applications › Nouvelle inscription**. Nom : « Gestion MEP ». Types de comptes : **comptes de cet annuaire organisationnel uniquement**.
2. **URI de redirection** : plateforme **Web**, coller l'adresse de retour. Enregistrer.
3. **Certificats et secrets › Nouveau secret client**. Copier la **Valeur** (pas l'ID du secret) et noter la date d'expiration.
4. **Vue d'ensemble** : copier l'**ID de l'annuaire (locataire)** et l'**ID d'application (client)**.
5. Facultatif, pour donner le rôle administrateur depuis Entra : **Rôles d'application › Créer un rôle d'application** (par exemple valeur `MEP.Admin`, membres autorisés : utilisateurs/groupes), puis dans **Applications d'entreprise › Gestion MEP › Utilisateurs et groupes**, attribuer ce rôle aux administrateurs.

Aucune permission d'API supplémentaire n'est nécessaire : l'application ne lit que l'identité de la personne (nom, identifiant, rôles).

## 2a. Régler depuis la console

Console d'administration › onglet **Connexion unique** :

1. Saisir l'ID de l'annuaire, l'ID d'application, le secret et sa date d'expiration. **Enregistrer**.
2. **Tester** : l'application vérifie que Microsoft connaît l'annuaire, l'application et le secret, sans connecter personne.
3. **Activer**. Le bouton apparaît aussitôt sur la page de connexion.

## 2b. Régler en ligne de commande

```bash
docker compose exec app gestion-mep sso afficher            # réglages et adresse de retour
# Le secret est lu sur l'entrée standard : il ne reste ni dans l'historique ni dans la liste des processus.
docker compose exec -T app gestion-mep sso configurer \
  --annuaire 72f988bf-0000-0000-0000-000000000000 \
  --client 3f2a1b4c-0000-0000-0000-000000000000 \
  --expiration 2027-04-01 \
  --secret-stdin < secret.txt
docker compose exec app gestion-mep sso tester              # code 1 si une vérification échoue
docker compose exec app gestion-mep sso activer
```

Sans redirection, `--secret-stdin` demande le secret au clavier sans l'afficher (`docker compose exec app …`, sans `-T`). `sso configurer` ne change que les options données. Autres options :

| Option | Rôle |
|---|---|
| `--emetteur <url>` | Autre fournisseur OpenID Connect : adresse https de l'émetteur, à la place de `--annuaire`. |
| `--role-admin <valeur>` | Rôle d'application qui donne le rôle administrateur (`aucun` pour revenir à une gestion dans l'application). |
| `--creation-auto oui\|non` | Créer le compte à la première connexion (défaut `oui`). |
| `--libelle "texte"` | Texte du bouton de la page de connexion. |
| `--expiration aucune` | Retire la date d'expiration du secret. |

`gestion-mep sso desactiver` retire le bouton (les liaisons sont conservées). `gestion-mep sso comptes` liste les comptes reliés, `gestion-mep sso delier <login>` supprime une liaison. `gestion-mep config verifier` signale une connexion unique active mais inutilisable, et un secret qui expire dans moins de 30 jours.

## Comment les comptes sont reliés

- À la **première connexion** par Microsoft, le compte est créé (identifiant = adresse Microsoft de la personne, sans mot de passe). Avec « Créer le compte à la première connexion » décoché (`--creation-auto non`), seuls les comptes déjà reliés peuvent entrer.
- Un compte reste relié à la personne par l'**identifiant immuable** que Microsoft lui donne dans l'annuaire, **jamais par l'adresse mail** : un changement de nom ou d'adresse ne casse rien, et personne ne peut prendre un compte en obtenant la même adresse ailleurs.
- **Un compte local existant** (créé avant le SSO) se relie par la personne elle-même : connectée avec son mot de passe, « Mon profil › Connexion unique › Lier mon compte ». Si elle passe directement par le bouton Microsoft, la connexion est refusée tant qu'un compte local porte déjà son identifiant, avec la marche à suivre.
- **Rôle administrateur** : sans rôle d'application réglé, il se gère dans l'onglet Comptes, comme pour un compte local. Avec un rôle réglé (`MEP.Admin`), il est relu à chaque connexion par Microsoft : présent, la personne devient administrateur ; absent, elle ne l'est plus. Le dernier administrateur actif garde toujours son rôle.
- Désactiver un compte dans l'application bloque aussi sa connexion par Microsoft. Supprimer la personne de l'annuaire Entra l'empêche de se connecter par Microsoft ; pensez à désactiver aussi son compte dans l'application s'il a un mot de passe.

## Sécurité

- Flux OpenID Connect « code d'autorisation » avec PKCE ; jeton d'identité vérifié (émetteur, audience, signature, nonce). L'annuaire est celui de l'entreprise : `common` ou `organizations`, qui ouvriraient l'application à tous les comptes Microsoft, sont refusés.
- Le secret client est chiffré en base avec les clés de l'application (volume `cles`) et n'est jamais renvoyé par l'API ni affiché.
- Après une [restauration](Exploiter.md#restaurer-ou-déménager) sur une autre machine (autres clés), le secret devient illisible : la console et `gestion-mep sso afficher` le signalent, il suffit de le saisir de nouveau.
- Chaque réglage, activation, liaison et compte créé par Microsoft est inscrit dans le journal d'administration.

## Renouveler le secret

Un secret Entra expire (6 mois à 2 ans). La console prévient 30 jours avant la date saisie. Créer un nouveau secret dans Entra, puis :

```bash
docker compose exec -T app gestion-mep sso configurer --expiration 2028-04-01 --secret-stdin < nouveau-secret.txt
docker compose exec app gestion-mep sso tester
```

ou le saisir dans la console. Supprimer ensuite l'ancien secret dans Entra.

## Dépannage

| Message | Cause et solution |
|---|---|
| « Secret refusé : copiez la Valeur du secret » | L'ID du secret a été copié à la place de sa valeur. |
| « Application introuvable dans cet annuaire » | ID d'application erroné, ou application inscrite dans un autre annuaire. |
| « Microsoft ne connaît pas cet annuaire » | ID de l'annuaire erroné. |
| « Injoignable depuis le serveur » | Pas d'accès sortant vers `login.microsoftonline.com` : ouvrir le flux ou régler le proxy sortant (`HTTPS_PROXY`). |
| Microsoft affiche « AADSTS50011 : redirect URI mismatch » | L'adresse de retour déclarée dans Entra diffère de celle affichée par la console : la recopier exactement, ou définir `URL_PUBLIQUE`. |
| « Un compte local porte déjà cet identifiant » | Se connecter avec son mot de passe, puis lier son compte depuis « Mon profil ». |
