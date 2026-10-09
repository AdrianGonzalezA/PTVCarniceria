import '@angular/compiler';
import { TestBed } from '@angular/core/testing';
import { PosDeviceClient } from './pos-device-client';

describe('PosDeviceClient', () => {
  const original = (window as Window & { carnicerias?: unknown }).carnicerias;
  afterEach(() => { (window as Window & { carnicerias?: unknown }).carnicerias = original; });

  it('accepts a fresh stable serial weight', async () => {
    (window as Window & { carnicerias?: unknown }).carnicerias = {
      readSerialScale: () => Promise.resolve({ weightKg: 0.75, stable: true,
        observedAtUtc: new Date().toISOString() }),
    };
    expect(await TestBed.inject(PosDeviceClient).readScale()).toBe(0.75);
  });

  it('rejects an unstable or stale reading', async () => {
    (window as Window & { carnicerias?: unknown }).carnicerias = {
      readSerialScale: () => Promise.resolve({ weightKg: 0.75, stable: false,
        observedAtUtc: new Date().toISOString() }),
    };
    await expect(TestBed.inject(PosDeviceClient).readScale()).rejects.toThrow('INVALID_SCALE_READING');
  });

  it('sends a confirmed receipt to the serial printer bridge', async () => {
    let saleId = '';
    (window as Window & { carnicerias?: unknown }).carnicerias = {
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
    expect(result.port).toBe('COM1');
  });
});
