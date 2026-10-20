# Front-end

React 19, TypeScript 7 (strict), Vite, Tailwind CSS 4, primitives [Radix](https://www.radix-ui.com/) et icônes Lucide. En production, le résultat de `npm run build` est servi par l'API .NET (dossier `wwwroot` de l'image).

## Commandes

| Commande                                  | Rôle                                                                                              |
| ----------------------------------------- | ------------------------------------------------------------------------------------------------- |
| `npm run dev`                             | Serveur de développement sur http://localhost:5173 ; `/api` est relayé vers http://localhost:5080 |
| `npm test`                                | Tests Vitest (jsdom)                                                                              |
| `npm run e2e`                             | Tests de bout en bout Playwright (voir plus bas)                                                  |
| `npm run lint`                            | Oxlint avec analyse des types                                                                     |
| `npm run format` / `npm run format:check` | Prettier                                                                                          |
| `npm run typecheck`                       | `tsc --noEmit`                                                                                    |
| `npm run build`                           | Typage puis construction dans `dist/`                                                             |
| `npm run api:types`                       | Régénère `src/api/schema.d.ts` depuis `docs/openapi.json`                                         |

## Organisation

| Dossier               | Contenu                                                                                                                                                                           |
| --------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `src/pages`           | Pages : connexion, liste des MEP (barre de filtres et tableau des résultats dans `pages/liste`), une MEP, Suivi en direct, rapport de fin, profil                                 |
| `src/mep`             | Onglets et dialogues d'une MEP (préparation, plan : étapes, retour arrière, recette ; exécution, journal ; `onglets.ts`)                                                          |
| `src/mep/ensemble`    | Vue d'ensemble d'une MEP et calculs du suivi (états, étapes bloquées, fin estimée, ordre du tableau de bord) dans `calculs.ts`                                                    |
| `src/mep/graphe`      | Graphe libre des étapes (React Flow, rangement ELK), sections, verrou d'édition d'une étape ; appels de l'API du graphe dans `apiGraphe.ts`                                       |
| `src/mep/preparation` | Sections de l'onglet Préparation                                                                                                                                                  |
| `src/parametres`      | Paramètres globaux : catalogue des systèmes (et historique des MEP d'un système), résolutions types, bibliothèques d'étapes et de recettes, critères go / no-go, modèles de texte |
| `src/textes`          | Textes à copier : remplissage des modèles de texte (`modelesTexte.ts`, logique pure), aperçu, copie mise en forme ou simple                                                       |
| `src/layout`          | Cadre de l'application, palette de commandes, bandeau « nouvelle version », repli temporaire du menu latéral (`menuLateral.ts`)                                                   |
| `src/charte`          | Charte visuelle : calculs des couleurs (`charte.ts`, `couleurs.ts`, mêmes résultats que le serveur), sélecteur de couleur, aperçus                                                |
| `src/ui`              | Composants génériques (boutons, dialogues, menus, champs, toasts…)                                                                                                                |
| `src/test`            | Configuration Vitest (les données partagées de `referentiels.ts` sont oubliées après chaque test : l'ordre des tests est indifférent) et fabrique de données de test              |

Dans `src/mep/graphe`, `GrapheLibre.tsx` s'appuie sur `BarreOutilsGraphe.tsx` (barre d'outils), `MiniCarte.tsx`, `useMontageAnime.ts` (mouvement des nœuds après un rangement) et `noeudsRf.ts` (nœuds React Flow) ; `moteurElk.ts` exécute ELK dans un Web Worker (délai, reprise après échec) et `petitEcran.ts` règle la vue compacte (fenêtre de moins de 800 px de haut).

Logique pure, testée sans DOM :

| Module             | Rôle                                                                                                                                                                                             |
| ------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `api.ts`           | Client HTTP : une fonction par route, erreurs (_problem details_) converties en `ApiError` (statut, message français, motifs `erreurs`), en-tête anti-CSRF `X-Requested-With` sur chaque requête |
| `types.ts`         | Noms courts des types de l'API (`Mep`, `Etape`…), alias des types générés                                                                                                                        |
| `api/schema.d.ts`  | Types générés depuis `docs/openapi.json` par `npm run api:types` ; ne jamais modifier à la main                                                                                                  |
| `domaine.ts`       | Règles d'affichage : plan actif, étapes à vous, bloqueurs, progression, bilan de recette                                                                                                         |
| `graphe.ts`        | Lignes du graphe (profondeur des étapes, ordre de l'export), liens du plan                                                                                                                       |
| `personnes.ts`     | Comptes reliés regroupés par personne, équipes d'une personne, filtres par équipe, recherche des comptes, personnes proposées par la recherche des MEP                                           |
| `rechercheMeps.ts` | Recherche des MEP : critères lus et écrits dans l'adresse, mêmes règles que le serveur (`FiltreMeps.cs`), vérifiées par les cas partagés de `vecteurs-filtre-meps.json`                          |
| `recherche.ts`     | Recherche insensible aux accents, score et surlignage                                                                                                                                            |
| `blocs.ts`         | Découpage des instructions en blocs Texte / Code et raccourcis de mise en forme markdown                                                                                                         |
| `format.ts`        | Libellés, dates, durées, comptes à rebours                                                                                                                                                       |
| `bibliotheque.ts`  | Bibliothèques (étapes, recettes, résolutions) : recherche à plusieurs mots (noms de systèmes reconnus), tri par utilisations puis création, types d'étape                                        |

## Principes

### L'API renvoie toujours la MEP complète

Chaque action d'écriture renvoie la MEP entière, recalculée pour l'utilisateur courant : disponibilités des étapes, erreurs du plan, droits (`mep.droits`). Le front n'a donc pas à recalculer ces règles : il remplace son état par la réponse.

### `MepContext.run`

Les composants d'une MEP lisent la MEP et lancent leurs actions par `useMep()` (`mepContext.tsx`), fourni par `MepPage` :

```tsx
const { mep, run } = useMep();
await run(() => api.demarrerEtape(mep.id, etape.id, etape.revision), 'Étape démarrée.');
```

`run` applique la MEP renvoyée et affiche le message de succès. Sur un **409** (quelqu'un d'autre a modifié la MEP), il relit la MEP et prévient, sans toucher aux saisies en cours. Sur une autre erreur, il affiche le message de l'API, ou le passe au rappel `surErreur` s'il est fourni. Il renvoie `true` si l'action a réussi.

### Brouillons et saisie en cours

- `useDraft(source)` (`hooks.ts`) garde la saisie d'un formulaire : tant que l'utilisateur n'a rien modifié, le formulaire suit la source ; dès qu'il tape, sa saisie prime.
- Le brouillon mémorise aussi la source au début de la saisie. Si elle change ensuite (modification d'un collègue reçue entre-temps), `conflit` passe à vrai : `ui/AvisConflit.tsx` affiche « Modifié pendant votre saisie » et bloque l'enregistrement jusqu'à ce que l'utilisateur abandonne sa saisie ou la garde (`garderSaisie`). La source ne doit contenir que les champs du formulaire, pas la version de la MEP.
- `useReportDirty(dirty, libelle)` signale une saisie non enregistrée (`DirtyContext`) à `layout/GardeModifications.tsx`, qui entoure les pages connectées. Quitter la vue (autre page, onglet ou sous-onglet, voir `changeDeVue`) ouvre alors « Quitter sans enregistrer ? » avec la liste des libellés ; fermer ou recharger le navigateur déclenche son avertissement (`beforeunload`). La garde utilise `useBlocker` : l'application tourne dans un routeur de données (`createBrowserRouter`, `main.tsx`) et ses tests dans `createMemoryRouter`.
- Passer aussi le brouillon (`useReportDirty(d.dirty, 'Intervention', d)`) : avant une reconnexion Microsoft (session expirée), la saisie est copiée dans `sessionStorage` (`brouillons.ts`), puis reprise au retour sur la même page par le formulaire de même libellé.
- Session expirée : un 401 ouvre `layout/SessionExpiree.tsx` par-dessus la page ; la requête attend la reconnexion (`setUnauthorizedHandler`, `auth.tsx`) puis repart une fois. Les saisies restent en mémoire.
- Une MEP relue (modification d'un collègue) s'affiche aussitôt : les formulaires gardent leur brouillon et `AvisConflit` signale un changement de leur section.

### Chargements et erreurs

- Listes communes : `useReferentiel('catalogue' | 'resolutions' | 'comptes' | 'equipes')` et `useDonneePartagee(cle, charger)` (`referentiels.ts`) lisent une clé une seule fois pour toute la page et renvoient `{ donnees, erreur, pret, enCours, recharger }` ; `charger` reçoit la dernière réponse gardée, pour ne lire que la suite d'une donnée lue par morceaux (journal d'une MEP : `useJournalMep`). Ne jamais remplacer une erreur par une liste vide (`.catch(() => [])`) : « Aucun… » ne s'affiche que sur une réponse vide.
- `useAsync` expose `echec` (l'erreur reçue) en plus du message `erreur`.
- `ui/ErreurChargement.tsx` affiche l'erreur (`variante` `bloc`, `ligne` ou `page`), sa cause, « Réessayer » (`onReessayer`, `enCours`) et, pour un administrateur, les détails techniques. Un 401 n'affiche rien : la reconnexion s'en charge.
- Un écran qui dépend de plusieurs listes montre un seul squelette puis tout d'un coup (`ui/Apparition.tsx`), comme l'onglet Préparation (`usePreparation`).

### Temps réel

`MepPage` ouvre un flux SSE (`/api/meps/{id}/flux?session=…`, une session par onglet). Chaque événement `maj` porte la nouvelle version de la MEP ; si elle est plus récente que celle affichée, la MEP est rechargée. Chaque événement `presence` donne les personnes qui ont la MEP ouverte et leurs éléments en cours (`presence.tsx`) : « Sur cette MEP : » dans l'en-tête, encadré « Prénom modifie » sur l'élément (`MarquePresence`). Un composant déclare son élément par `usePresenceElement(cle, actif)` ou en s'enveloppant dans `ZonePresence` (focus ou saisie en cours) ; `MepPage` regroupe ces déclarations, les envoie (`PUT /api/meps/{id}/presence`) et les redéclare à chaque ouverture du flux. Après une erreur (par exemple un 502 pendant un déploiement), le flux est recréé avec un délai croissant de 1 à 30 secondes. Un rafraîchissement toutes les 30 secondes complète le flux, car certaines disponibilités dépendent de l'heure (échéance de retour arrière).

### Nouvelle version

`/api/version` renvoie l'empreinte du `index.html` servi. `layout/NouvelleVersion.tsx` la compare périodiquement à celle chargée et propose de recharger après un déploiement. Il le propose aussi sur l'événement `vite:preloadError` : un fichier chargé à la demande (une page, le moteur ELK) a disparu du serveur après un déploiement.

### Tests de bout en bout

`npm run e2e` lance Playwright (`playwright.config.ts`, fichiers `e2e/*.e2e.ts`) sur une instance déjà démarrée en mode démonstration (`DEMO=true`) : `E2E_URL` donne son adresse (par défaut http://localhost:5173, le serveur de développement). `e2e/petit-ecran.e2e.ts` vérifie, sur une fenêtre de 1366 × 641, que le graphe de la vue d'une section garde au moins 480 px de haut et reste visible, avec la carte de l'étape, quand l'étape est ouverte en tiroir. Le navigateur Chromium de Playwright doit être installé (`npx playwright install chromium`).

### Charte visuelle

Le serveur sert la charte en vigueur dans `/charte.css` (lien `<link id="charte">` de `index.html`) : seulement les jetons qui diffèrent de `index.css`, sous `:root:root:not(.dark)` et `:root:root.dark` pour l'emporter sur eux. Sans charte personnalisée, la feuille ne contient que l'empreinte : l'application est exactement celle de `index.css`. `/api/version` renvoie aussi l'empreinte de la charte ; quand elle change, `charte/appliquer.ts` charge la nouvelle feuille à côté de l'ancienne et retire l'ancienne une fois chargée (aucun rechargement de la page, aucun clignement). L'onglet Charte visuelle de la console (`admin/CharteTab.tsx`) calcule son aperçu et ses contrôles de lisibilité avec `charte/charte.ts`, qui doit donner les mêmes résultats que `CharteVisuelle.cs` : les cas de `charte/vecteurs-charte.json` sont joués des deux côtés, et un test vérifie que `charte/defauts.ts` reprend `index.css` à l'identique.

## Style

- Couleurs, ombres et rayons sont des **jetons CSS** définis dans `src/index.css` (`--bg`, `--surface`, `--fg`, `--accent`…), redéfinis pour le thème sombre (`:root.dark`). La charte visuelle les remplace par `/charte.css` : un nouveau jeton qui dépend de la primaire, de la secondaire ou de la tertiaire doit aussi être calculé dans `charte/charte.ts` et `CharteVisuelle.cs`. Le menu latéral utilise ses propres jetons (`--nav-*`), la page de connexion les siens (`--connexion-*`). Les tons de statut (`ok`, `run`, `wait`, `danger`, `warn`, `rollback`…) ont chacun un fond, un texte, une bordure et une couleur pleine.
- Les classes Tailwind utilisent ces jetons (`bg-surface`, `text-fg-muted`). Pas de couleur en dur.
- Texte sur une couleur pleine : le jeton apparié (`bg-ok-solid text-ok-solid-fg`, `TON[ton].texteSolide`), jamais `text-white`. `--X-solid-fg` vaut blanc ou encre selon le contraste (`texteSur()` de `charte/charte.ts`, recalculé par la charte) ; `text-ok-text` et `text-warn-text` écrivent le vert et l'orange sur un fond clair. Couleur d'une personne : `stylePersonne(nom)` de `ui/Avatar.tsx`. Voile des dialogues : `bg-voile`.
- Tailles de texte : l'échelle Tailwind complétée de `text-2xs` (11 px, le minimum), `text-ui` (13 px) et `text-md` (15 px), définies dans `@theme` ; pas de `text-[…px]`.
- Étages (z-index) : `z-graphe` (graphe plein écran) < `z-panneau` (tiroir, espace d'une étape) < `z-dialogue` < `z-menu` (menus, info-bulles, ouverts au-dessus d'un dialogue) < `z-toast` (notifications, dialogue « session expirée ») ; pas de `z-[…]`. Les classes numériques de Tailwind (`z-1` à `z-40`) restent pour ordonner les éléments d'une page ou d'un composant (en-têtes collants, commandes du graphe), jamais au-dessus d'un étage.
- `charte/echelles.test.ts` échoue si `text-[…px]`, `text-white`, `bg-black/…`, une couleur nommée de Tailwind ou un `z-[…]` revient dans le code (hors aperçu et sélecteur de la charte, qui dessinent sur la couleur choisie).
- `ui/cn.ts` fusionne les classes (`clsx` + `tailwind-merge`, qui connaît les tailles et les étages ajoutés : `text-ui` n'efface pas `text-fg`).
- Le thème (clair, sombre, système) est géré par `theme.tsx`.
