import { spawnSync } from 'node:child_process';
// Playwright enables FORCE_COLOR in child workers. Drop only the inherited test
// process NO_COLOR so the two controls do not conflict; user settings stay intact.
const env = { ...process.env };
delete env.NO_COLOR;
const result = spawnSync(
  process.execPath,
  ['node_modules/@playwright/test/cli.js', 'test', ...process.argv.slice(2)],
  { stdio: 'inherit', env },
);
process.exit(result.status ?? 1);
