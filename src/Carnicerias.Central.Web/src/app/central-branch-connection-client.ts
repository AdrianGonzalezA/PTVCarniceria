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
}

export type CreateCentralBranchConnection = Pick<CentralBranchConnection,
  'installationId' | 'remoteCompanyId' | 'remoteBranchId' | 'label' | 'destinationUrl'> & {
  readonly token: string;
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
}
