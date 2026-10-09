const { randomUUID } = require('node:crypto');
const { readFile, readdir } = require('node:fs/promises');
const { join } = require('node:path');
const { isDeepStrictEqual } = require('node:util');

const directory = process.argv[2];
const credential = process.env.CARNICERIAS_TEST_USER;
const password = process.env.CARNICERIAS_TEST_PASSWORD;
const baseUrl = 'http://127.0.0.1:5197';
const mime = 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet';

async function main() {
  if (!directory || !credential || !password)
    throw new Error('Indicá directorio de fixtures y credenciales de prueba en el entorno.');
  const login = await fetch(`${baseUrl}/api/sessions`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ credential, password }),
  });
  if (!login.ok) throw new Error(`Inicio de sesión HTTP ${login.status}`);
  const token = login.headers.get('set-cookie')?.match(/^carnicerias\.session=([^;]+)/)?.[1];
  if (!token) throw new Error('No se recibió cookie de sesión.');
  const cookie = `carnicerias.session=${token}`;
  const contexts = await fetch(`${baseUrl}/api/operational-contexts`,
    { headers: { Cookie: cookie } }).then((response) => response.json());
  if (contexts.length !== 1 || contexts[0].branches.length !== 1)
    throw new Error('Se esperaba un único contexto de prueba.');
  const context = await fetch(`${baseUrl}/api/sessions/current/context`, {
    method: 'PUT', headers: { Cookie: cookie, 'Content-Type': 'application/json' },
    body: JSON.stringify({ companyId: contexts[0].companyId, branchId: contexts[0].branches[0].branchId }),
  });
  if (!context.ok) throw new Error(`Selección de contexto HTTP ${context.status}`);

  const files = await readdir(directory);
  const scenarios = [
    ['categories', 'categories'], ['products', 'products'],
    ['products-reimport', 'products'], ['price-lists', 'price-lists'],
    ['price-lists-update', 'price-lists'], ['customers', 'customers'],
  ];
  for (const [name, kind] of scenarios) {
    const filename = files.find((file) => new RegExp(`^import-${name}-\\d{14}\\.xlsx$`).test(file));
    if (!filename) throw new Error(`Falta el fixture ${name}.`);
    const bytes = await readFile(join(directory, filename));
    const headers = { Cookie: cookie, 'Content-Type': mime };
    const previewResponse = await fetch(`${baseUrl}/api/admin/imports/${kind}/preview`, {
      method: 'POST', headers, body: bytes,
    });
    const preview = await previewResponse.json();
    if (!previewResponse.ok || !preview.canApply)
      throw new Error(`${name} preview HTTP ${previewResponse.status}: ${JSON.stringify(preview)}`);
    const key = randomUUID();
    const applyHeaders = { ...headers, 'Idempotency-Key': key };
    const applyResponse = await fetch(`${baseUrl}/api/admin/imports/${kind}/apply`, {
      method: 'POST', headers: applyHeaders, body: bytes,
    });
    const applied = await applyResponse.json();
    if (!applyResponse.ok) throw new Error(`${name} apply HTTP ${applyResponse.status}: ${JSON.stringify(applied)}`);
    const replayResponse = await fetch(`${baseUrl}/api/admin/imports/${kind}/apply`, {
      method: 'POST', headers: applyHeaders, body: bytes,
    });
    const replay = await replayResponse.json();
    if (!replayResponse.ok || !isDeepStrictEqual(replay, applied))
      throw new Error(`${name}: el reintento no devolvió el resultado original.`);
    console.log(`${name}: altas=${applied.createCount}, cambios=${applied.updateCount}, ` +
      `no-importados=${applied.skippedCount}, sin-cambios=${applied.unchangedCount}; reintento OK`);
  }

  const suffix = files.find((file) => file.startsWith('import-products-'))?.match(/(\d{14})\.xlsx$/)?.[1];
  const products = await fetch(`${baseUrl}/api/admin/products?search=IMP-${suffix}`,
    { headers: { Cookie: cookie } }).then((response) => response.json());
  const product = products.items?.find((item) => item.code === `IMP-${suffix}`);
  if (!product || product.name !== 'Artículo de prueba importado' ||
      product.cost !== 1000 || product.alternateCodeCount !== 2)
    throw new Error('La reimportación sobrescribió el artículo o perdió códigos alternativos.');
  const lists = await fetch(`${baseUrl}/api/admin/price-lists?search=Importaci%C3%B3n%20${suffix}`,
    { headers: { Cookie: cookie } }).then((response) => response.json());
  const list = lists.items?.find((item) => item.name === `Importación ${suffix}`);
  if (!list) throw new Error('Falta la lista importada.');
  const prices = await fetch(`${baseUrl}/api/admin/price-lists/${list.id}/prices?search=IMP-${suffix}`,
    { headers: { Cookie: cookie } }).then((response) => response.json());
  const price = prices.items?.find((item) => item.productId === product.id);
  if (price?.currentPrice !== 800) throw new Error('No quedó el precio vigente de 800 ARS.');
  const history = await fetch(`${baseUrl}/api/admin/price-lists/${list.id}/products/${product.id}/history`,
    { headers: { Cookie: cookie } }).then((response) => response.json());
  if (history.length !== 2 || history[0].amount !== 800 || history[1].amount !== 900)
    throw new Error('El cambio de precio no conservó ambas vigencias.');

  const costUpdate = await fetch(`${baseUrl}/api/admin/products/${product.id}`, {
    method: 'PATCH', headers: { Cookie: cookie, 'Content-Type': 'application/json' },
    body: JSON.stringify({ cost: 1500 }),
  });
  if (!costUpdate.ok) throw new Error(`Cambio de costo HTTP ${costUpdate.status}`);
  const costs = await fetch(`${baseUrl}/api/admin/products/${product.id}/cost-history`,
    { headers: { Cookie: cookie } }).then((response) => response.json());
  if (costs.length !== 2 || costs[0].amount !== 1500 || costs[1].amount !== 1000)
    throw new Error('El cambio de costo no conservó ambas vigencias.');
  const customers = await fetch(`${baseUrl}/api/admin/customers?search=CLI-${suffix}`,
    { headers: { Cookie: cookie } }).then((response) => response.json());
  if (!customers.items?.some((item) => item.code === `CLI-${suffix}` && item.creditEnabled))
    throw new Error('El cliente importado no tiene cuenta corriente habilitada.');
  console.log('Artículo conservado, 2 códigos; precio 800 < costo 1500; ambos historiales y cliente con cuenta OK.');
}

main().catch((error) => { console.error(error.message); process.exitCode = 1; });
