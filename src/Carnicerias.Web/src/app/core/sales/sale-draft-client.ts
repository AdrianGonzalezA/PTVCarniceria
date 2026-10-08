import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export interface SaleDraftLine {
  readonly id: string;
  readonly productId: string;
  readonly productCode: string;
  readonly productName: string;
  readonly unit: string;
  readonly saleMode: 'weight' | 'unit';
  readonly quantity: number;
  readonly unitPrice: number;
  readonly inventoryPieceId?: string | null;
  readonly pieceIdentifier?: string | null;
}

export interface SaleDraft {
  readonly id: string;
  readonly priceListId: string;
  readonly updatedAtUtc: string;
  readonly lines: readonly SaleDraftLine[];
}

export type SalePaymentMethod = 'cash' | 'debit' | 'credit' | 'transfer' | 'mercadoPago' | 'cheque';

export interface ConfirmedSale {
  readonly id: string;
  readonly total: number;
  readonly changeAmount: number;
  readonly confirmedAtUtc: string;
  readonly lines: readonly { readonly code: string; readonly name: string; readonly unit: string; readonly quantity: number; readonly unitPrice: number; readonly lineTotal: number; readonly pieceIdentifier?: string | null }[];
  readonly payments: readonly { readonly method: SalePaymentMethod; readonly tenderedAmount: number; readonly appliedAmount: number }[];
}

@Injectable({ providedIn: 'root' })
export class SaleDraftClient {
  private readonly http = inject(HttpClient);

  current() {
    return this.http.get<SaleDraft | null>('/api/sales/draft', { withCredentials: true });
  }

  save(priceListId: string, lines: readonly { readonly productId: string; readonly quantity: number; readonly inventoryPieceId?: string }[]) {
    return this.http.put<SaleDraft>('/api/sales/draft', { priceListId, lines }, { withCredentials: true });
  }

  cancel() {
    return this.http.delete<void>('/api/sales/draft', { withCredentials: true });
  }

  confirm(draftId: string, payments: readonly { readonly method: SalePaymentMethod; readonly amount: number }[]) {
    return this.http.post<ConfirmedSale>(`/api/sales/drafts/${draftId}/confirmation`, { payments }, { withCredentials: true });
  }
}
