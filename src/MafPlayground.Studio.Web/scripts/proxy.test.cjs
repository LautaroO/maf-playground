const { test } = require('node:test');
const assert = require('node:assert/strict');
const { spawnSync } = require('node:child_process');
const config = require('../proxy.conf.cjs');
function proxyRequest(origin) {
  let handler;
  config['/api/studio/**'].configure({
    on: (name, callback) => {
      if (name === 'proxyReq') handler = callback;
    },
  });
  const headers = {};
  let rejected = false;
  handler(
    { setHeader: (name, value) => (headers[name] = value), destroy: () => (rejected = true) },
    { headers: { host: '127.0.0.1:4200', origin } },
  );
  return { headers, rejected };
}
test('only the same browser origin may be rewritten to the backend authority', () => {
  const own = proxyRequest('http://127.0.0.1:4200');
  assert.equal(own.rejected, false);
  assert.equal(own.headers.origin, 'http://127.0.0.1:5051');
  for (const origin of ['https://foreign.invalid', 'http://127.0.0.1:4000', 'null']) {
    const foreign = proxyRequest(origin);
    assert.equal(foreign.rejected, true);
    assert.equal(foreign.headers.origin, undefined);
  }
});
test('backend target is a credential-free authority without path, query or fragment', () => {
  for (const target of [
    'http://foreign.invalid',
    'http://user:pass@localhost:5051',
    'http://localhost:5051/path',
    'http://localhost:5051/?secret=value',
    'http://localhost:5051/#value',
  ]) {
    const result = spawnSync(process.execPath, ['-e', "require('./proxy.conf.cjs')"], {
      cwd: __dirname + '/..',
      env: { ...process.env, STUDIO_BACKEND_URL: target },
    });
    assert.notEqual(result.status, 0);
  }
});
