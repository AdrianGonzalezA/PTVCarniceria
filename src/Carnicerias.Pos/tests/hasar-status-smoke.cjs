// Read-only lab probe. Sends only Hasar StatusRequest (*) and ACKs its response.
const { HasarSerialSession } = require('../dist/hasar-serial.js');
const { SerialPort } = require('serialport');

const port = process.argv[2] || 'COM8';
if (!/^COM[1-9][0-9]{0,2}$/i.test(port)) throw new Error('Invalid COM port');
const session = new HasarSerialSession({ port: port.toUpperCase(), baudRate: 9600 }, (options) => {
  const serial = new SerialPort(options);
  if (process.argv.includes('--trace'))
    serial.on('data', (bytes) => process.stderr.write(`RX ${bytes.toString('hex')}\n`));
  return serial;
});

(async () => {
  try {
    await session.open();
    const status = await session.queryStatus();
    process.stdout.write(JSON.stringify({ port, status }, null, 2) + '\n');
  } finally { await session.close(); }
})().catch((error) => {
  process.stderr.write(`${error instanceof Error ? error.message : String(error)}\n`);
  process.exitCode = 1;
});
