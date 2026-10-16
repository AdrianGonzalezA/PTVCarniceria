import '@angular/compiler';
import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { PosDeviceClient } from './pos-device-client';

describe('PosDeviceClient', () => {
  const original = (window as Window & { carnicerias?: unknown }).carnicerias;
  afterEach(() => { (window as Window & { carnicerias?: unknown }).carnicerias = original; });

  it('accepts a fresh stable serial weight', async () => {
    (window as Window & { carnicerias?: unknown }).carnicerias = {
      getDeviceAvailability: () => Promise.resolve({ scale: { port: 'COM6', baudRate: 9600 }, printer: null }),
      readSerialScale: () => Promise.resolve({ weightKg: 0.75, stable: true,
        observedAtUtc: new Date().toISOString() }),
    };
    expect(await TestBed.inject(PosDeviceClient).readScale()).toBe(0.75);
  });

  it('rejects an unstable or stale reading', async () => {
    (window as Window & { carnicerias?: unknown }).carnicerias = {
      getDeviceAvailability: () => Promise.resolve({ scale: { port: 'COM6', baudRate: 9600 }, printer: null }),
      readSerialScale: () => Promise.resolve({ weightKg: 0.75, stable: false,
        observedAtUtc: new Date().toISOString() }),
    };
    await expect(TestBed.inject(PosDeviceClient).readScale()).rejects.toThrow('INVALID_SCALE_READING');
  });

  it('sends a confirmed receipt to the serial printer bridge', async () => {
    let saleId = '';
    (window as Window & { carnicerias?: unknown }).carnicerias = {
      getDeviceAvailability: () => Promise.resolve({ scale: null,
        printer: { kind: 'text', port: 'COM1', baudRate: 9600 } }),
      printSerialReceipt: (request: { saleId: string }) => {
        saleId = request.saleId;
        return Promise.resolve({ port: 'COM1', bytesWritten: 400, confirmation: 'drained' });
      },
    };
    const result = await TestBed.inject(PosDeviceClient).print({
      id: 'sale-1', confirmedAtUtc: '2026-10-08T15:00:00Z', total: 2450, changeAmount: 0,
      customerId: null, customerCode: null, customerName: null, accountChargeAmount: 0,
      creditAppliedAmount: 0,
      lines: [{ code: '1002', name: 'Asado', unit: 'kg', quantity: 0.5, unitPrice: 4900, lineTotal: 2450 }],
      payments: [{ method: 'cash', tenderedAmount: 2450, appliedAmount: 2450 }],
    }, 'Sucursal', 'Caja 1', 'cajero');
    expect(saleId).toBe('sale-1');
    expect(result?.port).toBe('COM1');
  });

  it('routes a confirmed sale ticket to the configured Hasar on COM8', async () => {
    let saleId = '';
    (window as Window & { carnicerias?: unknown }).carnicerias = {
      getDeviceAvailability: () => Promise.resolve({ scale: null,
        printer: { kind: 'hasar', port: 'COM8', baudRate: 9600 } }),
      getHasarSaleAvailability: () => Promise.resolve({ port: 'COM8' }),
      printHasarSaleReceipt: (request: { saleId: string }) => {
        saleId = request.saleId;
        return Promise.resolve({ port: 'COM8', kind: 'nonFiscal', number: null,
          trainingMode: false, total: 2450 });
      },
    };
    const client = TestBed.inject(PosDeviceClient);
    expect(await client.hasarSaleAvailability()).toBe('COM8');
    const result = await client.print({ id: 'sale-1', documentType: 'nonFiscalTicket',
      confirmedAtUtc: '2026-10-08T15:00:00Z', total: 2450, changeAmount: 0,
      customerId: null, customerCode: null, customerName: null, accountChargeAmount: 0,
      creditAppliedAmount: 0,
      lines: [{ code: '1002', name: 'Asado', unit: 'kg', quantity: 0.5,
        unitPrice: 4900, lineTotal: 2450 }],
      payments: [{ method: 'cash', tenderedAmount: 2450, appliedAmount: 2450 }],
    }, 'Sucursal', 'Caja 1', 'cajero');
    expect(saleId).toBe('sale-1');
    expect(result?.port).toBe('COM8');
  });

  it('does not expose the Hasar printer in the browser without the Electron bridge', async () => {
    (window as Window & { carnicerias?: unknown }).carnicerias = undefined;
    const client = TestBed.inject(PosDeviceClient);
    expect(await client.hasarSaleAvailability()).toBeNull();
  });

  it('does not read or print when this box has no assigned devices', async () => {
    const readSerialScale = vi.fn();
    const printSerialReceipt = vi.fn();
    const printHasarSaleReceipt = vi.fn();
    (window as Window & { carnicerias?: unknown }).carnicerias = {
      getDeviceAvailability: () => Promise.resolve({ scale: null, printer: null }),
      readSerialScale, printSerialReceipt, printHasarSaleReceipt,
    };
    const client = TestBed.inject(PosDeviceClient);
    await expect(client.readScale()).rejects.toThrow('SERIAL_SCALE_NOT_CONFIGURED');
    const result = await client.print({ id: 'sale-1', documentType: 'nonFiscalTicket',
      confirmedAtUtc: '2026-10-08T15:00:00Z', total: 100, changeAmount: 0,
      customerId: null, customerCode: null, customerName: null, accountChargeAmount: 0,
      creditAppliedAmount: 0, lines: [], payments: [],
    }, 'Sucursal', 'Caja', 'cajero');
    expect(result).toBeNull();
    expect(readSerialScale).not.toHaveBeenCalled();
    expect(printSerialReceipt).not.toHaveBeenCalled();
    expect(printHasarSaleReceipt).not.toHaveBeenCalled();
  });
});
