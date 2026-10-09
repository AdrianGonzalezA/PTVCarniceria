import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export type TaxKind = 'iva' | 'otro';

export interface TaxCatalogEntry {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly kind: TaxKind;
  readonly ratePercent: number;
  readonly isActive: boolean;
  readonly createdAtUtc: string;
  readonly deactivatedAtUtc: string | null;
}

export interface TaxCatalogPage {
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
  readonly items: readonly TaxCatalogEntry[];
}

export interface VatTaxOption {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly ratePercent: number;
}

@Injectable({ providedIn: 'root' })
export class AdminTaxCatalogClient {
  private readonly http = inject(HttpClient);

  list(page = 1, search = '') {
    let params = new HttpParams().set('page', page);
    if (search) params = params.set('search', search);
    return this.http.get<TaxCatalogPage>('/api/admin/taxes', { params, withCredentials: true });
  }

  vatOptions() {
    return this.http.get<readonly VatTaxOption[]>('/api/admin/taxes/options',
      { withCredentials: true });
  }

  active() {
    return this.http.get<readonly TaxCatalogEntry[]>('/api/admin/taxes/active',
      { withCredentials: true });
  }

  create(code: string, name: string, kind: TaxKind, ratePercent: number) {
    return this.http.post<TaxCatalogEntry>('/api/admin/taxes',
      { code, name, kind, ratePercent }, { withCredentials: true });
  }

  deactivate(taxId: string) {
    return this.http.patch<TaxCatalogEntry>(`/api/admin/taxes/${encodeURIComponent(taxId)}`,
      { isActive: false }, { withCredentials: true });
  }
}
