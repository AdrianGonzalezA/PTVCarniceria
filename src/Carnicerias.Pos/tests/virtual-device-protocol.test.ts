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
    const server = createVirtualDeviceServer(async () => ({
      ok: true, reading: { weightKg: 0.75, stable: true, observedAtUtc: '2026-10-08T15:00:00Z' },
    }));
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

  it('rejects the retired virtual printer action', async () => {
    const pipe = pipeName();
    const server = createVirtualDeviceServer(async () => ({ ok: false, error: 'UNSUPPORTED' }));
    servers.push(server);
    await new Promise<void>((resolve) => server.listen(pipe, resolve));
    const response = await new Promise<string>((resolve, reject) => {
      const socket = net.createConnection(pipe);
      socket.once('connect', () => socket.write('{"action":"print-receipt"}\n'));
      socket.once('data', (data) => { resolve(data.toString('utf8')); socket.end(); });
      socket.once('error', reject);
    });
    expect(JSON.parse(response)).toEqual({ ok: false, error: 'INVALID_REQUEST' });
  });
});
