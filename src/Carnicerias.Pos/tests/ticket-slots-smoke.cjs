const { writeFileSync } = require('node:fs');
const { tmpdir } = require('node:os');
const { resolve } = require('node:path');

async function main() {
  const pages = await fetch('http://localhost:9224/json/list').then((response) => response.json());
  const page = pages.find((item) => item.type === 'page' && item.url.startsWith('app://bundle/'));
  if (!page) throw new Error('No se encontró la ventana del POS en Electron.');

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
    const response = await send('Runtime.evaluate', { expression, returnByValue: true });
    if (response.exceptionDetails) throw new Error(response.exceptionDetails.text);
    return response.result.value;
  };
  const pause = (ms) => new Promise((done) => setTimeout(done, ms));
  const waitFor = async (expression) => {
    for (let attempt = 0; attempt < 50; attempt++) {
      if (await evaluate(expression)) return;
      await pause(100);
    }
    throw new Error(`No se alcanzó el estado esperado: ${expression}`);
  };
  const activeSlot = () => evaluate("document.querySelector('.ticket-tab[aria-current=page]')?.innerText.trim()");
  const lineCount = () => evaluate("document.querySelectorAll('.line-table tbody tr').length");
  const switchTo = async (slot) => {
    await evaluate(`([...document.querySelectorAll('.ticket-tab')].find(button => button.innerText.trim().startsWith('Ticket ${slot}')))?.click()`);
    await waitFor(`document.querySelector('.ticket-tab[aria-current=page]')?.innerText.trim().startsWith('Ticket ${slot}') && !!document.querySelector('.price-list-lock')?.innerText.includes('Borrador guardado')`);
  };
  const addProduct = async (name) => {
    await evaluate(`([...document.querySelectorAll('.product-card')].find(button => button.innerText.includes(${JSON.stringify(name)})))?.click()`);
    await waitFor("!!document.querySelector('.pos-dialog .finish-button')");
    await evaluate("document.querySelector('.pos-dialog .finish-button').click()");
    await waitFor("!!document.querySelector('.price-list-lock')?.innerText.includes('Borrador guardado') && document.querySelectorAll('.line-table tbody tr').length === 1");
  };

  if (process.argv.includes('--reload')) {
    await send('Page.reload', { ignoreCache: true });
    await waitFor("document.querySelectorAll('.ticket-tab').length === 4 && !!document.querySelector('.price-list-lock')?.innerText.includes('Borrador guardado') && !!document.querySelector('.ticket-tab[aria-current=page]')?.innerText.includes('Guardado')");
  }
  const switchOption = process.argv.find((argument) => /^--switch=[ABCD]$/.test(argument));
  if (switchOption) await switchTo(switchOption.at(-1));

  if (process.argv.includes('--check-saved-marker')) {
    if (!(await activeSlot())?.startsWith('Ticket C') || await lineCount() !== 0)
      throw new Error('El Ticket C no está vacío; se omite la carga para conservar los datos existentes.');
    await addProduct('Pan rallado');
    await waitFor("document.querySelector('.ticket-tab[aria-current=page]')?.innerText.includes('Guardado')");
    console.log('El Ticket C quedó guardado y marcado como ocupado en Electron.');
  }

  if (process.argv.includes('--prepare')) {
    if (!(await activeSlot())?.startsWith('Ticket A') || await lineCount() !== 0)
      throw new Error('El Ticket A no está vacío; se omite la prueba para conservar los datos existentes.');
    await switchTo('B');
    if (await lineCount() !== 0)
      throw new Error('El Ticket B no está vacío; se omite la prueba para conservar los datos existentes.');
    await switchTo('A');
    await addProduct('Gaseosa cola');
    await switchTo('B');
    await addProduct('Pan rallado');
    console.log('Preparados dos tickets independientes en PostgreSQL; reiniciar Electron y ejecutar --verify.');
  }

  if (process.argv.includes('--verify')) {
    await waitFor("!!document.querySelector('.price-list-lock')?.innerText.includes('Borrador guardado')");
    if (!(await activeSlot())?.startsWith('Ticket B') || await lineCount() !== 1 ||
        !await evaluate("document.querySelector('.line-table')?.innerText.includes('Pan rallado')"))
      throw new Error('El Ticket B no se recuperó después de reiniciar Electron.');
    await switchTo('A');
    if (await lineCount() !== 1 ||
        !await evaluate("document.querySelector('.line-table')?.innerText.includes('Gaseosa cola')"))
      throw new Error('El Ticket A no se conservó independientemente.');
    await switchTo('B');
    console.log('Tickets A y B restaurados con sus respectivos productos tras reiniciar Electron.');
  }

  const state = await send('Runtime.evaluate', { expression: `({
    url: location.href,
    title: document.title,
    text: document.body.innerText.slice(0, 3000),
    tickets: [...document.querySelectorAll('.ticket-tab')].map(button => ({
      label: button.innerText, disabled: button.disabled,
      active: button.getAttribute('aria-current') === 'page'
    })),
    ticketStorage: Object.keys(localStorage).filter(key => key.startsWith('carnicerias:ticket-slot:'))
      .map(key => ({ key, slot: localStorage.getItem(key) })),
    lines: [...document.querySelectorAll('.line-table tbody tr')].map(row => row.innerText)
  })`, returnByValue: true });
  const screenshot = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
  const screenshotPath = resolve(tmpdir(), 'electron-ticket-slots.png');
  writeFileSync(screenshotPath, Buffer.from(screenshot.data, 'base64'));
  socket.close();
  console.log(JSON.stringify({ ...state.result.value, screenshotPath }, null, 2));
}

main().catch((error) => { console.error(error); process.exit(1); });
