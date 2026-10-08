import net from 'node:net';
import { randomUUID } from 'node:crypto';
import { afterEach, describe, expect, it } from 'vitest';
import { createVirtualDeviceServer, requestVirtualDevice } from '../src/virtual-device-protocol';

const servers: net.Server[] = [];
afterEach(async () => {
  await Promise.all(servers.splice(0).map((server) => new Promise<void>((resolve) => server.close(() => resolve()))));
});

function pipeName(): string {
  return process.platform === 'win32'
    ? `\\\\.\\pipe\\carnicerias-test-${randomUUID()}`
    : `/tmp/carnicerias-test-${randomUUID()}.sock`;
}

describe('virtual devices', () => {
  it('reads a stable scale measurement from an independent process', async () => {
    const pipe = pipeName();
    const server = createVirtualDeviceServer(async (request) => request.action === 'read-scale'
      ? { ok: true, reading: { weightKg: 0.75, stable: true, observedAtUtc: '2026-10-08T15:00:00Z' } }
      : { ok: false, error: 'UNSUPPORTED' });
    servers.push(server);
    await new Promise<void>((resolve) => server.listen(pipe, resolve));

    await expect(requestVirtualDevice(pipe, { action: 'read-scale' })).resolves.toEqual({
      ok: true, reading: { weightKg: 0.75, stable: true, observedAtUtc: '2026-10-08T15:00:00Z' },
    });
  });

  it('returns a connection error when the emulator is not running', async () => {
    await expect(requestVirtualDevice(pipeName(), { action: 'read-scale' }))
      .rejects.toThrow('VIRTUAL_DEVICE_UNAVAILABLE');
  });

  it('delivers receipt detail with UTF-8 product names to the virtual printer', async () => {
    const pipe = pipeName();
    let receivedName = '';
    const server = createVirtualDeviceServer(async (request) => {
      if (request.action !== 'print-receipt') return { ok: false, error: 'UNSUPPORTED' };
      receivedName = request.receipt.lines[0].name;
      return { ok: true, path: 'ticket-virtual.html' };
    });
    servers.push(server);
    await new Promise<void>((resolve) => server.listen(pipe, resolve));
    const response = await requestVirtualDevice(pipe, { action: 'print-receipt', receipt: {
      saleId: 'venta-1', branch: 'Sucursal', terminal: 'Caja 1', cashier: 'cajero',
      confirmedAtUtc: '2026-10-08T15:00:00Z', total: 2450, changeAmount: 0,
      lines: [{ code: '1002', name: 'Asado de tira ñ', unit: 'kg', quantity: 0.5,
        unitPrice: 4900, lineTotal: 2450 }],
      payments: [{ method: 'cash', tenderedAmount: 2450, appliedAmount: 2450 }],
    } });
    expect(response).toEqual({ ok: true, path: 'ticket-virtual.html' });
    expect(receivedName).toBe('Asado de tira ñ');
  });
});
