import type { ReceiptRequest } from './receipt-pdf';
import type { ScaleReading } from './serial-scale';

export interface NativeDiagnostic {
  readonly electronVersion: string;
  readonly platform: NodeJS.Platform;
  readonly status: 'ready';
}

export interface CarniceriasNativeApi {
  readonly getDiagnostic: () => Promise<NativeDiagnostic>;
  readonly saveReceiptPdf: (request: ReceiptRequest) => Promise<{ readonly path: string }>;
  readonly readSerialScale: () => Promise<ScaleReading>;
  readonly printSerialReceipt: (request: ReceiptRequest) => Promise<{
    readonly port: string; readonly bytesWritten: number;
    readonly confirmation: 'drained' | 'write-only';
  }>;
}

export const enum NativeChannel {
  Diagnostic = 'carnicerias:diagnostic',
  SaveReceiptPdf = 'carnicerias:save-receipt-pdf',
  ReadSerialScale = 'carnicerias:read-serial-scale',
  PrintSerialReceipt = 'carnicerias:print-serial-receipt',
}
