import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';

export type PreferenceTheme = 'systeme' | 'clair' | 'sombre';
interface ThemeValue { preference: PreferenceTheme; sombre: boolean; definir: (p: PreferenceTheme) => void; basculer: () => void }

const CLE = 'mep.theme';
const ThemeContext = createContext<ThemeValue | null>(null);

function lire(): PreferenceTheme {
  try {
    const v = window.localStorage.getItem(CLE);
    return v === 'clair' || v === 'sombre' ? v : 'systeme';
  } catch {
    return 'systeme';
  }
}

const systemeSombre = () => typeof window.matchMedia === 'function' && window.matchMedia('(prefers-color-scheme: dark)').matches;

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [preference, setPreference] = useState<PreferenceTheme>(lire);
  const [systeme, setSysteme] = useState(systemeSombre);

  useEffect(() => {
    if (typeof window.matchMedia !== 'function') return;
    const mq = window.matchMedia('(prefers-color-scheme: dark)');
    const fn = () => setSysteme(mq.matches);
    mq.addEventListener('change', fn);
    return () => mq.removeEventListener('change', fn);
  }, []);

  const sombre = preference === 'sombre' || (preference === 'systeme' && systeme);
  useEffect(() => {
    document.documentElement.classList.toggle('dark', sombre);
  }, [sombre]);

  const definir = useCallback((p: PreferenceTheme) => {
    setPreference(p);
    try { window.localStorage.setItem(CLE, p); } catch { /* stockage indisponible */ }
  }, []);
  const basculer = useCallback(() => definir(sombre ? 'clair' : 'sombre'), [definir, sombre]);

  const value = useMemo(() => ({ preference, sombre, definir, basculer }), [preference, sombre, definir, basculer]);
  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>;
}

export function useTheme(): ThemeValue {
  const ctx = useContext(ThemeContext);
  if (!ctx) throw new Error('useTheme hors ThemeProvider');
  return ctx;
}
