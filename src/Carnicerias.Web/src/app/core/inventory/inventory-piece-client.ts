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
  readonly originPieceId?: string | null;
  readonly productionOrderId?: string | null;
  readonly reservedByProductionOrderId?: string | null;
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
  readonly originPieceId?: string | null;
  readonly productionOrderId?: string | null;
  readonly reservedByProductionOrderId?: string | null;
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
  readonly evidenceReference: string | null;
  readonly evidenceSha256: string | null;
  readonly observedWeightKg: number | null;
  readonly countedOnHand: number | null;
  readonly countedAtUtc: string | null;
  readonly inventoryMovementId: string | null;
  readonly productionOrderId?: string | null;
}

export interface InventoryPieceReconciliationLease {
  readonly lockId: string;
  readonly pieceId: string;
  readonly startedAtUtc: string;
  readonly expiresAtUtc: string;
}

export interface ReconcilePieceRequest {
  readonly lockId: string;
  readonly operationId: string;
  readonly outcome: 'available' | 'unavailable';
  readonly observedWeightKg: number;
  readonly countedOnHand: number;
  readonly reason: string;
  readonly evidenceReference: string;
  readonly evidenceSha256?: string;
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

  list(page = 1, pageSize = 20, status?: string) {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    if (status) params = params.set('status', status);
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

  acquireReconciliationLease(id: string, lockId: string) {
    return this.http.post<InventoryPieceReconciliationLease>(
      `/api/inventory/pieces/${id}/reconciliation-lease`, { lockId }, { withCredentials: true });
  }

  renewReconciliationLease(id: string, lockId: string) {
    return this.http.post<{ readonly lockId: string; readonly expiresAtUtc: string }>(
      `/api/inventory/pieces/${id}/reconciliation-lease/renew`, { lockId },
      { withCredentials: true });
  }

  releaseReconciliationLease(id: string, lockId: string) {
    return this.http.delete<void>(`/api/inventory/pieces/${id}/reconciliation-lease/${lockId}`,
      { withCredentials: true });
  }

  reconcile(id: string, request: ReconcilePieceRequest) {
    return this.http.post<{ readonly pieceId: string; readonly status: string;
      readonly eventId: string; readonly alreadyApplied: boolean }>(
      `/api/inventory/pieces/${id}/reconcile`, request, { withCredentials: true });
  }

  lookup(code: string) {
    return this.http.get<PosPieceLookup>('/api/pos/pieces/lookup', {
      params: new HttpParams().set('code', code), withCredentials: true,
    });
  }
}
