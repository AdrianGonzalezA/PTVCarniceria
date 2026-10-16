import { Injectable } from '@angular/core';
import { ConfirmedSale } from '../sales/sale-draft-client';
import { receiptPayload, ReceiptPayload } from '../sales/receipt-pdf-client';
import { AuthorizedFiscalDocument } from '../sales/fiscal-document-client';

interface DeviceBridge {
  readSerialScale(): Promise<{ readonly weightKg: number; readonly stable: boolean; readonly observedAtUtc: string }>;
  printSerialReceipt(request: ReceiptPayload): Promise<SerialPrintResult>;
  getDeviceAvailability?(): Promise<PosDeviceAvailability>;
  getHasarSaleAvailability?(): Promise<{ readonly port: string | null }>;
  printHasarSaleReceipt?(request: ReceiptPayload): Promise<HasarSalePrintResult>;
}

export interface PosDeviceAvailability {
  readonly scale: { readonly port: string; readonly baudRate: number } | null;
  readonly printer: { readonly kind: 'text' | 'hasar'; readonly port: string;
    readonly baudRate: number } | null;
}

const noDevices: PosDeviceAvailability = { scale: null, printer: null };

export interface HasarSalePrintResult {
  readonly port: string;
  readonly kind: 'fiscal' | 'nonFiscal';
  readonly number: number | null;
  readonly trainingMode: boolean;
  readonly total: number;
}

export interface SerialPrintResult {
  readonly port: string;
  readonly bytesWritten: number;
  readonly confirmation: 'drained' | 'write-only';
}

@Injectable({ providedIn: 'root' })
export class PosDeviceClient {
  async deviceAvailability(): Promise<PosDeviceAvailability> {
    const bridge = (window as Window & { carnicerias?: DeviceBridge }).carnicerias;
    return bridge?.getDeviceAvailability ? bridge.getDeviceAvailability() : noDevices;
  }

  async hasarSaleAvailability(): Promise<string | null> {
    const bridge = (window as Window & { carnicerias?: DeviceBridge }).carnicerias;
    return bridge?.getHasarSaleAvailability ? (await bridge.getHasarSaleAvailability()).port : null;
  }

  async readScale(): Promise<number> {
    const bridge = (window as Window & { carnicerias?: DeviceBridge }).carnicerias;
    const devices = await this.deviceAvailability();
    if (!devices.scale) throw new Error('SERIAL_SCALE_NOT_CONFIGURED');
    if (!bridge) throw new Error('DEVICE_UNAVAILABLE');
    return bridge.readSerialScale().then(({ weightKg, stable, observedAtUtc }) => {
      if (!stable || !Number.isFinite(weightKg) || weightKg <= 0 || weightKg > 10000 ||
          Math.abs(Math.round(weightKg * 1000) - weightKg * 1000) > 0.000001 ||
          Number.isNaN(Date.parse(observedAtUtc)) ||
          Math.abs(Date.now() - Date.parse(observedAtUtc)) > 30000)
        throw new Error('INVALID_SCALE_READING');
      return weightKg;
    });
  }

  async print(sale: ConfirmedSale, branch: string, terminal: string, cashier: string,
    fiscal?: AuthorizedFiscalDocument): Promise<SerialPrintResult | HasarSalePrintResult | null> {
    const bridge = (window as Window & { carnicerias?: DeviceBridge }).carnicerias;
    const devices = await this.deviceAvailability();
    if (!devices.printer || (sale.documentType === 'electronicInvoice' &&
        devices.printer.kind === 'hasar')) return null;
    if (!bridge) throw new Error('DEVICE_UNAVAILABLE');
    const payload = receiptPayload(sale, branch, terminal, cashier, fiscal);
    if (devices.printer.kind === 'hasar') {
      if (!bridge.printHasarSaleReceipt) throw new Error('DEVICE_UNAVAILABLE');
      return bridge.printHasarSaleReceipt(payload);
    }
    return bridge.printSerialReceipt(payload);
  }
}
