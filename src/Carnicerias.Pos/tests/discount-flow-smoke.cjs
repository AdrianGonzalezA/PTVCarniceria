// Exercises one authorized discount sale in an already signed-in Electron cashier window.
async function main() {
  if (!process.argv.includes('--confirm-development-sale'))
    throw new Error('Se requiere --confirm-development-sale para registrar una venta ficticia.');
  const port = Number(process.argv.find((value) => value.startsWith('--port='))?.slice(7) ?? 9228);
  const pages = await fetch(`http://localhost:${port}/json/list`).then((response) => response.json());
  const page = pages.find((item) => item.type === 'page' && item.url.startsWith('app://bundle/'));
  if (!page) throw new Error('No se encontró la ventana del POS en Electron.');
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
    const result = await send('Runtime.evaluate', { expression, returnByValue: true });
    if (result.exceptionDetails) throw new Error(result.exceptionDetails.text);
    return result.result.value;
  };
  const waitFor = async (expression) => {
    for (let attempt = 0; attempt < 100; attempt++) {
      if (await evaluate(expression)) return;
      await new Promise((done) => setTimeout(done, 100));
    }
    throw new Error(`No se alcanzó el estado esperado: ${expression}`);
  };
  try {
    if (process.argv.includes('--verify-output-only')) {
      if (!await evaluate("document.querySelector('.sale-receipt')?.innerText.includes('Promoción de desarrollo')"))
        throw new Error('No hay un ticket descontado visible.');
      await evaluate("document.querySelector('.sale-receipt .save-pdf-button').click()");
      await waitFor("!!document.querySelector('.sale-receipt .receipt-file') || !!document.querySelector('.sale-receipt .pos-error')");
      const output = await evaluate("document.querySelector('.sale-receipt .receipt-file')?.innerText ?? document.querySelector('.sale-receipt .pos-error')?.innerText");
      if (!output.includes('.pdf')) throw new Error(`No se pudo guardar el PDF: ${output}`);
      console.log(output);
      return;
    }
    if (!await evaluate("document.querySelector('.ticket-tab[aria-current=page]')?.innerText.trim() === 'Ticket A'"))
      throw new Error('La prueba requiere Ticket A activo.');
    if (!await evaluate("document.querySelectorAll('.line-table tbody tr').length === 0"))
      throw new Error('La prueba requiere Ticket A vacío.');
    await waitFor("[...document.querySelectorAll('.product-card')].some(button => button.innerText.includes('Gaseosa cola') && button.innerText.includes('Disponible:'))");
    await evaluate("[...document.querySelectorAll('.product-card')].find(button => button.innerText.includes('Gaseosa cola')).click()");
    await waitFor("!!document.querySelector('.pos-dialog .finish-button')");
    await evaluate("document.querySelector('.pos-dialog .finish-button').click()");
    await waitFor("document.querySelectorAll('.line-table tbody tr').length === 1 && !!document.querySelector('.price-list-lock')?.innerText.includes('Borrador guardado')");
    await waitFor("!!document.querySelector('#sale-discount-amount')");
    await evaluate(`(() => { const input = document.querySelector('#sale-discount-amount');
      input.value = '125'; input.dispatchEvent(new Event('change', { bubbles: true })); })()`);
    await waitFor("!!document.querySelector('#sale-discount-reason')");
    await evaluate(`(() => { const input = document.querySelector('#sale-discount-reason');
      input.value = 'Promoción de desarrollo'; input.dispatchEvent(new Event('change', { bubbles: true })); })()`);
    await waitFor("document.querySelector('.sale-footer')?.innerText.includes('2.375,00') && !!document.querySelector('.price-list-lock')?.innerText.includes('Borrador guardado')");
    await send('Page.reload', { ignoreCache: true });
    await waitFor("document.querySelector('#sale-discount-amount')?.value === '125' && document.querySelector('#sale-discount-reason')?.value === 'Promoción de desarrollo'");
    await evaluate("document.querySelector('.sale-footer .finish-button').click()");
    await waitFor("!!document.querySelector('.checkout-dialog .payment-amount')");
    await evaluate(`(() => { const input = document.querySelector('.checkout-dialog .payment-amount');
      input.value = '2375'; input.dispatchEvent(new Event('input', { bubbles: true })); })()`);
    await waitFor("!document.querySelector('.checkout-dialog .finish-button')?.disabled");
    await evaluate("document.querySelector('.checkout-dialog .finish-button').click()");
    await waitFor("!!document.querySelector('.sale-receipt') || !!document.querySelector('.checkout-dialog .pos-error')");
    const result = await evaluate(`({ receipt: document.querySelector('.sale-receipt')?.innerText ?? null,
      error: document.querySelector('.checkout-dialog .pos-error')?.innerText ?? null })`);
    if (result.error || !result.receipt?.includes('2.375,00') || !result.receipt.includes('125,00'))
      throw new Error(`No se confirmó el descuento: ${result.error ?? result.receipt}`);
    console.log(JSON.stringify(result, null, 2));
  } finally {
    socket.close();
  }
}

main().catch((error) => { console.error(error); process.exitCode = 1; });
