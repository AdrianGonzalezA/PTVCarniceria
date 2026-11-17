import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export interface ScaleOverrideAuthorizer {
  readonly userId: string;
  readonly username: string;
}

export interface ScaleOverrideGrant {
  readonly grantId: string;
  readonly authorizerName: string;
  readonly expiresAtUtc: string;
}

@Injectable({ providedIn: 'root' })
export class ScaleManualOverrideClient {
  private readonly http = inject(HttpClient);

  authorizers() {
    return this.http.get<readonly ScaleOverrideAuthorizer[]>(
      '/api/sales/scale-overrides/authorizers', { withCredentials: true });
  }

  authorize(productId: string, authorizerUserId: string, pin: string, reason: string) {
    return this.http.post<ScaleOverrideGrant>('/api/sales/scale-overrides',
      { productId, authorizerUserId, pin, reason }, { withCredentials: true });
  }
}
