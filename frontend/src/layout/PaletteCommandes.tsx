import {
  ClipboardCheck,
  ClipboardList,
  Layers,
  Library,
  ListChecks,
  Moon,
  NotebookText,
  Play,
  Rocket,
  ShieldCheck,
  ShieldUser,
  Sun,
  SunMoon,
  Undo2,
  UserRound,
  Wrench,
} from 'lucide-react';
import { Command } from 'cmdk';
import { Dialog as D } from 'radix-ui';
import { useEffect, useState, type ReactNode } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { api } from '../api';
import { useAuth } from '../auth';
import { LIBELLE_PHASE } from '../format';
import { useTheme } from '../theme';
import type { MepResume } from '../types';
import { Kbd } from '../ui/Kbd';

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

const GROUPE =
  'px-1 py-1 [&_[cmdk-group-heading]]:px-3 [&_[cmdk-group-heading]]:py-1.5 [&_[cmdk-group-heading]]:text-[11px] [&_[cmdk-group-heading]]:font-semibold [&_[cmdk-group-heading]]:tracking-wider [&_[cmdk-group-heading]]:text-fg-subtle [&_[cmdk-group-heading]]:uppercase';

/** Palette de commandes Ctrl+K : MEP, onglets, paramètres, thème. */
export function PaletteCommandes({ open, onOpenChange }: { open: boolean; onOpenChange: (o: boolean) => void }) {
  const navigate = useNavigate();
  const { pathname } = useLocation();
  const { definir, sombre } = useTheme();
  const { utilisateur } = useAuth();
  const [meps, setMeps] = useState<MepResume[]>([]);

  useEffect(() => {
    if (!open) return;
    api
      .meps()
      .then(setMeps)
      .catch(() => setMeps([]));
  }, [open]);

  const mepCourante = /^\/meps\/([^/]+)/.exec(pathname)?.[1];
  const aller = (chemin: string) => {
    onOpenChange(false);
    void navigate(chemin);
  };

  return (
    <D.Root open={open} onOpenChange={onOpenChange}>
      <D.Portal>
        <D.Overlay className="fixed inset-0 z-[90] bg-black/50 backdrop-blur-[2px] data-[state=open]:animate-fade-in" />
        <D.Content
          className="fixed top-[14vh] left-1/2 z-[90] w-[min(40rem,calc(100vw-2rem))] -translate-x-1/2 overflow-hidden rounded-2xl border border-line bg-surface shadow-lg outline-none data-[state=open]:animate-fade-in"
          aria-describedby={undefined}
        >
          <D.Title className="sr-only">Palette de commandes</D.Title>
          <Command filter={filtre} label="Palette de commandes">
            <Command.Input
              autoFocus
              placeholder="Aller à une MEP, un onglet, un réglage…"
              className="h-12 w-full border-b border-line bg-transparent px-4 text-[15px] text-fg outline-none placeholder:text-fg-subtle"
            />
            <Command.List className="max-h-[50vh] overflow-y-auto p-1.5">
              <Command.Empty className="px-4 py-8 text-center text-sm text-fg-subtle">Aucun résultat.</Command.Empty>
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
              <Command.Group heading="Mises en production" className={GROUPE}>
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
