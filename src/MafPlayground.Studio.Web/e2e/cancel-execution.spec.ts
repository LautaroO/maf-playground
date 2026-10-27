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
  await expect(page.locator('[data-node-id="translate-es"]')).not.toContainText('Issue recorded');

  await expect(page.getByRole('region', { name: 'Execution issues', exact: true })).toHaveCount(0);

  await page.setViewportSize({ width: 1024, height: 768 });
  await page.screenshot({ path: testInfo.outputPath('cancelled-1024.png'), fullPage: true });
  await openEntity(page, 'translation-workflow');
  await runInput(page, { text: 'fixture-validation-fail', targetLanguages: ['es', 'fr'] });
  await expect(page.locator('.run-heading studio-status')).toContainText('Partial result');
  const issues = page.getByRole('region', { name: 'Execution issues', exact: true });
  const failedValidation = issues.locator('[data-run-issue]').filter({ hasText: 'validate-fr' });
  await expect(failedValidation).toHaveCount(1);
  await expect(failedValidation).toContainText('Model call failed');
  await failedValidation.getByRole('button', { name: 'Show cause', exact: true }).click();
  await expect(failedValidation).toContainText('Rate or quota limit · HTTP 429');
  await failedValidation.getByRole('button', { name: 'Inspect evidence', exact: true }).click();
  await expect(page.locator('[data-node-id="validate-fr"]')).toHaveAttribute(
    'aria-pressed',
    'true',
  );
  await expect(page.locator('[data-node-id="validate-fr"]')).toContainText('Completed');
  await expect(page.locator('[data-node-id="validate-fr"]')).toContainText('Issue recorded');
  await page.screenshot({
    path: testInfo.outputPath('partial-error-cause-1440.png'),
    fullPage: true,
  });
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
