import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface CentralBranchConnection {
  readonly installationId: string;
  readonly remoteCompanyId: string;
  readonly remoteBranchId: string;
  readonly label: string;
  readonly destinationUrl: string;
  readonly isActive: boolean;
  readonly hasCredential: boolean;
  readonly lastCheckStatus: 'notChecked' | 'available' | 'unavailable';
  readonly lastCheckedAtUtc: string | null;
}

export type CreateCentralBranchConnection = Pick<CentralBranchConnection,
  'installationId' | 'remoteCompanyId' | 'remoteBranchId' | 'label' | 'destinationUrl'> & {
  readonly token: string;
};
export type EditCentralBranchConnection = {
  readonly label?: string;
  readonly destinationUrl?: string;
  readonly token?: string;
};

export interface CentralBranchConnectionPage {
  readonly data: readonly CentralBranchConnection[];
  readonly pagination: {
    readonly page: number;
    readonly pageSize: number;
    readonly totalCount: number;
    readonly totalPages: number;
  };
}

@Injectable({ providedIn: 'root' })
export class CentralBranchConnectionClient {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/v1/branch-connections';

  list(page: number): Observable<CentralBranchConnectionPage> {
    const params = new HttpParams().set('page', page).set('pageSize', 20);
    return this.http.get<CentralBranchConnectionPage>(this.baseUrl,
      { params, credentials: 'same-origin' });
  }

  create(input: CreateCentralBranchConnection): Observable<CentralBranchConnection> {
    return this.http.post<CentralBranchConnection>(this.baseUrl, input, {
      credentials: 'same-origin',
      headers: { 'X-Carnicerias-CSRF': '1' },
    });
  }

  check(installationId: string): Observable<CentralBranchConnection> {
    return this.http.post<CentralBranchConnection>(
      `${this.baseUrl}/${encodeURIComponent(installationId)}/checks`, null,
      { credentials: 'same-origin', headers: { 'X-Carnicerias-CSRF': '1' } });
  }

  edit(installationId: string, input: EditCentralBranchConnection): Observable<CentralBranchConnection> {
    return this.http.patch<CentralBranchConnection>(
      `${this.baseUrl}/${encodeURIComponent(installationId)}`, input,
      { credentials: 'same-origin', headers: { 'X-Carnicerias-CSRF': '1' } });
  }

  changeState(installationId: string, isActive: boolean): Observable<CentralBranchConnection> {
    return this.http.patch<CentralBranchConnection>(
      `${this.baseUrl}/${encodeURIComponent(installationId)}/state`, { isActive },
      { credentials: 'same-origin', headers: { 'X-Carnicerias-CSRF': '1' } });
  }
}
