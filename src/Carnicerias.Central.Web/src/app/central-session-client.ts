import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface CentralSession {
  userId: string;
  centralCompanyId: string;
  username: string;
  expiresAtUtc: string;
}

@Injectable({ providedIn: 'root' })
export class CentralSessionClient {
  private readonly http = inject(HttpClient);

  login(companyCode: string, credential: string, password: string): Observable<CentralSession> {
    // Same-origin credentials: https://angular.dev/guide/http/making-requests#credentials-handling
    return this.http.post<CentralSession>('/api/v1/sessions',
      { companyCode, credential, password },
      { credentials: 'same-origin', headers: { 'X-Carnicerias-CSRF': '1' } });
  }

  current(): Observable<CentralSession> {
    return this.http.get<CentralSession>('/api/v1/sessions/current',
      { credentials: 'same-origin' });
  }

  logout(): Observable<void> {
    return this.http.delete<void>('/api/v1/sessions/current',
      { credentials: 'same-origin', headers: { 'X-Carnicerias-CSRF': '1' } });
  }
}
