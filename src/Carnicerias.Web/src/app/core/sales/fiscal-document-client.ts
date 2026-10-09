import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export interface AuthorizedFiscalDocument {
  readonly saleId: string;
  readonly status: 'NotRequested' | 'Prepared' | 'NeedsReconciliation' | 'Rejected' | 'Authorized';
  readonly saleDocumentType: 'FiscalTicket' | 'ElectronicInvoice';
  readonly issuerCuit: string | null;
  readonly issuerName: string;
  readonly issuerAddress: string;
  readonly pointOfSale: number | null;
  readonly voucherType: number | null;
  readonly number: number | null;
  readonly issueDate: string | null;
  readonly total: number;
  readonly receiverName: string | null;
  readonly receiverAddress: string | null;
  readonly receiverTaxStatus: string;
  readonly receiverDocumentType: number | null;
  readonly receiverDocumentNumber: number | null;
  readonly vatBreakdown: readonly { readonly ratePercent: number; readonly taxableBase: number;
    readonly taxAmount: number }[];
  readonly exemptAmount: number;
  readonly notTaxedAmount: number;
  readonly cae: string | null;
  readonly caeExpiry: string | null;
  readonly errorCodes: string | null;
}

@Injectable({ providedIn: 'root' })
export class FiscalDocumentClient {
  private readonly http = inject(HttpClient);

  issue(saleId: string) {
    return this.http.post<AuthorizedFiscalDocument>(
      `/api/sales/${encodeURIComponent(saleId)}/fiscal-document`, {}, { withCredentials: true });
  }

  get(saleId: string) {
    return this.http.get<AuthorizedFiscalDocument>(
      `/api/sales/${encodeURIComponent(saleId)}/fiscal-document`, { withCredentials: true });
  }
}
