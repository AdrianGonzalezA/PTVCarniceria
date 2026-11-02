import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export interface InventoryPieceReceipt {
  readonly id: string;
  readonly operationId: string;
  readonly productId: string;
  readonly barcodeProfileId: string;
  readonly sourceSystem: string;
  readonly externalIdentifier: string;
  readonly receivedWeightKg: number;
  readonly rawBarcode: string;
  readonly receivedAtUtc: string;
  readonly status: string;
}

export interface InventoryPieceListItem {
  readonly id: string;
  readonly productId: string;
  readonly productCode: string;
  readonly productName: string;
  readonly sourceSystem: string;
  readonly externalIdentifier: string;
  readonly receivedWeightKg: number;
  readonly rawBarcode: string;
  readonly receivedAtUtc: string;
  readonly status: string;
  readonly reservedBySaleDraftId: string | null;
  readonly reservedAtUtc: string | null;
}

export interface InventoryPieceEvent {
  readonly id: string;
  readonly previousStatus: string | null;
  readonly status: string;
  readonly kind: string;
  readonly saleDraftId: string | null;
  readonly actorUserId: string | null;
  readonly reason: string;
  readonly createdAtUtc: string;
}

export interface ReleasePieceResult {
  readonly pieceId: string;
  readonly draftId: string;
  readonly draftCancelled: boolean;
  readonly discountReset: boolean;
}

export interface InventoryPiecePage {
  readonly items: readonly InventoryPieceListItem[];
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface ReceivePieceRequest {
  readonly operationId: string;
  readonly productId: string;
  readonly barcodeProfileId: string;
  readonly sourceSystem: string;
  readonly identifierField: string;
  readonly code: string;
}

export interface PosPieceLookup {
  readonly id: string;
  readonly productId: string;
  readonly productCode: string;
  readonly externalIdentifier: string;
  readonly receivedWeightKg: number;
  readonly rawBarcode: string;
}

@Injectable({ providedIn: 'root' })
export class InventoryPieceClient {
  private readonly http = inject(HttpClient);

  list(page = 1, pageSize = 20) {
    const params = new HttpParams().set('page', page).set('pageSize', pageSize);
    return this.http.get<InventoryPiecePage>('/api/inventory/pieces',
      { params, withCredentials: true });
  }

  receive(request: ReceivePieceRequest) {
    return this.http.post<InventoryPieceReceipt>('/api/inventory/pieces', request,
      { withCredentials: true });
  }

  events(id: string) {
    return this.http.get<readonly InventoryPieceEvent[]>(`/api/inventory/pieces/${id}/events`,
      { withCredentials: true });
  }

  release(id: string, reason: string) {
    return this.http.post<ReleasePieceResult>(`/api/inventory/pieces/${id}/release`,
      { reason }, { withCredentials: true });
  }

  lookup(code: string) {
    return this.http.get<PosPieceLookup>('/api/pos/pieces/lookup', {
      params: new HttpParams().set('code', code), withCredentials: true,
    });
  }
}
