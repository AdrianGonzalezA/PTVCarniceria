import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import type { SalePaymentMethod } from './sale-draft-client';
import type { PaymentInstrumentDetails } from './sale-checkout-client';

export interface PaymentDetailOption {
  readonly id: string;
  readonly name: string;
}

export interface PaymentCardBrandOption extends PaymentDetailOption {
  readonly supportsDebit: boolean;
  readonly supportsCredit: boolean;
}

export interface PaymentDetailCatalog {
  readonly cardBrands: readonly PaymentCardBrandOption[];
  readonly processors: readonly PaymentDetailOption[];
  readonly banks: readonly PaymentDetailOption[];
  readonly wallets: readonly PaymentDetailOption[];
}

export const emptyPaymentDetailCatalog: PaymentDetailCatalog = {
  cardBrands: [], processors: [], banks: [], wallets: [],
};

@Injectable({ providedIn: 'root' })
export class PaymentDetailCatalogClient {
  private readonly http = inject(HttpClient);

  get() {
    return this.http.get<PaymentDetailCatalog>('/api/catalog/payment-details', {
      withCredentials: true,
    });
  }
}

export function paymentDetailOptions(catalog: PaymentDetailCatalog, method: SalePaymentMethod,
  field: keyof PaymentInstrumentDetails): readonly PaymentDetailOption[] {
  if (field === 'cardBrand') return method === 'debit'
    ? catalog.cardBrands.filter(option => option.supportsDebit)
    : method === 'credit' ? catalog.cardBrands.filter(option => option.supportsCredit) : [];
  if (field === 'processor' && (method === 'debit' || method === 'credit')) return catalog.processors;
  if (field === 'entity' && method === 'transfer') return [...catalog.banks, ...catalog.wallets];
  if (field === 'bank' && method === 'cheque') return catalog.banks;
  return [];
}
