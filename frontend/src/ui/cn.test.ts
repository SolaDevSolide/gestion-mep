import { cn } from './cn';

describe('cn', () => {
  it('distingue les tailles de l’échelle des couleurs de texte', () => {
    expect(cn('text-ui text-fg-muted')).toBe('text-ui text-fg-muted');
    expect(cn('text-2xs text-ok-solid-fg')).toBe('text-2xs text-ok-solid-fg');
    expect(cn('text-sm', 'text-ui')).toBe('text-ui');
    expect(cn('text-md', 'text-xs')).toBe('text-xs');
  });

  it('fusionne les étages de l’échelle des z-index', () => {
    expect(cn('z-10', 'z-dialogue')).toBe('z-dialogue');
    expect(cn('z-menu', 'z-toast')).toBe('z-toast');
  });
});
