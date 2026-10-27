import { copyFileSync, mkdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
const root = new URL('../', import.meta.url);
const destination = new URL('dist/studio/browser/licenses/', root);
mkdirSync(destination, { recursive: true });
for (const name of ['LICENSE', 'ThirdPartyNotices.txt']) {
  copyFileSync(
    fileURLToPath(new URL(`node_modules/monaco-editor/${name}`, root)),
    fileURLToPath(new URL(`Monaco-${name}`, destination)),
  );
}
copyFileSync(
  fileURLToPath(new URL('dist/studio/3rdpartylicenses.txt', root)),
  fileURLToPath(new URL('ThirdParty-LICENSES.txt', destination)),
);
