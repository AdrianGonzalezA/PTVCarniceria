import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export interface AdminProductCode {
  readonly code: string;
  readonly isActive: boolean;
}

@Injectable({ providedIn: 'root' })
export class AdminProductCodeClient {
  private readonly http = inject(HttpClient);

  private endpoint(productId: string): string {
    return `/api/admin/products/${productId}/codes`;
  }

  list(productId: string) {
    return this.http.get<readonly AdminProductCode[]>(this.endpoint(productId), { withCredentials: true });
  }

  create(productId: string, code: string) {
    return this.http.post<AdminProductCode>(this.endpoint(productId), { code }, { withCredentials: true });
  }

  changeState(productId: string, code: string, isActive: boolean) {
    return this.http.patch<AdminProductCode>(this.endpoint(productId),
      { code, isActive }, { withCredentials: true });
  }
}
