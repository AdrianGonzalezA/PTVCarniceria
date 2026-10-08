import { afterEach, describe, expect, it } from 'vitest';
import { SerialPortMock } from 'serialport';
import type { ReceiptRequest } from '../src/receipt-pdf';
import { sendSerialReceipt } from '../src/serial-printer';

const receipt: ReceiptRequest = {
  saleId: 'VENTA-SERIAL', branch: 'Sucursal', terminal: 'Caja 1', cashier: 'cajero1',
  confirmedAtUtc: '2026-10-08T15:00:00Z', total: 2450, changeAmount: 0,
  lines: [{ code: '1002', name: 'Asado', unit: 'kg', quantity: 0.5,
    unitPrice: 4900, lineTotal: 2450 }],
  payments: [{ method: 'cash', tenderedAmount: 2450, appliedAmount: 2450 }],
};

afterEach(() => SerialPortMock.binding.reset());

describe('serial printer', () => {
  it('writes and drains a receipt to COM1 at 9600 8N1, then releases the port', async () => {
    SerialPortMock.binding.createPort('COM1', { record: true });
    let port: SerialPortMock | undefined;
    const result = await sendSerialReceipt(receipt, (options) => {
      expect(options).toMatchObject({ path: 'COM1', baudRate: 9600,
        dataBits: 8, stopBits: 1, parity: 'none', autoOpen: false });
      port = new SerialPortMock(options);
      return port;
    });

    expect(result).toEqual({ port: 'COM1', bytesWritten: expect.any(Number), confirmation: 'drained' });
    expect(result.bytesWritten).toBeGreaterThan(100);
    expect(port?.port?.recording.toString('utf8')).toContain('VENTA-SERIAL');
    expect(port?.isOpen).toBe(false);
  });

  it('rejects a missing or occupied port without reporting a successful print', async () => {
    await expect(sendSerialReceipt(receipt, (options) => new SerialPortMock(options)))
      .rejects.toThrow();
  });

  it('reports write-only when a virtual Windows driver cannot drain', async () => {
    SerialPortMock.binding.createPort('COM1', { record: true });
    let port: SerialPortMock | undefined;
    const result = await sendSerialReceipt(receipt, (options) => {
      port = new SerialPortMock(options);
      port.drain = (callback) => callback?.(new Error('Draining connection (FlushFileBuffers): Unknown error code 1'));
      return port;
    });
    expect(result.confirmation).toBe('write-only');
    expect(port?.port?.recording.toString('utf8')).toContain('VENTA-SERIAL');
    expect(port?.isOpen).toBe(false);
  });

  it('does not claim success for other drain failures', async () => {
    SerialPortMock.binding.createPort('COM1', { record: true });
    let port: SerialPortMock | undefined;
    await expect(sendSerialReceipt(receipt, (options) => {
      port = new SerialPortMock(options);
      port.drain = (callback) => callback?.(new Error('port disconnected'));
      return port;
    })).rejects.toThrow('port disconnected');
    expect(port?.isOpen).toBe(false);
  });
});
