async function main() {
  if (!process.argv.includes('--confirm-development-sale'))
    throw new Error('Se requiere confirmar expresamente la venta de prueba.');
  const pages = await fetch('http://localhost:9228/json/list').then((response) => response.json());
  const page = pages.find((item) => item.type === 'page' && item.url.startsWith('app://bundle/'));
  if (!page) throw new Error('No se encontró Caja 1 en Electron.');
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
    const response = await send('Runtime.evaluate', { expression, returnByValue: true });
    if (response.exceptionDetails) throw new Error(response.exceptionDetails.text);
    return response.result.value;
  };
  const waitFor = async (expression) => {
    for (let attempt = 0; attempt < 150; attempt++) {
      if (await evaluate(expression)) return;
      await new Promise((done) => setTimeout(done, 100));
    }
    throw new Error(`No se alcanzó: ${expression}. Vista: ${await evaluate('document.body.innerText.slice(-700)')}`);
  };
  await evaluate("document.querySelector('.account-collection-dialog .dialog-cancel')?.click()");
  await waitFor("!document.querySelector('.account-collection-dialog')");
  await evaluate("[...document.querySelectorAll('.ticket-tab')].find(item => item.innerText.trim().startsWith('Ticket A')).click()");
  await waitFor("document.querySelector('.ticket-tab[aria-current=page]')?.innerText.trim().startsWith('Ticket A')");
  await waitFor("document.querySelectorAll('.line-table tbody tr').length === 0");
  if (await evaluate("!!document.querySelector('.ticket-tab[aria-current=page]')?.innerText.includes('Guardado')"))
    throw new Error('El Ticket A ya tiene un borrador; no se altera.');
  await waitFor("[...document.querySelectorAll('.product-card')].some(item => item.innerText.includes('Gaseosa cola'))");
  await evaluate("[...document.querySelectorAll('.product-card')].find(item => item.innerText.includes('Gaseosa cola')).click()");
  await waitFor("!!document.querySelector('.pos-dialog .finish-button')");
  await evaluate("document.querySelector('.pos-dialog .finish-button').click()");
  await waitFor("document.querySelectorAll('.line-table tbody tr').length === 1 && !!document.querySelector('.price-list-lock')?.innerText.includes('Borrador guardado')");
  await evaluate("document.querySelector('.sale-footer .finish-button').click()");
  await waitFor("!!document.querySelector('#credit-applied') && document.querySelectorAll('#credit-customer option').length > 1");
  await evaluate(`(() => {
    const customer = document.querySelector('#credit-customer');
    customer.value = customer.options[1].value;
    customer.dispatchEvent(new Event('change', { bubbles: true }));
  })()`);
  await waitFor("document.querySelector('.credit-account-summary')?.innerText.includes('500,00')");
  await evaluate(`(() => {
    const input = document.querySelector('#credit-applied');
    input.value = '500';
    input.dispatchEvent(new Event('input', { bubbles: true }));
  })()`);
  await waitFor("document.querySelector('.payment-amount')?.value === '2000'");
  if (await evaluate("document.querySelector('.checkout-dialog .finish-button')?.disabled"))
    throw new Error(`La aplicación del saldo quedó inválida: ${await evaluate("document.querySelector('.checkout-dialog .payment-guidance')?.innerText")}`);
  await evaluate("document.querySelector('.checkout-dialog .finish-button').click()");
  await waitFor("!!document.querySelector('.sale-receipt') || !!document.querySelector('.checkout-dialog .pos-error')");
  const result = await evaluate(`({ receipt: document.querySelector('.sale-receipt')?.innerText ?? null,
    error: document.querySelector('.checkout-dialog .pos-error')?.innerText ?? null })`);
  if (result.error || !result.receipt?.includes('Saldo a favor aplicado') ||
      !result.receipt.includes('500,00'))
    throw new Error(`La venta no aplicó el saldo: ${result.error ?? result.receipt}`);
  socket.close();
  console.log(JSON.stringify(result, null, 2));
}

main().catch((error) => { console.error(error); process.exitCode = 1; });
