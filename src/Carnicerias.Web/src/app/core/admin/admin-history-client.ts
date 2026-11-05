import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export type HistorySaleDocumentType = 'nonFiscalTicket' | 'fiscalTicket' | 'electronicInvoice';

export interface HistoryFilters {
  readonly page: number;
  readonly pageSize: number;
  readonly branchId?: string;
  readonly terminalId?: string;
  readonly fromUtc?: string;
  readonly toUtc?: string;
  readonly documentType?: HistorySaleDocumentType;
}

export interface HistoryPage<T> {
  readonly items: readonly T[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalItems: number;
}

export interface BusinessSummary {
  readonly fromUtc: string;
  readonly toUtc: string;
  readonly branchId: string | null;
  readonly saleCount: number;
  readonly salesTotal: number;
  readonly immediateSalePayments: number;
  readonly paymentsByMethod: readonly { readonly method: string; readonly amount: number }[];
  readonly newAccountCharges: number;
  readonly registeredAccountCharges: number;
  readonly creditApplied: number;
  readonly outstandingDebt: number;
  readonly companyCreditAvailable: number;
  readonly collectionsReceived: number;
  readonly collectionsRefunded: number;
  readonly cashCollectionsNet: number;
  readonly nonCashCollectionsNet: number;
  readonly openShiftCount: number;
  readonly openShiftCashBalance: number;
}

export interface SaleHistoryItem {
  readonly id: string;
  readonly confirmedAtUtc: string;
  readonly total: number;
  readonly branchId: string;
  readonly branchName: string;
  readonly terminalId: string | null;
  readonly terminalName: string | null;
  readonly cashierId: string;
  readonly cashierName: string;
  readonly shiftId: string;
}

export interface SaleDetail {
  readonly id: string;
  readonly confirmedAtUtc: string;
  readonly total: number;
  readonly discountAmount: number;
  readonly discountReason: string | null;
  readonly documentType: HistorySaleDocumentType;
  readonly recipientTaxStatus: 'finalConsumer' | 'registered' | 'smallTaxpayer' | 'exempt';
  readonly recipientName: string | null;
  readonly recipientDocumentNumber: string | null;
  readonly recipientAddress: string | null;
  readonly recipientLocality?: string | null;
  readonly recipientProvince?: string | null;
  readonly fiscalCustomerCode?: string | null;
  readonly purchaseOrderNumber?: string | null;
  readonly taxSnapshotStatus: 'legacy' | 'unconfigured' | 'complete';
  readonly taxableBase: number | null;
  readonly taxAmount: number | null;
  readonly exemptAmount: number | null;
  readonly notTaxedAmount: number | null;
  readonly fiscalDocument?: FiscalDocumentDetail | null;
  readonly lines: readonly {
    readonly code: string;
    readonly name: string;
    readonly unit: string;
    readonly saleMode: 'weight' | 'unit';
    readonly quantity: number;
    readonly unitPrice: number;
    readonly lineTotal: number;
    readonly pieceIdentifier?: string | null;
    readonly pieceCount?: number | null;
    readonly orderDiscountAmount: number;
    readonly netAfterDiscount: number | null;
    readonly taxTreatment: string | null;
    readonly taxRatePercent: number | null;
    readonly taxableBase: number | null;
    readonly taxAmount: number | null;
  }[];
  readonly payments: readonly {
    readonly method: string;
    readonly tenderedAmount: number;
    readonly appliedAmount: number;
  }[];
}

export interface FiscalDocumentDetail {
  readonly id: string;
  readonly status: 'Prepared' | 'NeedsReconciliation' | 'Rejected' | 'Authorized';
  readonly pointOfSale: number;
  readonly voucherType: number;
  readonly number: number;
  readonly issueDate: string;
  readonly total: number;
  readonly cae: string | null;
  readonly snapshotStatus: 'legacyIncomplete' | 'complete';
  readonly taxableBase: number | null;
  readonly taxAmount: number | null;
  readonly exemptAmount: number | null;
  readonly notTaxedAmount: number | null;
  readonly vatLines: readonly {
    readonly arcaRateCode: number;
    readonly ratePercent: number;
    readonly taxableBase: number;
    readonly taxAmount: number;
  }[] | null;
}

export interface ShiftHistoryItem {
  readonly id: string;
  readonly openedAtUtc: string;
  readonly closedAtUtc: string | null;
  readonly status: 'open' | 'closed';
  readonly openingCash: number;
  readonly branchId: string;
  readonly branchName: string;
  readonly terminalId: string | null;
  readonly terminalName: string | null;
  readonly cashierId: string;
  readonly cashierName: string;
  readonly hasReport: boolean;
}

export interface CashHistoryItem {
  readonly id: string;
  readonly createdAtUtc: string;
  readonly branchId: string;
  readonly branchName: string;
  readonly terminalId: string | null;
  readonly terminalName: string | null;
  readonly cashierId: string;
  readonly cashierName: string;
  readonly shiftId: string;
  readonly saleId: string | null;
  readonly kind: 'opening' | 'salePayment' | 'change' | 'accountCollection' |
    'accountCollectionRefund' | 'accountCollectionChange' | 'extraordinaryWithdrawal';
  readonly method: string;
  readonly amountDelta: number;
}

export interface StockHistoryItem {
  readonly id: string;
  readonly createdAtUtc: string;
  readonly branchId: string;
  readonly branchName: string;
  readonly productId: string;
  readonly productName: string;
  readonly saleMode: 'weight' | 'unit';
  readonly userId: string;
  readonly username: string;
  readonly kind: 'openingBalance' | 'adjustment' | 'sale';
  readonly quantityDelta: number;
  readonly reason: string;
}

@Injectable({ providedIn: 'root' })
export class AdminHistoryClient {
  private readonly http = inject(HttpClient);

  summary(branchId?: string) {
    return this.http.get<BusinessSummary>('/api/admin/history/summary', {
      params: branchId ? new HttpParams().set('branchId', branchId) : undefined,
      withCredentials: true,
    });
  }

  sales(filters: HistoryFilters) {
    const params = this.params(filters);
    return this.http.get<HistoryPage<SaleHistoryItem>>('/api/admin/history/sales', {
      params: filters.documentType ? params.set('documentType', filters.documentType) : params,
      withCredentials: true,
    });
  }

  saleDetail(id: string) {
    return this.http.get<SaleDetail>(`/api/admin/history/sales/${encodeURIComponent(id)}`, {
      withCredentials: true,
    });
  }

  shifts(filters: HistoryFilters) {
    return this.http.get<HistoryPage<ShiftHistoryItem>>('/api/admin/history/shifts', {
      params: this.params(filters), withCredentials: true,
    });
  }

  shiftReport(id: string) {
    return this.http.get<import('../sales/cashier-shift-client').ShiftClosingReport>(
      `/api/admin/history/shifts/${encodeURIComponent(id)}/report`, { withCredentials: true });
  }

  shiftReportPdf(id: string) {
    return this.http.get(`/api/admin/history/shifts/${encodeURIComponent(id)}/report.pdf`,
      { responseType: 'blob', withCredentials: true });
  }

  cashMovements(filters: HistoryFilters) {
    return this.http.get<HistoryPage<CashHistoryItem>>('/api/admin/history/cash-movements', {
      params: this.params(filters), withCredentials: true,
    });
  }

  stockMovements(filters: HistoryFilters) {
    return this.http.get<HistoryPage<StockHistoryItem>>('/api/admin/history/stock-movements', {
      params: this.params(filters), withCredentials: true,
    });
  }

  private params(filters: HistoryFilters): HttpParams {
    let params = new HttpParams().set('page', filters.page).set('pageSize', filters.pageSize);
    if (filters.branchId) params = params.set('branchId', filters.branchId);
    if (filters.terminalId) params = params.set('terminalId', filters.terminalId);
    if (filters.fromUtc) params = params.set('fromUtc', filters.fromUtc);
    if (filters.toUtc) params = params.set('toUtc', filters.toUtc);
    return params;
  }
}
