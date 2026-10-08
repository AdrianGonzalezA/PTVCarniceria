import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export interface BarcodePreviewRequest {
  readonly formula: string;
  readonly code: string;
  readonly weightField: string;
  readonly weightDecimals: number;
}

export interface BarcodePreviewResponse {
  readonly length: number;
  readonly fields: Readonly<Record<string, string>>;
  readonly weightKg: number;
}

@Injectable({ providedIn: 'root' })
export class BarcodeLayoutClient {
  private readonly http = inject(HttpClient);

  preview(request: BarcodePreviewRequest) {
    return this.http.post<BarcodePreviewResponse>('/api/admin/barcode-layouts/preview', request,
      { withCredentials: true });
  }
}
