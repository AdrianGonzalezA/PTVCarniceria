import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export interface BranchCentralConnectionStatus {
  readonly companyId: string;
  readonly companyName: string;
  readonly branchId: string;
  readonly branchName: string;
  readonly installationId: string | null;
  readonly destinationUrl: string | null;
  readonly isActive: boolean;
  readonly hasCredential: boolean;
  readonly hasPreviousCredential: boolean;
  readonly previousCredentialExpiresAtUtc: string | null;
  readonly revision: number;
}

export interface BranchCentralConnectionBundle {
  readonly version: 1;
  readonly label: string;
  readonly destinationUrl: string;
  readonly installationId: string;
  readonly remoteCompanyId: string;
  readonly remoteBranchId: string;
  readonly token: string;
}

export interface BranchCentralConnectionMutation {
  readonly isActive: boolean;
  readonly revision: number;
}

@Injectable({ providedIn: 'root' })
export class AdminCentralConnectionClient {
  private readonly http = inject(HttpClient);
  private readonly url = '/api/admin/central-connection';
  private readonly writeOptions = {
    withCredentials: true, headers: { 'X-Carnicerias-CSRF': '1' },
  };

  get() {
    return this.http.get<BranchCentralConnectionStatus>(this.url, { withCredentials: true });
  }

  saveUrl(destinationUrl: string, revision: number) {
    return this.http.put<Pick<BranchCentralConnectionStatus,
      'installationId' | 'destinationUrl' | 'isActive' | 'revision'>>(
      this.url, { destinationUrl, revision }, this.writeOptions);
  }

  issueCredential() {
    return this.http.post<BranchCentralConnectionBundle>(
      `${this.url}/credentials`, null, this.writeOptions);
  }

  completeRotation() {
    return this.http.post<BranchCentralConnectionMutation>(`${this.url}/credentials/complete-rotation`,
      null, this.writeOptions);
  }

  deactivate() {
    return this.http.post<BranchCentralConnectionMutation>(
      `${this.url}/deactivation`, null, this.writeOptions);
  }
}
