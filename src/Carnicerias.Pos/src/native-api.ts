import type { ReceiptRequest } from './receipt-pdf';
import type { ScaleReading } from './virtual-device-protocol';

export interface NativeDiagnostic {
  readonly electronVersion: string;
  readonly platform: NodeJS.Platform;
  readonly status: 'ready';
}

export interface CarniceriasNativeApi {
  readonly getDiagnostic: () => Promise<NativeDiagnostic>;
  readonly saveReceiptPdf: (request: ReceiptRequest) => Promise<{ readonly path: string }>;
  readonly readVirtualScale: () => Promise<ScaleReading>;
  readonly printVirtualReceipt: (request: ReceiptRequest) => Promise<{ readonly path: string }>;
}

export const enum NativeChannel {
  Diagnostic = 'carnicerias:diagnostic',
  SaveReceiptPdf = 'carnicerias:save-receipt-pdf',
  ReadVirtualScale = 'carnicerias:read-virtual-scale',
  PrintVirtualReceipt = 'carnicerias:print-virtual-receipt',
}
