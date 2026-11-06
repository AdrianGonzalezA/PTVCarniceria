import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export type SaleMode = 'weight' | 'unit';

export interface AdminProduct {
  readonly id: string;
  readonly categoryId: string;
  readonly categoryName: string;
  readonly code: string;
  readonly name: string;
  readonly unit: string;
  readonly saleMode: SaleMode;
  readonly cost: number | null;
  readonly isActive: boolean;
  readonly alternateCodeCount: number;
  readonly allowSaleWithoutStock: boolean;
  readonly anatomicalGroup: string | null;
}

export interface AdminProductPage {
  readonly items: readonly AdminProduct[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalItems: number;
  readonly totalPages: number;
}

export interface AdminCostHistory {
  readonly id: string;
  readonly amount: number;
  readonly effectiveFromUtc: string;
  readonly effectiveToUtc: string | null;
  readonly changedByUsername: string | null;
}

export interface AdminProductCreate {
  readonly categoryId: string;
  readonly code: string;
  readonly name: string;
  readonly unit: string;
  readonly saleMode: SaleMode;
  readonly cost: number | null;
  readonly allowSaleWithoutStock?: boolean;
  readonly anatomicalGroup?: string | null;
}

export interface AdminProductUpdate {
  readonly categoryId?: string;
  readonly name?: string;
  readonly unit?: string;
  readonly saleMode?: SaleMode;
  readonly cost?: number;
  readonly isActive?: boolean;
  readonly allowSaleWithoutStock?: boolean;
  readonly anatomicalGroup?: string;
}

@Injectable({ providedIn: 'root' })
export class AdminProductClient {
  private readonly http = inject(HttpClient);
  private readonly endpoint = '/api/admin/products';

  list(page = 1, search = '', categoryId = '', status = '', anatomicalGroup = '') {
    let params = new HttpParams().set('page', page).set('pageSize', 20);
    if (search) params = params.set('search', search);
    if (categoryId) params = params.set('categoryId', categoryId);
    if (status) params = params.set('isActive', status === 'active');
    if (anatomicalGroup) params = params.set('anatomicalGroup', anatomicalGroup);
    return this.http.get<AdminProductPage>(this.endpoint, { params, withCredentials: true });
  }

  anatomicalGroups() {
    return this.http.get<readonly string[]>(`${this.endpoint}/anatomical-groups`,
      { withCredentials: true });
  }

  create(product: AdminProductCreate) {
    return this.http.post<AdminProduct>(this.endpoint, product, { withCredentials: true });
  }

  update(id: string, changes: AdminProductUpdate) {
    return this.http.patch<AdminProduct>(`${this.endpoint}/${id}`, changes, { withCredentials: true });
  }

  costHistory(id: string) {
    return this.http.get<readonly AdminCostHistory[]>(`${this.endpoint}/${id}/cost-history`,
      { withCredentials: true });
  }
}
