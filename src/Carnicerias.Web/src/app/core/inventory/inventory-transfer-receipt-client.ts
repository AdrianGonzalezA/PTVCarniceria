import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export interface TransferReceiptLine {
  readonly id: string;
  readonly productId: string;
  readonly productCode: string;
  readonly productName: string;
  readonly unit: string;
  readonly saleMode: 'unit' | 'weight';
  readonly sentQuantity: number;
  readonly receivedQuantity: number;
  readonly isPiece: boolean;
  readonly sourceLineId: string | null;
  readonly sourcePieceId: string | null;
  readonly sourceExternalIdentifier: string | null;
  readonly externalIdentifier: string | null;
  readonly rawBarcode: string | null;
  readonly sourcePieceVerified: boolean;
  readonly inventoryMovementId: string | null;
  readonly inventoryPieceId: string | null;
  readonly differenceStatus: 'None' | 'PendingReview' | 'Resolved';
  readonly differenceReason: string | null;
}

export interface TransferReceipt {
  readonly id: string;
  readonly operationId: string;
  readonly branchId: string;
  readonly sourceBranchName: string;
  readonly sourceBranchId: string | null;
  readonly sourceTransferId: string | null;
  readonly documentNumber: string;
  readonly documentDate: string;
  readonly status: 'Draft' | 'Approved' | 'Canceled';
  readonly version: number;
  readonly importedSha256: string | null;
  readonly createdAtUtc: string;
  readonly approvedAtUtc: string | null;
  readonly canceledAtUtc: string | null;
  readonly lines: readonly TransferReceiptLine[];
}

export interface TransferReceiptPage {
  readonly items: readonly Pick<TransferReceipt, 'id' | 'sourceBranchName' |
    'documentNumber' | 'documentDate' | 'status' | 'version' |
    'createdAtUtc' | 'approvedAtUtc'>[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalItems: number;
  readonly totalPages: number;
}

@Injectable({ providedIn: 'root' })
export class InventoryTransferReceiptClient {
  private readonly http = inject(HttpClient);
  private readonly root = '/api/inventory/transfer-receipts';

  list(page = 1) {
    return this.http.get<TransferReceiptPage>(this.root, {
      params: new HttpParams().set('page', page).set('pageSize', 20),
      withCredentials: true,
    });
  }

  get(id: string) {
    return this.http.get<TransferReceipt>(`${this.root}/${id}`, { withCredentials: true });
  }

  create(input: { operationId: string; sourceBranchName: string;
    documentNumber: string; documentDate: string }) {
    return this.http.post<TransferReceipt>(this.root, input, { withCredentials: true });
  }

  importRemito(operationId: string, remito: object) {
    return this.http.post<TransferReceipt>(`${this.root}/import`,
      { operationId, remito }, { withCredentials: true });
  }

  addLine(id: string, input: { version: number; productId: string;
    sentQuantity: number; receivedQuantity: number; isPiece: boolean }) {
    return this.http.post<TransferReceipt>(`${this.root}/${id}/lines`, input,
      { withCredentials: true });
  }

  setReceived(id: string, lineId: string, version: number, receivedQuantity: number) {
    return this.http.patch<TransferReceipt>(`${this.root}/${id}/lines/${lineId}`,
      { version, receivedQuantity }, { withCredentials: true });
  }

  scan(id: string, lineId: string, input: { version: number;
    barcodeProfileId: string; sourceSystem: string;
    identifierField: string; code: string }) {
    return this.http.post<TransferReceipt>(`${this.root}/${id}/lines/${lineId}/scan`,
      input, { withCredentials: true });
  }

  approve(id: string, version: number) {
    return this.http.post<TransferReceipt>(`${this.root}/${id}/approve`,
      { version }, { withCredentials: true });
  }

  cancel(id: string, version: number) {
    return this.http.post<TransferReceipt>(`${this.root}/${id}/cancel`,
      { version }, { withCredentials: true });
  }

  resolve(id: string, lineId: string, version: number, reason: string) {
    return this.http.post<TransferReceipt>(`${this.root}/${id}/lines/${lineId}/resolve`,
      { version, reason }, { withCredentials: true });
  }
}
