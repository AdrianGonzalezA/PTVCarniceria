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

@Injectable({ providedIn: 'root' })
export class CreditCustomerClient {
  private readonly http = inject(HttpClient);

  search(search = '') {
    let params = new HttpParams();
    if (search) params = params.set('search', search);
    return this.http.get<readonly CreditCustomerOption[]>('/api/customers/credit-options',
      { params, withCredentials: true });
  }


  account(customerId: string, page = 1) {
    return this.http.get<CreditCustomerAccount>(`/api/customers/${encodeURIComponent(customerId)}/account`,
      { params: new HttpParams().set('page', page), withCredentials: true });
  }
}
