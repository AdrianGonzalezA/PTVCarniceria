import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export interface ArcaCertificateStatus {
  readonly subject: string;
  readonly thumbprint: string;
  readonly validFromUtc: string;
  readonly expiresAtUtc: string;
  readonly isExpired: boolean;
  readonly isAccessible: boolean;
}

export interface ArcaSettings {
  readonly companyId: string;
  readonly source: 'none' | 'environment' | 'database';
  readonly issuerCuit: string;
  readonly pointOfSale: number;
  readonly issuerName: string;
  readonly issuerAddress: string;
  readonly issuerIibb: string | null;
  readonly issuerActivityStartDate: string | null;
  readonly certificate: ArcaCertificateStatus | null;
}

export interface ArcaMetadata {
  readonly issuerCuit: string;
  readonly pointOfSale: number;
  readonly issuerName: string;
  readonly issuerAddress: string;
  readonly issuerIibb: string | null;
  readonly issuerActivityStartDate: string | null;
}

@Injectable({ providedIn: 'root' })
export class AdminArcaClient {
  private readonly http = inject(HttpClient);
  private readonly endpoint = '/api/admin/arca-settings';

  get() {
    return this.http.get<ArcaSettings>(this.endpoint, { withCredentials: true });
  }

  save(metadata: ArcaMetadata) {
    return this.http.put<ArcaSettings>(this.endpoint, metadata, { withCredentials: true });
  }

  upload(contentBase64: string, password: string | null) {
    return this.http.post<ArcaSettings>(`${this.endpoint}/certificate`,
      { contentBase64, password }, { withCredentials: true });
  }
}
