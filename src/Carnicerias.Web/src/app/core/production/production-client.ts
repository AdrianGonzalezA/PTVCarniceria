import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export interface ProductionClassifier {
  readonly id: string;
  readonly name: string;
  readonly isActive: boolean;
}

export interface ProductionProduct {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly unit: string;
  readonly saleMode: 'Weight' | 'Unit';
}

export interface ProductionBarcodeProfile {
  readonly id: string;
  readonly name: string;
  readonly revision: number;
  readonly formula: string;
  readonly weightField: string;
  readonly weightDecimals: number;
}

export interface ProductionPiece {
  readonly id: string;
  readonly externalIdentifier: string;
  readonly weightKg: number;
  readonly rawBarcode: string;
  readonly ownedByOrder: boolean;
}

export interface ProductionLineInput {
  readonly productId: string;
  readonly direction: 'Input' | 'Output';
  readonly quantity: number;
  readonly massKg: number;
  readonly inputPieceId: string | null;
  readonly outputBarcodeProfileId: string | null;
  readonly outputSourceSystem: string | null;
  readonly outputRawBarcode: string | null;
  readonly outputIdentifierField: string | null;
}

export interface ProductionLine extends ProductionLineInput {
  readonly id: string;
  readonly productCode: string;
  readonly productName: string;
  readonly unit: string;
  readonly saleMode: 'Weight' | 'Unit';
  readonly outputExternalIdentifier: string | null;
  readonly inventoryMovementId: string | null;
  readonly outputPieceId: string | null;
}

export interface ProductionOrder {
  readonly id: string;
  readonly operationId: string;
  readonly branchId: string;
  readonly kind: 'Standard' | 'Correction';
  readonly status: 'Open' | 'Closed' | 'Canceled';
  readonly classifierId: string | null;
  readonly correctsOrderId: string | null;
  readonly version: number;
  readonly createdByUserId: string;
  readonly createdAtUtc: string;
  readonly closedByUserId: string | null;
  readonly closedAtUtc: string | null;
  readonly canceledByUserId: string | null;
  readonly canceledAtUtc: string | null;
  readonly inputMassKg: number;
  readonly outputMassKg: number;
  readonly reportedWasteKg: number;
  readonly massDifferenceKg: number;
  readonly wasteReason: string;
  readonly lines: readonly ProductionLine[];
}

export interface ProductionOrderPage {
  readonly items: readonly ProductionOrder[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalItems: number;
  readonly totalPages: number;
}

export interface ProductionSummary {
  readonly kind: 'Standard' | 'Correction';
  readonly classifierId: string | null;
  readonly orderCount: number;
  readonly inputMassKg: number;
  readonly outputMassKg: number;
  readonly reportedWasteKg: number;
  readonly massDifferenceKg: number;
}

@Injectable({ providedIn: 'root' })
export class ProductionClient {
  private readonly http = inject(HttpClient);

  classifiers(includeInactive = false) {
    return this.http.get<readonly ProductionClassifier[]>('/api/production/classifiers', {
      params: { includeInactive }, withCredentials: true,
    });
  }

  createClassifier(name: string) {
    return this.http.post<ProductionClassifier>('/api/production/classifiers',
      { name }, { withCredentials: true });
  }

  updateClassifier(id: string, values: { name?: string; isActive?: boolean }) {
    return this.http.patch<ProductionClassifier>(`/api/production/classifiers/${id}`,
      values, { withCredentials: true });
  }

  options() {
    return this.http.get<{ readonly products: readonly ProductionProduct[];
      readonly profiles: readonly ProductionBarcodeProfile[] }>(
      '/api/production/orders/options', { withCredentials: true });
  }

  pieces(productId: string, orderId?: string, query?: string) {
    let params = new HttpParams().set('productId', productId);
    if (orderId) params = params.set('orderId', orderId);
    if (query) params = params.set('query', query);
    return this.http.get<readonly ProductionPiece[]>('/api/production/orders/pieces',
      { params, withCredentials: true });
  }

  list(page = 1, status?: ProductionOrder['status']) {
    let params = new HttpParams().set('page', page);
    if (status) params = params.set('status', status);
    return this.http.get<ProductionOrderPage>('/api/production/orders',
      { params, withCredentials: true });
  }

  summary() {
    return this.http.get<readonly ProductionSummary[]>('/api/production/orders/summary',
      { withCredentials: true });
  }

  get(id: string) {
    return this.http.get<ProductionOrder>(`/api/production/orders/${id}`,
      { withCredentials: true });
  }

  create(input: { operationId: string; classifierId: string | null;
    correctsOrderId: string | null }) {
    return this.http.post<ProductionOrder>('/api/production/orders', input,
      { withCredentials: true });
  }

  replaceLines(order: ProductionOrder, lines: readonly ProductionLineInput[]) {
    return this.http.put<ProductionOrder>(`/api/production/orders/${order.id}/lines`,
      { version: order.version, lines }, { withCredentials: true });
  }

  cancel(order: ProductionOrder) {
    return this.http.post<ProductionOrder>(`/api/production/orders/${order.id}/cancel`,
      { version: order.version }, { withCredentials: true });
  }

  close(order: ProductionOrder, reportedWasteKg: number, wasteReason: string) {
    return this.http.post<ProductionOrder>(`/api/production/orders/${order.id}/close`,
      { version: order.version, reportedWasteKg, wasteReason },
      { withCredentials: true });
  }
}
