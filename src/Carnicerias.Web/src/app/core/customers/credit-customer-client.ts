import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export interface CreditCustomerOption {
  readonly id: string;
  readonly code: string;
  readonly name: string;
}

export interface CreditCustomerAccount {
  readonly customerId: string;
  readonly customerCode: string;
  readonly customerName: string;
  readonly isActive: boolean;
  readonly creditEnabled: boolean;
  readonly totalDebt: number;
  readonly creditAvailable: number;
  readonly saleCount: number;
  readonly page: number;
  readonly sales: readonly { readonly saleId: string; readonly chargedAtUtc: string;
    readonly originalAmount: number; readonly outstandingAmount: number }[];
}

export interface CustomerCollectionReceipt {
  readonly id: string;
  readonly receiptNumber: number;
  readonly customerId: string;
  readonly amount: number;
  readonly creditAmount: number;
  readonly method: string;
  readonly createdAtUtc: string;
  readonly allocations: readonly { readonly saleId: string; readonly amount: number }[];
}

export interface CustomerCollectionRequest {
  readonly operationId: string;
  readonly method: string;
  readonly amount: number;
  readonly allocations?: readonly { readonly saleId: string; readonly amount: number }[];
}

@Injectable({ providedIn: 'root' })
export class CreditCustomerClient {
  private readonly http = inject(HttpClient);

  search(search = '') {
    let params = new HttpParams();
    if (search) params = params.set('search', search);
    return this.http.get<readonly CreditCustomerOption[]>('/api/customers/credit-options',
      { params, withCredentials: true });
  }

  searchAccounts(search = '') {
    let params = new HttpParams();
    if (search) params = params.set('search', search);
    return this.http.get<readonly CreditCustomerOption[]>('/api/customers/account-options',
      { params, withCredentials: true });
  }


  account(customerId: string, page = 1) {
    return this.http.get<CreditCustomerAccount>(`/api/customers/${encodeURIComponent(customerId)}/account`,
      { params: new HttpParams().set('page', page), withCredentials: true });
  }


  collect(customerId: string, request: CustomerCollectionRequest) {
    return this.http.post<CustomerCollectionReceipt>(
      `/api/customers/${encodeURIComponent(customerId)}/collections`, request,
      { withCredentials: true });
  }

  findCollection(operationId: string) {
    return this.http.get<CustomerCollectionReceipt | null>(
      `/api/customers/collections/operations/${encodeURIComponent(operationId)}`,
      { withCredentials: true });
  }
}
