import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export interface AdminTerminal {
  readonly id: string;
  readonly name: string;
  readonly isActive: boolean;
  readonly isHistorical: boolean;
  readonly hasCredential: boolean;
}

export interface ProvisionedTerminal {
  readonly id: string;
  readonly name: string;
  readonly credential: string;
}

export interface AdminTerminalUpdate extends AdminTerminal {
  readonly newCredential: string | null;
}

@Injectable({ providedIn: 'root' })
export class AdminTerminalClient {
  private readonly http = inject(HttpClient);

  private endpoint(companyId: string, branchId: string): string {
    return `/api/admin/companies/${companyId}/branches/${branchId}/terminals`;
  }

  list(companyId: string, branchId: string) {
    return this.http.get<readonly AdminTerminal[]>(this.endpoint(companyId, branchId),
      { withCredentials: true });
  }

  create(companyId: string, branchId: string, name: string) {
    return this.http.post<ProvisionedTerminal>(this.endpoint(companyId, branchId),
      { name }, { withCredentials: true });
  }

  update(companyId: string, branchId: string, terminalId: string,
    changes: { readonly name?: string; readonly isActive?: boolean }) {
    return this.http.patch<AdminTerminalUpdate>(
      `${this.endpoint(companyId, branchId)}/${terminalId}`, changes, { withCredentials: true });
  }

  rotate(companyId: string, branchId: string, terminalId: string) {
    return this.http.post<ProvisionedTerminal>(
      `${this.endpoint(companyId, branchId)}/${terminalId}/rotate`, {}, { withCredentials: true });
  }
}
