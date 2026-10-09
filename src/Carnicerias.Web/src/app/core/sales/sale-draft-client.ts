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
  readonly ticketSlot: SaleTicketSlot;
  readonly priceListId: string;
  readonly updatedAtUtc: string;
  readonly lines: readonly SaleDraftLine[];
}

export type SaleTicketSlot = 'A' | 'B' | 'C' | 'D';

export type SalePaymentMethod = 'cash' | 'debit' | 'credit' | 'transfer' | 'mercadoPago' | 'cheque';

export interface ConfirmedSale {
  readonly id: string;
  readonly total: number;
  readonly changeAmount: number;
  readonly confirmedAtUtc: string;
  readonly customerId: string | null;
  readonly customerCode: string | null;
  readonly customerName: string | null;
  readonly accountChargeAmount: number;
  readonly creditAppliedAmount: number;
  readonly lines: readonly { readonly code: string; readonly name: string; readonly unit: string; readonly quantity: number; readonly unitPrice: number; readonly lineTotal: number; readonly pieceIdentifier?: string | null }[];
  readonly payments: readonly { readonly method: SalePaymentMethod; readonly tenderedAmount: number; readonly appliedAmount: number }[];
}

@Injectable({ providedIn: 'root' })
export class SaleDraftClient {
  private readonly http = inject(HttpClient);

  list() {
    return this.http.get<readonly SaleDraft[]>('/api/sales/drafts', { withCredentials: true });
  }

  current(slot: SaleTicketSlot = 'A') {
    return this.http.get<SaleDraft | null>(this.draftUrl(slot), { withCredentials: true });
  }

  save(priceListId: string, lines: readonly { readonly productId: string; readonly quantity: number; readonly inventoryPieceId?: string }[],
    slot: SaleTicketSlot = 'A') {
    return this.http.put<SaleDraft>(this.draftUrl(slot), { priceListId, lines }, { withCredentials: true });
  }

  cancel(slot: SaleTicketSlot = 'A') {
    return this.http.delete<void>(this.draftUrl(slot), { withCredentials: true });
  }

  confirm(draftId: string, payments: readonly { readonly method: SalePaymentMethod; readonly amount: number }[],
    account?: { readonly customerId: string; readonly amount: number; readonly creditAppliedAmount: number }) {
    return this.http.post<ConfirmedSale>(`/api/sales/drafts/${draftId}/confirmation`, {
      payments,
      ...(account ? { customerId: account.customerId, accountChargeAmount: account.amount,
        creditAppliedAmount: account.creditAppliedAmount, accountChargeConfirmed: account.amount > 0 } : {}),
    }, { withCredentials: true });
  }

  private draftUrl(slot: SaleTicketSlot): string {
    return slot === 'A' ? '/api/sales/draft' : `/api/sales/drafts/${slot}`;
  }
}
