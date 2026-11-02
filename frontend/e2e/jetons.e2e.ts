import { expect, test } from '@playwright/test';

// Jetons d'accès (#128) : créé depuis « Mon profil », montré une fois, utilisable aussitôt par l'API, révocable.
const ENTETES = { 'X-Requested-With': 'XMLHttpRequest' };

test.describe('Jetons d’accès personnels', () => {
  test('un jeton créé depuis « Mon profil » appelle l’API, puis ne fonctionne plus une fois révoqué', async ({
    page,
    playwright,
  }) => {
    const connexion = await page.request.post('/api/auth/connexion', {
      data: { login: 'alice', motDePasse: 'demo' },
      headers: ENTETES,
    });
    expect(connexion.ok()).toBe(true);
    const nom = `E2E ${Date.now()}`;

    await page.goto('/profil');
    await page.getByRole('button', { name: 'Nouveau jeton' }).click();
    const dialogue = page.getByRole('dialog', { name: 'Nouveau jeton personnel' });
    await dialogue.getByLabel('Nom *').fill(nom);
    await dialogue.getByRole('button', { name: 'Créer le jeton' }).click();

    const cree = page.getByRole('dialog', { name: 'Jeton créé' });
    const secret = (await cree.getByLabel('Jeton d’accès').textContent())!.trim();
    expect(secret).toMatch(/^gmep_[0-9A-Za-z]{43}$/);

    // Sans cookie ni en-tête anti-CSRF : le jeton seul.
    const outil = await playwright.request.newContext({
      baseURL: new URL(page.url()).origin,
      extraHTTPHeaders: { Authorization: `Bearer ${secret}` },
    });
    expect((await (await outil.get('/api/auth/moi')).json()).login).toBe('alice');

    await cree.getByRole('button', { name: 'Copier' }).first().click();
    await cree.getByRole('button', { name: 'J’ai copié le jeton' }).click();
    await page.getByRole('button', { name: `Révoquer « ${nom} »` }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Révoquer' }).click();
    await expect(page.getByText(`Jeton « ${nom} » révoqué.`)).toBeVisible();
    expect((await outil.get('/api/auth/moi')).status()).toBe(401);
    await outil.dispose();
  });

  test('sur un téléphone, la section et la console tiennent sans défilement horizontal', async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await page.request.post('/api/auth/connexion', { data: { login: 'alice', motDePasse: 'demo' }, headers: ENTETES });
    for (const adresse of ['/profil', '/admin?onglet=jetons']) {
      await page.goto(adresse);
      await expect(page.getByRole('heading', { name: /Jetons/ }).first()).toBeVisible();
      await expect(page.locator('[aria-busy="true"]')).toHaveCount(0);
      const debord = await page.evaluate(
        () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
      );
      expect(debord, adresse).toBe(0);
    }
  });
});
