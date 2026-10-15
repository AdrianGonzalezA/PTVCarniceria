import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface CentralBusinessBranch {
  installationId: string;
  label: string;
  status: 'ok' | 'unavailable';
  generatedAtUtc: string | null;
  saleCount: number | null;
  salesTotal: number | null;
  salePaymentsTotal: number | null;
  openShiftCount: number | null;
  openShiftCashBalance: number | null;
  paymentsByMethod: { method: string; amount: number }[] | null;
}

export interface CentralBusinessSummary {
  window: { fromUtc: string; toUtc: string };
  retrievedAtUtc: string;
  completeness: 'complete' | 'incomplete' | 'empty';
  aggregate: { currency: 'ARS'; saleCount: number; salesTotal: number;
    salePaymentsTotal: number } | null;
  branches: CentralBusinessBranch[];
}

@Injectable({ providedIn: 'root' })
export class CentralBusinessClient {
  private readonly http = inject(HttpClient);

  today(): Observable<CentralBusinessSummary> {
    return this.http.get<CentralBusinessSummary>('/api/v1/business/summary',
      { credentials: 'same-origin' });
  }

  dates(fromDate: string, toDate: string): Observable<CentralBusinessSummary> {
    const params = new HttpParams().set('fromDate', fromDate).set('toDate', toDate);
    return this.http.get<CentralBusinessSummary>('/api/v1/business/summary',
      { params, credentials: 'same-origin' });
  }
}
