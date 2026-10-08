import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export interface AdminCompany {
  readonly id: string;
  readonly name: string;
  readonly isActive: boolean;
  readonly activeBranchCount: number;
}

export interface AdminCompanyCreated {
  readonly id: string;
  readonly name: string;
  readonly initialBranchId: string;
  readonly initialBranchName: string;
}

export interface AdminBranch {
  readonly id: string;
  readonly name: string;
  readonly isActive: boolean;
  readonly activeTerminalCount: number;
}

@Injectable({ providedIn: 'root' })
export class AdminOrganizationClient {
  private readonly http = inject(HttpClient);
  private readonly endpoint = '/api/admin/companies';

  companies() {
    return this.http.get<readonly AdminCompany[]>(this.endpoint, { withCredentials: true });
  }

  createCompany(name: string, initialBranchName: string) {
    return this.http.post<AdminCompanyCreated>(this.endpoint,
      { name, initialBranchName }, { withCredentials: true });
  }

  updateCompany(id: string, changes: { readonly name?: string; readonly isActive?: boolean }) {
    return this.http.patch<AdminCompany>(`${this.endpoint}/${id}`, changes, { withCredentials: true });
  }

  branches(companyId: string) {
    return this.http.get<readonly AdminBranch[]>(`${this.endpoint}/${companyId}/branches`,
      { withCredentials: true });
  }

  createBranch(companyId: string, name: string) {
    return this.http.post<AdminBranch>(`${this.endpoint}/${companyId}/branches`,
      { name }, { withCredentials: true });
  }

  updateBranch(companyId: string, branchId: string,
    changes: { readonly name?: string; readonly isActive?: boolean }) {
    return this.http.patch<AdminBranch>(`${this.endpoint}/${companyId}/branches/${branchId}`,
      changes, { withCredentials: true });
  }
}
