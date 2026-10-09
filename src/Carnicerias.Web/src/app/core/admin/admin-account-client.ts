import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export interface AccountPage<T> {
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
  readonly items: readonly T[];
}

export interface AccountCustomer {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly isActive: boolean;
  readonly creditEnabled: boolean;
  readonly totalDebt: number;
  readonly creditAvailable: number;
}

export interface AccountSale {
  readonly saleId: string;
  readonly branchId: string;
  readonly cashierShiftId: string;
  readonly chargedAtUtc: string;
  readonly originalAmount: number;
  readonly outstandingAmount: number;
}

export interface AccountCorrection {
  readonly correctionNumber: number;
  readonly kind: 'refund' | 'reallocate';
  readonly reason: string;
  readonly replacementReceiptId: string | null;
  readonly createdAtUtc: string;
}

export interface AccountReceipt {
  readonly id: string;
  readonly receiptNumber: number;
  readonly branchId: string;
  readonly cashierShiftId: string;
  readonly createdAtUtc: string;
  readonly amount: number;
  readonly creditAmount: number;
  readonly method: string;
  readonly origin: 'cashReceived' | 'reallocation';
  readonly isVoided: boolean;
  readonly replacesReceiptId: string | null;
  readonly correction: AccountCorrection | null;
  readonly allocations: readonly { readonly saleId: string; readonly amount: number }[];
}

export interface AccountCreditApplication {
  readonly id: string;
  readonly saleId: string;
  readonly branchId: string;
  readonly cashierShiftId: string;
  readonly amount: number;
  readonly createdAtUtc: string;
}

export interface AccountDetail {
  readonly customerId: string;
  readonly code: string;
  readonly name: string;
  readonly isActive: boolean;
  readonly creditEnabled: boolean;
  readonly totalDebt: number;
  readonly creditAvailable: number;
  readonly sales: AccountPage<AccountSale>;
  readonly receipts: AccountPage<AccountReceipt>;
  readonly creditApplications: AccountPage<AccountCreditApplication>;
}

export interface CollectionCorrectionRequest {
  readonly operationId: string;
  readonly kind: 'refund' | 'reallocate';
  readonly reason: string;
  readonly targetCustomerId?: string;
  readonly allocations?: readonly { readonly saleId: string; readonly amount: number }[];
}

export interface CollectionCorrectionResult {
  readonly id: string;
  readonly correctionNumber: number;
  readonly originalReceiptId: string;
  readonly replacementReceiptId: string | null;
  readonly kind: string;
  readonly reason: string;
  readonly amount: number;
  readonly createdAtUtc: string;
  readonly replacementReceiptNumber: number | null;
  readonly customerId: string | null;
  readonly creditAmount: number | null;
}

@Injectable({ providedIn: 'root' })
export class AdminAccountClient {
  private readonly http = inject(HttpClient);

  list(page = 1, search = '') {
    let params = new HttpParams().set('page', page);
    if (search) params = params.set('search', search);
    return this.http.get<AccountPage<AccountCustomer>>('/api/admin/accounts',
      { params, withCredentials: true });
  }

  detail(customerId: string, salePage = 1, receiptPage = 1, applicationPage = 1) {
    const params = new HttpParams().set('salePage', salePage)
      .set('receiptPage', receiptPage).set('applicationPage', applicationPage);
    return this.http.get<AccountDetail>(`/api/admin/accounts/${encodeURIComponent(customerId)}`,
      { params, withCredentials: true });
  }

  correct(receiptId: string, request: CollectionCorrectionRequest) {
    return this.http.post<CollectionCorrectionResult>(
      `/api/customers/collections/${encodeURIComponent(receiptId)}/corrections`, request,
      { withCredentials: true });
  }
}
