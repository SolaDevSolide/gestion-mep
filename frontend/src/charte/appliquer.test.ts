import { appliquerEmpreinte } from './appliquer';

describe('appliquerEmpreinte', () => {
  let lien: HTMLLinkElement;
  beforeEach(() => {
    lien = document.createElement('link');
    lien.id = 'charte';
    lien.rel = 'stylesheet';
    lien.href = '/charte.css';
    lien.dataset.empreinte = 'ancienne';
    document.head.append(lien);
  });
  afterEach(() => {
    document.head.querySelectorAll('link').forEach((l) => l.remove());
  });

  it('charge la nouvelle feuille à côté de l’ancienne, puis retire l’ancienne une fois chargée', () => {
    appliquerEmpreinte('nouvelle');
    const liens = document.head.querySelectorAll('link[rel="stylesheet"]');
    expect(liens).toHaveLength(2);
    const nouvelle = liens[1] as HTMLLinkElement;
    expect(nouvelle.getAttribute('href')).toBe('/charte.css?v=nouvelle');
    // Pas de second chargement pour la même empreinte pendant que la première charge.
    appliquerEmpreinte('nouvelle');
    expect(document.head.querySelectorAll('link[rel="stylesheet"]')).toHaveLength(2);

    nouvelle.dispatchEvent(new Event('load'));
    expect(lien.isConnected).toBe(false);
    expect(document.getElementById('charte')).toBe(nouvelle);
    appliquerEmpreinte('nouvelle');
    expect(document.head.querySelectorAll('link[rel="stylesheet"]')).toHaveLength(1);
  });

  it('ne fait rien pour la charte déjà appliquée ou sans empreinte', () => {
    appliquerEmpreinte('ancienne');
    appliquerEmpreinte(null);
    expect(document.head.querySelectorAll('link')).toHaveLength(1);
  });

  it('garde l’ancienne feuille si la nouvelle ne se charge pas', () => {
    appliquerEmpreinte('injoignable');
    const nouvelle = document.head.querySelectorAll('link')[1];
    nouvelle.dispatchEvent(new Event('error'));
    expect(document.head.querySelectorAll('link')).toHaveLength(1);
    expect(document.getElementById('charte')).toBe(lien);
  });
});
