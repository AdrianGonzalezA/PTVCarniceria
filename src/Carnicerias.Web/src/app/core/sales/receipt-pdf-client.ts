import { Injectable } from '@angular/core';
import { ConfirmedSale } from './sale-draft-client';

interface ReceiptBridge {
  saveReceiptPdf(request: Omit<ConfirmedSale, 'id'> & {
    readonly saleId: string; readonly branch: string; readonly terminal: string; readonly cashier: string;
  }): Promise<{ readonly path: string }>;
}

@Injectable({ providedIn: 'root' })
export class ReceiptPdfClient {
  save(sale: ConfirmedSale, branch: string, terminal: string, cashier: string): Promise<string> {
    const bridge = (window as Window & { carnicerias?: ReceiptBridge }).carnicerias;
    if (!bridge) return Promise.reject(new Error('La impresión virtual solo está disponible en Electron.'));
    const { id, ...detail } = sale;
    return bridge.saveReceiptPdf({ ...detail, saleId: id, branch, terminal, cashier }).then(({ path }) => path);
  }
}
