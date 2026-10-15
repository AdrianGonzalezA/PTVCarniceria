import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface CentralManufacturerSession {
  readonly userId: string;
  readonly username: string;
  readonly expiresAtUtc: string;
}

export interface CentralManagedCompany {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly isActive: boolean;
  readonly administratorCount: number;
  readonly installationCount: number;
}

export interface CentralManagedAdministrator {
  readonly id: string;
  readonly username: string;
  readonly isActive: boolean;
}

export interface CentralPage<T> {
  readonly data: readonly T[];
  readonly pagination: {
    readonly page: number;
    readonly pageSize: number;
    readonly totalCount: number;
    readonly totalPages: number;
  };
}

export interface CreateCentralCompany {
  readonly code: string;
  readonly name: string;
  readonly initialAdministratorUsername: string;
  readonly initialAdministratorPassword: string;
}

export interface CreatedCentralCompany {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly initialAdministratorId: string;
}

@Injectable({ providedIn: 'root' })
export class CentralManufacturerClient {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/v1/superadmin';
  private readonly mutationOptions = {
    credentials: 'same-origin' as const,
    headers: { 'X-Carnicerias-CSRF': '1' },
  };

  login(credential: string, password: string): Observable<CentralManufacturerSession> {
    return this.http.post<CentralManufacturerSession>(`${this.baseUrl}/sessions`,
      { credential, password }, this.mutationOptions);
  }

  current(): Observable<CentralManufacturerSession> {
    return this.http.get<CentralManufacturerSession>(`${this.baseUrl}/sessions/current`,
      { credentials: 'same-origin' });
  }

  logout(): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/sessions/current`, this.mutationOptions);
  }

  companies(page: number): Observable<CentralPage<CentralManagedCompany>> {
    return this.http.get<CentralPage<CentralManagedCompany>>(
      `${this.baseUrl}/companies?page=${page}&pageSize=20`, { credentials: 'same-origin' });
  }

  createCompany(input: CreateCentralCompany): Observable<CreatedCentralCompany> {
    return this.http.post<CreatedCentralCompany>(`${this.baseUrl}/companies`,
      input, this.mutationOptions);
  }

  administrators(companyId: string, page: number): Observable<CentralPage<CentralManagedAdministrator>> {
    return this.http.get<CentralPage<CentralManagedAdministrator>>(
      `${this.baseUrl}/companies/${encodeURIComponent(companyId)}/administrators?page=${page}&pageSize=20`,
      { credentials: 'same-origin' });
  }

  addAdministrator(companyId: string, input: { readonly username: string; readonly password: string })
    : Observable<CentralManagedAdministrator> {
    return this.http.post<CentralManagedAdministrator>(
      `${this.baseUrl}/companies/${encodeURIComponent(companyId)}/administrators`,
      input, this.mutationOptions);
  }
}
