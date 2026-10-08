import { Injectable } from '@angular/core';
import { ConfirmedSale } from '../sales/sale-draft-client';
import { receiptPayload, ReceiptPayload } from '../sales/receipt-pdf-client';

interface DeviceBridge {
  readSerialScale(): Promise<{ readonly weightKg: number; readonly stable: boolean; readonly observedAtUtc: string }>;
  printSerialReceipt(request: ReceiptPayload): Promise<SerialPrintResult>;
}

export interface SerialPrintResult {
  readonly port: string;
  readonly bytesWritten: number;
  readonly confirmation: 'drained' | 'write-only';
}

@Injectable({ providedIn: 'root' })
export class PosDeviceClient {
  readScale(): Promise<number> {
    const bridge = (window as Window & { carnicerias?: DeviceBridge }).carnicerias;
    if (!bridge) return Promise.reject(new Error('DEVICE_UNAVAILABLE'));
    return bridge.readSerialScale().then(({ weightKg, stable, observedAtUtc }) => {
      if (!stable || !Number.isFinite(weightKg) || weightKg <= 0 || weightKg > 10000 ||
          Math.abs(Math.round(weightKg * 1000) - weightKg * 1000) > 0.000001 ||
          Number.isNaN(Date.parse(observedAtUtc)) ||
          Math.abs(Date.now() - Date.parse(observedAtUtc)) > 30000)
        throw new Error('INVALID_SCALE_READING');
      return weightKg;
    });
  }

  print(sale: ConfirmedSale, branch: string, terminal: string, cashier: string): Promise<SerialPrintResult> {
    const bridge = (window as Window & { carnicerias?: DeviceBridge }).carnicerias;
    if (!bridge) return Promise.reject(new Error('DEVICE_UNAVAILABLE'));
    return bridge.printSerialReceipt(receiptPayload(sale, branch, terminal, cashier));
  }
}
