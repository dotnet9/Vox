const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const prepareMetadata = require('./sync.cjs');
prepareMetadata();
const types = {'.html':'text/html; charset=utf-8','.js':'text/javascript; charset=utf-8','.css':'text/css; charset=utf-8','.svg':'image/svg+xml','.md':'text/markdown; charset=utf-8','.png':'image/png','.woff2':'font/woff2','.ttf':'font/ttf','.txt':'text/plain; charset=utf-8'};
const port = Number(process.env.VOX_PREVIEW_PORT || 4318);
const server = http.createServer((req,res) => {
  let pathname;
  try {pathname = decodeURIComponent(new URL(req.url,'http://localhost').pathname);} catch {res.writeHead(400).end();return;}
  if (pathname === '/' || pathname === '/design/') pathname = '/design/index.html';
  if (pathname === '/design/assets/project-info.js') {
    try { prepareMetadata(); } catch (error) { console.error(error.message);res.writeHead(500).end('Metadata preparation failed');return; }
  }
  const isDesign = pathname.startsWith('/design/');
  if (!isDesign) {res.writeHead(404).end();return;}
  const target = path.resolve(root,'.' + pathname);
  const allowedRoot = path.resolve(root, 'design');
  const allowed = target.startsWith(allowedRoot + path.sep);
  if (!allowed) {res.writeHead(403).end();return;}
  fs.readFile(target,(error,data) => {if (error) {res.writeHead(404).end();return;}res.writeHead(200,{'Content-Type':types[path.extname(target)] || 'application/octet-stream','Cache-Control':'no-store'}).end(data);});
});
server.listen(port,'127.0.0.1',() => console.log('Vox prototype: http://127.0.0.1:' + server.address().port + '/design/'));
