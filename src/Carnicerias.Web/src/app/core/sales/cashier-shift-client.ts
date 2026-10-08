import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export interface CashierShift {
  readonly id: string;
  readonly openingCash: number;
  readonly cashSales: number;
  readonly nonCashSales: number;
  readonly accountSales: number;
  readonly salesTotal: number;
  readonly cashBalance: number;
  readonly openedAtUtc: string;
  readonly closedAtUtc: string | null;
}

@Injectable({ providedIn: 'root' })
export class CashierShiftClient {
  private readonly http = inject(HttpClient);

  current() {
    return this.http.get<CashierShift | null>('/api/cashier-shifts/current', { withCredentials: true });
  }

  lastClosed() {
    return this.http.get<CashierShift | null>('/api/cashier-shifts/last-closed', { withCredentials: true });
  }

  open(openingCash: number) {
    return this.http.post<CashierShift>('/api/cashier-shifts', { openingCash }, { withCredentials: true });
  }

  close() {
    return this.http.post<CashierShift>('/api/cashier-shifts/current/close', {}, { withCredentials: true });
  }
}
