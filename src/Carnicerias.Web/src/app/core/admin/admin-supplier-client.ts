import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export interface AdminSupplier {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly identification: string | null;
  readonly isActive: boolean;
}

export interface AdminSupplierPage {
  readonly items: readonly AdminSupplier[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalItems: number;
  readonly totalPages: number;
}

export interface AdminSupplierOption {
  readonly id: string;
  readonly code: string;
  readonly name: string;
}

export interface AdminSupplierDetails {
  readonly code: string;
  readonly name: string;
  readonly identification: string | null;
}

@Injectable({ providedIn: 'root' })
export class AdminSupplierClient {
  private readonly http = inject(HttpClient);
  private readonly endpoint = '/api/admin/suppliers';

  list(page = 1, search = '') {
    let params = new HttpParams().set('page', page).set('pageSize', 20);
    if (search) params = params.set('search', search);
    return this.http.get<AdminSupplierPage>(this.endpoint, { params, withCredentials: true });
  }

  options() {
    return this.http.get<readonly AdminSupplierOption[]>(`${this.endpoint}/options`, { withCredentials: true });
  }

  create(details: AdminSupplierDetails) {
    return this.http.post<AdminSupplier>(this.endpoint, details, { withCredentials: true });
  }

  update(id: string, changes: AdminSupplierDetails | { readonly isActive: boolean }) {
    return this.http.patch<AdminSupplier>(`${this.endpoint}/${id}`, changes, { withCredentials: true });
  }
}
