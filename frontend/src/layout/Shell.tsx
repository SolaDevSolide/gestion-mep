import { LogOut, Moon, PanelLeftClose, PanelLeftOpen, Rocket, Search, Sun, SunMoon, Wrench, Layers } from 'lucide-react';
import { useEffect, useState } from 'react';
import { NavLink, Outlet } from 'react-router-dom';
import { useAuth } from '../auth';
import { useLocalStorageState } from '../hooks';
import { useTheme, type PreferenceTheme } from '../theme';
import { Avatar } from '../ui/Avatar';
import { BoutonIcone } from '../ui/Button';
import { cn } from '../ui/cn';
import { Kbd } from '../ui/Kbd';
import { Menu, MenuContent, MenuItem, MenuLabel, MenuSeparator, MenuTrigger } from '../ui/Menu';
import { Tooltip } from '../ui/Tooltip';
import { PaletteCommandes } from './PaletteCommandes';

const THEMES: { id: PreferenceTheme; libelle: string; Icone: typeof Sun }[] = [
  { id: 'systeme', libelle: 'Système', Icone: SunMoon },
  { id: 'clair', libelle: 'Clair', Icone: Sun },
  { id: 'sombre', libelle: 'Sombre', Icone: Moon },
];

function Lien({ to, end, icone: Icone, libelle, replie }: { to: string; end?: boolean; icone: typeof Rocket; libelle: string; replie: boolean }) {
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
  return replie ? <Tooltip content={libelle} cote="right">{lien}</Tooltip> : lien;
}

export function Shell() {
  const { utilisateur, deconnecter } = useAuth();
  const { preference, sombre, definir, basculer } = useTheme();
  const [replie, setReplie] = useLocalStorageState('mep.menu.replie', typeof window !== 'undefined' && window.innerWidth < 1100);
  const [palette, setPalette] = useState(false);

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
    <div className="flex min-h-screen">
      <aside
        className={cn(
          'sticky top-0 flex h-screen shrink-0 flex-col border-r border-line bg-surface transition-[width] duration-200',
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
            {!replie && (<><span className="flex-1 text-left">Rechercher…</span><Kbd>Ctrl K</Kbd></>)}
          </button>
        </div>

        <nav aria-label="Navigation principale" className="mt-4 flex flex-1 flex-col gap-0.5 overflow-y-auto px-3">
          <Lien to="/" end icone={Rocket} libelle="Mises en production" replie={replie} />
          <div className={cn('mt-5 mb-1.5 px-2.5 text-[11px] font-semibold tracking-wider text-fg-subtle uppercase', replie && 'text-center px-0')}>
            {replie ? '···' : 'Paramètres'}
          </div>
          <Lien to="/parametres/systemes" icone={Layers} libelle="Systèmes" replie={replie} />
          <Lien to="/parametres/resolutions" icone={Wrench} libelle="Résolutions types" replie={replie} />
        </nav>

        <div className="border-t border-line p-3">
          <div className={cn('flex items-center gap-1', replie && 'flex-col')}>
            <Menu>
              <MenuTrigger asChild>
                <button
                  type="button"
                  className={cn('flex min-w-0 flex-1 items-center gap-2.5 rounded-lg p-1.5 text-left transition-colors duration-150 hover:bg-surface-2', replie && 'flex-none')}
                  aria-label="Menu utilisateur"
                >
                  <Avatar nom={utilisateur?.nomAffiche} />
                  {!replie && (
                    <span className="min-w-0">
                      <span className="block truncate text-sm font-medium">{utilisateur?.nomAffiche}</span>
                      <span className="block truncate text-xs text-fg-subtle">{utilisateur?.estAdmin ? 'Administrateur' : 'Utilisateur'}</span>
                    </span>
                  )}
                </button>
              </MenuTrigger>
              <MenuContent align="start" side="top">
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
              </MenuContent>
            </Menu>
            <Tooltip content={sombre ? 'Passer en mode clair' : 'Passer en mode sombre'}>
              <BoutonIcone label={sombre ? 'Passer en mode clair' : 'Passer en mode sombre'} onClick={basculer}>
                {sombre ? <Sun className="size-4" aria-hidden /> : <Moon className="size-4" aria-hidden />}
              </BoutonIcone>
            </Tooltip>
            <Tooltip content="Se déconnecter">
              <BoutonIcone label="Se déconnecter" onClick={() => void deconnecter()}><LogOut className="size-4" aria-hidden /></BoutonIcone>
            </Tooltip>
          </div>
          <button
            type="button"
            onClick={() => setReplie(!replie)}
            className="mt-2 flex h-8 w-full items-center justify-center gap-2 rounded-lg text-xs text-fg-subtle transition-colors duration-150 hover:bg-surface-2 hover:text-fg"
            aria-label={replie ? 'Déplier le menu' : 'Replier le menu'}
          >
            {replie ? <PanelLeftOpen className="size-4" aria-hidden /> : <><PanelLeftClose className="size-4" aria-hidden /> Replier</>}
          </button>
        </div>
      </aside>

      <div className="flex min-w-0 flex-1 flex-col">
        <Outlet />
      </div>
      <PaletteCommandes open={palette} onOpenChange={setPalette} />
    </div>
  );
}

