import { test, expect, openEntity, runInput, inputJson } from './fixtures/studio';
test('investigates real translation validation, retry cycle and partial branch', async ({
  studio: page,
}, testInfo) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.screenshot({ path: testInfo.outputPath('catalog-empty-1440.png') });
  await page.setViewportSize({ width: 1024, height: 768 });
  await page.screenshot({ path: testInfo.outputPath('catalog-empty-1024.png') });
  await page.setViewportSize({ width: 1440, height: 900 });
  await openEntity(page, 'translation-workflow');
  await expect(page.getByText('JSON · Monaco', { exact: true })).toBeVisible();
  // The compiled page must load Monaco's structural CSS, not only its JS/theme.
  // Missing CSS places JSON lines outside the editor even though typing works.
  for (const width of [1440, 1024]) {
    await page.setViewportSize({ width, height: width === 1440 ? 900 : 768 });
    await expect
      .poll(async () => {
        const editorBox = await page.locator('studio-json-editor .monaco').boundingBox();
        const line = await page.locator('studio-json-editor .view-line').first().boundingBox();
        return (
          !!editorBox &&
          !!line &&
          line.x >= editorBox.x &&
          line.y >= editorBox.y &&
          line.x + line.width <= editorBox.x + editorBox.width + 1 &&
          line.y + line.height <= editorBox.y + editorBox.height
        );
      })
      .toBe(true);
    await page.screenshot({ path: testInfo.outputPath(`input-editor-${width}.png`) });
  }
  await page.setViewportSize({ width: 1440, height: 900 });
  await runInput(page, { text: 'Your order 247 is ready.', targetLanguages: ['es', 'fr'] });
  await expect(page.locator('.run-heading studio-status')).toContainText('Partial result');
  await expect(page.getByRole('heading', { name: 'Declared workflow topology' })).toBeVisible();
  const french = page.locator('[data-node-id="translate-fr"]');
  const fullTimelineCount = await page.locator('[data-span]').count();
  await french.click();
  await expect.poll(() => page.locator('[data-span]').count()).toBeLessThan(fullTimelineCount);
  await page.getByRole('button', { name: 'Reset timeline filters' }).click();
  await expect(page.locator('[data-span]')).toHaveCount(fullTimelineCount);
  await french.click();
  await expect(french).toContainText('Completed');
  await expect(page.getByLabel('Select invocation').locator('option')).toHaveCount(3);
  await page.getByLabel('Select invocation').selectOption({ index: 2 });
  const invocation = await page.getByLabel('Select invocation').inputValue();
  const graph = page.getByRole('img', { name: 'Interactive workflow topology' });
  const hierarchy = await graph.evaluate((element: any) => {
    const cy = element._cyreg.cy;
    const position = (id: string) => cy.getElementById('node:' + id).position();
    const feedback = cy
      .edges()
      .filter(
        (edge: any) =>
          edge.source().data('actualId') === 'validate-fr' &&
          edge.target().data('actualId') === 'translate-fr',
      )[0];
    const source = feedback.source().boundingBox(),
      target = feedback.target().boundingBox();
    const middle = feedback.midpoint();
    return {
      entry: position('translation-input'),
      left: position('translate-es'),
      right: position('translate-fr'),
      leftCheck: position('validate-es'),
      rightCheck: position('validate-fr'),
      join: position('translation-aggregate'),
      returnOutsideNodes:
        middle.x < Math.min(source.x1, target.x1) || middle.x > Math.max(source.x2, target.x2),
    };
  });
  expect(hierarchy.entry.y).toBeLessThan(hierarchy.left.y);
  expect(hierarchy.left.y).toBe(hierarchy.right.y);
  expect(hierarchy.leftCheck.y).toBe(hierarchy.rightCheck.y);
  expect(hierarchy.left.y).toBeLessThan(hierarchy.leftCheck.y);
  expect(hierarchy.leftCheck.y).toBeLessThan(hierarchy.join.y);
  expect(hierarchy.returnOutsideNodes).toBe(true);
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
  await expect(page.getByLabel('Select invocation')).not.toHaveValue('');
  const validation = page.getByRole('region', { name: 'Validation result', exact: true });
  await validation.getByRole('button', { name: 'Load content', exact: true }).click();
  await expect(validation.locator('pre')).toContainText('MissingData');
  await expect(validation.locator('pre')).toContainText('247');
  await expect(page.locator('details[data-technical-events]')).not.toHaveAttribute('open', '');
  // The inspector owns vertical scrolling; expanded event/JSON content must not
  // create a second scroll container inside the same right-hand panel.
  await page.locator('details[data-technical-events] > summary').click();
  const stepOutput = page.getByRole('region', { name: 'Step output', exact: true });
  await stepOutput.getByRole('button', { name: 'Load content', exact: true }).click();
  await expect(stepOutput.locator('pre')).toContainText('247');
  for (const width of [1440, 1024]) {
    await page.setViewportSize({ width, height: width === 1440 ? 900 : 768 });
    const inspector = page.locator('aside.inspector');
    expect(
      await inspector.evaluate((element) =>
        Array.from(element.querySelectorAll('*'))
          .filter(
            (child) =>
              /(auto|scroll)/.test(getComputedStyle(child).overflowY) &&
              child.scrollHeight > child.clientHeight + 1,
          )
          .map((child) => child.tagName + '.' + child.className),
      ),
    ).toEqual([]);
    await page.getByRole('button', { name: 'Input', exact: true }).scrollIntoViewIfNeeded();
    await expect(page.getByRole('button', { name: 'Input', exact: true })).toBeVisible();
  }
  await page.locator('details[data-technical-events] > summary').click();
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.screenshot({ path: testInfo.outputPath('translation-investigation-1440.png') });
  const span = page.locator('[data-span]').filter({ hasText: 'Model' }).first();
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
  const baselineId = page.url().split('/').pop()!;
  await page.getByRole('button', { name: 'Run again' }).click();
  await expect(page).toHaveURL(/\/input$/);
  await expect(page.getByRole('button', { name: /Run entity/ })).toBeDisabled();
  await page.getByLabel('Execution profile').selectOption('reviewed');
  await inputJson(page, { text: 'Your order 247 is ready now.', targetLanguages: ['es', 'fr'] });
  await page.getByLabel('I reviewed the input, profile and session for this rerun').check();
  await page.getByRole('button', { name: /Run entity/ }).click();
  await expect(page).toHaveURL(/\/runs\//);
  const candidateId = page.url().split('/').pop()!;
  expect(candidateId).not.toBe(baselineId);
  await expect(page.locator('.run-heading studio-status')).toContainText('Completed');
  await page.locator('.run-heading').getByRole('link', { name: 'Compare' }).click();
  await page.getByLabel('Baseline run', { exact: true }).selectOption(baselineId);
  await page.getByLabel('Candidate run', { exact: true }).selectOption(candidateId);
  await page.getByRole('button', { name: 'Compare runs' }).click();
  await expect(
    page.getByRole('heading', { name: 'Structural and text differences' }),
  ).toBeVisible();
  await expect(page.locator('.summaries')).toContainText('reviewed');
  await expect(page.locator('.summaries')).toContainText('Partial result');
  await expect(page.locator('.summaries')).toContainText('Completed');
  await expect(page.locator('.changes')).toContainText('/input/text');
  await expect(page.locator('.changes')).toContainText('/result');
  await expect(page.locator('studio-run-comparison')).toContainText('Unknown');
  await expect(page.locator('studio-run-comparison')).toContainText('not causal equivalence');
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.getByRole('heading', { name: 'Compare executions' }).scrollIntoViewIfNeeded();
  await page.screenshot({ path: testInfo.outputPath('comparison-1440.png'), fullPage: true });
  await page.setViewportSize({ width: 1024, height: 768 });
  await page.screenshot({ path: testInfo.outputPath('comparison-1024.png'), fullPage: true });
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(1024);
});
