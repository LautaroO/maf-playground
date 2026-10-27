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
  const toolRunId = await page.locator('.run-item').first().locator('.run-id').innerText();
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
  await page.locator('.run-item').filter({ hasText: toolRunId }).click();
  await expect(page.getByRole('heading', { name: 'Observed call hierarchy' })).toBeVisible();
  await expect(
    page.getByRole('heading', { name: 'Observed execution graph', exact: true }),
  ).toBeVisible();
  const graph = page.getByRole('img', {
    name: 'Interactive observed execution graph',
    exact: true,
  });
  await expect(graph).toBeVisible();
  const tool = page
    .getByRole('group', { name: 'Captured operations' })
    .getByRole('button')
    .filter({ hasText: 'get_current_date_time' });
  await expect(tool).toHaveCount(1);
  const operations = page.getByRole('group', { name: 'Captured operations' });
  await expect(operations.getByRole('button')).toHaveCount(4);
  await expect(operations.getByRole('button').filter({ hasText: /Model(?: ·| call)/ })).toHaveCount(
    2,
  );
  await expect(operations).not.toContainText('model_call');
  await expect(operations).not.toContainText('chat ');
  await expect(page.getByLabel('Trace view', { exact: true })).toHaveValue('operations');

  const spanId = await tool.getAttribute('data-span-node-id');
  await page.getByRole('button', { name: 'Fit graph', exact: true }).click();
  const point = await graph.evaluate(
    (element: any, id) => element._cyreg.cy.getElementById('node:' + id).renderedPosition(),
    spanId,
  );
  const box = (await graph.boundingBox())!;
  await page.mouse.click(box.x + point.x, box.y + point.y);
  await expect
    .poll(() =>
      graph.evaluate((element: any, id) => {
        const node = element._cyreg.cy.getElementById('node:' + id);
        const before = node.incomers('edge.temporal');
        const after = node.outgoers('edge.temporal');
        return (
          before.length > 0 &&
          after.length > 0 &&
          before.every((edge: any) => edge.source().position().y < node.position().y) &&
          after.every((edge: any) => node.position().y < edge.target().position().y)
        );
      }, spanId),
    )
    .toBe(true);
  await expect(tool).toHaveAttribute('aria-pressed', 'true');
  await expect(page.locator('studio-span-inspector h3').first()).toHaveText(
    'Tool · get_current_date_time',
  );
  await expect(page.locator('[data-span][aria-pressed=true]')).toHaveCount(1);
  const nodePosition = () =>
    graph.evaluate(
      (element: any, id) => element._cyreg.cy.getElementById('node:' + id).position(),
      spanId,
    );
  const originalPosition = await nodePosition();
  await page.mouse.move(box.x + point.x, box.y + point.y);
  await page.mouse.down();
  await page.mouse.move(box.x + point.x + 24, box.y + point.y, { steps: 5 });
  await page.mouse.up();
  const movedPosition = await nodePosition();
  expect(movedPosition).not.toEqual(originalPosition);
  await tool.click();
  await expect(tool).toHaveAttribute('aria-pressed', 'true');
  expect(await nodePosition()).toEqual(movedPosition);
  const metrics = await page.locator('.metrics').innerText();
  await page.getByLabel('Trace view', { exact: true }).selectOption('technical');
  await expect(operations.getByRole('button').filter({ hasText: 'model_call' })).toHaveCount(2);
  await expect(
    operations.locator('button strong').filter({ hasText: /^chat(?:\s|$)/ }),
  ).toHaveCount(2);
  await expect(tool).toHaveAttribute('aria-pressed', 'true');
  expect(await page.locator('.metrics').innerText()).toEqual(metrics);
  await page.getByLabel('Trace view', { exact: true }).selectOption('operations');
  await expect(operations.getByRole('button')).toHaveCount(4);
  await expect(tool).toHaveAttribute('aria-pressed', 'true');

  await page
    .locator('studio-payload-viewer')
    .first()
    .getByRole('button', { name: 'Load content', exact: true })
    .click();
  await expect(page.locator('studio-payload-viewer pre').first()).toContainText(
    'get_current_date_time',
  );
  await page.setViewportSize({ width: 1024, height: 768 });
  await page.getByRole('button', { name: 'Fit graph', exact: true }).click();
  await expect(tool).toHaveAttribute('aria-pressed', 'true');
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(1024);
  await page.screenshot({ path: testInfo.outputPath('agent-observed-1024.png'), fullPage: true });
});
