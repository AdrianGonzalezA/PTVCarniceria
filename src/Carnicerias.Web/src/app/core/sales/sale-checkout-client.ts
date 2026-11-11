import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { ConfirmedSale, SalePaymentMethod } from './sale-draft-client';

export type CheckoutStatus = 'Prepared' | 'Committed' | 'PendingBalance' | 'Ready' | 'Completed' | 'Canceled';
export type CheckoutInstrumentStatus = 'Prepared' | 'InProcess' | 'Approved' | 'Rejected' | 'Uncertain';

export interface PaymentInstrumentDetails {
  readonly cardBrand?: string;
  readonly processor?: string;
  readonly terminal?: string;
  readonly batch?: string;
  readonly voucher?: string;
  readonly authorization?: string;
  readonly entity?: string;
  readonly date?: string;
  readonly reference?: string;
  readonly bank?: string;
  readonly branch?: string;
  readonly number?: string;
  readonly holder?: string;
  readonly holderCuit?: string;
  readonly issuedOn?: string;
  readonly payableOn?: string;
}

export interface CheckoutTerms {
  readonly customerId: string | null;
  readonly accountChargeAmount: number;
  readonly creditAppliedAmount: number;
  readonly accountChargeConfirmed: boolean;
  readonly accountChargeReference?: string | null;
  readonly documentType: string;
  readonly recipientTaxStatus: string;
  readonly recipientName?: string | null;
  readonly recipientDocumentNumber?: string | null;
  readonly recipientAddress?: string | null;
  readonly recipientLocality?: string | null;
  readonly recipientProvince?: string | null;
  readonly fiscalCustomerId?: string | null;
  readonly purchaseOrderNumber?: string | null;
}

export interface CheckoutInstrument {
  readonly id: string;
  readonly method: SalePaymentMethod;
  readonly amount: number;
  readonly status: CheckoutInstrumentStatus;
  readonly reference: string | null;
  readonly details?: PaymentInstrumentDetails | null;
  readonly pointPaymentIntentId: string | null;
  readonly approvedAtUtc: string | null;
}

export interface SaleCheckout {
  readonly id: string;
  readonly saleDraftId: string;
  readonly status: CheckoutStatus;
  readonly revision: number;
  readonly total: number;
  readonly planHash: string;
  readonly commitKey: string | null;
  readonly approvedAmount: number;
  readonly remainingAmount: number;
  readonly confirmedSaleId: string | null;
  readonly terms: CheckoutTerms;
  readonly instruments: readonly CheckoutInstrument[];
}

@Injectable({ providedIn: 'root' })
export class SaleCheckoutClient {
  private readonly http = inject(HttpClient);

  current(draftId: string) {
    return this.http.get<SaleCheckout | null>(this.url(draftId), { withCredentials: true });
  }

  prepare(draftId: string, expectedRevision: number, terms: CheckoutTerms,
    payments: readonly { readonly id: string; readonly method: SalePaymentMethod;
      readonly amount: number; readonly reference?: string | null;
      readonly details?: PaymentInstrumentDetails | null }[]) {
    return this.http.put<SaleCheckout>(this.url(draftId),
      { expectedRevision, terms, payments }, { withCredentials: true });
  }

  commit(draftId: string, checkoutId: string, expectedRevision: number, key: string) {
    return this.http.post<SaleCheckout>(`${this.url(draftId)}/${checkoutId}/commit`,
      { expectedRevision, key }, { withCredentials: true });
  }

  approve(draftId: string, checkoutId: string, instrumentId: string, reference?: string | null) {
    return this.http.post<SaleCheckout>(
      `${this.url(draftId)}/${checkoutId}/instruments/${instrumentId}/approve`,
      { reference }, { withCredentials: true });
  }

  reject(draftId: string, checkoutId: string, instrumentId: string, reason: string) {
    return this.http.post<SaleCheckout>(
      `${this.url(draftId)}/${checkoutId}/instruments/${instrumentId}/reject`,
      { reference: reason }, { withCredentials: true });
  }

  addReplacement(draftId: string, checkoutId: string,
    payment: { readonly id: string; readonly method: SalePaymentMethod; readonly amount: number;
      readonly reference?: string | null; readonly details?: PaymentInstrumentDetails | null }) {
    return this.http.post<SaleCheckout>(`${this.url(draftId)}/${checkoutId}/instruments`, payment,
      { withCredentials: true });
  }

  confirm(draftId: string, checkoutId: string) {
    return this.http.post<ConfirmedSale>(`${this.url(draftId)}/${checkoutId}/confirmation`, {},
      { withCredentials: true });
  }

  private url(draftId: string): string {
    return `/api/sales/drafts/${draftId}/checkout`;
  }
}
