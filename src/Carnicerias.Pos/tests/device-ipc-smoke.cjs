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
  const pos = await renderer(Number(process.argv[2] ?? 9223), 'app://bundle/');
  try {
    if (process.argv.includes('--scale') || process.argv.includes('--scale-only')) {
      const reading = await evaluate(pos, 'window.carnicerias.readSerialScale()');
      if (reading?.weightKg !== 0.75 || !reading.stable)
        throw new Error('POS did not receive 0.750 kg from COM6');
      process.stdout.write(`Scale IPC smoke passed: ${reading.weightKg} kg from COM6\n`);
    }
    if (process.argv.includes('--scale-only')) return;
    const receipt = {
      saleId: 'SERIAL-IPC-SMOKE', branch: 'Sucursal de prueba', terminal: 'Caja de prueba', cashier: 'prueba',
      confirmedAtUtc: new Date().toISOString(), total: 2450, changeAmount: 0,
      lines: [{ code: '1002', name: 'Asado', unit: 'kg', quantity: 0.5, unitPrice: 4900, lineTotal: 2450 }],
      payments: [{ method: 'cash', tenderedAmount: 2450, appliedAmount: 2450 }],
    };
    const printed = await evaluate(pos, `window.carnicerias.printSerialReceipt(${JSON.stringify(receipt)})`);
    if (printed?.port !== 'COM1' || printed.bytesWritten <= 0 ||
        !['drained', 'write-only'].includes(printed.confirmation))
      throw new Error('POS did not send ticket to COM1');
    process.stdout.write(`Device IPC smoke passed: ${printed.port}, ${printed.bytesWritten} bytes, ${printed.confirmation}\n`);
  } finally {
    pos.close();
  }
}

main().catch((error) => {
  process.stderr.write(`${error instanceof Error ? error.message : String(error)}\n`);
  process.exitCode = 1;
});
