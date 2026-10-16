import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface CentralInstallation {
  installationId: string;
  remoteCompanyId: string;
  remoteBranchId: string;
  label: string;
}

export interface CentralInstallationPage {
  data: CentralInstallation[];
  pagination: {
    page: number;
    pageSize: number;
    totalCount: number;
    totalPages: number;
  };
}

@Injectable({ providedIn: 'root' })
export class CentralInstallationClient {
  private readonly http = inject(HttpClient);

  list(page: number): Observable<CentralInstallationPage> {
    const params = new HttpParams().set('page', page).set('pageSize', 20);
    return this.http.get<CentralInstallationPage>('/api/v1/installations',
      { params, credentials: 'same-origin' });
  }
}
