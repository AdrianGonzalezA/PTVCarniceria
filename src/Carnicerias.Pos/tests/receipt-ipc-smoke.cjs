const fs = require('node:fs');

async function main() {
  const targets = await (await fetch('http://127.0.0.1:9223/json')).json();
  const target = targets.find((item) => item.type === 'page' && item.url.startsWith('app://bundle/'));
  if (!target) throw new Error('No Electron renderer on debug port 9223');
  const socket = new WebSocket(target.webSocketDebuggerUrl);
  await new Promise((resolve, reject) => {
    socket.addEventListener('open', resolve, { once: true });
    socket.addEventListener('error', reject, { once: true });
  });
  const request = {
    saleId: 'IPC-SMOKE', branch: 'Sucursal de prueba', terminal: 'Caja de prueba', cashier: 'prueba',
    confirmedAtUtc: new Date().toISOString(), total: 2450, changeAmount: 0,
    lines: [{ code: '1002', name: 'Asado', unit: 'kg', quantity: 0.5, unitPrice: 4900, lineTotal: 2450 }],
    payments: [{ method: 'cash', tenderedAmount: 2450, appliedAmount: 2450 }],
  };
  const expression = `window.carnicerias.saveReceiptPdf(${JSON.stringify(request)})`;
  socket.send(JSON.stringify({ id: 1, method: 'Runtime.evaluate',
    params: { expression, awaitPromise: true, returnByValue: true } }));
  let result;
  try {
    result = await new Promise((resolve, reject) => {
      const timer = setTimeout(() => reject(new Error('Receipt IPC timed out')), 15000);
      socket.addEventListener('message', (event) => {
        const message = JSON.parse(event.data);
        if (message.id !== 1) return;
        clearTimeout(timer);
        if (message.result?.exceptionDetails)
          reject(new Error(message.result.exceptionDetails.exception?.description ??
            message.result.exceptionDetails.text));
        else resolve(message.result?.result?.value);
      });
    });
  } finally {
    socket.close();
  }
  if (!result?.path || !fs.existsSync(result.path) ||
      fs.readFileSync(result.path).subarray(0, 5).toString() !== '%PDF-')
    throw new Error('IPC returned no valid PDF file');
  process.stdout.write(`Receipt IPC smoke passed: ${result.path}\n`);
}

main().catch((error) => {
  process.stderr.write(`${error instanceof Error ? error.message : String(error)}\n`);
  process.exitCode = 1;
});
