const target = process.env.STUDIO_BACKEND_URL || 'http://127.0.0.1:5051';
const url = new URL(target);
if (
  !['127.0.0.1', 'localhost', '[::1]'].includes(url.hostname) ||
  url.protocol !== 'http:' ||
  url.username ||
  url.password ||
  url.pathname !== '/' ||
  url.search ||
  url.hash
) {
  throw new Error('STUDIO_BACKEND_URL must be a credential-free loopback HTTP authority.');
}
module.exports = {
  '/api/studio/**': { target, changeOrigin: true, configure: configure },
  '/studio/login': { target, changeOrigin: true, configure: configure },
};
function configure(proxy) {
  proxy.on('proxyReq', (request, incoming) => {
    // Never turn a foreign browser origin into a trusted backend origin.
    if (incoming.headers.origin && incoming.headers.origin !== `http://${incoming.headers.host}`) {
      request.destroy(new Error('Studio development proxy rejected a foreign browser origin.'));
      return;
    }
    // Only this explicitly configured loopback development proxy rewrites origin.
    if (incoming.headers.origin) request.setHeader('origin', url.origin);
  });
}
