import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export interface ManufacturerSession {
  readonly userId: string;
  readonly username: string;
  readonly expiresAtUtc: string;
}

export interface ManufacturerCompany {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly isActive: boolean;
  readonly activeBranchCount: number;
}

export interface NewManufacturerCompany {
  readonly name: string;
  readonly code: string;
  readonly initialBranchName: string;
  readonly adminUsername: string;
  readonly adminEmail: string;
  readonly adminPassword: string;
}

export interface CreatedManufacturerCompany {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly initialBranchId: string;
  readonly initialAdministratorId: string;
}

@Injectable({ providedIn: 'root' })
export class ManufacturerClient {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/manufacturer';

  login(credential: string, password: string) {
    return this.http.post<ManufacturerSession>(`${this.baseUrl}/sessions`,
      { credential, password }, { withCredentials: true });
  }

  current() {
    return this.http.get<ManufacturerSession>(`${this.baseUrl}/sessions/current`,
      { withCredentials: true });
  }

  logout() {
    return this.http.delete<void>(`${this.baseUrl}/sessions/current`, { withCredentials: true });
  }

  companies() {
    return this.http.get<readonly ManufacturerCompany[]>(`${this.baseUrl}/companies`,
      { withCredentials: true });
  }

  createCompany(input: NewManufacturerCompany) {
    return this.http.post<CreatedManufacturerCompany>(`${this.baseUrl}/companies`, input,
      { withCredentials: true });
  }

  updateCompany(companyId: string, changes: { readonly name?: string; readonly isActive?: boolean }) {
    return this.http.patch<ManufacturerCompany>(
      `${this.baseUrl}/companies/${encodeURIComponent(companyId)}`, changes,
      { withCredentials: true });
  }
}
