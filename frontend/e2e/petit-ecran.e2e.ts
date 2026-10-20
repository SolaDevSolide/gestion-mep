import { expect, test, type Page } from '@playwright/test';

// Données de démonstration : MEP « Facturation électronique », section « Tables factures », étape E0002.
const ENTETES = { 'X-Requested-With': 'XMLHttpRequest' };

interface MepDemo {
  id: string;
  outil: string;
}
interface DetailMep {
  sections: { id: string; titre: string }[];
  etapes: { id: string; code: string; sectionId: string | null }[];
}

async function preparer(page: Page) {
  const connexion = await page.request.post('/api/auth/connexion', {
    data: { login: 'alice', motDePasse: 'demo' },
    headers: ENTETES,
  });
  expect(connexion.ok()).toBe(true);
  // Bandeau de démonstration masqué : les mesures portent sur la fenêtre telle qu'en production.
  await page.route(/\/api\/demo$/, (r) => r.fulfill({ status: 404, body: '' }));
  const meps: MepDemo[] = await (await page.request.get('/api/meps', { headers: ENTETES })).json();
  const mep = meps.find((m) => m.outil === 'Facturation électronique');
  expect(mep, 'MEP de démonstration « Facturation électronique »').toBeDefined();
  const detail: DetailMep = await (await page.request.get(`/api/meps/${mep!.id}`, { headers: ENTETES })).json();
  const section = detail.sections.find((s) => s.titre === 'Tables factures')!;
  const etape = detail.etapes.find((e) => e.code === 'E0002')!;
  return { mep: mep!.id, section: section.id, etape: etape.id };
}

/** Partie du graphe visible dans la fenêtre, tiroir de l'étape déduit. */
function grapheVisible(page: Page) {
  return page.evaluate(() => {
    const zone = document.querySelector('[data-graphe-libre]')!.getBoundingClientRect();
    const tiroir = document.querySelector('[data-tiroir-etape]')?.getBoundingClientRect();
    const droite = tiroir && tiroir.left > zone.left ? Math.min(tiroir.left, zone.right) : zone.right;
    return {
      largeur: Math.round(droite - Math.max(zone.left, 0)),
      hauteur: Math.round(Math.min(zone.bottom, innerHeight) - Math.max(zone.top, 0)),
    };
  });
}

test.describe('Petit écran (1366 × 641) : vue compacte du plan', () => {
  test('le graphe d’une section garde au moins 480 px de haut, et reste visible avec une étape ouverte', async ({
    page,
  }) => {
    const { mep, section, etape } = await preparer(page);
    const plan = `/meps/${mep}?onglet=plan&plan=etapes&section=${section}`;

    await page.goto(plan);
    const graphe = page.locator('[data-graphe-libre]');
    await expect(graphe).toBeVisible({ timeout: 20_000 });
    await expect(graphe.locator('[aria-busy]')).toHaveCount(0, { timeout: 20_000 });
    await expect(page.getByRole('button', { name: 'Détails de la section' })).toBeVisible();
    // La page a défilé jusqu'au cadre du graphe.
    await expect.poll(async () => (await grapheVisible(page)).hauteur).toBeGreaterThanOrEqual(480);

    await page.goto(`${plan}&etape=${etape}`);
    const tiroir = page.locator('[data-tiroir-etape]');
    await expect(tiroir).toBeVisible();
    await expect(graphe.locator('[aria-busy]')).toHaveCount(0, { timeout: 20_000 });
    await expect.poll(async () => (await grapheVisible(page)).hauteur).toBeGreaterThanOrEqual(480);
    const visible = await grapheVisible(page);
    expect(visible.largeur).toBeGreaterThanOrEqual(400);
    // La carte de l'étape ouverte reste dans la partie visible du graphe.
    const carte = await page.locator(`.react-flow__node[data-id="${etape}"]`).boundingBox();
    const bord = await tiroir.boundingBox();
    expect(carte).not.toBeNull();
    expect(carte!.x + carte!.width).toBeLessThanOrEqual(bord!.x);
  });
});
