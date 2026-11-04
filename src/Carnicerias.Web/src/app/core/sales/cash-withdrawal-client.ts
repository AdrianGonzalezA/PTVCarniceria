import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export type CashWithdrawalStatus = 'pending' | 'approved' | 'rejected';

export interface CashWithdrawal {
  readonly id: string;
  readonly operationId: string;
  readonly cashierShiftId: string;
  readonly method: 'cash';
  readonly amount: number;
  readonly reason: string;
  readonly status: CashWithdrawalStatus;
  readonly requestedByUserId: string;
  readonly requesterName: string | null;
  readonly requestedAtUtc: string;
  readonly decidedByUserId: string | null;
  readonly decisionByName: string | null;
  readonly decidedAtUtc: string | null;
  readonly decisionReason: string | null;
  readonly ledgerMovementId: string | null;
}

export interface CashWithdrawalPage {
  readonly items: readonly CashWithdrawal[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalItems: number;
}

@Injectable({ providedIn: 'root' })
export class CashWithdrawalClient {
  private readonly http = inject(HttpClient);

  current() {
    return this.http.get<readonly CashWithdrawal[]>('/api/cashier-shifts/current/withdrawals',
      { withCredentials: true });
  }

  request(operationId: string, amount: number, reason: string) {
    return this.http.post<CashWithdrawal>('/api/cashier-shifts/current/withdrawals',
      { operationId, method: 'cash', amount, reason }, { withCredentials: true });
  }

  adminList(status: CashWithdrawalStatus | '', page = 1) {
    const params = new HttpParams().set('page', page).set('pageSize', 30)
      .set('status', status);
    return this.http.get<CashWithdrawalPage>('/api/admin/cash-withdrawals',
      { params, withCredentials: true });
  }

  decide(id: string, action: 'approve' | 'reject', reason?: string) {
    return this.http.post<CashWithdrawal>(`/api/admin/cash-withdrawals/${id}/decision`,
      { action, reason }, { withCredentials: true });
  }
}
