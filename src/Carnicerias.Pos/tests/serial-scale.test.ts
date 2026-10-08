import { afterEach, describe, expect, it } from 'vitest';
import { SerialPortMock } from 'serialport';
import { SerialScale, parseScaleFrame } from '../src/serial-scale';

afterEach(() => SerialPortMock.binding.reset());

describe('serial scale', () => {
  it('parses stable and unstable kilogram frames, rejecting malformed weights', () => {
    expect(parseScaleFrame('ST,0.750,kg')).toEqual({ weightKg: 0.75, stable: true });
    expect(parseScaleFrame('US,1.250,kg')).toEqual({ weightKg: 1.25, stable: false });
    expect(parseScaleFrame('ST,0,kg')).toBeNull();
    expect(parseScaleFrame('ST,1.2345,kg')).toBeNull();
    expect(parseScaleFrame('ST,-1,kg')).toBeNull();
    expect(parseScaleFrame('ST,1,lb')).toBeNull();
    expect(parseScaleFrame('ST,1,kg\x1b')).toBeNull();
  });

  it('listens continuously on COM6 at 9600 8N1 and accepts CR, LF or CRLF', async () => {
    SerialPortMock.binding.createPort('COM6', { record: true });
    let port: SerialPortMock | undefined;
    const scale = new SerialScale((options) => {
      expect(options).toMatchObject({ path: 'COM6', baudRate: 9600, dataBits: 8,
        stopBits: 1, parity: 'none', autoOpen: false });
      port = new SerialPortMock(options);
      return port;
    });
    scale.start();
    await scale.whenOpen();
    const binding = port!.port!;
    binding.emitData('ST,0.');
    binding.emitData('750,kg\r');
    await new Promise((resolve) => setImmediate(resolve));
    expect(scale.read()).toMatchObject({ weightKg: 0.75, stable: true });
    binding.emitData('US,1.250,kg\n');
    await new Promise((resolve) => setImmediate(resolve));
    expect(scale.read()).toMatchObject({ weightKg: 1.25, stable: false });
    binding.emitData('ST,2.500,kg\r\n');
    await new Promise((resolve) => setImmediate(resolve));
    expect(scale.read()).toMatchObject({ weightKg: 2.5, stable: true });
    await scale.stop();
  });

  it('does not return a stale, malformed, or missing reading', async () => {
    SerialPortMock.binding.createPort('COM6', { record: true });
    let port: SerialPortMock | undefined;
    const scale = new SerialScale((options) => {
      port = new SerialPortMock(options);
      return port;
    });
    scale.start();
    await scale.whenOpen();
    expect(() => scale.read()).toThrow('SERIAL_SCALE_NO_READING');
    const binding = port!.port!;
    binding.emitData('ST,1.000,kg\r');
    await new Promise((resolve) => setImmediate(resolve));
    expect(scale.read().weightKg).toBe(1);
    binding.emitData('garbage\r');
    await new Promise((resolve) => setImmediate(resolve));
    expect(() => scale.read()).toThrow('SERIAL_SCALE_INVALID_READING');
    binding.emitData('ST,1.000,kg\x1bST,3.000,kg\r');
    await new Promise((resolve) => setImmediate(resolve));
    expect(() => scale.read()).toThrow('SERIAL_SCALE_INVALID_READING');
    binding.emitData('ST,2.000,kg\r');
    await new Promise((resolve) => setImmediate(resolve));
    expect(() => scale.read(Date.now() + 31000)).toThrow('SERIAL_SCALE_STALE_READING');
    await scale.stop();
  });

  it('keeps the POS available when COM6 cannot be opened', async () => {
    const scale = new SerialScale((options) => new SerialPortMock(options));
    scale.start();
    await scale.whenOpen();
    expect(() => scale.read()).toThrow('SERIAL_SCALE_UNAVAILABLE');
    await scale.stop();
  });
});
