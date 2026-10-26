import { test, expect, openEntity, runInput } from './fixtures/studio';
test('cancels a running provider attempt and completes a fresh execution', async ({
  studio: page,
}, testInfo) => {
  await page.setViewportSize({ width: 1024, height: 768 });
  await openEntity(page, 'basic-agent');
  await runInput(page, { message: 'fixture-wait' });
  await expect(page.locator('.run-heading studio-status')).toContainText('Running');
  await page.screenshot({ path: testInfo.outputPath('running-empty-1024.png'), fullPage: true });
  const runId = page.url().split('/').at(-1)!;
  await expect
    .poll(async () => {
      const response = await page.request.get(
        `/api/studio/v1/runs/${runId}/event-page?after=0&limit=100`,
      );
      return (await response.json()).events.some(
        (event: { kind: string }) => event.kind === 'model_request',
      );
    })
    .toBe(true);
  await page.getByRole('button', { name: 'Cancel run', exact: true }).click();
  await expect(page.locator('.run-heading studio-status')).toContainText('Cancelled');
  await page.screenshot({ path: testInfo.outputPath('cancelled-1024.png'), fullPage: true });
  await openEntity(page, 'basic-agent');
  await runInput(page, { message: 'fresh-after-cancel' });
  await expect(page.locator('.run-heading studio-status')).toContainText('Completed');
  await expect(page.locator('studio-run-workspace')).toContainText('fresh-after-cancel');
});
