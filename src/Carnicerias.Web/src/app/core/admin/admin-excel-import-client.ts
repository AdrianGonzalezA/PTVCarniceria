import { HttpClient, HttpHeaders } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export type AdminImportKind = 'categories' | 'products' | 'price-lists' | 'customers';

export interface AdminImportIssue {
  readonly sheet: string;
  readonly row: number;
  readonly column: string;
  readonly code: string;
  readonly message: string;
}

export interface AdminImportRow {
  readonly sheet: string;
  readonly row: number;
  readonly status: 'create' | 'update' | 'skipped' | 'unchanged';
  readonly message: string;
}

export interface AdminImportPreview {
  readonly canApply: boolean;
  readonly createCount: number;
  readonly updateCount: number;
  readonly skippedCount: number;
  readonly unchangedCount: number;
  readonly rows: readonly AdminImportRow[];
  readonly issues: readonly AdminImportIssue[];
}

@Injectable({ providedIn: 'root' })
export class AdminExcelImportClient {
  private readonly http = inject(HttpClient);
  private readonly mime = 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet';

  templateUrl(kind: AdminImportKind): string {
    return `/api/admin/imports/${kind}/template`;
  }

  preview(kind: AdminImportKind, file: File) {
    return this.http.post<AdminImportPreview>(`/api/admin/imports/${kind}/preview`, file,
      { headers: new HttpHeaders({ 'Content-Type': this.mime }), withCredentials: true });
  }

  apply(kind: AdminImportKind, file: File, operationId: string) {
    return this.http.post<AdminImportPreview>(`/api/admin/imports/${kind}/apply`, file,
      { headers: new HttpHeaders({ 'Content-Type': this.mime, 'Idempotency-Key': operationId }),
        withCredentials: true });
  }
}
