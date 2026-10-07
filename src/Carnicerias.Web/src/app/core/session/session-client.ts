import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export interface CurrentSession {
  readonly userId: string;
  readonly username: string;
  readonly expiresAtUtc: string;
  readonly context: OperationalContext | null;
}

export interface OperationalContext {
  readonly userId: string;
  readonly companyId: string;
  readonly companyName: string;
  readonly branchId: string;
  readonly branchName: string;
  readonly permissions: readonly string[];
  readonly sessionId: string;
}

export interface OperationalCompanyOption {
  readonly companyId: string;
  readonly companyName: string;
  readonly branches: readonly OperationalBranchOption[];
}

export interface OperationalBranchOption {
  readonly branchId: string;
  readonly branchName: string;
}

@Injectable({ providedIn: 'root' })
export class SessionClient {
  private readonly http = inject(HttpClient);

  login(credential: string, password: string) {
    return this.http.post<CurrentSession>(
      '/api/sessions',
      { credential, password },
      { withCredentials: true },
    );
  }

  current() {
    return this.http.get<CurrentSession>('/api/sessions/current', { withCredentials: true });
  }

  operationalContexts() {
    return this.http.get<readonly OperationalCompanyOption[]>('/api/operational-contexts', {
      withCredentials: true,
    });
  }

  selectOperationalContext(companyId: string, branchId: string) {
    return this.http.put<CurrentSession>(
      '/api/sessions/current/context',
      { companyId, branchId },
      { withCredentials: true },
    );
  }

  logout() {
    return this.http.delete<void>('/api/sessions/current', { withCredentials: true });
  }
}
