import '@angular/compiler';
import { TestBed } from '@angular/core/testing';
import { PosDeviceClient } from './pos-device-client';

describe('PosDeviceClient', () => {
  const original = (window as Window & { carnicerias?: unknown }).carnicerias;
  afterEach(() => { (window as Window & { carnicerias?: unknown }).carnicerias = original; });

  it('accepts a fresh stable virtual weight', async () => {
    (window as Window & { carnicerias?: unknown }).carnicerias = {
      readVirtualScale: () => Promise.resolve({ weightKg: 0.75, stable: true,
        observedAtUtc: new Date().toISOString() }),
    };
    expect(await TestBed.inject(PosDeviceClient).readScale()).toBe(0.75);
  });

  it('rejects an unstable or stale reading', async () => {
    (window as Window & { carnicerias?: unknown }).carnicerias = {
      readVirtualScale: () => Promise.resolve({ weightKg: 0.75, stable: false,
        observedAtUtc: new Date().toISOString() }),
    };
    await expect(TestBed.inject(PosDeviceClient).readScale()).rejects.toThrow('INVALID_SCALE_READING');
  });
});
