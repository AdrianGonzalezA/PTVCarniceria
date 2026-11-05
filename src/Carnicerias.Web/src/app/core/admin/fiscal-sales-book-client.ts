import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export interface FiscalBookVatLine {
  readonly arcaRateCode: number;
  readonly ratePercent: number;
  readonly taxableBase: number;
  readonly taxAmount: number;
}

export interface FiscalBookItem {
  readonly fiscalDocumentId: string;
  readonly issueDate: string;
  readonly branchName: string;
  readonly documentType: 'fiscalTicket' | 'electronicInvoice';
  readonly voucherType: number;
  readonly pointOfSale: number;
  readonly number: number;
  readonly receiverDocument: string;
  readonly recipientName: string | null;
  readonly receiverVatConditionCode: number | null;
  readonly cae: string;
  readonly snapshotStatus: 'complete' | 'legacyIncomplete' | 'inconsistent';
  readonly total: number;
  readonly taxableBase: number | null;
  readonly vatAmount: number | null;
  readonly exemptAmount: number | null;
  readonly notTaxedAmount: number | null;
  readonly vatBreakdown: readonly FiscalBookVatLine[];
}

export interface FiscalBookTotals {
  readonly completeCount: number;
  readonly incompleteCount: number;
  readonly completeTotal: number;
  readonly incompleteKnownTotal: number;
  readonly taxableBase: number;
  readonly vatAmount: number;
  readonly exemptAmount: number;
  readonly notTaxedAmount: number;
  readonly vatBreakdown: readonly FiscalBookVatLine[];
  readonly otherTaxesStatus: 'notModeled';
}

export interface FiscalBookPage {
  readonly from: string;
  readonly to: string;
  readonly branchId: string | null;
  readonly page: number;
  readonly pageSize: number;
  readonly totalItems: number;
  readonly items: readonly FiscalBookItem[];
  readonly totals: FiscalBookTotals;
}

@Injectable({ providedIn: 'root' })
export class FiscalSalesBookClient {
  private readonly http = inject(HttpClient);

  list(from: string, to: string, branchId: string, page: number) {
    return this.http.get<FiscalBookPage>('/api/admin/fiscal-sales-book', {
      params: this.params(from, to, branchId).set('page', page).set('pageSize', 30),
      withCredentials: true,
    });
  }

  export(from: string, to: string, branchId: string) {
    return this.http.get('/api/admin/fiscal-sales-book/export', {
      params: this.params(from, to, branchId), responseType: 'blob', withCredentials: true,
    });
  }

  private params(from: string, to: string, branchId: string): HttpParams {
    let params = new HttpParams().set('from', from).set('to', to);
    if (branchId) params = params.set('branchId', branchId);
    return params;
  }
}
