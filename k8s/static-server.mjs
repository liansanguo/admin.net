import { createReadStream, existsSync, statSync } from 'node:fs';
import { createServer, request } from 'node:http';
import { extname, join, normalize } from 'node:path';

const root = '/app/dist';
const apiHost = '172.16.244.180';
const apiPort = 5005;

const mimeTypes = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'application/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.jpeg': 'image/jpeg',
  '.svg': 'image/svg+xml',
  '.ico': 'image/x-icon',
  '.woff': 'font/woff',
  '.woff2': 'font/woff2',
};

function serveFile(res, filePath) {
  res.writeHead(200, {
    'content-type': mimeTypes[extname(filePath)] || 'application/octet-stream',
    'cache-control': filePath.endsWith('index.html') ? 'no-store' : 'public, max-age=31536000, immutable',
  });
  createReadStream(filePath).pipe(res);
}

function proxyApi(req, res) {
  const proxiedPath = req.url.replace(/^\/prod-api/, '') || '/';
  const upstream = request({
    hostname: apiHost,
    port: apiPort,
    method: req.method,
    path: proxiedPath,
    headers: {
      ...req.headers,
      host: apiHost,
    },
  }, (upstreamRes) => {
    res.writeHead(upstreamRes.statusCode || 502, upstreamRes.headers);
    upstreamRes.pipe(res);
  });
  upstream.on('error', (error) => {
    res.writeHead(502, { 'content-type': 'text/plain; charset=utf-8' });
    res.end(`Bad gateway: ${error.message}`);
  });
  req.pipe(upstream);
}

createServer((req, res) => {
  if (req.url?.startsWith('/prod-api/')) {
    proxyApi(req, res);
    return;
  }

  const requested = normalize(decodeURIComponent(req.url?.split('?')[0] || '/')).replace(/^(\.\.[/\\])+/, '');
  let filePath = join(root, requested);
  if (!existsSync(filePath) || statSync(filePath).isDirectory()) {
    filePath = join(root, 'index.html');
  }
  serveFile(res, filePath);
}).listen(80, '0.0.0.0');
