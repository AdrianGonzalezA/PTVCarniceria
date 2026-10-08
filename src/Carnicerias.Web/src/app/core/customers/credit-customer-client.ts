import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export interface CreditCustomerOption {
  readonly id: string;
  readonly code: string;
  readonly name: string;
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
}
