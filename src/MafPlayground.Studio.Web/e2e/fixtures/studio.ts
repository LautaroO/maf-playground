import { test as base, expect, type Page } from '@playwright/test';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { resolve } from 'node:path';
export const test = base.extend<{ studio: Page }>({
  studio: async ({ page }, use) => {
    const child = spawn(
      'dotnet',
      [resolve('e2e/fixtures/StudioFixture/bin/Debug/net10.0/StudioFixture.dll')],
      { stdio: ['ignore', 'pipe', 'pipe'] },
    );
    child.stderr.resume();
    try {
      const code = await new Promise<string>((accept, reject) => {
        let buffer = '';
        const timeout = setTimeout(
          () =>
            reject(
              new Error(
                'Studio fixture did not start. Build the fixture first and check port 5053.',
              ),
            ),
          15000,
        );
        child.once('exit', () => {
          clearTimeout(timeout);
          reject(new Error('Studio fixture exited before readiness.'));
        });
        child.stdout.on('data', (chunk) => {
          buffer += String(chunk);
          const match = /FIXTURE_READY ([A-F0-9]{64})/.exec(buffer);
          if (match) {
            clearTimeout(timeout);
            accept(match[1]);
            buffer = '';
          }
        });
      });
      await page.goto('login');
      await page.getByLabel('Access code').fill(code);
      await page.getByRole('button', { name: 'Open Studio' }).click();
      await page.goto('./catalog');
      await expect(page.getByRole('heading', { name: 'Registered entities' })).toBeVisible();
      await use(page);
    } finally {
      await page.close();
      const exit = once(child, 'exit');
      child.kill('SIGTERM');
      const timer = setTimeout(() => child.kill('SIGKILL'), 5000);
      timer.unref();
      if (child.exitCode === null) await exit;
      clearTimeout(timer);
    }
  },
});
export { expect };
export async function openEntity(page: Page, id: string) {
  await page.getByRole('link', { name: 'Catalog', exact: true }).click();
  await page
    .locator('.entity-row')
    .filter({ hasText: id })
    .getByRole('button', { name: 'Inspect' })
    .click();
}
export async function inputJson(page: Page, value: unknown) {
  await expect(page.getByText('JSON · Monaco', { exact: true })).toBeVisible();
  await page.locator('.monaco .view-lines').click();
  await page.keyboard.press('ControlOrMeta+a');
  await page.keyboard.press('Backspace');
  await page.keyboard.type(JSON.stringify(value));
  await expect(page.getByText('Input matches the contract')).toBeVisible();
}
export async function runInput(page: Page, value: unknown) {
  await inputJson(page, value);
  await page.getByRole('button', { name: /Run entity/ }).click();
  await expect(page).toHaveURL(/\/runs\//);
}
