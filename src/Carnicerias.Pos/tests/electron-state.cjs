const { writeFileSync } = require('node:fs');
const { tmpdir } = require('node:os');
const { join } = require('node:path');

async function main() {
  const port = Number(process.argv.find((value) => value.startsWith('--port='))?.slice(7) ?? 9228);
  const pages = await fetch(`http://localhost:${port}/json/list`).then((response) => response.json());
  const page = pages.find((item) => item.type === 'page' && item.url.startsWith('app://bundle/'));
  if (!page) throw new Error('No se encontró la ventana de Electron.');
  const socket = new WebSocket(page.webSocketDebuggerUrl);
  await new Promise((resolve, reject) => {
    socket.addEventListener('open', resolve, { once: true });
    socket.addEventListener('error', reject, { once: true });
  });
  let nextId = 0;
  const send = (method, params = {}) => new Promise((resolve, reject) => {
    const id = ++nextId;
    const onMessage = (event) => {
      const response = JSON.parse(event.data);
      if (response.id !== id) return;
      socket.removeEventListener('message', onMessage);
      if (response.error) reject(new Error(response.error.message));
      else resolve(response.result);
    };
    socket.addEventListener('message', onMessage);
    socket.send(JSON.stringify({ id, method, params }));
  });
  const navigation = process.argv.find((item) => item.startsWith('--navigate='));
  if (navigation) {
    await send('Page.navigate', { url: navigation.slice(11) });
    await new Promise((done) => setTimeout(done, 1800));
  } else if (process.argv.includes('--reload')) {
    await send('Page.reload', { ignoreCache: true });
    await new Promise((done) => setTimeout(done, 1200));
  }
  const downloadPath = process.argv.find((item) => item.startsWith('--download-path='))?.slice(16);
  if (downloadPath) await send('Page.setDownloadBehavior', { behavior: 'allow', downloadPath });
  for (const field of ['credential', 'password']) {
    const value = process.argv.find((item) => item.startsWith(`--fill-${field}=`))?.slice(field.length + 8);
    if (value === undefined) continue;
    const selector = `[formControlName="${field}"]`;
    const filled = await send('Runtime.evaluate', {
      expression: `(() => { const input = document.querySelector(${JSON.stringify(selector)});
        if (!input) return false; const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
        setter.call(input, ${JSON.stringify(value)}); input.dispatchEvent(new Event('input', { bubbles: true })); return true; })()`,
      returnByValue: true,
    });
    if (!filled.result.value) throw new Error(`No se encontrÃ³ el campo: ${selector}`);
  }
  if (process.argv.includes('--select-context')) {
    const selected = await send('Runtime.evaluate', {
      expression: `(() => { for (const id of ['company', 'branch']) {
        const input = document.getElementById(id); const option = input?.querySelector('option[value]:not([value=""])');
        if (!option) return false; input.value = option.value;
        input.dispatchEvent(new Event('change', { bubbles: true })); } return true; })()`,
      returnByValue: true,
    });
    if (!selected.result.value) throw new Error('No se pudo seleccionar la empresa y sucursal.');
  }
  const paymentMode = process.argv.find((item) => item.startsWith('--select-payment-mode='))?.slice(22);
  if (paymentMode) {
    if (!['point', 'qr'].includes(paymentMode)) throw new Error('Modo de pago inválido.');
    const selected = await send('Runtime.evaluate', {
      expression: `(() => { const input = document.getElementById('mercado-pago-mode');
        if (!input || input.disabled) return false; input.value = ${JSON.stringify(paymentMode)};
        input.dispatchEvent(new Event('change', { bubbles: true })); return true; })()`,
      returnByValue: true,
    });
    if (!selected.result.value) throw new Error('No se pudo seleccionar el modo de Mercado Pago.');
  }
  const clickSelector = process.argv.find((item) => item.startsWith('--click-selector='))?.slice(17);
  if (clickSelector) {
    const clicked = await send('Runtime.evaluate', {
      expression: `(() => { const button = document.querySelector(${JSON.stringify(clickSelector)});
        if (!button || button.disabled) return false; button.click(); return true; })()`,
      returnByValue: true,
    });
    if (!clicked.result.value) throw new Error(`No se pudo abrir la acción: ${clickSelector}`);
    await new Promise((done) => setTimeout(done, 400));
  }
  const selectedFile = process.argv.find((item) => item.startsWith('--select-file='))?.slice(14);
  if (selectedFile) {
    const fileSelector = process.argv.find((item) => item.startsWith('--file-selector='))?.slice(16) ?? 'dialog input[type="file"]';
    const document = await send('DOM.getDocument', { depth: 1 });
    const input = await send('DOM.querySelector', {
      nodeId: document.root.nodeId, selector: fileSelector,
    });
    if (!input.nodeId) throw new Error('No se encontró el selector de archivo del modal.');
    await send('DOM.setFileInputFiles', { files: [selectedFile], nodeId: input.nodeId });
    await new Promise((done) => setTimeout(done, 250));
  }
  const afterSelect = process.argv.find((item) => item.startsWith('--click-after-select='))?.slice(21);
  if (afterSelect) {
    const clicked = await send('Runtime.evaluate', {
      expression: `(() => { const button = document.querySelector(${JSON.stringify(afterSelect)});
        if (!button || button.disabled) return false; button.click(); return true; })()`,
      returnByValue: true,
    });
    if (!clicked.result.value) throw new Error(`No se pudo abrir la acción: ${afterSelect}`);
    await new Promise((done) => setTimeout(done, 1000));
  }
  if (process.argv.includes('--click-first-detail')) {
    await send('Runtime.evaluate', { expression: "document.querySelector('table tbody button')?.click()" });
    await new Promise((done) => setTimeout(done, 450));
    await send('Runtime.evaluate', { expression: "document.querySelector('.ticket-detail')?.scrollIntoView()" });
  }
  const state = await send('Runtime.evaluate', {
    expression: `({ title: document.title, readyState: document.readyState,
      location: location.href, htmlLength: document.documentElement?.outerHTML.length,
      headings: [...document.querySelectorAll('h1,h2')]
      .map(item => item.textContent?.trim()).slice(0, 12),
      buttons: [...document.querySelectorAll('button')].map(item => item.textContent?.trim()).filter(Boolean).slice(0, 30),
      text: document.body.innerText.slice(0, 1200),
      dialogs: [...document.querySelectorAll('dialog[open]')].map(dialog => ({
        label: document.getElementById(dialog.getAttribute('aria-labelledby'))?.textContent?.trim(),
        width: Math.round(dialog.getBoundingClientRect().width),
        visible: dialog.getBoundingClientRect().top >= 0 &&
          dialog.getBoundingClientRect().bottom <= innerHeight,
      })),
      detail: document.querySelector('.ticket-detail')?.innerText ?? null,
      mercadoPago: document.querySelector('.mercado-pago-panel')?.innerText ?? null })`, returnByValue: true,
  });
  console.log(JSON.stringify(state.result.value, null, 2));
  if (process.argv.includes('--screenshot')) {
    const screenshot = await send('Page.captureScreenshot', { format: 'png',
      captureBeyondViewport: process.argv.includes('--full-screenshot') });
    const path = join(tmpdir(), `carnicerias-electron-${port}.png`);
    writeFileSync(path, Buffer.from(screenshot.data, 'base64'));
    console.log(`Captura: ${path}`);
  }
  socket.close();
}

main().catch((error) => { console.error(error); process.exitCode = 1; });
