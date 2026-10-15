import type { ReceiptRequest } from './receipt-pdf';
import type { ScaleReading } from './serial-scale';
import type { HasarSalePrintResult } from './hasar-sale-ledger';
import type { AssignedDevices } from './terminal-devices';

export interface NativeDiagnostic {
  readonly electronVersion: string;
  readonly platform: NodeJS.Platform;
  readonly status: 'ready';
}

export interface CarniceriasNativeApi {
  readonly getDiagnostic: () => Promise<NativeDiagnostic>;
  readonly saveReceiptPdf: (request: ReceiptRequest) => Promise<{ readonly path: string }>;
  readonly readSerialScale: () => Promise<ScaleReading>;
  readonly getDeviceAvailability: () => Promise<AssignedDevices>;
  readonly printSerialReceipt: (request: ReceiptRequest) => Promise<{
    readonly port: string; readonly bytesWritten: number;
    readonly confirmation: 'drained' | 'write-only';
  }>;
  readonly getHasarSaleAvailability: () => Promise<{ readonly port: string | null }>;
  readonly printHasarSaleReceipt: (request: ReceiptRequest) => Promise<HasarSalePrintResult & {
    readonly port: string;
  }>;
}

export const enum NativeChannel {
  Diagnostic = 'carnicerias:diagnostic',
  SaveReceiptPdf = 'carnicerias:save-receipt-pdf',
  ReadSerialScale = 'carnicerias:read-serial-scale',
  DeviceAvailability = 'carnicerias:device-availability',
  PrintSerialReceipt = 'carnicerias:print-serial-receipt',
  HasarSaleAvailability = 'carnicerias:hasar-sale-availability',
  PrintHasarSaleReceipt = 'carnicerias:print-hasar-sale-receipt',
}
