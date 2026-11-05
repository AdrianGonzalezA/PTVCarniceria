import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export interface TransferProductOption {
  readonly productId: string;
  readonly code: string;
  readonly name: string;
  readonly unit: string;
  readonly saleMode: 'unit' | 'weight';
}

export interface TransferBranchOption {
  readonly id: string;
  readonly name: string;
}

export interface TransferPieceOption {
  readonly id: string;
  readonly externalIdentifier: string;
  readonly rawBarcode: string;
  readonly weightKg: number;
}

export interface TransferLineInput {
  readonly productId: string;
  readonly quantity: number;
  readonly pieceId: string | null;
}

export interface TransferLine extends TransferLineInput {
  readonly id: string;
  readonly productCode: string;
  readonly productName: string;
  readonly unit: string;
  readonly saleMode: 'unit' | 'weight';
  readonly pieceIdentifier: string | null;
  readonly inventoryMovementId: string | null;
}

export interface InventoryTransfer {
  readonly id: string;
  readonly operationId: string;
  readonly branchId: string;
  readonly destinationBranchId: string | null;
  readonly destinationName: string;
  readonly documentNumber: string;
  readonly documentDate: string;
  readonly status: 'Draft' | 'Dispatched' | 'Canceled';
  readonly version: number;
  readonly createdAtUtc: string;
  readonly dispatchedAtUtc: string | null;
  readonly canceledAtUtc: string | null;
  readonly lines: readonly TransferLine[];
}

export interface TransferSummary {
  readonly id: string;
  readonly documentNumber: string;
  readonly documentDate: string;
  readonly destinationName: string;
  readonly status: InventoryTransfer['status'];
  readonly version: number;
  readonly createdAtUtc: string;
  readonly dispatchedAtUtc: string | null;
}

export interface TransferPage {
  readonly items: readonly TransferSummary[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalItems: number;
  readonly totalPages: number;
}

@Injectable({ providedIn: 'root' })
export class InventoryTransferClient {
  private readonly http = inject(HttpClient);
  private readonly root = '/api/inventory/transfers';

  products() {
    return this.http.get<readonly TransferProductOption[]>(`${this.root}/product-options`,
      { withCredentials: true });
  }

  branches() {
    return this.http.get<readonly TransferBranchOption[]>(`${this.root}/branch-options`,
      { withCredentials: true });
  }

  pieces(productId: string, query?: string) {
    let params = new HttpParams().set('productId', productId);
    if (query) params = params.set('query', query);
    return this.http.get<readonly TransferPieceOption[]>(`${this.root}/piece-options`,
      { params, withCredentials: true });
  }

  list(page = 1, status?: InventoryTransfer['status']) {
    let params = new HttpParams().set('page', page).set('pageSize', 20);
    if (status) params = params.set('status', status);
    return this.http.get<TransferPage>(this.root, { params, withCredentials: true });
  }

  get(id: string) {
    return this.http.get<InventoryTransfer>(`${this.root}/${id}`, { withCredentials: true });
  }

  create(input: { operationId: string; destinationName: string;
    destinationBranchId: string | null; documentDate: string }) {
    return this.http.post<InventoryTransfer>(this.root, input, { withCredentials: true });
  }

  replaceLines(id: string, version: number, lines: readonly TransferLineInput[]) {
    return this.http.put<InventoryTransfer>(`${this.root}/${id}/lines`,
      { version, lines }, { withCredentials: true });
  }

  dispatch(id: string, version: number) {
    return this.http.post<InventoryTransfer>(`${this.root}/${id}/dispatch`,
      { version }, { withCredentials: true });
  }

  cancel(id: string, version: number) {
    return this.http.post<InventoryTransfer>(`${this.root}/${id}/cancel`,
      { version }, { withCredentials: true });
  }
}
