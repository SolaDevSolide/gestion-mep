import {
  Blocks,
  ClipboardCheck,
  History,
  ClipboardList,
  Layers,
  Library,
  ListChecks,
  Mail,
  Moon,
  NotebookText,
  Play,
  Rocket,
  Search,
  ShieldCheck,
  ShieldUser,
  Sun,
  SunMoon,
  Undo2,
  UserRound,
  UserSearch,
  Wrench,
} from 'lucide-react';
import { Command } from 'cmdk';
import { Dialog as D } from 'radix-ui';
import { useEffect, useMemo, useState, type ReactNode } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { api } from '../api';
import { useAuth } from '../auth';
import { useAsync } from '../hooks';
import { useReferentiel } from '../referentiels';
import { LIBELLE_PHASE } from '../format';
import { useTheme } from '../theme';
import { lienHistoriqueSysteme } from '../mep/SystemeCliquable';
import { personnesDeRecherche } from '../personnes';
import { ecrireFiltre, FILTRE_VIDE } from '../rechercheMeps';
import { ErreurChargement } from '../ui/ErreurChargement';
import { Kbd } from '../ui/Kbd';
import { EnChargement, Squelette } from '../ui/Squelette';

const norm = (s: string) => s.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();
const filtre = (_v: string, recherche: string, mots?: string[]) =>
  norm((mots ?? []).join(' ')).includes(norm(recherche)) ? 1 : 0;

function Entree({
  icone,
  children,
  mots,
  onSelect,
  aide,
}: {
  icone: ReactNode;
  children: ReactNode;
  mots: string[];
  onSelect: () => void;
  aide?: string;
}) {
  return (
    <Command.Item
      value={mots.join('|')}
      keywords={mots}
      onSelect={onSelect}
      className="flex cursor-pointer items-center gap-3 rounded-lg px-3 py-2 text-sm text-fg data-[selected=true]:bg-surface-2"
    >
      <span className="text-fg-subtle" aria-hidden>
        {icone}
      </span>
      <span className="flex-1 truncate">{children}</span>
      {aide && <span className="text-xs text-fg-subtle">{aide}</span>}
    </Command.Item>
  );
}

/** Groupe pas encore lu (lignes squelettes) ou en erreur (avec « Réessayer ») ; rien une fois lu. */
function EtatGroupe({
  chargement,
  erreur,
  titre,
  onReessayer,
  enCours,
}: {
  chargement: boolean;
  erreur: unknown;
  titre: string;
  onReessayer: () => void;
  enCours: boolean;
}) {
  if (erreur != null)
    return (
      <ErreurChargement
        variante="ligne"
        className="mx-1 my-1"
        erreur={erreur}
        titre={titre}
        onReessayer={onReessayer}
        enCours={enCours}
      />
    );
  if (!chargement) return null;
  return (
    <EnChargement libelle={`${titre.replace(/ non chargé.*$/, '')} : chargement…`} className="grid gap-2 px-3 py-2">
      <Squelette className="h-4 w-3/5" />
      <Squelette className="h-4 w-2/5" />
    </EnChargement>
  );
}

const GROUPE =
  'px-1 py-1 [&_[cmdk-group-heading]]:px-3 [&_[cmdk-group-heading]]:py-1.5 [&_[cmdk-group-heading]]:text-[11px] [&_[cmdk-group-heading]]:font-semibold [&_[cmdk-group-heading]]:tracking-wider [&_[cmdk-group-heading]]:text-fg-subtle [&_[cmdk-group-heading]]:uppercase';

/**
 * Palette de commandes Ctrl+K : MEP, onglets, paramètres, thème. Dès qu'on tape (#59) : rechercher le texte dans la liste
 * des MEP, ouvrir l'historique d'un système du catalogue ou les MEP d'une personne.
 */
export function PaletteCommandes({ open, onOpenChange }: { open: boolean; onOpenChange: (o: boolean) => void }) {
  const navigate = useNavigate();
  const { pathname } = useLocation();
  const { definir, sombre } = useTheme();
  const { utilisateur } = useAuth();
  // Relues à chaque ouverture ; tant qu'un groupe n'est pas là, des lignes squelettes ; en erreur, le groupe le dit
  // (les autres restent utilisables).
  const mepsLues = useAsync(() => (open ? api.meps() : Promise.resolve(null)), [open]);
  const catalogue = useReferentiel('catalogue', open);
  const comptes = useReferentiel('comptes', open);
  const meps = mepsLues.donnees ?? [];
  const systemes = useMemo(() => (catalogue.donnees ?? []).filter((x) => x.actif), [catalogue.donnees]);
  const personnes = useMemo(() => personnesDeRecherche(comptes.donnees ?? []), [comptes.donnees]);
  const [saisie, setSaisie] = useState('');

  useEffect(() => {
    if (open) setSaisie('');
  }, [open]);
  const q = saisie.trim();
  const mepsAttendues = mepsLues.chargement || mepsLues.donnees === null;
  const toutLu = !mepsAttendues && !!catalogue.donnees && !!comptes.donnees;

  const mepCourante = /^\/meps\/([^/]+)/.exec(pathname)?.[1];
  const aller = (chemin: string) => {
    onOpenChange(false);
    void navigate(chemin);
  };

  return (
    <D.Root open={open} onOpenChange={onOpenChange}>
      <D.Portal>
        <D.Overlay className="fixed inset-0 z-[90] bg-voile backdrop-blur-[2px] data-[state=open]:animate-fade-in" />
        <D.Content
          className="fixed top-[14vh] left-1/2 z-[90] w-[min(40rem,calc(100vw-2rem))] -translate-x-1/2 overflow-hidden rounded-2xl border border-line bg-surface shadow-lg outline-none data-[state=open]:animate-fade-in"
          aria-describedby={undefined}
        >
          <D.Title className="sr-only">Palette de commandes</D.Title>
          <Command filter={filtre} label="Palette de commandes">
            <Command.Input
              autoFocus
              value={saisie}
              onValueChange={setSaisie}
              placeholder="Aller à une MEP, un système, une personne, un réglage…"
              className="h-12 w-full border-b border-line bg-transparent px-4 text-[15px] text-fg outline-none placeholder:text-fg-subtle"
            />
            <Command.List className="max-h-[50vh] overflow-y-auto p-1.5">
              {/* « Aucun résultat » seulement une fois toutes les listes lues. */}
              {toutLu && (
                <Command.Empty className="px-4 py-8 text-center text-sm text-fg-subtle">Aucun résultat.</Command.Empty>
              )}
              {q && (
                <Command.Group heading="Recherche" className={GROUPE}>
                  {/* Les mots reprennent la saisie : l'entrée reste toujours proposée, en tête. */}
                  <Entree
                    icone={<Search className="size-4" />}
                    mots={[q]}
                    aide="Liste des MEP"
                    onSelect={() => aller(`/?${ecrireFiltre({ ...FILTRE_VIDE, recherche: q }).toString()}`)}
                  >
                    Rechercher « {q} » dans les MEP
                  </Entree>
                </Command.Group>
              )}
              {mepCourante && (
                <Command.Group heading="Cette MEP" className={GROUPE}>
                  <Entree
                    icone={<ClipboardList className="size-4" />}
                    mots={['onglet', 'préparation']}
                    onSelect={() => aller(`/meps/${mepCourante}?onglet=preparation`)}
                  >
                    Onglet Préparation
                  </Entree>
                  <Entree
                    icone={<ListChecks className="size-4" />}
                    mots={['onglet', 'étapes', 'plan']}
                    onSelect={() => aller(`/meps/${mepCourante}?onglet=plan&plan=etapes`)}
                  >
                    Plan › Étapes
                  </Entree>
                  <Entree
                    icone={<Play className="size-4" />}
                    mots={['onglet', 'exécution', 'execution']}
                    onSelect={() => aller(`/meps/${mepCourante}?onglet=execution`)}
                  >
                    Onglet Exécution
                  </Entree>
                  <Entree
                    icone={<Undo2 className="size-4" />}
                    mots={['onglet', 'retour arrière', 'repli']}
                    onSelect={() => aller(`/meps/${mepCourante}?onglet=plan&plan=retour`)}
                  >
                    Plan › Retour arrière
                  </Entree>
                  <Entree
                    icone={<ClipboardCheck className="size-4" />}
                    mots={['onglet', 'recette', 'tests']}
                    onSelect={() => aller(`/meps/${mepCourante}?onglet=plan&plan=recette`)}
                  >
                    Plan › Recette
                  </Entree>
                  <Entree
                    icone={<NotebookText className="size-4" />}
                    mots={['onglet', 'journal']}
                    onSelect={() => aller(`/meps/${mepCourante}?onglet=journal`)}
                  >
                    Onglet Journal
                  </Entree>
                </Command.Group>
              )}
              <Command.Group
                heading="Mises en production"
                className={GROUPE}
                forceMount={mepsLues.echec !== null || mepsAttendues}
              >
                <EtatGroupe
                  chargement={mepsAttendues && mepsLues.echec === null}
                  erreur={mepsLues.echec}
                  titre="MEP non chargées"
                  onReessayer={mepsLues.recharger}
                  enCours={mepsLues.chargement}
                />
                <Entree
                  icone={<Rocket className="size-4" />}
                  mots={['liste', 'toutes les mep', 'accueil']}
                  onSelect={() => aller('/')}
                >
                  Toutes les mises en production
                </Entree>
                {meps.map((m) => (
                  <Entree
                    key={m.id}
                    icone={<Rocket className="size-4" />}
                    mots={[m.outil, m.versionCible, m.objet, m.pilote ?? '']}
                    aide={LIBELLE_PHASE[m.phase]}
                    onSelect={() => aller(`/meps/${m.id}`)}
                  >
                    {m.outil}
                    {m.versionCible.trim() && ` → ${m.versionCible}`}
                  </Entree>
                ))}
              </Command.Group>
              {q && !catalogue.donnees && (
                <Command.Group heading="Systèmes" className={GROUPE} forceMount>
                  <EtatGroupe
                    chargement={catalogue.erreur == null}
                    erreur={catalogue.erreur}
                    titre="Systèmes non chargés"
                    onReessayer={catalogue.recharger}
                    enCours={catalogue.enCours}
                  />
                </Command.Group>
              )}
              {q && systemes.length > 0 && (
                <Command.Group heading="Systèmes" className={GROUPE}>
                  {systemes.map((s) => (
                    <Entree
                      key={s.id}
                      icone={<History className="size-4" />}
                      mots={[s.nom, s.description, s.cible, 'système', 'historique']}
                      aide="Historique des MEP"
                      onSelect={() => aller(lienHistoriqueSysteme(s.id))}
                    >
                      {s.nom}
                    </Entree>
                  ))}
                </Command.Group>
              )}
              {q && !comptes.donnees && (
                <Command.Group heading="Personnes" className={GROUPE} forceMount>
                  <EtatGroupe
                    chargement={comptes.erreur == null}
                    erreur={comptes.erreur}
                    titre="Personnes non chargées"
                    onReessayer={comptes.recharger}
                    enCours={comptes.enCours}
                  />
                </Command.Group>
              )}
              {q && personnes.length > 0 && (
                <Command.Group heading="Personnes" className={GROUPE}>
                  {personnes.map((p) => (
                    <Entree
                      key={p.id}
                      icone={<UserSearch className="size-4" />}
                      mots={[p.nom, p.detail, 'personne']}
                      aide="Ses MEP"
                      onSelect={() => aller(`/?${ecrireFiltre({ ...FILTRE_VIDE, personne: p.id }).toString()}`)}
                    >
                      {p.nom}
                    </Entree>
                  ))}
                </Command.Group>
              )}
              <Command.Group heading="Paramètres" className={GROUPE}>
                <Entree
                  icone={<Layers className="size-4" />}
                  mots={['paramètres', 'systèmes', 'catalogue']}
                  onSelect={() => aller('/parametres/systemes')}
                >
                  Paramètres : Systèmes
                </Entree>
                <Entree
                  icone={<Wrench className="size-4" />}
                  mots={['paramètres', 'résolutions', 'types']}
                  onSelect={() => aller('/parametres/resolutions')}
                >
                  Paramètres : Résolutions types
                </Entree>
                <Entree
                  icone={<Blocks className="size-4" />}
                  mots={['paramètres', 'bibliothèque', 'étapes', 'modèles']}
                  onSelect={() => aller('/parametres/etapes')}
                >
                  Paramètres : Bibliothèque d’étapes
                </Entree>
                <Entree
                  icone={<Library className="size-4" />}
                  mots={['paramètres', 'bibliothèque', 'recettes', 'modèles', 'fiches']}
                  onSelect={() => aller('/parametres/recettes')}
                >
                  Paramètres : Bibliothèque de recettes
                </Entree>
                <Entree
                  icone={<ShieldCheck className="size-4" />}
                  mots={['paramètres', 'go', 'no-go', 'critères', 'approbations']}
                  onSelect={() => aller('/parametres/go-no-go')}
                >
                  Paramètres : Go / no-go et approbations
                </Entree>
                <Entree
                  icone={<Mail className="size-4" />}
                  mots={['paramètres', 'modèles', 'textes', 'courriels', 'mails', 'annonce', 'communication']}
                  onSelect={() => aller('/parametres/textes')}
                >
                  Paramètres : Modèles de texte
                </Entree>
              </Command.Group>
              {utilisateur?.estAdmin && (
                <Command.Group heading="Administration" className={GROUPE}>
                  <Entree
                    icone={<ShieldUser className="size-4" />}
                    mots={['administration', 'admin', 'console', 'comptes', 'utilisateurs', 'journal']}
                    onSelect={() => aller('/admin')}
                  >
                    Console d’administration
                  </Entree>
                </Command.Group>
              )}
              <Command.Group heading="Compte" className={GROUPE}>
                <Entree
                  icone={<UserRound className="size-4" />}
                  mots={['profil', 'compte', 'expertises', 'rôle', 'contact']}
                  onSelect={() => aller('/profil')}
                >
                  Mon profil
                </Entree>
              </Command.Group>
              <Command.Group heading="Apparence" className={GROUPE}>
                <Entree
                  icone={sombre ? <Sun className="size-4" /> : <Moon className="size-4" />}
                  mots={['thème', 'theme', 'sombre', 'clair']}
                  onSelect={() => {
                    definir(sombre ? 'clair' : 'sombre');
                    onOpenChange(false);
                  }}
                >
                  {sombre ? 'Passer en mode clair' : 'Passer en mode sombre'}
                </Entree>
                <Entree
                  icone={<SunMoon className="size-4" />}
                  mots={['thème', 'système']}
                  onSelect={() => {
                    definir('systeme');
                    onOpenChange(false);
                  }}
                >
                  Thème : suivre le système
                </Entree>
              </Command.Group>
            </Command.List>
            <div className="flex items-center justify-end gap-3 border-t border-line px-4 py-2 text-xs text-fg-subtle">
              <span className="inline-flex items-center gap-1">
                <Kbd>↑</Kbd>
                <Kbd>↓</Kbd> naviguer
              </span>
              <span className="inline-flex items-center gap-1">
                <Kbd>↵</Kbd> ouvrir
              </span>
              <span className="inline-flex items-center gap-1">
                <Kbd>Échap</Kbd> fermer
              </span>
            </div>
          </Command>
        </D.Content>
      </D.Portal>
    </D.Root>
  );
}
