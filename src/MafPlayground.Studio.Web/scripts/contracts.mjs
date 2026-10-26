import { execFileSync } from 'node:child_process';
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { compile } from 'json-schema-to-typescript';
const workspace = fileURLToPath(new URL('../', import.meta.url));
const root = resolve(workspace, '../..');
const temp = await mkdtemp(join(tmpdir(), 'studio-contracts-'));
try {
  // Build first so drift checks cannot silently read yesterday's assembly. This is offline: no host/model/DB.
  execFileSync(
    'dotnet',
    [
      'build',
      'src/MafPlayground.CLI',
      '--no-restore',
      '-m:1',
      '-p:UseSharedCompilation=false',
      '-nr:false',
    ],
    { cwd: root, stdio: 'pipe' },
  );
  execFileSync(
    'dotnet',
    [
      'run',
      '--project',
      'src/MafPlayground.CLI',
      '--no-build',
      '--',
      'studio',
      '--export-contracts',
      join(temp, 'contracts.json'),
    ],
    { cwd: root, stdio: 'pipe' },
  );
  const { transport } = JSON.parse(await readFile(join(temp, 'contracts.json'), 'utf8'));
  // MEAI emits an unconstrained items sibling beside repeated array $refs. It adds
  // no JSON Schema constraint, but the TS compiler otherwise replaces referenced items with unknown.
  const normalizeReferences = (node) => {
    if (!node || typeof node !== 'object') return;
    if (node.$ref && node.items && Object.keys(node.items).length === 0) delete node.items;
    for (const child of Object.values(node)) normalizeReferences(child);
  };
  normalizeReferences(transport);
  const parts = [];
  for (const [name, schema] of Object.entries(transport).sort(([a], [b]) => a.localeCompare(b))) {
    parts.push(
      await compile(schema, name, {
        bannerComment: '',
        unknownAny: true,
        enableConstEnums: false,
        additionalProperties: false,
      }),
    );
  }
  const output =
    '// Generated from the offline Studio transport export. Do not edit.\n// Entity inputs/outputs remain unknown JSON with runtime schemas.\n\n' +
    parts.join('\n');
  const target = join(workspace, 'src/app/core/studio-api.generated.ts');
  if (process.argv.includes('--check')) {
    if ((await readFile(target, 'utf8')) !== output)
      throw new Error('Transport drift. Run npm run generate:contracts and review the diff.');
    console.log('Transport contracts match the current offline backend export.');
  } else {
    await writeFile(target, output);
    console.log('Generated transport contracts.');
  }
} finally {
  await rm(temp, { recursive: true, force: true });
}
