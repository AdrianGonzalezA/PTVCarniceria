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
    const result = await send('Runtime.evaluate', { expression, returnByValue: true });
    if (result.exceptionDetails) throw new Error(result.exceptionDetails.text);
    return result.result.value;
  };
  const waitFor = async (expression) => {
    for (let attempt = 0; attempt < 50; attempt++) {
      if (await evaluate(expression)) return;
      await new Promise((done) => setTimeout(done, 100));
    }
    throw new Error(`No se alcanzó el estado esperado: ${expression}`);
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
    await waitFor("!!document.querySelector('.context-form') || !!document.querySelector('.context-summary') || document.querySelector('h1')?.textContent === 'Administración'");
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
      await waitFor("!!document.querySelector('.context-summary') || document.querySelector('h1')?.textContent === 'Administración'");
    }
    if (await evaluate("document.querySelector('h1')?.textContent !== 'Administración'"))
      await evaluate("document.querySelector('a[href=\"/admin\"]')?.click()");
    await waitFor("document.querySelector('h1')?.textContent === 'Administración'");
  }
  if (process.argv.includes('--customers')) {
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
