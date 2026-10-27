import '@angular/compiler';
import { TestBed } from '@angular/core/testing';
import { of, Subject } from 'rxjs';
import { vi } from 'vitest';
import { CreditCustomerClient } from '../../core/customers/credit-customer-client';
import { IntegratedGatewayClient } from '../../core/sales/integrated-gateway-client';
import { MercadoPagoClient, MercadoPagoIntent } from '../../core/sales/mercado-pago-client';
import { AccountCollectionDialog } from './account-collection-dialog';

describe('AccountCollectionDialog', () => {
  afterEach(() => localStorage.clear());

  function createDialog(withMercadoPago = false, recoverOrder = false, delayedSearch = false) {
    if (recoverOrder) localStorage.setItem('carnicerias:collection:test-cashier:mercado-pago',
      JSON.stringify({ operationId: 'operation-id', customer: { id: 'customer-id', code: 'C1', name: 'Cliente' },
        amount: '100', page: 1, payments: [{ method: 'cash', amount: 40 },
          { method: 'mercadoPago', amount: 60 }], manual: false, allocations: {} }));
    const receipt = { id: 'receipt-id', receiptNumber: 1, customerId: 'customer-id',
      amount: 100, creditAmount: 0, method: 'mixed', changeAmount: 0,
      payments: [{ method: 'cash', tenderedAmount: 60, appliedAmount: 60 },
        { method: 'debit', tenderedAmount: 40, appliedAmount: 40 }],
      createdAtUtc: '2026-10-27T00:00:00Z', allocations: [] };
    const searchResults = new Subject<readonly { id: string; code: string; name: string }[]>();
    const creditClient = { searchAccounts: vi.fn(() => delayedSearch ? searchResults.asObservable() :
      of([{ id: 'customer-id', code: 'C1', name: 'Cliente' }])),
      account: vi.fn(() => of({ customerId: 'customer-id', customerCode: 'C1', customerName: 'Cliente',
        isActive: true, creditEnabled: true, totalDebt: 100, creditAvailable: 0,
        saleCount: 0, page: 1, sales: [] })),
      collect: vi.fn(() => of(receipt)), findCollection: vi.fn(() => of(null)) };
    const pendingIntent: MercadoPagoIntent = { id: 'intent-id', mode: 'qr', amount: 60,
      status: 'Pending', providerOrderId: 'order-id', qrData: '000201TEST', approved: false,
      createdAtUtc: '2026-10-27T00:00:00Z' };
    const mercadoPagoClient = { getCollection: vi.fn(() => of(recoverOrder
      ? { ...pendingIntent, status: 'Approved' as const, approved: true } : null)),
      startCollection: vi.fn(() => of(pendingIntent)),
      checkCollection: vi.fn(() => of({ ...pendingIntent, status: 'Approved' as const, approved: true })),
      cancelCollection: vi.fn(() => of({ ...pendingIntent, status: 'Canceled' as const })) };
    TestBed.configureTestingModule({ providers: [
      { provide: CreditCustomerClient, useValue: creditClient },
      { provide: MercadoPagoClient, useValue: mercadoPagoClient },
      { provide: IntegratedGatewayClient, useValue: { available: () => of({ gateways:
        withMercadoPago ? [{ provider: 'mercadoPago', modes: ['qr', 'qrStatic', 'point'] }] : [] }) } },
    ] });
    const fixture = TestBed.createComponent(AccountCollectionDialog);
    fixture.componentRef.setInput('scopeKey', 'test-cashier');
    fixture.detectChanges();
    const customer = fixture.nativeElement.querySelector('#collection-customer') as HTMLSelectElement;
    customer.value = 'customer-id';
    customer.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();
    const amount = fixture.nativeElement.querySelector('#collection-amount') as HTMLInputElement;
    amount.value = '100';
    amount.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    return { fixture, creditClient, mercadoPagoClient, searchResults };
  }

  function setPayment(fixture: ReturnType<typeof createDialog>['fixture'],
    method: string, amount: string) {
    const checkbox = fixture.nativeElement.querySelector(
      `.collection-payment-methods input[type=checkbox][value=${method}]`) as HTMLInputElement;
    if (!checkbox.checked) checkbox.click();
    fixture.detectChanges();
    const input = fixture.nativeElement.querySelector(
      `.collection-payment-methods input[data-method=${method}]`) as HTMLInputElement;
    input.value = amount;
    input.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
  }

  it('sends two selected methods and their amounts in one collection', () => {
    const { fixture, creditClient } = createDialog();
    setPayment(fixture, 'cash', '60');
    setPayment(fixture, 'debit', '40');

    (fixture.nativeElement.querySelector('.account-collection-dialog .finish-button') as HTMLButtonElement).click();

    expect(creditClient.collect).toHaveBeenCalledWith('customer-id', expect.objectContaining({
      amount: 100, payments: [{ method: 'cash', amount: 60 }, { method: 'debit', amount: 40 }],
    }));
  });

  it('requires an accredited Mercado Pago order for this collection', () => {
    const { fixture, creditClient, mercadoPagoClient } = createDialog(true);
    setPayment(fixture, 'cash', '40');
    setPayment(fixture, 'mercadoPago', '60');
    expect((fixture.nativeElement.querySelector('.account-collection-dialog .finish-button') as HTMLButtonElement).disabled)
      .toBe(true);

    (fixture.nativeElement.querySelector('.account-collection-dialog .mercado-pago-actions button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(mercadoPagoClient.startCollection).toHaveBeenCalledWith(expect.any(String), 'customer-id', 'qr', 60);
    expect((fixture.nativeElement.querySelector('.account-collection-dialog .finish-button') as HTMLButtonElement).disabled)
      .toBe(true);
    (fixture.nativeElement.querySelectorAll('.account-collection-dialog .mercado-pago-actions button')[1] as HTMLButtonElement).click();
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('.account-collection-dialog .finish-button') as HTMLButtonElement).click();
    expect(creditClient.collect).toHaveBeenCalledWith('customer-id', expect.objectContaining({
      payments: [{ method: 'cash', amount: 40 }, { method: 'mercadoPago', amount: 60 }],
      mercadoPagoIntentId: 'intent-id',
    }));
  });

  it('does not offer Mercado Pago without a configured gateway on this register', () => {
    const { fixture } = createDialog();
    expect(fixture.nativeElement.querySelector('.collection-payment-methods')?.textContent)
      .not.toContain('Mercado Pago');
  });

  it('recovers an accredited order after restarting Electron and reuses the operation', () => {
    const { fixture, creditClient, mercadoPagoClient, searchResults } = createDialog(true, true, true);
    searchResults.next([{ id: 'customer-id', code: 'C1', name: 'Cliente' },
      { id: 'another-id', code: 'C2', name: 'Otro' }]);
    fixture.detectChanges();
    expect(mercadoPagoClient.getCollection).toHaveBeenCalledWith('operation-id');
    expect((fixture.nativeElement.querySelector('#collection-customer') as HTMLSelectElement).value)
      .toBe('customer-id');
    expect((fixture.nativeElement.querySelector('.account-collection-dialog .finish-button') as HTMLButtonElement).disabled)
      .toBe(false);

    (fixture.nativeElement.querySelector('.account-collection-dialog .finish-button') as HTMLButtonElement).click();
    expect(creditClient.collect).toHaveBeenCalledWith('customer-id', expect.objectContaining({
      operationId: 'operation-id', mercadoPagoIntentId: 'intent-id',
    }));
  });
});
