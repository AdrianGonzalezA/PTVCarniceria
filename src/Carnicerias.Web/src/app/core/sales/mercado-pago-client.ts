import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export type MercadoPagoMode = 'qr' | 'point';

export interface MercadoPagoIntent {
  readonly id: string;
  readonly mode: MercadoPagoMode;
  readonly amount: number;
  readonly status: 'Prepared' | 'Pending' | 'Approved' | 'Rejected' | 'Expired' |
    'Canceled' | 'NeedsReconciliation' | 'Refunded';
  readonly providerOrderId: string | null;
  readonly qrData: string | null;
  readonly approved: boolean;
  readonly createdAtUtc: string;
}

@Injectable({ providedIn: 'root' })
export class MercadoPagoClient {
  private readonly http = inject(HttpClient);

  get(draftId: string) {
    return this.http.get<MercadoPagoIntent | null>(this.url(draftId), { withCredentials: true });
  }

  start(draftId: string, mode: MercadoPagoMode, amount: number) {
    return this.http.post<MercadoPagoIntent>(this.url(draftId), { mode, amount },
      { withCredentials: true });
  }

  check(draftId: string, intentId: string) {
    return this.http.post<MercadoPagoIntent>(`${this.url(draftId)}/${intentId}/check`, {},
      { withCredentials: true });
  }

  cancel(draftId: string, intentId: string) {
    return this.http.post<MercadoPagoIntent>(`${this.url(draftId)}/${intentId}/cancel`, {},
      { withCredentials: true });
  }

  private url(draftId: string): string {
    return `/api/sales/drafts/${draftId}/mercado-pago`;
  }
}
