import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export interface AdminProductCode {
  readonly code: string;
  readonly isActive: boolean;
  readonly kind?: 'saleAlternative' | 'purchasePackage';
  readonly unitsPerPackage?: number | null;
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

  create(productId: string, code: string, kind: 'saleAlternative' | 'purchasePackage' = 'saleAlternative',
    unitsPerPackage?: number) {
    const body = kind === 'purchasePackage' ? { code, kind, unitsPerPackage } : { code };
    return this.http.post<AdminProductCode>(this.endpoint(productId), body, { withCredentials: true });
  }

  changeState(productId: string, code: string, isActive: boolean) {
    return this.http.patch<AdminProductCode>(this.endpoint(productId),
      { code, isActive }, { withCredentials: true });
  }

  changeFactor(productId: string, code: string, unitsPerPackage: number) {
    return this.http.patch<AdminProductCode>(this.endpoint(productId),
      { code, unitsPerPackage }, { withCredentials: true });
  }
}
