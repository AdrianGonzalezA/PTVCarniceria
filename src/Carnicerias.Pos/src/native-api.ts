import type { ReceiptRequest } from './receipt-pdf';

export interface NativeDiagnostic {
  readonly electronVersion: string;
  readonly platform: NodeJS.Platform;
  readonly status: 'ready';
}

export interface CarniceriasNativeApi {
  readonly getDiagnostic: () => Promise<NativeDiagnostic>;
  readonly saveReceiptPdf: (request: ReceiptRequest) => Promise<{ readonly path: string }>;
}

export const diagnosticChannel = 'carnicerias:diagnostic';
export const saveReceiptPdfChannel = 'carnicerias:save-receipt-pdf';
