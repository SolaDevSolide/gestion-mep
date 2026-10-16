import {
  Blocks,
  CircleArrowUp,
  Layers,
  Library,
  Loader2,
  LogOut,
  Mail,
  Megaphone,
  Moon,
  PanelLeftClose,
  PanelLeftOpen,
  Rocket,
  Activity,
  Search,
  ShieldCheck,
  ShieldUser,
  Sun,
  SunMoon,
  UserRound,
  Wrench,
} from 'lucide-react';
import { Suspense, useEffect, useState } from 'react';
import { NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom';
import { api } from '../api';
import { useAuth } from '../auth';
import { useAsync, useLocalStorageState } from '../hooks';
import { useTheme, type PreferenceTheme } from '../theme';
import { Avatar } from '../ui/Avatar';
import { BoutonIcone } from '../ui/Button';
import { cn } from '../ui/cn';
import { FiletErreur } from '../ui/FiletErreur';
import { Kbd } from '../ui/Kbd';
import { Menu, MenuContent, MenuItem, MenuLabel, MenuSeparator, MenuTrigger } from '../ui/Menu';
import { Tooltip } from '../ui/Tooltip';
import { PaletteCommandes } from './PaletteCommandes';

const THEMES: { id: PreferenceTheme; libelle: string; Icone: typeof Sun }[] = [
  { id: 'systeme', libelle: 'Système', Icone: SunMoon },
  { id: 'clair', libelle: 'Clair', Icone: Sun },
  { id: 'sombre', libelle: 'Sombre', Icone: Moon },
];

function Lien({
  to,
  end,
  icone: Icone,
  libelle,
  replie,
}: {
  to: string;
  end?: boolean;
  icone: typeof Rocket;
  libelle: string;
  replie: boolean;
}) {
  const lien = (
    <NavLink
      to={to}
      end={end}
      aria-label={replie ? libelle : undefined}
      className={({ isActive }) =>
        cn(
          'group flex h-9 items-center gap-3 rounded-lg px-2.5 text-sm font-medium transition-colors duration-150',
          isActive ? 'bg-accent-soft text-accent-text' : 'text-fg-muted hover:bg-surface-2 hover:text-fg',
          replie && 'justify-center px-0',
        )
      }
    >
      <Icone className="size-[18px] shrink-0" aria-hidden />
      {!replie && <span className="truncate">{libelle}</span>}
    </NavLink>
  );
  return replie ? (
    <Tooltip content={libelle} cote="right">
      {lien}
    </Tooltip>
  ) : (
    lien
  );
}

export function Shell() {
  const { utilisateur, deconnecter } = useAuth();
  const { preference, sombre, definir, basculer } = useTheme();
  const [replie, setReplie] = useLocalStorageState(
    'mep.menu.replie',
    typeof window !== 'undefined' && window.innerWidth < 1100,
  );
  const [palette, setPalette] = useState(false);
  const navigate = useNavigate();
  const location = useLocation();
  // Version, lien de signalement et, pour un administrateur, nouvelle version disponible.
  const { donnees: application } = useAsync(() => api.application(), [utilisateur?.id]);
  const miseAJour = application?.miseAJour?.disponible ? application.miseAJour : null;

  useEffect(() => {
    const fn = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
        e.preventDefault();
        setPalette((o) => !o);
      }
    };
    window.addEventListener('keydown', fn);
    return () => window.removeEventListener('keydown', fn);
  }, []);

  return (
    <div className="flex min-h-[calc(100vh-var(--bandeau-demo,0px))]">
      <aside
        className={cn(
          // Masqué à l'impression (rapport de fin, #61) : seule la page s'imprime.
          'sticky top-[var(--bandeau-demo,0px)] flex h-[calc(100vh-var(--bandeau-demo,0px))] shrink-0 flex-col border-r border-line bg-surface transition-[width] duration-200 print:hidden',
          replie ? 'w-[68px]' : 'w-64',
        )}
      >
        <div className={cn('flex h-14 items-center gap-2.5 px-4', replie && 'justify-center px-0')}>
          <span className="flex size-8 shrink-0 items-center justify-center rounded-lg bg-accent text-accent-fg shadow-sm">
            <Rocket className="size-[18px]" aria-hidden />
          </span>
          {!replie && <span className="text-[15px] font-semibold tracking-tight">Gestion MEP</span>}
        </div>

        <div className="px-3">
          <button
            type="button"
            onClick={() => setPalette(true)}
            aria-label="Ouvrir la palette de commandes"
            className={cn(
              'flex h-9 w-full items-center gap-2 rounded-lg border border-line bg-surface-2 px-2.5 text-sm text-fg-subtle transition-colors duration-150 hover:border-line-strong hover:text-fg',
              replie && 'justify-center px-0',
            )}
          >
            <Search className="size-4 shrink-0" aria-hidden />
            {!replie && (
              <>
                <span className="flex-1 text-left">Rechercher…</span>
                <Kbd>Ctrl K</Kbd>
              </>
            )}
          </button>
        </div>

        <nav aria-label="Navigation principale" className="mt-4 flex flex-1 flex-col gap-0.5 overflow-y-auto px-3">
          <Lien to="/" end icone={Rocket} libelle="Mises en production" replie={replie} />
          <Lien to="/suivi" icone={Activity} libelle="Suivi en direct" replie={replie} />
          <div
            className={cn(
              'mt-5 mb-1.5 px-2.5 text-[11px] font-semibold tracking-wider text-fg-subtle uppercase',
              replie && 'text-center px-0',
            )}
          >
            {replie ? '···' : 'Paramètres'}
          </div>
          <Lien to="/parametres/systemes" icone={Layers} libelle="Systèmes" replie={replie} />
          <Lien to="/parametres/resolutions" icone={Wrench} libelle="Résolutions types" replie={replie} />
          <Lien to="/parametres/etapes" icone={Blocks} libelle="Bibliothèque d’étapes" replie={replie} />
          <Lien to="/parametres/recettes" icone={Library} libelle="Bibliothèque de recettes" replie={replie} />
          <Lien to="/parametres/go-no-go" icone={ShieldCheck} libelle="Go / no-go" replie={replie} />
          <Lien to="/parametres/textes" icone={Mail} libelle="Modèles de texte" replie={replie} />
          {utilisateur?.estAdmin && (
            <>
              <div
                className={cn(
                  'mt-5 mb-1.5 px-2.5 text-[11px] font-semibold tracking-wider text-fg-subtle uppercase',
                  replie && 'text-center px-0',
                )}
              >
                {replie ? '···' : 'Administration'}
              </div>
              <Lien to="/admin" icone={ShieldUser} libelle="Console d’administration" replie={replie} />
              {miseAJour && (
                <Lien
                  to="/admin?onglet=version"
                  icone={CircleArrowUp}
                  libelle={`Mise à jour ${miseAJour.derniereVersion ?? ''} disponible`}
                  replie={replie}
                />
              )}
            </>
          )}
          <div
            className={cn(
              'mt-5 mb-1.5 px-2.5 text-[11px] font-semibold tracking-wider text-fg-subtle uppercase',
              replie && 'text-center px-0',
            )}
          >
            {replie ? '···' : 'Compte'}
          </div>
          <Lien to="/profil" icone={UserRound} libelle="Mon profil" replie={replie} />
        </nav>

        <div className="border-t border-line p-3">
          <div className={cn('flex items-center gap-1', replie && 'flex-col')}>
            <Menu>
              <MenuTrigger asChild>
                <button
                  type="button"
                  className={cn(
                    'flex min-w-0 flex-1 items-center gap-2.5 rounded-lg p-1.5 text-left transition-colors duration-150 hover:bg-surface-2',
                    replie && 'flex-none',
                  )}
                  aria-label="Menu utilisateur"
                >
                  <Avatar nom={utilisateur?.nomAffiche} />
                  {!replie && (
                    <span className="min-w-0">
                      <span className="block truncate text-sm font-medium">{utilisateur?.nomAffiche}</span>
                      <span className="block truncate text-xs text-fg-subtle">
                        {utilisateur?.role || (utilisateur?.estAdmin ? 'Administrateur' : 'Utilisateur')}
                      </span>
                    </span>
                  )}
                </button>
              </MenuTrigger>
              <MenuContent align="start" side="top">
                <MenuItem onSelect={() => void navigate('/profil')}>
                  <UserRound className="size-4" aria-hidden /> Mon profil
                </MenuItem>
                {application?.lienSignalement && (
                  <MenuItem
                    onSelect={() => window.open(application.lienSignalement ?? '', '_blank', 'noopener,noreferrer')}
                  >
                    <Megaphone className="size-4" aria-hidden /> Signaler un problème ou une idée
                  </MenuItem>
                )}
                <MenuSeparator />
                <MenuLabel>Thème</MenuLabel>
                {THEMES.map(({ id, libelle, Icone }) => (
                  <MenuItem key={id} onSelect={() => definir(id)}>
                    <Icone className="size-4" aria-hidden />
                    <span className="flex-1">{libelle}</span>
                    {preference === id && <span className="size-1.5 rounded-full bg-accent" aria-label="Sélectionné" />}
                  </MenuItem>
                ))}
                <MenuSeparator />
                <MenuItem onSelect={() => void deconnecter()}>
                  <LogOut className="size-4" aria-hidden /> Se déconnecter
                </MenuItem>
                {application && (
                  <>
                    <MenuSeparator />
                    <MenuLabel>Version {application.version}</MenuLabel>
                  </>
                )}
              </MenuContent>
            </Menu>
            <Tooltip content={sombre ? 'Passer en mode clair' : 'Passer en mode sombre'}>
              <BoutonIcone label={sombre ? 'Passer en mode clair' : 'Passer en mode sombre'} onClick={basculer}>
                {sombre ? <Sun className="size-4" aria-hidden /> : <Moon className="size-4" aria-hidden />}
              </BoutonIcone>
            </Tooltip>
            <Tooltip content="Se déconnecter">
              <BoutonIcone label="Se déconnecter" onClick={() => void deconnecter()}>
                <LogOut className="size-4" aria-hidden />
              </BoutonIcone>
            </Tooltip>
          </div>
          <button
            type="button"
            onClick={() => setReplie(!replie)}
            className="mt-2 flex h-8 w-full items-center justify-center gap-2 rounded-lg text-xs text-fg-subtle transition-colors duration-150 hover:bg-surface-2 hover:text-fg"
            aria-label={replie ? 'Déplier le menu' : 'Replier le menu'}
          >
            {replie ? (
              <PanelLeftOpen className="size-4" aria-hidden />
            ) : (
              <>
                <PanelLeftClose className="size-4" aria-hidden /> Replier
              </>
            )}
          </button>
        </div>
      </aside>

      <div className="flex min-w-0 flex-1 flex-col">
        {/* Pages chargées à la demande (voir App) : le menu reste affiché pendant le chargement, et après une erreur
            d'affichage (FiletErreur), réessayée au changement de page ou d'onglet. */}
        <FiletErreur cle={location.pathname + location.search}>
          <Suspense
            fallback={
              <output className="flex flex-1 items-center justify-center py-24 text-fg-subtle">
                <Loader2 className="mr-2 size-5 animate-spin" aria-hidden /> Chargement…
              </output>
            }
          >
            <Outlet />
          </Suspense>
        </FiletErreur>
      </div>
      <PaletteCommandes open={palette} onOpenChange={setPalette} />
    </div>
  );
}
