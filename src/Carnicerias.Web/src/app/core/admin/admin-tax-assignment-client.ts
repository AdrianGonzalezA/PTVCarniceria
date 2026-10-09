import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export interface OtherTaxAssignment {
  readonly id: string;
  readonly productId: string;
  readonly taxCatalogEntryId: string;
  readonly taxName: string;
  readonly ratePercent: number;
  readonly effectiveFromUtc: string;
  readonly effectiveToUtc: string | null;
  readonly assignedByUserId: string;
  readonly removedByUserId: string | null;
}

export interface AssignmentResult {
  readonly affectedCount: number;
  readonly changedCount: number;
}

@Injectable({ providedIn: 'root' })
export class AdminTaxAssignmentClient {
  private readonly http = inject(HttpClient);

  count() {
    return this.http.get<{ readonly total: number }>('/api/admin/tax-assignments/count',
      { withCredentials: true });
  }

  history(productId: string) {
    return this.http.get<readonly OtherTaxAssignment[]>(
      `/api/admin/tax-assignments/${encodeURIComponent(productId)}`,
      { withCredentials: true });
  }

  setSelected(taxCatalogEntryId: string, isAssigned: boolean, productIds: readonly string[]) {
    return this.http.put<AssignmentResult>('/api/admin/tax-assignments',
      { taxCatalogEntryId, isAssigned, scope: 'selected', productIds },
      { withCredentials: true });
  }

  setAll(taxCatalogEntryId: string, isAssigned: boolean, expectedProductCount: number) {
    return this.http.put<AssignmentResult>('/api/admin/tax-assignments',
      { taxCatalogEntryId, isAssigned, scope: 'all', expectedProductCount },
      { withCredentials: true });
  }
}
