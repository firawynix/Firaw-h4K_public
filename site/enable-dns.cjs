const fs = require('node:fs');
const settings = {};
for (const line of fs.readFileSync('/home/ksdev/firawynix-portfolio/.env', 'utf8').split(/\r?\n/)) {
  const match = line.match(/^([A-Z_]+)=(.*)$/);
  if (!match) continue;
  let value = match[2].trim();
  if ((value.startsWith('"') && value.endsWith('"')) || (value.startsWith("'") && value.endsWith("'"))) value = value.slice(1, -1);
  settings[match[1]] = value;
}
const token = settings.CLOUDFLARE_API_TOKEN;
if (!token) throw new Error('Token DNS do servidor não disponível.');
async function api(method, path, body) {
  const response = await fetch('https://api.cloudflare.com/client/v4' + path, {
    method,
    headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
    body: body ? JSON.stringify(body) : undefined,
  });
  const result = await response.json();
  if (!response.ok || !result.success) throw new Error(JSON.stringify(result.errors));
  return result.result;
}
(async () => {
  const zone = settings.CLOUDFLARE_ZONE_ID || (await api('GET', '/zones?name=firawynix.com.br'))[0]?.id;
  if (!zone) throw new Error('Zona firawynix.com.br não encontrada.');
  const reference = await api('GET', `/zones/${zone}/dns_records?name=ssh.firawynix.com.br`);
  const cname = reference.find(record => record.type === 'CNAME');
  if (!cname || !cname.content.endsWith('.cfargotunnel.com')) throw new Error('CNAME do túnel existente não encontrado.');
  const existing = await api('GET', `/zones/${zone}/dns_records?name=h4k.firawynix.com.br`);
  if (existing.length) {
    if (existing.length === 1 && existing[0].type === 'CNAME' && existing[0].content === cname.content) {
      console.log('DNS h4K já configurado.');
      return;
    }
    throw new Error('Já existe outro registro DNS para h4k.firawynix.com.br; nada foi alterado.');
  }
  await api('POST', `/zones/${zone}/dns_records`, {
    type: 'CNAME', name: 'h4k.firawynix.com.br', content: cname.content, proxied: true, ttl: 1,
    comment: 'Firaw h4K site'
  });
  console.log('DNS h4K criado no túnel existente.');
})().catch(error => { console.error(error.message); process.exitCode = 1; });
