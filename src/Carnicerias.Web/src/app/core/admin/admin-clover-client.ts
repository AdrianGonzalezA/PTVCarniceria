import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export interface CloverSettings {
  readonly provider: 'clover';
  readonly environment: 'sandbox';
  readonly clientId: string;
  readonly merchantId: string;
  readonly hasClientSecret: boolean;
  readonly isConnected: boolean;
  readonly accessTokenExpiresAtUtc: string | null;
  readonly refreshTokenExpiresAtUtc: string | null;
}

export interface CloverAuthorization {
  readonly authorizeUrl: string;
  readonly expiresAtUtc: string;
}

@Injectable({ providedIn: 'root' })
export class AdminCloverClient {
  private readonly http = inject(HttpClient);
  private readonly endpoint = '/api/admin/payment-gateways/clover';

  get() {
    return this.http.get<CloverSettings>(this.endpoint, { withCredentials: true });
  }

  save(clientId: string, merchantId: string, clientSecret: string) {
    return this.http.put<Pick<CloverSettings, 'clientId' | 'merchantId' | 'hasClientSecret' | 'isConnected'>>(
      this.endpoint, { clientId, merchantId, clientSecret: clientSecret || null },
      { withCredentials: true });
  }

  authorize() {
    return this.http.post<CloverAuthorization>(`${this.endpoint}/authorization`, null,
      { withCredentials: true });
  }
}
