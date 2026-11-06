import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export type StockCountPolicy = 'TheoreticalStock' | 'MassObservation';

export interface CountCategoryPolicy {
  readonly id: string;
  readonly name: string;
  readonly policy: StockCountPolicy;
}

export interface CountProductPolicy {
  readonly id: string;
  readonly categoryId: string;
  readonly code: string;
  readonly name: string;
  readonly unit: string;
  readonly saleMode: 'weight' | 'unit';
  readonly policyOverride: StockCountPolicy | null;
}

export interface CountPolicyOptions {
  readonly categories: readonly CountCategoryPolicy[];
  readonly products: readonly CountProductPolicy[];
}

export interface PhysicalCountLine {
  readonly id: string;
  readonly productId: string;
  readonly productCode: string;
  readonly productName: string;
  readonly unit: string;
  readonly saleMode: 'weight' | 'unit';
  readonly policy: StockCountPolicy;
  readonly systemOnHand: number;
  readonly systemReserved: number;
  readonly systemProtectedOnHand: number;
  readonly systemQuarantined: number;
  readonly countedQuantity: number;
  readonly delta: number;
  readonly countedByUserId: string;
  readonly countedAtUtc: string;
  readonly inventoryMovementId: string | null;
}

export interface PhysicalCount {
  readonly id: string;
  readonly operationId: string;
  readonly branchId: string;
  readonly status: 'Draft' | 'Submitted' | 'Approved' | 'Rejected' | 'Canceled';
  readonly version: number;
  readonly createdByUserId: string;
  readonly createdAtUtc: string;
  readonly submittedByUserId: string | null;
  readonly submittedAtUtc: string | null;
  readonly decidedByUserId: string | null;
  readonly decidedAtUtc: string | null;
  readonly decisionReason: string | null;
  readonly massObservedKg: number;
  readonly massLedgerReferenceKg: number;
  readonly lines: readonly PhysicalCountLine[];
}

export interface PhysicalCountPage {
  readonly items: readonly Pick<PhysicalCount, 'id' | 'status' | 'version' |
    'createdAtUtc' | 'submittedAtUtc' | 'decidedAtUtc'>[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalItems: number;
  readonly totalPages: number;
}

@Injectable({ providedIn: 'root' })
export class PhysicalStockCountClient {
  private readonly http = inject(HttpClient);
  private readonly root = '/api/inventory/counts';

  list(page = 1) {
    return this.http.get<PhysicalCountPage>(this.root, {
      params: new HttpParams().set('page', page).set('pageSize', 20),
      withCredentials: true,
    });
  }

  get(id: string) {
    return this.http.get<PhysicalCount>(`${this.root}/${id}`, { withCredentials: true });
  }

  policies() {
    return this.http.get<CountPolicyOptions>(`${this.root}/policy`,
      { withCredentials: true });
  }

  setCategoryPolicy(id: string, policy: StockCountPolicy) {
    return this.http.patch<CountCategoryPolicy>(
      `/api/inventory/count-policy/categories/${id}`, { policy },
      { withCredentials: true });
  }

  setProductPolicy(id: string, policyOverride: StockCountPolicy | null) {
    return this.http.patch<CountProductPolicy>(
      `/api/inventory/count-policy/products/${id}`, { policyOverride },
      { withCredentials: true });
  }

  create(operationId: string) {
    return this.http.post<PhysicalCount>(this.root, { operationId },
      { withCredentials: true });
  }

  addLine(id: string, version: number, productId: string,
    countedQuantity: number) {
    return this.http.post<PhysicalCount>(`${this.root}/${id}/lines`,
      { version, productId, countedQuantity }, { withCredentials: true });
  }

  submit(id: string, version: number) {
    return this.http.post<PhysicalCount>(`${this.root}/${id}/submit`,
      { version }, { withCredentials: true });
  }

  approve(id: string, version: number, reason: string) {
    return this.http.post<PhysicalCount>(`${this.root}/${id}/approve`,
      { version, reason }, { withCredentials: true });
  }

  reject(id: string, version: number, reason: string) {
    return this.http.post<PhysicalCount>(`${this.root}/${id}/reject`,
      { version, reason }, { withCredentials: true });
  }

  cancel(id: string, version: number) {
    return this.http.post<PhysicalCount>(`${this.root}/${id}/cancel`,
      { version }, { withCredentials: true });
  }
}
