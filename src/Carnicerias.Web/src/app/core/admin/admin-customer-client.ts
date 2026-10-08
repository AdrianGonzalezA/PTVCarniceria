import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export interface AdminCustomer {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly isActive: boolean;
  readonly creditEnabled: boolean;
}

export interface AdminCustomerPage {
  readonly items: readonly AdminCustomer[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalItems: number;
  readonly totalPages: number;
}

export interface AdminCustomerUpdate {
  readonly code?: string;
  readonly name?: string;
  readonly isActive?: boolean;
  readonly creditEnabled?: boolean;
}

@Injectable({ providedIn: 'root' })
export class AdminCustomerClient {
  private readonly http = inject(HttpClient);
  private readonly endpoint = '/api/admin/customers';

  list(page = 1, search = '') {
    let params = new HttpParams().set('page', page).set('pageSize', 20);
    if (search) params = params.set('search', search);
    return this.http.get<AdminCustomerPage>(this.endpoint, { params, withCredentials: true });
  }

  create(code: string, name: string) {
    return this.http.post<AdminCustomer>(this.endpoint, { code, name }, { withCredentials: true });
  }

  update(id: string, changes: AdminCustomerUpdate) {
    return this.http.patch<AdminCustomer>(`${this.endpoint}/${id}`, changes, { withCredentials: true });
  }
}
