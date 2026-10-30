import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export interface BranchDevice {
  readonly id: string;
  readonly name: string;
  readonly type: 'scale' | 'printer';
  readonly driver: 'serial-scale' | 'text' | 'hasar' | 'escpos';
  readonly port: string;
  readonly baudRate: number;
  readonly paperWidthMm: 58 | 80 | null;
  readonly isActive: boolean;
}

export type BranchDeviceChanges = Omit<BranchDevice, 'id'>;

@Injectable({ providedIn: 'root' })
export class AdminBranchDeviceClient {
  private readonly http = inject(HttpClient);

  private endpoint(companyId: string, branchId: string): string {
    return `/api/admin/companies/${companyId}/branches/${branchId}/devices`;
  }

  list(companyId: string, branchId: string) {
    return this.http.get<readonly BranchDevice[]>(this.endpoint(companyId, branchId),
      { withCredentials: true });
  }

  create(companyId: string, branchId: string, changes: BranchDeviceChanges) {
    return this.http.post<BranchDevice>(this.endpoint(companyId, branchId), changes,
      { withCredentials: true });
  }

  update(companyId: string, branchId: string, deviceId: string, changes: BranchDeviceChanges) {
    return this.http.put<BranchDevice>(`${this.endpoint(companyId, branchId)}/${deviceId}`,
      changes, { withCredentials: true });
  }
}
