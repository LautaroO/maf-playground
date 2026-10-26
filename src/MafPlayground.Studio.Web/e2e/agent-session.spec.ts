import { test, expect, openEntity, inputJson } from './fixtures/studio';
test('executes native agent tool, continues session and resets it', async ({
  studio: page,
}, testInfo) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await openEntity(page, 'basic-agent');
  await page.getByRole('button', { name: /New session/ }).click();
  await inputJson(page, { message: 'fixture-time' });
  await page.getByRole('button', { name: /Send turn/ }).click();
  await expect(page.locator('studio-chat')).toContainText('Tool result:');
  await inputJson(page, { message: 'remember-first' });
  await page.getByRole('button', { name: /Send turn/ }).click();
  await expect(page.locator('studio-chat')).toContainText(
    'User turns: fixture-time | remember-first',
  );
  const original = await page.getByLabel('Session', { exact: true }).inputValue();
  await page.getByRole('button', { name: 'Reset session' }).click();
  await expect(page.getByLabel('Session', { exact: true })).not.toHaveValue(original);
  await inputJson(page, { message: 'fresh-turn' });
  await page.getByRole('button', { name: /Send turn/ }).click();
  await expect(page.locator('studio-chat')).toContainText('User turns: fresh-turn');
  await expect(page.locator('studio-chat')).not.toContainText('remember-first');
  await page.screenshot({ path: testInfo.outputPath('session-reset-1440.png'), fullPage: true });
  await page.locator('.run-item').first().click();
  await expect(page.getByRole('heading', { name: 'Observed call hierarchy' })).toBeVisible();
  await page.setViewportSize({ width: 1024, height: 768 });
  await page.screenshot({ path: testInfo.outputPath('agent-observed-1024.png'), fullPage: true });
});
