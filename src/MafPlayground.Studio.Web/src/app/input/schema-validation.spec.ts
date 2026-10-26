import { validateJson } from './schema-validation';
import { novel } from '../core/testing.fixture';
it('validates unfamiliar nested required, enum and number constraints with editor locations', async () => {
  expect(await validateJson(JSON.stringify(novel.inputExample), novel.inputSchema)).toEqual([]);
  const invalid = await validateJson(
    '{"observations":[{"coordinates":{"latitude":120},"category":"unknown"}]}',
    novel.inputSchema,
  );
  expect(invalid.length).toBe(2);
  expect(invalid.every((item) => item.line > 0 && item.column > 0)).toBe(true);
  expect((await validateJson('{"observations":[]}', novel.inputSchema)).length).toBeGreaterThan(0);
  expect((await validateJson('{broken', novel.inputSchema)).length).toBeGreaterThan(0);
});
it('refuses remote references instead of fetching arbitrary schema URLs', async () => {
  const diagnostics = await validateJson('{}', { $ref: 'https://private.invalid/schema' });
  expect(diagnostics.length).toBeGreaterThan(0);
});
