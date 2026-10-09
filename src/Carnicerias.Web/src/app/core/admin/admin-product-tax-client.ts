import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export type TaxTreatment = 'taxed' | 'exempt' | 'notTaxed';

export interface ProductTaxRule {
  readonly id: string;
  readonly productId: string;
  readonly treatment: TaxTreatment;
  readonly ratePercent: number;
  readonly effectiveFromUtc: string;
  readonly effectiveToUtc: string | null;
  readonly changedByUserId: string;
}

export interface TaxProductRow {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly isActive: boolean;
  readonly currentRule: ProductTaxRule | null;
}

export interface TaxProductPage {
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
  readonly items: readonly TaxProductRow[];
}

@Injectable({ providedIn: 'root' })
export class AdminProductTaxClient {
  private readonly http = inject(HttpClient);

  list(page = 1, search = '') {
    let params = new HttpParams().set('page', page);
    if (search) params = params.set('search', search);
    return this.http.get<TaxProductPage>('/api/admin/product-tax-rules',
      { params, withCredentials: true });
  }

  history(productId: string) {
    return this.http.get<readonly ProductTaxRule[]>(
      `/api/admin/product-tax-rules/${encodeURIComponent(productId)}`,
      { withCredentials: true });
  }

  set(productId: string, treatment: TaxTreatment, ratePercent: number) {
    return this.http.put<ProductTaxRule>(
      `/api/admin/product-tax-rules/${encodeURIComponent(productId)}`,
      { treatment, ratePercent }, { withCredentials: true });
  }
}
