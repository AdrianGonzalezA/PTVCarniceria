const { requestVirtualDevice, virtualDevicePipe } = require('../dist/virtual-device-protocol.js');

async function main() {
  const response = await requestVirtualDevice(virtualDevicePipe, { action: 'read-qr' });
  if (!response.ok) throw new Error(response.error);
  const lines = response.qrPayload.split('\n');
  if (!response.companyId || lines.length < 4 || lines.length > 7 ||
      lines.slice(1).some(line => !/^\d{13}$/.test(line)))
    throw new Error('VIRTUAL_QR_INVALID');
  process.stdout.write(JSON.stringify({ companyId: response.companyId,
    cuts: lines.length - 1, header: lines[0] }) + '\n');
}

main().catch(error => {
  process.stderr.write(`${error instanceof Error ? error.message : 'VIRTUAL_QR_ERROR'}\n`);
  process.exitCode = 1;
});
