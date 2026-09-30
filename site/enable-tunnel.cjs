const fs = require('node:fs');
const { execFileSync } = require('node:child_process');
const path = '/etc/cloudflared/config.yml';
const original = fs.readFileSync(path, 'utf8');
if (original.includes('hostname: h4k.firawynix.com.br')) {
  console.log('Rota h4K já configurada.');
  process.exit(0);
}
const fallback = '  - service: http://localhost:8099';
if (original.split(fallback).length !== 2) throw new Error('Rota final do túnel não é a esperada; nenhuma alteração.');
const updated = original.replace(fallback, '  - hostname: h4k.firawynix.com.br\n    service: http://127.0.0.1:26018\n' + fallback);
const temp = path + '.h4k-pending';
fs.writeFileSync(temp, updated, { mode: 0o600 });
try {
  execFileSync('cloudflared', ['--config', temp, 'tunnel', 'ingress', 'validate'], { stdio: 'pipe' });
  fs.copyFileSync(path, path + '.before-h4k');
  fs.renameSync(temp, path);
  execFileSync('systemctl', ['restart', 'cloudflared'], { stdio: 'pipe' });
  console.log('Rota h4K adicionada e túnel reiniciado.');
} catch (error) {
  if (fs.existsSync(temp)) fs.unlinkSync(temp);
  throw error;
}
