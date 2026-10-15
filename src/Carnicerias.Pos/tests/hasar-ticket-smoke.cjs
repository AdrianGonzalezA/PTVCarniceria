// Explicit one-peso lab ticket. This issues a real ticket if the controller is fiscalized.
// Never run as part of an automated test suite.
const { HasarSerialSession } = require('../dist/hasar-serial.js');
const { issueHasarFiscalTicket } = require('../dist/hasar-ticket.js');

const confirmFiscalized = process.argv.includes('--confirm-fiscalized');
const port = process.argv.find((arg) => /^--port=COM[1-9][0-9]{0,2}$/i.test(arg))?.slice(7) || 'COM8';
const session = new HasarSerialSession({ port: port.toUpperCase(), baudRate: 9600 });

(async () => {
  try {
    await session.open();
    const result = await issueHasarFiscalTicket(session, {
      items: [{ name: 'PRUEBA POS LAB', quantity: 1, unitPrice: 1, vatRate: 21 }],
      total: 1,
      paymentName: 'Efectivo',
    }, { allowFiscalized: confirmFiscalized });
    process.stdout.write(JSON.stringify({ port, result }, null, 2) + '\n');
  } finally { await session.close(); }
})().catch((error) => {
  process.stderr.write(`${error instanceof Error ? error.message : String(error)}\n`);
  if (error instanceof Error && error.cause instanceof Error)
    process.stderr.write(`Cause: ${error.cause.message}\n`);
  process.exitCode = 1;
});
