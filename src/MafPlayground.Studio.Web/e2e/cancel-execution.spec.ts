import { test, expect, openEntity, runInput } from './fixtures/studio';
test('cancels a running native workflow and completes a fresh execution', async ({
  studio: page,
}, testInfo) => {
  await page.setViewportSize({ width: 1024, height: 768 });
  await openEntity(page, 'translation-workflow');
  await runInput(page, { text: 'fixture-wait', targetLanguages: ['es'] });
  await expect(page.locator('.run-heading studio-status')).toContainText('Running');
  await expect(page.locator('[data-node-id="translate-es"]')).toContainText('Active');
  await page.screenshot({ path: testInfo.outputPath('running-empty-1024.png'), fullPage: true });
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.screenshot({ path: testInfo.outputPath('running-1440.png'), fullPage: true });
  await page.getByRole('button', { name: 'Cancel run', exact: true }).click();
  await expect(page.locator('.run-heading studio-status')).toContainText('Cancelled');
  await expect(page.locator('[data-node-id="translate-es"]')).toContainText('Cancelled');
  await page.setViewportSize({ width: 1024, height: 768 });
  await page.screenshot({ path: testInfo.outputPath('cancelled-1024.png'), fullPage: true });
  await openEntity(page, 'basic-agent');
  await runInput(page, { message: 'fixture-fail' });
  await expect(page.locator('.run-heading studio-status')).toContainText('Failed');
  await page.screenshot({ path: testInfo.outputPath('failed-1024.png'), fullPage: true });
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.screenshot({ path: testInfo.outputPath('failed-1440.png'), fullPage: true });
  await openEntity(page, 'translation-workflow');
  await runInput(page, { text: 'fresh-after-cancel', targetLanguages: ['es'] });
  await expect(page.locator('.run-heading studio-status')).toContainText('Completed');
  await expect(page.locator('[data-node-id="translate-es"]')).toContainText('Completed');
});
