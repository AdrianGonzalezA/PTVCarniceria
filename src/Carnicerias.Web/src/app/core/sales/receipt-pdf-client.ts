import { Injectable } from '@angular/core';
import { ConfirmedSale } from './sale-draft-client';

export type ReceiptPayload = Omit<ConfirmedSale, 'id'> & {
  readonly saleId: string; readonly branch: string; readonly terminal: string; readonly cashier: string;
};

interface ReceiptBridge {
  saveReceiptPdf(request: ReceiptPayload): Promise<{ readonly path: string }>;
}

export function receiptPayload(sale: ConfirmedSale, branch: string, terminal: string,
  cashier: string): ReceiptPayload {
  const { id, ...detail } = sale;
  return { ...detail, saleId: id, branch, terminal, cashier };
}

@Injectable({ providedIn: 'root' })
export class ReceiptPdfClient {
  save(sale: ConfirmedSale, branch: string, terminal: string, cashier: string): Promise<string> {
    const bridge = (window as Window & { carnicerias?: ReceiptBridge }).carnicerias;
    if (!bridge) return Promise.reject(new Error('La impresión virtual solo está disponible en Electron.'));
    return bridge.saveReceiptPdf(receiptPayload(sale, branch, terminal, cashier)).then(({ path }) => path);
  }
}
