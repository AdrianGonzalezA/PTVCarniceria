const { writeFileSync } = require('node:fs');
const { tmpdir } = require('node:os');
const { join } = require('node:path');

async function main() {
  const port = Number(process.env.CARNICERIAS_ELECTRON_DEBUG_PORT || 9228);
  const pages = await fetch(`http://localhost:${port}/json/list`).then((response) => response.json());
  const page = pages.find((item) => item.type === 'page' && item.url.startsWith('app://bundle/'));
  if (!page) throw new Error('No se encontró el POS en Electron.');
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
  const evaluate = async (expression) => {
    const response = await send('Runtime.evaluate', { expression, returnByValue: true,
      awaitPromise: true });
    if (response.exceptionDetails) throw new Error(response.exceptionDetails.text);
    return response.result.value;
  };
  let cancelResponse = null;
  let shiftSummary = null;
  const waitFor = async (expression) => {
    for (let attempt = 0; attempt < 100; attempt++) {
      if (await evaluate(expression)) return;
      await new Promise((done) => setTimeout(done, 100));
    }
    throw new Error(`No se alcanzó: ${expression}. Vista: ${await evaluate('document.body.innerText.slice(0, 500)')}`);
  };

  if (process.argv.includes('--reload')) {
    await send('Page.reload', { ignoreCache: true });
    await waitFor("!!document.querySelector('.pos-shell') || !!document.querySelector('.login-form')");
  }

  if (process.argv.includes('--login')) {
    const username = process.env.CARNICERIAS_SMOKE_USERNAME;
    const password = process.env.CARNICERIAS_SMOKE_PASSWORD;
    if (!username || !password) throw new Error('Faltan las credenciales de prueba en variables de entorno.');
    await waitFor("!!document.querySelector('.login-form')");
    await evaluate(`(() => {
      for (const [selector, value] of [['#credential', ${JSON.stringify(username)}],
        ['#password', ${JSON.stringify(password)}]]) {
        const input = document.querySelector(selector);
        input.value = value;
        input.dispatchEvent(new Event('input', { bubbles: true }));
      }
      document.querySelector('.login-form').requestSubmit();
    })()`);
    await waitFor("!!document.querySelector('.context-form') || !!document.querySelector('.pos-shell') || !!document.querySelector('.login-error')");
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
    }
  }

  if (process.argv.includes('--open')) {
    await waitFor("!![...document.querySelectorAll('button')].find(item => item.textContent.trim() === 'Cobrar cuenta')");
    await evaluate("[...document.querySelectorAll('button')].find(item => item.textContent.trim() === 'Cobrar cuenta').click()");
    await waitFor("!!document.querySelector('.account-collection-dialog')");
  }

  if (process.argv.includes('--select')) {
    await waitFor("document.querySelectorAll('#collection-customer option').length > 1");
    await evaluate(`(() => {
      const select = document.querySelector('#collection-customer');
      select.value = select.options[1].value;
      select.dispatchEvent(new Event('change', { bubbles: true }));
    })()`);
    await waitFor("!!document.querySelector('.account-balance-summary') && !document.querySelector('.credit-customer-search button').disabled");
  }

  if (process.argv.includes('--new')) {
    await waitFor("!![...document.querySelectorAll('.account-collection-dialog button')].find(item => item.textContent.trim() === 'Nuevo cobro')");
    await evaluate("[...document.querySelectorAll('.account-collection-dialog button')].find(item => item.textContent.trim() === 'Nuevo cobro').click()");
    await waitFor("!!document.querySelector('.account-balance-summary') && !document.querySelector('.credit-customer-search button').disabled");
  }

  if (process.argv.includes('--manual-credit')) {
    await waitFor("!!document.querySelector('.account-allocation-toggle input')");
    await evaluate(`(() => {
      const input = document.querySelector('.account-allocation-toggle input');
      input.checked = true;
      input.dispatchEvent(new Event('change', { bubbles: true }));
    })()`);
    await waitFor("!!document.querySelector('.account-allocation-list')");
  }

  if (process.argv.includes('--confirm-development-collection')) {
    const amount = Number(process.env.CARNICERIAS_SMOKE_COLLECTION_AMOUNT);
    if (!Number.isFinite(amount) || amount <= 0 || !await evaluate("!!document.querySelector('.account-balance-summary')"))
      throw new Error('Falta cuenta consultada o importe de prueba válido.');
    await evaluate(`(() => {
      const input = document.querySelector('#collection-amount');
      input.value = ${JSON.stringify(String(amount))};
      input.dispatchEvent(new Event('input', { bubbles: true }));
    })()`);
    if (await evaluate("document.querySelector('.account-collection-dialog .finish-button')?.disabled"))
      throw new Error('El cobro quedó inválido en la interfaz.');
    await evaluate("document.querySelector('.account-collection-dialog .finish-button').click()");
    await waitFor("!!document.querySelector('.account-collection-dialog .shift-summary[role=status]') || !!document.querySelector('.account-collection-dialog [role=alert]')");
  }

  if (process.argv.includes('--confirm-development-mixed-collection')) {
    const cash = Number(process.env.CARNICERIAS_SMOKE_COLLECTION_CASH);
    const debit = Number(process.env.CARNICERIAS_SMOKE_COLLECTION_DEBIT);
    const amount = process.env.CARNICERIAS_SMOKE_COLLECTION_AMOUNT
      ? Number(process.env.CARNICERIAS_SMOKE_COLLECTION_AMOUNT) : cash + debit;
    if (!Number.isFinite(cash) || cash <= 0 || !Number.isFinite(debit) || debit <= 0 ||
        !Number.isFinite(amount) || amount <= 0 || debit > amount || cash + debit < amount ||
        !await evaluate("!!document.querySelector('.account-balance-summary')"))
      throw new Error('Falta cuenta consultada o importes de prueba válidos.');
    await evaluate(`(() => {
      const amount = document.querySelector('#collection-amount');
      amount.value = ${JSON.stringify(String(amount))};
      amount.dispatchEvent(new Event('input', { bubbles: true }));
      const cashInput = document.querySelector('.collection-payment-methods input[data-method=cash]');
      cashInput.value = ${JSON.stringify(String(cash))};
      cashInput.dispatchEvent(new Event('input', { bubbles: true }));
      document.querySelector('.collection-payment-methods input[type=checkbox][value=debit]').click();
    })()`);
    await waitFor("!!document.querySelector('.collection-payment-methods input[data-method=debit]')");
    await evaluate(`(() => {
      const debitInput = document.querySelector('.collection-payment-methods input[data-method=debit]');
      debitInput.value = ${JSON.stringify(String(debit))};
      debitInput.dispatchEvent(new Event('input', { bubbles: true }));
    })()`);
    if (await evaluate("document.querySelector('.account-collection-dialog .finish-button')?.disabled"))
      throw new Error('El cobro combinado quedó inválido en la interfaz.');
    await evaluate("document.querySelector('.account-collection-dialog .finish-button').click()");
    await waitFor("!!document.querySelector('.account-collection-dialog .shift-summary[role=status]') || !!document.querySelector('.account-collection-dialog [role=alert]')");
  }

  if (process.argv.includes('--request-development-mercado-pago')) {
    const amount = Number(process.env.CARNICERIAS_SMOKE_COLLECTION_AMOUNT);
    const mercadoPago = Number(process.env.CARNICERIAS_SMOKE_COLLECTION_MP);
    const cash = amount - mercadoPago;
    if (!Number.isFinite(amount) || !Number.isFinite(mercadoPago) ||
        mercadoPago <= 0 || cash <= 0 || !await evaluate("!!document.querySelector('.account-balance-summary')"))
      throw new Error('Falta cuenta consultada o importes de prueba válidos.');
    await evaluate(`(() => {
      const amount = document.querySelector('#collection-amount');
      amount.value = ${JSON.stringify(String(amount))};
      amount.dispatchEvent(new Event('input', { bubbles: true }));
      const cashInput = document.querySelector('.collection-payment-methods input[data-method=cash]');
      cashInput.value = ${JSON.stringify(String(cash))};
      cashInput.dispatchEvent(new Event('input', { bubbles: true }));
      document.querySelector('.collection-payment-methods input[type=checkbox][value=mercadoPago]').click();
    })()`);
    await waitFor("!!document.querySelector('.collection-payment-methods input[data-method=mercadoPago]')");
    await evaluate(`(() => {
      const input = document.querySelector('.collection-payment-methods input[data-method=mercadoPago]');
      input.value = ${JSON.stringify(String(mercadoPago))};
      input.dispatchEvent(new Event('input', { bubbles: true }));
    })()`);
    await waitFor("!!document.querySelector('.mercado-pago-actions button') && !document.querySelector('.mercado-pago-actions button').disabled");
    await evaluate("document.querySelector('.mercado-pago-actions button').click()");
    await waitFor("!!document.querySelector('.mercado-pago-state')");
  }

  if (process.argv.includes('--cancel-development-mercado-pago')) {
    const cancel = "[...document.querySelectorAll('.mercado-pago-actions button')].find(item => item.textContent.trim() === 'Cancelar orden')";
    await waitFor(`!!(${cancel})`);
    await send('Network.enable');
    const onNetwork = (event) => {
      const message = JSON.parse(event.data);
      if (message.method === 'Network.responseReceived' &&
          message.params.response.url.endsWith('/cancel'))
        cancelResponse = { status: message.params.response.status,
          requestId: message.params.requestId };
    };
    socket.addEventListener('message', onNetwork);
    await evaluate(`${cancel}.click()`);
    for (let attempt = 0; attempt < 100 && !cancelResponse; attempt++)
      await new Promise((done) => setTimeout(done, 100));
    socket.removeEventListener('message', onNetwork);
    if (!cancelResponse) throw new Error('La API no respondió a la cancelación.');
    const body = await send('Network.getResponseBody', { requestId: cancelResponse.requestId });
    cancelResponse = { status: cancelResponse.status, code: JSON.parse(body.body)?.error?.code ?? null };
  }

  if (process.argv.includes('--shift-summary')) {
    shiftSummary = await evaluate(`fetch('/api/cashier-shifts/current', { credentials: 'include' })
      .then(async response => ({ status: response.status, body: response.ok ?
        await response.json() : null }))`);
  }

  const state = await evaluate(`({ headings: [...document.querySelectorAll('h1,h2')]
    .map(item => item.textContent.trim()).slice(0, 10),
    text: document.body.innerText.slice(0, 1500),
    errors: [...document.querySelectorAll('[role="alert"]')].map(item => item.textContent.trim()),
    collection: document.querySelector('.account-collection-dialog')?.innerText.slice(0, 1500) })`);
  const screenshot = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
  const path = join(tmpdir(), `carnicerias-account-collection-${port}.png`);
  writeFileSync(path, Buffer.from(screenshot.data, 'base64'));
  socket.close();
  console.log(JSON.stringify({ state, screenshot: path, cancelResponse, shiftSummary }, null, 2));
}

main().catch((error) => { console.error(error); process.exitCode = 1; });
