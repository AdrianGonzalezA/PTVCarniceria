import { Injectable } from '@angular/core';
import { ConfirmedSale } from './sale-draft-client';
import { AuthorizedFiscalDocument } from './fiscal-document-client';

export type ReceiptPayload = Omit<ConfirmedSale, 'id'> & {
  readonly saleId: string; readonly branch: string; readonly terminal: string; readonly cashier: string;
  readonly fiscal?: AuthorizedFiscalDocument;
};

interface ReceiptBridge {
  saveReceiptPdf(request: ReceiptPayload): Promise<{ readonly path: string }>;
}

export function receiptPayload(sale: ConfirmedSale, branch: string, terminal: string,
  cashier: string, fiscal?: AuthorizedFiscalDocument): ReceiptPayload {
  const { id, ...detail } = sale;
  return { ...detail, saleId: id, branch, terminal, cashier, ...(fiscal ? { fiscal } : {}) };
}

@Injectable({ providedIn: 'root' })
export class ReceiptPdfClient {
  save(sale: ConfirmedSale, branch: string, terminal: string, cashier: string,
    fiscal?: AuthorizedFiscalDocument): Promise<string> {
    const bridge = (window as Window & { carnicerias?: ReceiptBridge }).carnicerias;
    if (!bridge) return Promise.reject(new Error('La impresión virtual solo está disponible en Electron.'));
    return bridge.saveReceiptPdf(receiptPayload(sale, branch, terminal, cashier, fiscal)).then(({ path }) => path);
  }
}
