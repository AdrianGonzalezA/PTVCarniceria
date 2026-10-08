const { sendSerialReceipt } = require('../dist/serial-printer.js');

const receipt = {
  saleId: 'SERIAL-SMOKE', branch: 'Sucursal de prueba', terminal: 'Caja 1', cashier: 'prueba',
  confirmedAtUtc: new Date().toISOString(), total: 2450, changeAmount: 0,
  lines: [{ code: '1002', name: 'Asado', unit: 'kg', quantity: 0.5, unitPrice: 4900, lineTotal: 2450 }],
  payments: [{ method: 'cash', tenderedAmount: 2450, appliedAmount: 2450 }],
};

sendSerialReceipt(receipt).then((result) => {
  process.stdout.write(`${result.bytesWritten} bytes enviados a ${result.port} (9600/8N1).\n`);
  if (result.confirmation === 'write-only')
    process.stdout.write('El controlador virtual no confirmó el vaciado. Verificá SERIAL-SMOKE en PuTTY COM2.\n');
}).catch((error) => {
  process.stderr.write(`No se pudo completar el envío serial: ${error instanceof Error ? error.message : String(error)}\n`);
  process.exitCode = 1;
});
