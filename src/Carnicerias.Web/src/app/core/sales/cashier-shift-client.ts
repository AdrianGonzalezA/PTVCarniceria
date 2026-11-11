import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import type { SalePaymentMethod } from './sale-draft-client';

export interface ShiftClosingReport {
  readonly version: number;
  readonly shiftId: string;
  readonly companyId: string;
  readonly branchId: string;
  readonly terminalId: string | null;
  readonly cashierId: string;
  readonly companyName: string;
  readonly branchName: string;
  readonly terminalName: string | null;
  readonly cashierName: string;
  readonly openedAtUtc: string;
  readonly closedAtUtc: string;
  readonly openingCash: number;
  readonly cashSales: number;
  readonly nonCashSales: number;
  readonly accountSales: number;
  readonly salesTotal: number;
  readonly cashCollections: number;
  readonly nonCashCollections: number;
  readonly creditApplied: number;
  readonly cashWithdrawals: number;
  readonly expectedCash: number;
  readonly countedCash: number | null;
  readonly handedCash: number | null;
  readonly cashDifference: number | null;
  readonly isComplete: boolean;
  readonly pendingApprovedCash?: number;
  readonly pendingApprovedNonCash?: number;
  readonly paymentBreakdown: readonly { readonly method: string; readonly amount: number }[];
}

export interface CashierShift {
  readonly id: string;
  readonly openingCash: number;
  readonly cashSales: number;
  readonly nonCashSales: number;
  readonly accountSales: number;
  readonly cashCollections: number;
  readonly nonCashCollections: number;
  readonly creditApplied: number;
  readonly salesTotal: number;
  readonly cashBalance: number;
  readonly pendingApprovedCash?: number;
  readonly pendingApprovedNonCash?: number;
  readonly cashWithdrawals?: number;
  readonly openedAtUtc: string;
  readonly closedAtUtc: string | null;
  readonly cashierName?: string | null;
  readonly paymentBreakdown?: readonly { readonly method: SalePaymentMethod; readonly amount: number }[];
  readonly report?: ShiftClosingReport | null;
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

  close(countedCash: number, handedCash: number) {
    return this.http.post<CashierShift>('/api/cashier-shifts/current/close',
      { countedCash, handedCash }, { withCredentials: true });
  }
}
