import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export interface ReceiptSupplierOption {
  readonly id: string;
  readonly code: string;
  readonly name: string;
}

export interface ReceiptProductOption {
  readonly productId: string;
  readonly code: string;
  readonly name: string;
  readonly unit: string;
  readonly saleMode: 'unit' | 'weight';
}

export interface ReceiptLine {
  readonly id: string;
  readonly productId: string;
  readonly productCode: string;
  readonly productName: string;
  readonly unit: string;
  readonly saleMode: 'unit' | 'weight';
  readonly expectedQuantity: number | null;
  readonly receivedQuantity: number;
  readonly differenceStatus: 'None' | 'PendingReview' | 'Resolved';
  readonly differenceReason: string | null;
  readonly inventoryMovementId: string | null;
}

export interface InventoryReceipt {
  readonly id: string;
  readonly operationId: string;
  readonly supplierId: string | null;
  readonly supplierCode: string;
  readonly supplierName: string;
  readonly supplierIdentification: string | null;
  readonly sourceSystem: string | null;
  readonly documentType: string;
  readonly documentNumber: string;
  readonly documentDate: string;
  readonly destination: string | null;
  readonly externalReference: string | null;
  readonly status: 'Draft' | 'Approved' | 'Canceled';
  readonly version: number;
  readonly createdAtUtc: string;
  readonly approvedAtUtc: string | null;
  readonly canceledAtUtc: string | null;
  readonly lines: readonly ReceiptLine[];
}

export interface ReceiptSummary {
  readonly id: string;
  readonly operationId: string;
  readonly supplierName: string;
  readonly sourceSystem: string | null;
  readonly documentType: string;
  readonly documentNumber: string;
  readonly documentDate: string;
  readonly status: InventoryReceipt['status'];
  readonly version: number;
  readonly createdAtUtc: string;
  readonly approvedAtUtc: string | null;
}

export interface ReceiptPage {
  readonly items: readonly ReceiptSummary[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalItems: number;
  readonly totalPages: number;
}

export interface ReceiptLineInput {
  readonly productId: string;
  readonly expectedQuantity: number | null;
  readonly receivedQuantity: number;
}

export interface CreateReceiptInput {
  readonly operationId: string;
  readonly supplierId: string;
  readonly documentType: string;
  readonly documentNumber: string;
  readonly documentDate: string;
  readonly destination?: string;
  readonly externalReference?: string;
}

export interface ReceiptImportPreview {
  readonly canApply: boolean;
  readonly createCount: number;
  readonly rows: readonly { sheet: string; row: number; status: string; message: string }[];
  readonly issues: readonly { sheet: string; row: number; column: string;
    code: string; message: string }[];
}

@Injectable({ providedIn: 'root' })
export class InventoryReceiptClient {
  private readonly http = inject(HttpClient);
  private readonly root = '/api/inventory/receipts';
  private readonly xlsx = 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet';

  suppliers() {
    return this.http.get<readonly ReceiptSupplierOption[]>(`${this.root}/supplier-options`,
      { withCredentials: true });
  }


  products() {
    return this.http.get<readonly ReceiptProductOption[]>(`${this.root}/product-options`,
      { withCredentials: true });
  }

  templateUrl(id: string): string {
    return `${this.root}/${id}/import/template`;
  }

  previewImport(id: string, version: number, file: File) {
    return this.http.post<ReceiptImportPreview>(`${this.root}/${id}/import/preview`, file,
      { params: new HttpParams().set('version', version),
        headers: new HttpHeaders({ 'Content-Type': this.xlsx }), withCredentials: true });
  }

  applyImport(id: string, version: number, file: File) {
    return this.http.post<InventoryReceipt>(`${this.root}/${id}/import/apply`, file,
      { params: new HttpParams().set('version', version),
        headers: new HttpHeaders({ 'Content-Type': this.xlsx }), withCredentials: true });
  }

  list(page = 1, status?: InventoryReceipt['status']) {
    let params = new HttpParams().set('page', page).set('pageSize', 20);
    if (status) params = params.set('status', status);
    return this.http.get<ReceiptPage>(this.root, { params, withCredentials: true });
  }

  get(id: string) {
    return this.http.get<InventoryReceipt>(`${this.root}/${id}`, { withCredentials: true });
  }

  create(input: CreateReceiptInput) {
    return this.http.post<InventoryReceipt>(this.root, input, { withCredentials: true });
  }

  replaceLines(id: string, version: number, lines: readonly ReceiptLineInput[]) {
    return this.http.put<InventoryReceipt>(`${this.root}/${id}/lines`,
      { version, lines }, { withCredentials: true });
  }

  approve(id: string, version: number) {
    return this.http.post<InventoryReceipt>(`${this.root}/${id}/approve`,
      { version }, { withCredentials: true });
  }

  cancel(id: string, version: number) {
    return this.http.post<InventoryReceipt>(`${this.root}/${id}/cancel`,
      { version }, { withCredentials: true });
  }

  resolveDifference(id: string, lineId: string, version: number, reason: string) {
    return this.http.post<InventoryReceipt>(`${this.root}/${id}/lines/${lineId}/resolve`,
      { version, reason }, { withCredentials: true });
  }
}
