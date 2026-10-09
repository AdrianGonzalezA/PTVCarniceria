const { writeFileSync } = require('node:fs');
const { tmpdir } = require('node:os');
const { resolve } = require('node:path');

async function main() {
  const port = Number(process.env.CARNICERIAS_ELECTRON_DEBUG_PORT || 9225);
  const pages = await fetch(`http://localhost:${port}/json/list`).then((response) => response.json());
  const page = pages.find((item) => item.type === 'page' && item.url.startsWith('app://bundle/'));
  if (!page) throw new Error('No se encontró la ventana de administración en Electron.');
  const socket = new WebSocket(page.webSocketDebuggerUrl);
  await new Promise((resolveOpen, reject) => {
    socket.addEventListener('open', resolveOpen, { once: true });
    socket.addEventListener('error', reject, { once: true });
  });
  let nextId = 0;
  const send = (method, params = {}) => new Promise((resolveResponse, reject) => {
    const id = ++nextId;
    const onMessage = (event) => {
      const response = JSON.parse(event.data);
      if (response.id !== id) return;
      socket.removeEventListener('message', onMessage);
      if (response.error) reject(new Error(response.error.message));
      else resolveResponse(response.result);
    };
    socket.addEventListener('message', onMessage);
    socket.send(JSON.stringify({ id, method, params }));
  });
  const evaluate = async (expression) => {
    const result = await send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
    if (result.exceptionDetails) throw new Error(result.exceptionDetails.text);
    return result.result.value;
  };
  const waitFor = async (expression) => {
    for (let attempt = 0; attempt < 50; attempt++) {
      if (await evaluate(expression)) return;
      await new Promise((done) => setTimeout(done, 100));
    }
    throw new Error(`No se alcanzó el estado esperado: ${expression}; vista=${JSON.stringify(await evaluate("({url: location.href, heading: document.querySelector('h1')?.textContent, text: document.body.innerText.slice(0, 300)})"))}`);
  };
  if (process.argv.includes('--home')) {
    await send('Page.navigate', { url: 'app://bundle/index.html' });
    await new Promise((done) => setTimeout(done, 500));
  }
  if (process.argv.includes('--login')) {
    const username = process.env.CARNICERIAS_SMOKE_USERNAME;
    const password = process.env.CARNICERIAS_SMOKE_PASSWORD;
    if (!username || !password) throw new Error('Faltan credenciales de prueba en variables de entorno.');
    await waitFor("!!document.querySelector('.login-form')");
    await evaluate(`(() => {
      for (const [selector, value] of [['#credential', ${JSON.stringify(username)}], ['#password', ${JSON.stringify(password)}]]) {
        const input = document.querySelector(selector);
        input.value = value;
        input.dispatchEvent(new Event('input', { bubbles: true }));
      }
      document.querySelector('.login-form').requestSubmit();
    })()`);
    await waitFor("!!document.querySelector('.context-form') || !!document.querySelector('.context-summary') || document.querySelector('h1')?.textContent === 'Negocio'");
    if (await evaluate("!!document.querySelector('.context-form')")) {
      await waitFor("document.querySelectorAll('#company option').length > 1");
      await evaluate(`(() => {
        const company = document.querySelector('#company');
        company.value = company.options[1].value;
        company.dispatchEvent(new Event('change', { bubbles: true }));
      })()`);
      await waitFor("document.querySelectorAll('#branch option').length > 1");
      await evaluate(`(() => {
        const branch = document.querySelector('#branch');
        branch.value = branch.options[1].value;
        branch.dispatchEvent(new Event('change', { bubbles: true }));
        document.querySelector('.context-form').requestSubmit();
      })()`);
      await waitFor("!!document.querySelector('.context-summary') || document.querySelector('h1')?.textContent === 'Negocio'");
    }
    if (await evaluate("document.querySelector('h1')?.textContent !== 'Negocio'"))
      await evaluate("document.querySelector('a[href=\"/admin\"]')?.click()");
    await waitFor("document.querySelector('h1')?.textContent === 'Negocio'");
  }
  if (process.argv.includes('--areas')) {
    await send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 720, deviceScaleFactor: 1, mobile: false });
    await send('Page.navigate', { url: 'app://bundle/admin' });
    await waitFor("document.querySelector('h1')?.textContent === 'Negocio'");
    await waitFor("!!document.querySelector('.summary-card') || !!document.querySelector('.summary-status[role=alert]')");
    const summaryCheck = await evaluate(`(async () => {
      const request = async (path) => {
        const response = await fetch(path, { credentials: 'include' });
        return { status: response.status, body: await response.json() };
      };
      const all = await request('/api/admin/history/summary');
      const range = await request('/api/admin/history/summary?fromUtc=' +
        encodeURIComponent(all.body.fromUtc) + '&toUtc=' + encodeURIComponent(all.body.toUtc));
      const reversed = await request('/api/admin/history/summary?fromUtc=' +
        encodeURIComponent(all.body.toUtc) + '&toUtc=' + encodeURIComponent(all.body.fromUtc));
      const branchId = [...document.querySelector('#summary-branch').options]
        .find(option => option.textContent === 'Sucursal Visual')?.value;
      const branch = await request('/api/admin/history/summary?branchId=' + branchId);
      const invalid = await request('/api/admin/history/summary?branchId=00000000-0000-0000-0000-000000000000');
      const unknown = await request('/api/admin/history/summary?branchId=ffffffff-ffff-ffff-ffff-ffffffffffff');
      return { all, range, branch, reversedStatus: reversed.status,
        invalidStatus: invalid.status, unknownStatus: unknown.status };
    })()`);
    if (summaryCheck.all.status !== 200 || summaryCheck.range.status !== 200 ||
        summaryCheck.range.body.salesTotal !== summaryCheck.all.body.salesTotal ||
        summaryCheck.branch.status !== 200 || summaryCheck.reversedStatus !== 400 ||
        summaryCheck.invalidStatus !== 400 || summaryCheck.unknownStatus !== 404 ||
        Math.abs(summaryCheck.all.body.salesTotal - summaryCheck.all.body.immediateSalePayments -
          summaryCheck.all.body.newAccountCharges) > 0.01)
      throw new Error('El resumen o la validación de sucursal no coincide con las operaciones persistidas.');
    const business = await evaluate("({ text: document.body.innerText.slice(0, 2000), links: [...document.querySelectorAll('.module-link')].map(link => link.getAttribute('href')) })");
    const businessImage = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
    const businessPath = resolve(tmpdir(), 'electron-admin-business.png');
    writeFileSync(businessPath, Buffer.from(businessImage.data, 'base64'));
    await evaluate("document.querySelector('a[href=\"/admin/configuracion\"]').click()");
    await waitFor("document.querySelector('h1')?.textContent === 'Configuración'");
    const configuration = await evaluate("({ text: document.body.innerText.slice(0, 2000), links: [...document.querySelectorAll('.module-link')].map(link => link.getAttribute('href')) })");
    const configurationImage = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
    const configurationPath = resolve(tmpdir(), 'electron-admin-configuration.png');
    writeFileSync(configurationPath, Buffer.from(configurationImage.data, 'base64'));
    await send('Page.navigate', { url: 'app://bundle/admin/pieces' });
    await waitFor("document.querySelector('h1')?.textContent === 'Recepción de piezas'");
    const receptionArea = await evaluate("document.querySelector('nav[aria-label=\"Áreas de administración\"] a[aria-current=\"page\"]')?.textContent?.trim()");
    if (receptionArea !== 'Negocio') throw new Error('Recepción de piezas no conserva el área Negocio.');
    await evaluate('window.scrollTo(0, 0)');
    const receptionImage = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
    const receptionPath = resolve(tmpdir(), 'electron-admin-reception.png');
    writeFileSync(receptionPath, Buffer.from(receptionImage.data, 'base64'));
    await send('Page.navigate', { url: 'app://bundle/admin/configuracion' });
    await waitFor("document.querySelector('h1')?.textContent === 'Configuración'");
    await evaluate("document.querySelector('a[href=\"/admin/products\"]')?.click()");
    await waitFor("document.querySelector('h1')?.textContent === 'Artículos'");
    const productsArea = await evaluate("document.querySelector('nav[aria-label=\"Áreas de administración\"] a[aria-current=\"page\"]')?.textContent?.trim()");
    if (productsArea !== 'Configuración') throw new Error('Artículos no conserva el área Configuración.');
    await send('Emulation.clearDeviceMetricsOverride');
    socket.close();
    console.log(JSON.stringify({ business, businessPath, configuration, configurationPath, receptionArea, receptionPath, productsArea, summaryCheck }, null, 2));
    return;
  }
  if (process.argv.includes('--customers')) {
    if (!await evaluate("!!document.querySelector('a[href=\"/admin/customers\"]')")) {
      await evaluate("document.querySelector('a[href=\"/admin/configuracion\"]')?.click()");
      await waitFor("!!document.querySelector('a[href=\"/admin/customers\"]')");
    }
    const hasLink = await evaluate("!!document.querySelector('a[href=\"/admin/customers\"]')");
    if (!hasLink) throw new Error('No aparece el módulo Clientes en la administración actual.');
    await evaluate("document.querySelector('a[href=\"/admin/customers\"]').click()");
    for (let attempt = 0; attempt < 40; attempt++) {
      if (await evaluate("document.querySelector('h1')?.textContent === 'Clientes'")) break;
      await new Promise((done) => setTimeout(done, 100));
    }
    await waitFor("document.querySelector('.category-panel')?.getAttribute('aria-busy') === 'false'");
  }
  if (process.argv.includes('--create-test')) {
    await waitFor("document.querySelector('h1')?.textContent === 'Clientes'");
    const existing = await evaluate("[...document.querySelectorAll('tbody tr')].some(row => row.innerText.includes('CLI-CC-PRUEBA'))");
    if (!existing) {
      await evaluate("[...document.querySelectorAll('button')].find(button => button.innerText.trim() === 'Nuevo cliente').click()");
      await waitFor("!!document.querySelector('.editor-form')");
      await evaluate(`(() => {
        for (const [selector, value] of [['#customer-code', 'CLI-CC-PRUEBA'], ['#customer-name', 'Cliente prueba cuenta corriente']]) {
          const input = document.querySelector(selector);
          input.value = value;
          input.dispatchEvent(new Event('input', { bubbles: true }));
        }
        document.querySelector('.editor-form').requestSubmit();
      })()`);
      await waitFor("[...document.querySelectorAll('tbody tr')].some(row => row.innerText.includes('CLI-CC-PRUEBA'))");
    }
    const enabled = await evaluate("[...document.querySelectorAll('tbody tr')].find(row => row.innerText.includes('CLI-CC-PRUEBA'))?.innerText.includes('Habilitada')");
    if (!enabled) {
      await evaluate(`(() => {
        const row = [...document.querySelectorAll('tbody tr')].find(item => item.innerText.includes('CLI-CC-PRUEBA'));
        const originalConfirm = window.confirm;
        try {
          window.confirm = () => true;
          [...row.querySelectorAll('button')].find(button => button.innerText.includes('Habilitar cuenta')).click();
        } finally { window.confirm = originalConfirm; }
      })()`);
      await waitFor("[...document.querySelectorAll('tbody tr')].find(row => row.innerText.includes('CLI-CC-PRUEBA'))?.innerText.includes('Habilitada')");
    }
  }
  const state = await evaluate(`({
    url: location.href,
    heading: document.querySelector('h1')?.textContent,
    text: document.body.innerText.slice(0, 1800),
    hasError: !!document.querySelector('[role=alert]'),
    buttons: [...document.querySelectorAll('button')].map(button => button.innerText.trim()).filter(Boolean).slice(0, 30),
    links: [...document.querySelectorAll('a')].map(link => ({ text: link.innerText.trim(), href: link.getAttribute('href') })).slice(0, 30)
  })`);
  const screenshot = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
  const screenshotPath = resolve(tmpdir(), 'electron-admin-customers.png');
  writeFileSync(screenshotPath, Buffer.from(screenshot.data, 'base64'));
  socket.close();
  console.log(JSON.stringify({ ...state, screenshotPath }, null, 2));
}

main().catch((error) => { console.error(error); process.exit(1); });
