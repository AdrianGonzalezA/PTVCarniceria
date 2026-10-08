import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export interface AdminPriceList {
  readonly id: string;
  readonly name: string;
  readonly isActive: boolean;
  readonly activeBranchCount: number;
  readonly currentPriceCount: number;
}

export interface AdminPriceListPage {
  readonly items: readonly AdminPriceList[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalItems: number;
  readonly totalPages: number;
}

export interface BranchPriceListAssignment {
  readonly branchId: string;
  readonly branchName: string;
  readonly branchActive: boolean;
  readonly isAssigned: boolean;
}

export interface AdminProductPrice {
  readonly productId: string;
  readonly code: string;
  readonly name: string;
  readonly unit: string;
  readonly cost: number;
  readonly isActive: boolean;
  readonly currentPrice: number | null;
  readonly effectiveFromUtc: string | null;
}

export interface AdminProductPricePage {
  readonly items: readonly AdminProductPrice[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalItems: number;
  readonly totalPages: number;
}

export interface AdminPriceHistory {
  readonly id: string;
  readonly amount: number;
  readonly effectiveFromUtc: string;
  readonly effectiveToUtc: string | null;
  readonly changedByUsername: string;
}

export interface AdminCurrentPrice {
  readonly id: string;
  readonly amount: number;
  readonly effectiveFromUtc: string;
}

@Injectable({ providedIn: 'root' })
export class AdminPriceListClient {
  private readonly http = inject(HttpClient);
  private readonly endpoint = '/api/admin/price-lists';

  list(page = 1, search = '') {
    let params = new HttpParams().set('page', page).set('pageSize', 20);
    if (search) params = params.set('search', search);
    return this.http.get<AdminPriceListPage>(this.endpoint, { params, withCredentials: true });
  }

  create(name: string) {
    return this.http.post<AdminPriceList>(this.endpoint, { name }, { withCredentials: true });
  }

  update(id: string, changes: { readonly name?: string; readonly isActive?: boolean }) {
    return this.http.patch<AdminPriceList>(`${this.endpoint}/${id}`, changes, { withCredentials: true });
  }

  branches(id: string) {
    return this.http.get<readonly BranchPriceListAssignment[]>(`${this.endpoint}/${id}/branches`,
      { withCredentials: true });
  }

  assignBranch(id: string, branchId: string, isActive: boolean) {
    return this.http.put<BranchPriceListAssignment>(`${this.endpoint}/${id}/branches/${branchId}`,
      { isActive }, { withCredentials: true });
  }

  prices(id: string, page = 1, search = '') {
    let params = new HttpParams().set('page', page).set('pageSize', 20);
    if (search) params = params.set('search', search);
    return this.http.get<AdminProductPricePage>(`${this.endpoint}/${id}/prices`,
      { params, withCredentials: true });
  }

  history(id: string, productId: string) {
    return this.http.get<readonly AdminPriceHistory[]>(
      `${this.endpoint}/${id}/products/${productId}/history`, { withCredentials: true });
  }

  setPrice(id: string, productId: string, amount: number) {
    return this.http.put<AdminCurrentPrice>(`${this.endpoint}/${id}/products/${productId}/price`,
      { amount }, { withCredentials: true });
  }
}
