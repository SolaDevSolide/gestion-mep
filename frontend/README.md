# Front-end

React 19, TypeScript 7 (strict), Vite, Tailwind CSS 4, primitives [Radix](https://www.radix-ui.com/) et icônes Lucide. En production, le résultat de `npm run build` est servi par l'API .NET (dossier `wwwroot` de l'image).

## Commandes

| Commande                                  | Rôle                                                                                              |
| ----------------------------------------- | ------------------------------------------------------------------------------------------------- |
| `npm run dev`                             | Serveur de développement sur http://localhost:5173 ; `/api` est relayé vers http://localhost:5080 |
| `npm test`                                | Tests Vitest (jsdom)                                                                              |
| `npm run lint`                            | Oxlint avec analyse des types                                                                     |
| `npm run format` / `npm run format:check` | Prettier                                                                                          |
| `npm run typecheck`                       | `tsc --noEmit`                                                                                    |
| `npm run build`                           | Typage puis construction dans `dist/`                                                             |
| `npm run api:types`                       | Régénère `src/api/schema.d.ts` depuis `docs/openapi.json`                                         |

## Organisation

| Dossier               | Contenu                                                                                                                  |
| --------------------- | ------------------------------------------------------------------------------------------------------------------------ |
| `src/pages`           | Pages : connexion, liste des MEP, une MEP, profil                                                                        |
| `src/mep`             | Onglets et dialogues d'une MEP (préparation, plan : étapes, retour arrière, recette ; exécution, journal ; `onglets.ts`) |
| `src/mep/graphe`      | Éditeur et vue d'exécution du graphe des étapes                                                                          |
| `src/mep/preparation` | Sections de l'onglet Préparation                                                                                         |
| `src/parametres`      | Paramètres globaux : catalogue des systèmes, résolutions types, bibliothèque de recettes, critères go / no-go            |
| `src/layout`          | Cadre de l'application, palette de commandes, bandeau « nouvelle version »                                               |
| `src/ui`              | Composants génériques (boutons, dialogues, menus, champs, toasts…)                                                       |
| `src/test`            | Configuration Vitest et fabrique de données de test                                                                      |

Logique pure, testée sans DOM :

| Module            | Rôle                                                                                                                                                        |
| ----------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `api.ts`          | Client HTTP : une fonction par route, erreurs converties en `ApiError` (statut + message français), en-tête anti-CSRF `X-Requested-With` sur chaque requête |
| `types.ts`        | Noms courts des types de l'API (`Mep`, `Etape`…), alias des types générés                                                                                   |
| `api/schema.d.ts` | Types générés depuis `docs/openapi.json` par `npm run api:types` ; ne jamais modifier à la main                                                             |
| `domaine.ts`      | Règles d'affichage : plan actif, étapes à vous, bloqueurs, progression, bilan de recette                                                                    |
| `graphe.ts`       | Lignes du graphe, liens, tracé des jonctions                                                                                                                |
| `personnes.ts`    | Comptes reliés regroupés par personne, équipes d'une personne, filtres par équipe et recherche des comptes et des MEP                                       |
| `recherche.ts`    | Recherche insensible aux accents, score et surlignage                                                                                                       |
| `blocs.ts`        | Découpage des instructions en blocs Texte / Code et raccourcis de mise en forme markdown                                                                    |
| `format.ts`       | Libellés, dates, durées, comptes à rebours                                                                                                                  |

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
- Une MEP relue (modification d'un collègue) s'affiche aussitôt : les formulaires gardent leur brouillon et `AvisConflit` signale un changement de leur section.

### Temps réel

`MepPage` ouvre un flux SSE (`/api/meps/{id}/flux?session=…`, une session par onglet). Chaque événement `maj` porte la nouvelle version de la MEP ; si elle est plus récente que celle affichée, la MEP est rechargée. Chaque événement `presence` donne les personnes qui ont la MEP ouverte et leurs éléments en cours (`presence.tsx`) : « Sur cette MEP : » dans l'en-tête, encadré « Prénom modifie » sur l'élément (`MarquePresence`). Un composant déclare son élément par `usePresenceElement(cle, actif)` ou en s'enveloppant dans `ZonePresence` (focus ou saisie en cours) ; `MepPage` regroupe ces déclarations, les envoie (`PUT /api/meps/{id}/presence`) et les redéclare à chaque ouverture du flux. Après une erreur (par exemple un 502 pendant un déploiement), le flux est recréé avec un délai croissant de 1 à 30 secondes. Un rafraîchissement toutes les 30 secondes complète le flux, car certaines disponibilités dépendent de l'heure (échéance de retour arrière).

### Nouvelle version

`/api/version` renvoie l'empreinte du `index.html` servi. `layout/NouvelleVersion.tsx` la compare périodiquement à celle chargée et propose de recharger après un déploiement.

## Style

- Couleurs, ombres et rayons sont des **jetons CSS** définis dans `src/index.css` (`--bg`, `--surface`, `--fg`, `--accent`…), redéfinis pour le thème sombre (`:root.dark`). Les tons de statut (`ok`, `run`, `wait`, `danger`, `warn`, `rollback`…) ont chacun un fond, un texte, une bordure et une couleur pleine.
- Les classes Tailwind utilisent ces jetons (`bg-surface`, `text-fg-muted`). Pas de couleur en dur.
- `ui/cn.ts` fusionne les classes (`clsx` + `tailwind-merge`).
- Le thème (clair, sombre, système) est géré par `theme.tsx`.
