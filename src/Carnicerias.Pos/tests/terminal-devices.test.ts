import { describe, expect, it, vi } from 'vitest';
import { fetchAssignedDevices, parseAssignedDevices, TerminalDevices } from '../src/terminal-devices';

describe('terminal device assignments', () => {
  const none = { scale: null, printer: null };

  it('fetches only for a provisioned terminal and never invents device defaults', async () => {
    const request = vi.fn(async () => new Response(JSON.stringify({ devices: none }), { status: 200 }));
    expect(await fetchAssignedDevices('http://localhost:5197', undefined, request)).toEqual(none);
    expect(request).not.toHaveBeenCalled();

    expect(await fetchAssignedDevices('http://localhost:5197', 'terminal-secret', request)).toEqual(none);
    const sent = request.mock.calls[0]?.[0] as Request;
    expect(sent.url).toBe('http://localhost:5197/api/pos-terminals/current');
    expect(sent.headers.get('X-Pos-Terminal-Credential')).toBe('terminal-secret');
  });

  it('accepts a valid assignment and rejects invalid, shared or mismatched ports', () => {
    expect(parseAssignedDevices({ devices: {
      scale: { port: 'COM6', baudRate: 9600 },
      printer: { kind: 'hasar', port: 'COM8', baudRate: 9600 },
    } })).toEqual({ scale: { port: 'COM6', baudRate: 9600 },
      printer: { kind: 'hasar', port: 'COM8', baudRate: 9600 } });
    expect(() => parseAssignedDevices({ devices: {
      scale: { port: 'COM6', baudRate: 9600 },
      printer: { kind: 'text', port: 'COM6', baudRate: 9600 },
    } })).toThrow('POS_DEVICE_CONFIG_INVALID');
    expect(() => parseAssignedDevices({ devices: {
      scale: null, printer: { kind: 'hasar', port: 'COM8', baudRate: 19200 },
    } })).toThrow('POS_DEVICE_CONFIG_INVALID');
    expect(() => parseAssignedDevices({ devices: { scale: null } }))
      .toThrow('POS_DEVICE_CONFIG_INVALID');
  });

  it('does not accept an expired terminal credential as a device assignment', async () => {
    await expect(fetchAssignedDevices('http://localhost:5197', 'expired',
      async () => new Response(null, { status: 401 })))
      .rejects.toThrow('POS_DEVICE_CONFIG_UNAVAILABLE');
  });

  it('does not open or read a scale when unassigned, and closes it when unassigned later', async () => {
    const start = vi.fn();
    const stop = vi.fn(async () => undefined);
    const read = vi.fn(() => ({ weightKg: 1, stable: true, observedAtUtc: new Date().toISOString() }));
    const createScale = vi.fn(() => ({ start, stop, read }));
    const devices = new TerminalDevices(createScale);
    expect(devices.availability()).toEqual(none);
    expect(() => devices.readScale()).toThrow('SERIAL_SCALE_NOT_CONFIGURED');
    expect(createScale).not.toHaveBeenCalled();

    await devices.apply({ scale: { port: 'COM6', baudRate: 9600 }, printer: null });
    expect(start).toHaveBeenCalledOnce();
    expect(devices.readScale().weightKg).toBe(1);
    await devices.apply(none);
    expect(stop).toHaveBeenCalledOnce();
    expect(() => devices.readScale()).toThrow('SERIAL_SCALE_NOT_CONFIGURED');
  });

  it('never supplies a printer port when no printer is assigned', async () => {
    const devices = new TerminalDevices();
    expect(() => devices.textPrinter()).toThrow('PRINTER_NOT_CONFIGURED');
    expect(() => devices.hasarPrinter()).toThrow('HASAR_PRINTER_NOT_CONFIGURED');
    await devices.apply({ scale: null, printer: { kind: 'text', port: 'COM1', baudRate: 9600 } });
    expect(devices.textPrinter().port).toBe('COM1');
    expect(() => devices.hasarPrinter()).toThrow('HASAR_PRINTER_NOT_CONFIGURED');
  });
});
