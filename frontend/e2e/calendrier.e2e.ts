import { expect, test, type Page } from '@playwright/test';

// Calendrier des MEP et périodes de gel (#129), sur les données de démonstration.
const ENTETES = { 'X-Requested-With': 'XMLHttpRequest' };

async function connecter(page: Page) {
  const connexion = await page.request.post('/api/auth/connexion', {
    data: { login: 'alice', motDePasse: 'demo' },
    headers: ENTETES,
  });
  expect(connexion.ok()).toBe(true);
  await page.addInitScript(() => localStorage.setItem('liste-meps.affichage', JSON.stringify('calendrier')));
}

const largeurPage = (page: Page) => page.evaluate(() => document.documentElement.scrollWidth);

test.describe('Calendrier des MEP', () => {
  for (const largeur of [390, 1366]) {
    test(`mois, semaine et liste sans défilement horizontal (${largeur} px)`, async ({ page }) => {
      await page.setViewportSize({ width: largeur, height: 800 });
      await connecter(page);
      for (const vue of ['Mois', 'Semaine', 'Liste']) {
        await page.goto(`/?vue=${vue.toLowerCase()}`);
        const calendrier = page.getByRole('region', { name: 'Calendrier des MEP' });
        await expect(calendrier).toBeVisible({ timeout: 20_000 });
        await expect(calendrier.locator('[aria-busy]')).toHaveCount(0, { timeout: 20_000 });
        await expect(page.getByRole('radio', { name: vue, checked: true })).toBeVisible();
        expect(await largeurPage(page)).toBeLessThanOrEqual(largeur);
      }
    });
  }

  test('« S’abonner » montre l’état de l’adresse personnelle', async ({ page }) => {
    await connecter(page);
    await page.goto('/');
    await page.getByRole('button', { name: 'S’abonner' }).click();
    const dialogue = page.getByRole('dialog', { name: 'S’abonner au calendrier' });
    await expect(dialogue.getByRole('heading', { name: 'Adresse secrète personnelle' })).toBeVisible();
  });
});

test('Paramètres › Périodes de gel : la page se lit sur téléphone', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 800 });
  await connecter(page);
  await page.goto('/parametres/periodes-gel');
  await expect(page.getByRole('heading', { name: 'Périodes de gel', level: 1 })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Nouvelle période' }).first()).toBeVisible({ timeout: 20_000 });
  expect(await largeurPage(page)).toBeLessThanOrEqual(390);
});
