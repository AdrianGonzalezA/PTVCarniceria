// One explicit, guarded development sale in Ticket A; leaves the saved Ticket D untouched.
async function main() {
  if (!process.argv.includes('--confirm-development-sale'))
    throw new Error('Se requiere --confirm-development-sale para crear una venta de prueba.');
  const pages = await fetch('http://localhost:9224/json/list').then((response) => response.json());
  const page = pages.find((item) => item.type === 'page' && item.url.startsWith('app://bundle/'));
  if (!page) throw new Error('No se encontró Caja 1 en Electron.');
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
    for (let attempt = 0; attempt < 150; attempt++) {
      if (await evaluate(expression)) return;
      await new Promise((done) => setTimeout(done, 100));
    }
    throw new Error(`No se alcanzó el estado esperado: ${expression}`);
  };
  if (!process.argv.includes('--resume-a')) {
    const guarded = await evaluate(`
      document.querySelector('.ticket-tab[aria-current=page]')?.innerText.includes('Ticket D') &&
      document.querySelector('.line-table')?.innerText.includes('Gaseosa cola') &&
      !!document.querySelector('.price-list-lock')?.innerText.includes('Borrador guardado')`);
    if (!guarded) throw new Error('El Ticket D no tiene el borrador esperado; no se crea otra venta.');
    await evaluate("[...document.querySelectorAll('.ticket-tab')].find(button => button.innerText.trim() === 'Ticket A').click()");
    await waitFor("document.querySelector('.ticket-tab[aria-current=page]')?.innerText.trim() === 'Ticket A'");
  } else if (!await evaluate("document.querySelector('.ticket-tab[aria-current=page]')?.innerText.trim() === 'Ticket A'")) {
    throw new Error('La reanudación solo está permitida en el Ticket A vacío.');
  }
  await waitFor("document.querySelectorAll('.line-table tbody tr').length === 0");
  if (await evaluate("!!document.querySelector('.ticket-tab[aria-current=page]')?.innerText.includes('Guardado')"))
    throw new Error('El Ticket A ya contiene un borrador guardado; no se modifica.');

  await waitFor("[...document.querySelectorAll('.product-card')].some(button => button.innerText.includes('Gaseosa cola') && button.innerText.includes('Disponible:'))");
  await evaluate("[...document.querySelectorAll('.product-card')].find(button => button.innerText.includes('Gaseosa cola'))?.click()");
  await waitFor("!!document.querySelector('.pos-dialog .finish-button')");
  await evaluate("document.querySelector('.pos-dialog .finish-button').click()");
  await waitFor("document.querySelectorAll('.line-table tbody tr').length === 1 && !!document.querySelector('.price-list-lock')?.innerText.includes('Borrador guardado')");

  await evaluate("document.querySelector('.sale-footer .finish-button').click()");
  await waitFor("!!document.querySelector('#account-charge') && document.querySelectorAll('#credit-customer option').length > 1");
  await evaluate(`(() => {
    const customer = document.querySelector('#credit-customer');
    customer.value = customer.options[1].value;
    customer.dispatchEvent(new Event('change', { bubbles: true }));
    const amount = document.querySelector('#account-charge');
    amount.value = '1000';
    amount.dispatchEvent(new Event('input', { bubbles: true }));
  })()`);
  await waitFor("document.querySelector('.payment-amount')?.value === '1500'");
  if (await evaluate("document.querySelector('.checkout-dialog .finish-button')?.disabled"))
    throw new Error('El cobro mixto quedó inválido en pantalla; no se confirma.');
  const promptShown = await evaluate(`(() => {
    let shown = false;
    const originalConfirm = window.confirm;
    try {
      window.confirm = () => { shown = true; return true; };
      document.querySelector('.checkout-dialog .finish-button').click();
    } finally { window.confirm = originalConfirm; }
    return shown;
  })()`);
  if (!promptShown) throw new Error('No apareció la confirmación adicional de cuenta corriente.');
  await waitFor("!!document.querySelector('.sale-receipt') || !!document.querySelector('.checkout-dialog .pos-error')");
  const result = await evaluate(`({
    receipt: document.querySelector('.sale-receipt')?.innerText ?? null,
    error: document.querySelector('.checkout-dialog .pos-error')?.innerText ?? null
  })`);
  if (result.error || !result.receipt?.includes('A cuenta corriente') ||
      !result.receipt.includes('1.000,00'))
    throw new Error(`No se confirmó correctamente la venta a cuenta: ${result.error ?? result.receipt}`);

  await evaluate("document.querySelector('.sale-receipt .finish-button').click()");
  await evaluate("[...document.querySelectorAll('.ticket-tab')].find(button => button.innerText.includes('Ticket D')).click()");
  await waitFor("document.querySelector('.ticket-tab[aria-current=page]')?.innerText.includes('Ticket D') && document.querySelector('.line-table')?.innerText.includes('Gaseosa cola') && !!document.querySelector('.price-list-lock')?.innerText.includes('Borrador guardado')");
  socket.close();
  console.log(JSON.stringify({ result, originalDraftRestored: true }, null, 2));
}

main().catch((error) => { console.error(error); process.exit(1); });
