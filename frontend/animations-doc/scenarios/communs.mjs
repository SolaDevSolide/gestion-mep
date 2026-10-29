// Repères partagés par les scénarios.

/** Carte d'une étape dans le graphe, par son code (E0006, R0001…). */
export const carte = (page, code) => page.locator(`[data-carte][aria-label^="${code} "]`);

/** Ouvre une MEP de démonstration (onglet et paramètres de l'adresse facultatifs). */
export async function ouvrirMep({ page, api }, outil, parametres = '') {
  const mep = await api.mep(outil);
  await page.goto(`/meps/${mep.id}${parametres}`);
  await page.getByRole('heading', { level: 1 }).waitFor();
  return mep;
}

/**
 * Ouvre le graphe d'une MEP en plein écran, à 100 %, sans mini-carte ni bac, centré sur une étape. Par défaut celui de
 * Plan › Étapes ; `vue` choisit un autre onglet (`?onglet=execution`, `?onglet=plan&plan=retour`…).
 */
export async function planPleinEcran(ctx, outil, centre, vue = '?onglet=plan&plan=etapes') {
  const { page } = ctx;
  const mep = await ouvrirMep(ctx, outil, vue);
  await page.getByRole('application', { name: 'Graphe des étapes' }).waitFor();
  await page.getByRole('button', { name: 'Plan en plein écran' }).click();
  await page.getByRole('button', { name: 'Masquer la mini-carte' }).click();
  const bac = page.getByRole('button', { name: 'Replier le bac' });
  if (await bac.isVisible()) await bac.click();
  await page.getByRole('button', { name: 'Ajuster à l’écran' }).click();
  await page.waitForTimeout(400);
  await page.getByRole('button', { name: /^Zoom \d+ % : revenir à 100 %$/ }).click();
  if (centre) {
    await page.getByRole('combobox', { name: 'Aller à une étape (code)' }).fill(centre);
    await page.keyboard.press('Enter');
    await page.waitForTimeout(600);
    await page.keyboard.press('Escape');
  }
  await page.waitForTimeout(1200);
  return mep;
}
