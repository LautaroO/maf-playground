import { test, expect, openEntity, runInput } from './fixtures/studio';
test('investigates real translation validation, retry cycle and partial branch', async ({
  studio: page,
}, testInfo) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.screenshot({ path: testInfo.outputPath('catalog-empty-1440.png') });
  await openEntity(page, 'translation-workflow');
  await runInput(page, { text: 'Your order 247 is ready.', targetLanguages: ['es', 'fr'] });
  await expect(page.locator('.run-heading studio-status')).toContainText('Partial result');
  await expect(page.getByRole('heading', { name: 'Declared workflow topology' })).toBeVisible();
  const french = page.locator('[data-node-id="translate-fr"]');
  await french.click();
  await expect(page.getByLabel('Select invocation').locator('option')).toHaveCount(3);
  await page.getByLabel('Select invocation').selectOption({ index: 2 });
  const invocation = await page.getByLabel('Select invocation').inputValue();
  const graph = page.getByRole('img', { name: 'Interactive workflow topology' });
  const view = () =>
    graph.evaluate((element: any) => ({
      zoom: element._cyreg.cy.zoom(),
      pan: element._cyreg.cy.pan(),
    }));
  await expect
    .poll(() =>
      graph.evaluate((element: any) => {
        const positions = element._cyreg.cy.nodes().map((node: any) => {
          const point = node.position();
          return `${point.x},${point.y}`;
        });
        return new Set(positions).size;
      }),
    )
    .toBeGreaterThan(1);
  const initial = await view();
  await page.getByRole('button', { name: 'Zoom in graph' }).click();
  expect((await view()).zoom).toBeGreaterThan(initial.zoom);
  const box = (await graph.boundingBox())!;
  await page.mouse.move(box.x + 8, box.y + 8);
  await page.mouse.down();
  await page.mouse.move(box.x + 50, box.y + 45, { steps: 5 });
  await page.mouse.up();
  expect((await view()).pan).not.toEqual(initial.pan);
  const preserved = await view();
  const beforeWidth = (await graph.boundingBox())!.width;
  const widthControl = page.getByLabel('Inspector width');
  await widthControl.press('ArrowRight');
  await expect(widthControl).toHaveValue('330');
  await expect.poll(async () => (await graph.boundingBox())!.width).toBeLessThan(beforeWidth);
  expect(await view()).toEqual(preserved);
  await expect(page.getByLabel('Select invocation')).toHaveValue(invocation);
  await page.getByRole('button', { name: 'Fit graph' }).click();
  await graph.scrollIntoViewIfNeeded();
  const clickBox = (await graph.boundingBox())!;
  await page.screenshot({ path: testInfo.outputPath('graph-before-selection-1440.png') });
  const node = await graph.evaluate((element: any) => {
    const n = element._cyreg.cy.getElementById('node:validate-fr');
    return n.renderedPosition();
  });
  await page.mouse.click(clickBox.x + node.x, clickBox.y + node.y);
  await expect(page.locator('[data-node-id="validate-fr"]')).toHaveAttribute(
    'aria-pressed',
    'true',
  );
  await page.getByLabel('Select invocation').selectOption({ index: 1 });
  await page.getByLabel('Search selected events').fill('MissingData');
  await page
    .locator('studio-span-inspector summary')
    .filter({ hasText: 'executor_output' })
    .click();
  await page
    .locator('studio-span-inspector details[open]')
    .getByRole('button', { name: 'Load content' })
    .click();
  await expect(page.locator('studio-span-inspector pre')).toContainText('247');
  await page.screenshot({ path: testInfo.outputPath('translation-investigation-1440.png') });
  const span = page.locator('[data-span]').filter({ hasText: 'model_call' }).first();
  await span.click();
  await page
    .locator('studio-payload-viewer')
    .first()
    .getByRole('button', { name: 'Load content' })
    .click();
  await expect(page.locator('studio-payload-viewer pre').first()).toBeVisible();
  await page.setViewportSize({ width: 1024, height: 768 });
  await page.getByRole('button', { name: 'Fit graph' }).click();
  await page.screenshot({ path: testInfo.outputPath('translation-span-1024.png') });
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(1024);
  await page.getByRole('button', { name: 'Collapse graph' }).click();
  await expect(graph).toBeHidden();
  await expect(page.locator('[data-span][aria-pressed=true]')).toHaveCount(1);
  await page.getByRole('heading', { name: 'Trace timeline' }).scrollIntoViewIfNeeded();
  await page.screenshot({ path: testInfo.outputPath('timeline-collapsed-1024.png') });
  expect(invocation).not.toBe('');
  // Task 9 extends this same journey with baseline/candidate comparison.
});
