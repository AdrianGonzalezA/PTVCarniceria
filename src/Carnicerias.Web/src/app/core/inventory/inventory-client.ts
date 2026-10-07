import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export interface InventoryStockItem {
  readonly productId: string;
  readonly code: string;
  readonly name: string;
  readonly onHand: number;
  readonly reserved: number;
  readonly available: number;
}

export interface StockAdjustmentResponse {
  readonly operationId: string;
  readonly onHand: number;
  readonly reserved: number;
  readonly available: number;
}

@Injectable({ providedIn: 'root' })
export class InventoryClient {
  private readonly http = inject(HttpClient);

  stock() {
    return this.http.get<readonly InventoryStockItem[]>('/api/inventory/stock', { withCredentials: true });
  }

  adjust(productId: string, operationId: string, quantityDelta: number, reason: string) {
    return this.http.post<StockAdjustmentResponse>('/api/inventory/adjustments',
      { productId, operationId, quantityDelta, reason }, { withCredentials: true });
  }
}
