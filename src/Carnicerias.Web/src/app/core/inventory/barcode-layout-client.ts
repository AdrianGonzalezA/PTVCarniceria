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

export interface BarcodeProfile {
  readonly id: string;
  readonly name: string;
  readonly revision: number;
  readonly formula: string;
  readonly weightField: string;
  readonly weightDecimals: number;
  readonly createdAtUtc: string;
}

export interface BarcodeProfileRequest {
  readonly name: string;
  readonly formula: string;
  readonly weightField: string;
  readonly weightDecimals: number;
}

@Injectable({ providedIn: 'root' })
export class BarcodeLayoutClient {
  private readonly http = inject(HttpClient);

  profiles() {
    return this.http.get<readonly BarcodeProfile[]>('/api/admin/barcode-layouts',
      { withCredentials: true });
  }

  saveProfile(request: BarcodeProfileRequest) {
    return this.http.post<BarcodeProfile>('/api/admin/barcode-layouts', request,
      { withCredentials: true });
  }

  preview(request: BarcodePreviewRequest) {
    return this.http.post<BarcodePreviewResponse>('/api/admin/barcode-layouts/preview', request,
      { withCredentials: true });
  }
}
