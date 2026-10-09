import type { ReactNode } from 'react';

export function Champ({ label, children, large = false }: { label: string; children: ReactNode; large?: boolean }) {
  return (
    <label className={`champ${large ? ' champ-large' : ''}`}>
      <span>{label}</span>
      {children}
    </label>
  );
}

export function Section({ numero, titre, children }: { numero: number; titre: string; children: ReactNode }) {
  return (
    <section className="carte section">
      <h2><span className="section-num">{numero}</span> {titre}</h2>
      {children}
    </section>
  );
}

export function Case({ label, checked, onChange, disabled }: { label: string; checked: boolean; onChange: (v: boolean) => void; disabled?: boolean }) {
  return (
    <label className="case">
      <input type="checkbox" checked={checked} disabled={disabled} onChange={(e) => onChange(e.target.checked)} />
      <span>{label}</span>
    </label>
  );
}

export const vide = (v: string): string | null => (v === '' ? null : v);
