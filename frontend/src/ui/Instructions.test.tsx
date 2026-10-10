import { rendreMarkdown } from './Instructions';

const rendu = (source: string) => {
  const div = document.createElement('div');
  div.innerHTML = rendreMarkdown(source);
  return div;
};

describe('rendreMarkdown', () => {
  it('garde le markdown courant : titres, gras, listes, code, tableaux, liens', () => {
    const div = rendu(
      '# Titre\n\n**gras** et `code`\n\n- un\n- deux\n\n| a | b |\n|---|---|\n| 1 | 2 |\n\n[doc](https://exemple.fr)',
    );
    expect(div.querySelector('h1')?.textContent).toBe('Titre');
    expect(div.querySelector('strong')?.textContent).toBe('gras');
    expect(div.querySelectorAll('li')).toHaveLength(2);
    expect(div.querySelector('td')?.textContent).toBe('1');
    expect(div.querySelector('a')?.getAttribute('href')).toBe('https://exemple.fr');
  });

  it('ajoute rel="noopener noreferrer" aux liens', () => {
    expect(rendu('[x](https://exemple.fr)').querySelector('a')?.getAttribute('rel')).toBe('noopener noreferrer');
  });

  it('garde les cases à cocher des listes de tâches, en lecture seule', () => {
    const cases = rendu('- [x] fait\n- [ ] à faire').querySelectorAll('input');
    expect(cases).toHaveLength(2);
    cases.forEach((c) => {
      expect(c.type).toBe('checkbox');
      expect(c.disabled).toBe(true);
    });
  });

  it('retire style, class, formulaires et champs de saisie', () => {
    const div = rendu(
      '<a href="https://x.fr" style="position:fixed;inset:0" class="z">lien</a>\n\n<form action="https://x.fr"><input type="password" name="mdp"><button>OK</button></form>',
    );
    expect(div.querySelector('[style]')).toBeNull();
    expect(div.querySelector('[class]')).toBeNull();
    expect(div.querySelector('form')).toBeNull();
    expect(div.querySelector('input')).toBeNull();
    expect(div.querySelector('button')).toBeNull();
  });

  it('retire les scripts, gestionnaires et URL dangereuses', () => {
    const div = rendu(
      '<img src="/a.png" onerror="alert(1)"><script>alert(1)</script>[x](javascript:alert(1)) [y](//evil.fr) <iframe src="https://x.fr"></iframe>',
    );
    expect(div.innerHTML).not.toMatch(/onerror|script|javascript|iframe|evil/);
  });

  it('retire les images externes mais garde celles de l’application', () => {
    const div = rendu('![pistage](https://tiers.fr/p.gif) ![capture](/api/pieces-jointes/1/contenu)');
    const images = div.querySelectorAll('img');
    expect(images).toHaveLength(1);
    expect(images[0].getAttribute('src')).toBe('/api/pieces-jointes/1/contenu');
  });
});
