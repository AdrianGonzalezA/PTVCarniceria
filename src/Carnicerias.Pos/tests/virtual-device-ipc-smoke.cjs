const fs = require('node:fs');

async function renderer(port, startsWith) {
  const targets = await (await fetch(`http://127.0.0.1:${port}/json`)).json();
  const target = targets.find((item) => item.type === 'page' && item.url.startsWith(startsWith));
  if (!target) throw new Error(`No Electron renderer on debug port ${port}`);
  const socket = new WebSocket(target.webSocketDebuggerUrl);
  await new Promise((resolve, reject) => {
    socket.addEventListener('open', resolve, { once: true });
    socket.addEventListener('error', reject, { once: true });
  });
  return socket;
}

function evaluate(socket, expression) {
  const id = Math.floor(Math.random() * 1000000000);
  socket.send(JSON.stringify({ id, method: 'Runtime.evaluate',
    params: { expression, awaitPromise: true, returnByValue: true } }));
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error('Electron IPC timed out')), 15000);
    const onMessage = (event) => {
      const message = JSON.parse(event.data);
      if (message.id !== id) return;
      clearTimeout(timer);
      socket.removeEventListener('message', onMessage);
      if (message.result?.exceptionDetails)
        reject(new Error(message.result.exceptionDetails.exception?.description ??
          message.result.exceptionDetails.text));
      else resolve(message.result?.result?.value);
    };
    socket.addEventListener('message', onMessage);
  });
}

async function main() {
  const emulator = await renderer(9224, 'file:///');
  const pos = await renderer(9223, 'app://bundle/');
  try {
    await evaluate(emulator, 'window.carniceriasEmulator.setWeight(0.75, true)');
    const reading = await evaluate(pos, 'window.carnicerias.readVirtualScale()');
    if (reading?.weightKg !== 0.75 || !reading.stable) throw new Error('POS did not receive virtual weight');
    const receipt = {
      saleId: 'DEVICE-IPC-SMOKE', branch: 'Sucursal de prueba', terminal: 'Caja de prueba', cashier: 'prueba',
      confirmedAtUtc: new Date().toISOString(), total: 2450, changeAmount: 0,
      lines: [{ code: '1002', name: 'Asado', unit: 'kg', quantity: 0.5, unitPrice: 4900, lineTotal: 2450 }],
      payments: [{ method: 'cash', tenderedAmount: 2450, appliedAmount: 2450 }],
    };
    const printed = await evaluate(pos, `window.carnicerias.printVirtualReceipt(${JSON.stringify(receipt)})`);
    if (!printed?.path || !fs.existsSync(printed.path) ||
        !fs.readFileSync(printed.path, 'utf8').includes('DEVICE-IPC-SMOKE'))
      throw new Error('Emulator did not persist a printed ticket');
    const state = await evaluate(emulator, 'window.carniceriasEmulator.getState()');
    if (state.printed[0]?.saleId !== receipt.saleId) throw new Error('Emulator did not display ticket');
    await new Promise((resolve) => setTimeout(resolve, 1200));
    const visible = await evaluate(emulator, 'document.querySelector("#tickets")?.textContent');
    if (!visible?.includes(receipt.saleId)) {
      const status = await evaluate(emulator, 'document.querySelector("#status")?.textContent');
      throw new Error(`Emulator UI did not show ticket: ${JSON.stringify({ visible, status })}`);
    }
    process.stdout.write(`Device IPC smoke passed: ${printed.path}\n`);
  } finally {
    emulator.close();
    pos.close();
  }
}

main().catch((error) => {
  process.stderr.write(`${error instanceof Error ? error.message : String(error)}\n`);
  process.exitCode = 1;
});
