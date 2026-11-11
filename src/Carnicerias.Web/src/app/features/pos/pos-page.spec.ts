import '@angular/compiler';
import { HttpErrorResponse } from '@angular/common/http';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter, Router } from '@angular/router';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NEVER, of, Subject, throwError } from 'rxjs';
import { vi } from 'vitest';
import { CatalogClient } from '../../core/catalog/catalog-client';
import { PosTerminalClient } from '../../core/pos/pos-terminal-client';
import { PosDeviceClient } from '../../core/pos/pos-device-client';
import { InventoryClient } from '../../core/inventory/inventory-client';
import { InventoryPieceClient } from '../../core/inventory/inventory-piece-client';
import { CashierShiftClient } from '../../core/sales/cashier-shift-client';
import { CashWithdrawalClient } from '../../core/sales/cash-withdrawal-client';
import { SaleDraftClient } from '../../core/sales/sale-draft-client';
import { CheckoutTerms, PaymentInstrumentDetails, SaleCheckout, SaleCheckoutClient } from '../../core/sales/sale-checkout-client';
import { MercadoPagoClient } from '../../core/sales/mercado-pago-client';
import type { MercadoPagoIntent } from '../../core/sales/mercado-pago-client';
import type { MercadoPagoMode } from '../../core/sales/mercado-pago-client';
import { IntegratedGatewayClient } from '../../core/sales/integrated-gateway-client';
import { ReceiptPdfClient } from '../../core/sales/receipt-pdf-client';
import { AuthorizedFiscalDocument, FiscalDocumentClient } from '../../core/sales/fiscal-document-client';
import { CurrentSession, SessionClient } from '../../core/session/session-client';
import { PosPage } from './pos-page';
import { CreditCustomerClient, FiscalLookupResult } from '../../core/customers/credit-customer-client';
import { POS_ELECTRON_MODE } from '../../core/pos/pos-electron-mode';

describe('PosPage', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [
    { provide: CashWithdrawalClient, useValue: { current: () => of([]), request: () => NEVER } },
  ] }));

  function checkoutClientMock() {
    let checkout: SaleCheckout | null = null;
    return {
      current: vi.fn(() => of(checkout)),
      prepare: vi.fn((draftId: string, expectedRevision: number, terms: CheckoutTerms,
        payments: readonly { id: string; method: 'cash' | 'debit' | 'credit' | 'transfer' | 'mercadoPago' | 'cheque';
          amount: number; reference?: string | null; details?: PaymentInstrumentDetails | null }[]) => {
        checkout = { id: 'checkout-id', saleDraftId: draftId, status: 'Prepared', revision: expectedRevision + 1,
          total: payments.reduce((sum, payment) => sum + payment.amount, 0), planHash: 'hash',
          commitKey: null, approvedAmount: 0, remainingAmount: payments.reduce((sum, payment) => sum + payment.amount, 0),
          confirmedSaleId: null, terms,
          instruments: payments.map((payment) => ({ ...payment, status: 'Prepared' as const,
            reference: payment.reference ?? null, details: payment.details ?? null,
            pointPaymentIntentId: null, approvedAtUtc: null })) };
        return of(checkout);
      }),
      commit: vi.fn((_draftId: string, _checkoutId: string, _revision: number, key: string) => {
        checkout = { ...checkout!, status: 'Committed', commitKey: key };
        return of(checkout);
      }),
      approve: vi.fn((_draftId: string, _checkoutId: string, instrumentId: string) => {
        const instruments = checkout!.instruments.map((item) => item.id === instrumentId
          ? { ...item, status: 'Approved' as const } : item);
        checkout = { ...checkout!, instruments, status: 'Ready',
          approvedAmount: instruments.reduce((sum, item) => sum + (item.status === 'Approved' ? item.amount : 0), 0),
          remainingAmount: 0 };
        return of(checkout);
      }),
      confirm: vi.fn((draftId: string) => TestBed.inject(SaleDraftClient).confirm(draftId,
        checkout!.instruments.map(({ method, amount }) => ({ method, amount })),
        checkout!.terms.customerId ? { customerId: checkout!.terms.customerId,
          amount: checkout!.terms.accountChargeAmount,
          creditAppliedAmount: checkout!.terms.creditAppliedAmount } : undefined,
        { documentType: checkout!.terms.documentType as 'nonFiscalTicket' | 'fiscalTicket' | 'electronicInvoice',
          recipientTaxStatus: checkout!.terms.recipientTaxStatus as 'finalConsumer' | 'registered' | 'smallTaxpayer' | 'exempt',
          ...(checkout!.terms.recipientName ? { recipientName: checkout!.terms.recipientName } : {}),
          ...(checkout!.terms.recipientDocumentNumber ? { recipientDocumentNumber: checkout!.terms.recipientDocumentNumber } : {}),
          ...(checkout!.terms.recipientAddress ? { recipientAddress: checkout!.terms.recipientAddress } : {}),
        }, undefined)),
    };
  }

  function continueToDocument(fixture: ComponentFixture<PosPage>): void {
    const button = fixture.nativeElement.querySelector('.customer-continue-button') as HTMLButtonElement | null;
    if (button) {
      button.click();
      fixture.detectChanges();
    }
  }

  function continueAsFinalConsumer(fixture: ComponentFixture<PosPage>): void {
    continueToDocument(fixture);
    const button = fixture.nativeElement.querySelector('.document-continue-button') as HTMLButtonElement | null;
    if (button) {
      button.click();
      fixture.detectChanges();
    }
  }

  it('does not offer branch switching from the menu of an Electron box', () => {
    TestBed.configureTestingModule({ providers: [
      provideRouter([]), provideHttpClient(),
      { provide: POS_ELECTRON_MODE, useValue: true },
      { provide: SessionClient, useValue: { current: () => NEVER } },
    ] });
    const fixture = TestBed.createComponent(PosPage);
    fixture.detectChanges();

    (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('.menu-trigger')!.click();
    fixture.detectChanges();
    const menu = (fixture.nativeElement as HTMLElement).querySelector('.account-menu');
    expect(menu?.textContent).toContain('Cerrar sesi');
    expect(menu?.textContent).not.toContain('Cambiar sucursal');
  });

  async function checkoutWithGateways(intent: MercadoPagoIntent | null, unavailable = false,
    modes: readonly MercadoPagoMode[] = [], total = 2500, recovered?: SaleCheckout,
    fiscal = false, advance = true, credit = false) {
    const checkoutClient = checkoutClientMock();
    if (recovered) checkoutClient.current.mockReturnValue(of(recovered));
    const session: CurrentSession = {
      userId: 'user-id', username: 'cajero', expiresAtUtc: '2026-10-14T20:00:00Z',
      context: { userId: 'user-id', companyId: 'company-id', companyName: 'Empresa',
        branchId: 'branch-id', branchName: 'Sucursal',
        permissions: credit ? ['pos.account.charge'] : [], sessionId: 'session-id' },
    };
    TestBed.configureTestingModule({ providers: [
      provideRouter([]),
      { provide: SessionClient, useValue: { current: () => of(session) } },
      { provide: CatalogClient, useValue: { priceLists: () => of([{ id: 'list-id', name: 'Mostrador' }]),
        categories: () => of([]) } },
      { provide: SaleDraftClient, useValue: { list: () => of([]), current: () => of({
        id: 'draft-id', ticketSlot: 'A', priceListId: 'list-id', updatedAtUtc: '2026-10-13T15:00:00Z',
        lines: [{ id: 'line-id', productId: 'product-id', productCode: '1001', productName: 'Asado',
          unit: 'kg', saleMode: 'weight', quantity: 1, unitPrice: total }],
      }) } },
      { provide: MercadoPagoClient, useValue: { get: () => of(intent),
        start: () => NEVER, executeCheckout: () => NEVER,
        check: () => NEVER, cancel: () => NEVER } },
      { provide: SaleCheckoutClient, useValue: checkoutClient },
      { provide: IntegratedGatewayClient, useValue: { available: () => unavailable
        ? throwError(() => new HttpErrorResponse({ status: 503 })) : of({ gateways: modes.length
          ? [{ provider: 'mercadoPago', modes }] : [] }) } },
      { provide: InventoryClient, useValue: { stock: () => of([]) } },
      ...(credit ? [{ provide: CreditCustomerClient, useValue: {
        search: () => of([{ id: 'customer-id', code: 'C001', name: 'Cliente de prueba' }]),
        account: () => of({ customerId: 'customer-id', customerCode: 'C001',
          customerName: 'Cliente de prueba', totalDebt: 0, creditAvailable: 0, saleCount: 0 }),
      } }] : []),
    ] });
    const fixture = TestBed.createComponent(PosPage);
    fixture.detectChanges();
    await fixture.whenStable();
    (fixture.nativeElement.querySelector('.sale-footer .finish-button') as HTMLButtonElement).click();
    fixture.detectChanges();
    if (advance && !recovered) {
      if (fiscal) {
        continueToDocument(fixture);
        const documentType = fixture.nativeElement.querySelector('#checkout-document-type') as HTMLSelectElement;
        documentType.value = 'electronicInvoice';
        documentType.dispatchEvent(new Event('change', { bubbles: true }));
        fixture.detectChanges();
      } else continueAsFinalConsumer(fixture);
    }
    return fixture;
  }

  it('asks for customer type at the start of checkout instead of the open sale', async () => {
    const fixture = await checkoutWithGateways(null, false, [], 2500, undefined, true, false);
    const page = fixture.nativeElement as HTMLElement;
    const lookup = vi.spyOn(TestBed.inject(CreditCustomerClient), 'lookupFiscal');
    expect(page.querySelector('.sale-metadata #sale-recipient-tax-status')).toBeNull();
    expect(page.querySelector('.sale-metadata #sale-document-type')).toBeNull();
    expect((page.querySelector('#checkout-customer-kind') as HTMLSelectElement).value).toBe('finalConsumer');
    expect(page.querySelector('.payment-methods')).toBeNull();
    const customerKind = page.querySelector('#checkout-customer-kind') as HTMLSelectElement;
    customerKind.value = 'other';
    customerKind.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();
    const cuit = page.querySelector('#checkout-cuit') as HTMLInputElement;
    cuit.value = '20164755101';
    cuit.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    expect((page.querySelector('.customer-continue-button') as HTMLButtonElement).disabled).toBe(true);
    expect(lookup).not.toHaveBeenCalled();
    cuit.value = '20164755100';
    cuit.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    expect(cuit.value).toBe('20-16475510-0');
    page.querySelector<HTMLButtonElement>('.customer-continue-button')!.click();
    fixture.detectChanges();
    expect(lookup).toHaveBeenCalledExactlyOnceWith('20164755100');
    expect(page.querySelector('.payment-methods')).toBeNull();
    expect((page.querySelector('#checkout-document-type') as HTMLSelectElement).value).toBe('nonFiscalTicket');
    const documentType = page.querySelector('#checkout-document-type') as HTMLSelectElement;
    documentType.value = 'electronicInvoice';
    documentType.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();
    expect((page.querySelector('#checkout-recipient-tax-status') as HTMLSelectElement).value).toBe('');
    expect((page.querySelector('.document-continue-button') as HTMLButtonElement).disabled).toBe(true);
  });

  it('allows manual tax status if the CUIT lookup is still pending', async () => {
    const fixture = await checkoutWithGateways(null, false, [], 2500, undefined, false, false);
    const page = fixture.nativeElement as HTMLElement;
    const pending = new Subject<FiscalLookupResult>();
    vi.spyOn(TestBed.inject(CreditCustomerClient), 'lookupFiscal').mockReturnValue(pending.asObservable());
    const kind = page.querySelector('#checkout-customer-kind') as HTMLSelectElement;
    kind.value = 'other';
    kind.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();
    const cuit = page.querySelector('#checkout-cuit') as HTMLInputElement;
    cuit.value = '20164755100';
    cuit.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    page.querySelector<HTMLButtonElement>('.customer-continue-button')!.click();
    fixture.detectChanges();
    expect((page.querySelector('.document-continue-button') as HTMLButtonElement).disabled).toBe(true);
    const status = page.querySelector('#checkout-recipient-tax-status') as HTMLSelectElement;
    status.value = 'registered';
    status.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();
    expect((page.querySelector('.document-continue-button') as HTMLButtonElement).disabled).toBe(false);
    page.querySelector<HTMLButtonElement>('.document-continue-button')!.click();
    fixture.detectChanges();
    expect((page.querySelector('.checkout-dialog .finish-button') as HTMLButtonElement).disabled).toBe(false);
    pending.next({ source: 'arca', customerId: null, customerCode: null, name: 'Cliente tardío',
      documentNumber: '20164755100', address: 'Otra calle', locality: null, province: null,
      taxStatus: 'exempt', notice: null });
    fixture.detectChanges();
    expect(status.value).toBe('registered');
  });

  it('uses a taxpayer returned by ARCA and advances through fiscal checkout', async () => {
    const fixture = await checkoutWithGateways(null, false, [], 2500, undefined, false, false);
    const page = fixture.nativeElement as HTMLElement;
    vi.spyOn(TestBed.inject(CreditCustomerClient), 'lookupFiscal').mockReturnValue(of({
      source: 'arca', customerId: null, customerCode: null,
      name: 'CONTRIBUYENTE CONSULTADO', documentNumber: '20164755100',
      address: 'DOMICILIO CONSULTADO', locality: null, province: null,
      taxStatus: 'registered', notice: null,
    }));
    const kind = page.querySelector('#checkout-customer-kind') as HTMLSelectElement;
    kind.value = 'other';
    kind.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();
    const cuit = page.querySelector('#checkout-cuit') as HTMLInputElement;
    cuit.value = '20164755100';
    cuit.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    page.querySelector<HTMLButtonElement>('.customer-continue-button')!.click();
    fixture.detectChanges();

    expect((page.querySelector('#checkout-recipient-tax-status') as HTMLSelectElement).value).toBe('registered');
    expect(page.querySelector('.sale-document')?.textContent).not.toContain('Datos simulados');
    const documentType = page.querySelector('#checkout-document-type') as HTMLSelectElement;
    documentType.value = 'electronicInvoice';
    documentType.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();
    expect((page.querySelector('#sale-recipient-name') as HTMLInputElement).value).toBe('CONTRIBUYENTE CONSULTADO');
    expect((page.querySelector('.document-continue-button') as HTMLButtonElement).disabled).toBe(false);
    page.querySelector<HTMLButtonElement>('.document-continue-button')!.click();
    fixture.detectChanges();
    expect(page.querySelector('.checkout-review')?.textContent).toContain('Factura electrónica');
  });

  it('does not offer a new integrated payment on an unconfigured register', async () => {
    const fixture = await checkoutWithGateways(null);
    const options = fixture.nativeElement.querySelector('.payment-methods').textContent as string;
    expect(options).toContain('Efectivo');
    expect(options).not.toContain('Mercado Pago');
  });

  it('keeps fiscal fields but removes the redundant lookup block and allows stepping back', async () => {
    const fixture = await checkoutWithGateways(null, false, [], 2500, undefined, true);
    const page = fixture.nativeElement as HTMLElement;
    expect(page.querySelector('.fiscal-customer-lookup')).toBeNull();
    expect(page.textContent).not.toContain('La venta y el cobro se guardan primero');
    expect(page.querySelector('#sale-recipient-name')).not.toBeNull();
    page.querySelector<HTMLButtonElement>('.document-continue-button')!.click();
    fixture.detectChanges();
    expect(page.querySelector('.payment-methods')).not.toBeNull();
    page.querySelector<HTMLButtonElement>('.payment-back-button')!.click();
    fixture.detectChanges();
    expect((page.querySelector('#checkout-document-type') as HTMLSelectElement).value).toBe('electronicInvoice');
    page.querySelector<HTMLButtonElement>('.dialog-cancel')!.click();
    fixture.detectChanges();
    expect((page.querySelector('#checkout-customer-kind') as HTMLSelectElement).value).toBe('finalConsumer');
  });

  it('treats account charge as a selectable mixed payment and asks for its customer', async () => {
    const fixture = await checkoutWithGateways(null, false, [], 2500, undefined, false, true, true);
    const page = fixture.nativeElement as HTMLElement;
    const accountOption = page.querySelector<HTMLElement>('.payment-option[data-payment-method="account"]')!;
    expect(accountOption).not.toBeNull();
    expect(page.querySelector('.credit-payment')).toBeNull();
    accountOption.querySelector<HTMLInputElement>('input[type="checkbox"]')!.click();
    fixture.detectChanges();
    expect(page.querySelector('#credit-customer')).not.toBeNull();
    expect(page.querySelector('.credit-payment')?.textContent).toContain('Importe a cuenta corriente');
    expect(page.querySelector('.payment-guidance')?.textContent).toContain('cliente habilitado');
  });

  it('shows instrument fields instead of a generic reference or another cash payment', async () => {
    const fixture = await checkoutWithGateways(null);
    const page = fixture.nativeElement as HTMLElement;
    const option = (method: string) => page.querySelector<HTMLElement>(
      `.payment-option[data-payment-method="${method}"]`)!;
    expect(option('cash').querySelector('input[aria-label^="Referencia"]')).toBeNull();
    expect(option('cash').textContent).not.toContain('Otro pago');
    expect(option('cash').querySelector('.add-payment-operation')).toBeNull();
    option('debit').querySelector<HTMLInputElement>('input[type="checkbox"]')!.click();
    option('transfer').querySelector<HTMLInputElement>('input[type="checkbox"]')!.click();
    option('cheque').querySelector<HTMLInputElement>('input[type="checkbox"]')!.click();
    fixture.detectChanges();
    expect(option('debit').querySelector('[data-instrument-field="cardBrand"]')).not.toBeNull();
    expect(option('debit').querySelector('[data-instrument-field="authorization"]')).not.toBeNull();
    expect(option('transfer').querySelector('[data-instrument-field="entity"]')).not.toBeNull();
    expect(option('transfer').querySelector('[data-instrument-field="date"]')).not.toBeNull();
    expect(option('cheque').querySelector('[data-instrument-field="bank"]')).not.toBeNull();
    expect(option('cheque').querySelector('[data-instrument-field="holderCuit"]')).not.toBeNull();
    expect(option('transfer').textContent).not.toContain('Otro pago');
    expect(option('transfer').querySelector('.add-payment-operation')?.textContent).toContain('Agregar otra transferencia');
  });

  it('keeps transfer details when stepping back and sends them per operation', async () => {
    const fixture = await checkoutWithGateways(null);
    const page = fixture.nativeElement as HTMLElement;
    const cash = page.querySelector<HTMLElement>('.payment-option[data-payment-method="cash"]')!;
    cash.querySelector<HTMLInputElement>('input[type="checkbox"]')!.click();
    const transfer = page.querySelector<HTMLElement>('.payment-option[data-payment-method="transfer"]')!;
    transfer.querySelector<HTMLInputElement>('input[type="checkbox"]')!.click();
    fixture.detectChanges();
    const fill = (field: string, value: string) => {
      const input = transfer.querySelector<HTMLInputElement>(`[data-instrument-field="${field}"]`)!;
      input.value = value;
      input.dispatchEvent(new Event('input', { bubbles: true }));
      fixture.detectChanges();
    };
    fill('entity', 'Banco Nación');
    fill('date', '2026-11-11');
    fill('reference', 'TR-123');
    page.querySelector<HTMLButtonElement>('.payment-back-button')!.click();
    fixture.detectChanges();
    page.querySelector<HTMLButtonElement>('.document-continue-button')!.click();
    fixture.detectChanges();
    expect(page.querySelector<HTMLInputElement>('.payment-option[data-payment-method="transfer"] [data-instrument-field="reference"]')!.value).toBe('TR-123');
    const checkout = TestBed.inject(SaleCheckoutClient);
    page.querySelector<HTMLButtonElement>('.checkout-dialog .finish-button')!.click();
    await vi.waitFor(() => expect(vi.mocked(checkout.prepare)).toHaveBeenCalledOnce());
    const operations = vi.mocked(checkout.prepare).mock.calls[0][3];
    expect(operations).toHaveLength(1);
    expect(operations[0]).toMatchObject({ method: 'transfer', amount: 2500,
      reference: 'TR-123', details: { entity: 'Banco Nación', date: '2026-11-11', reference: 'TR-123' } });
  });

  it('keeps two transfers as separate instruments with their own references', async () => {
    const fixture = await checkoutWithGateways(null);
    const page = fixture.nativeElement as HTMLElement;
    page.querySelector<HTMLElement>('.payment-option[data-payment-method="cash"] input[type="checkbox"]')!.click();
    page.querySelector<HTMLElement>('.payment-option[data-payment-method="transfer"] input[type="checkbox"]')!.click();
    fixture.detectChanges();
    const option = page.querySelector<HTMLElement>('.payment-option[data-payment-method="transfer"]')!;
    option.querySelector<HTMLButtonElement>('.add-payment-operation')!.click();
    fixture.detectChanges();
    const instruments = option.querySelectorAll<HTMLElement>('.payment-instrument');
    expect(instruments).toHaveLength(2);
    const fill = (index: number, selector: string, value: string) => {
      const input = instruments[index].querySelector<HTMLInputElement>(selector)!;
      input.value = value;
      input.dispatchEvent(new Event('input', { bubbles: true }));
      fixture.detectChanges();
    };
    fill(0, '.payment-amount', '1000');
    fill(1, '.payment-amount', '1500');
    for (let index = 0; index < 2; index++) {
      fill(index, '[data-instrument-field="entity"]', 'Banco');
      fill(index, '[data-instrument-field="date"]', '2026-11-11');
      fill(index, '[data-instrument-field="reference"]', `TR-${index + 1}`);
    }
    const checkout = TestBed.inject(SaleCheckoutClient);
    page.querySelector<HTMLButtonElement>('.checkout-dialog .finish-button')!.click();
    await vi.waitFor(() => expect(vi.mocked(checkout.prepare)).toHaveBeenCalledOnce());
    const operations = vi.mocked(checkout.prepare).mock.calls[0][3];
    expect(operations.map((item) => item.reference)).toEqual(['TR-1', 'TR-2']);
    expect(operations.map((item) => item.amount)).toEqual([1000, 1500]);
    expect(new Set(operations.map((item) => item.id)).size).toBe(2);
  });

  it('sends account charge through the existing customer terms, without a cash instrument', async () => {
    const fixture = await checkoutWithGateways(null, false, [], 2500, undefined, false, true, true);
    const page = fixture.nativeElement as HTMLElement;
    page.querySelector<HTMLElement>('.payment-option[data-payment-method="account"] input[type="checkbox"]')!.click();
    fixture.detectChanges();
    const customer = page.querySelector<HTMLSelectElement>('#credit-customer')!;
    customer.value = 'customer-id';
    customer.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();
    const reference = page.querySelector<HTMLInputElement>('#account-charge-reference')!;
    reference.value = 'Autorización cliente 45';
    reference.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    expect(page.querySelector<HTMLInputElement>('.payment-option[data-payment-method="cash"] input[type="checkbox"]')!.checked).toBe(false);
    const checkout = TestBed.inject(SaleCheckoutClient);
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    page.querySelector<HTMLButtonElement>('.checkout-dialog .finish-button')!.click();
    await vi.waitFor(() => expect(vi.mocked(checkout.prepare)).toHaveBeenCalledOnce());
    expect(vi.mocked(checkout.prepare).mock.calls[0][2]).toMatchObject({ customerId: 'customer-id',
      accountChargeAmount: 2500, accountChargeReference: 'Autorización cliente 45' });
    expect(vi.mocked(checkout.prepare).mock.calls[0][3]).toHaveLength(0);
  });

  it('rounds the remaining amount to cents when adding a payment method', async () => {
    const fixture = await checkoutWithGateways(null, false, ['qr'], 79398.7);
    const methods = [...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('.payment-option')];
    const payment = (name: string) => methods.find((method) => method.textContent?.includes(name))!;
    const setAmount = (name: string, amount: string) => {
      const input = payment(name).querySelector<HTMLInputElement>('.payment-amount')!;
      input.value = amount;
      input.dispatchEvent(new Event('input', { bubbles: true }));
      fixture.detectChanges();
    };
    setAmount('Efectivo', '50000');
    payment('Débito').querySelector<HTMLInputElement>('input[type="checkbox"]')!.click();
    fixture.detectChanges();
    setAmount('Débito', '10000');
    payment('Mercado Pago').querySelector<HTMLInputElement>('input[type="checkbox"]')!.click();
    fixture.detectChanges();
    setAmount('Mercado Pago', '19000');
    payment('Transferencia').querySelector<HTMLInputElement>('input[type="checkbox"]')!.click();
    fixture.detectChanges();

    expect(payment('Transferencia').querySelector<HTMLInputElement>('.payment-amount')!.value).toBe('398,70');
    expect((fixture.nativeElement as HTMLElement).querySelector('.checkout-dialog')!.textContent)
      .not.toContain('Completá cada medio de pago con un importe válido.');
  });

  it('interprets both separators as decimal in a payment and displays es-AR grouping', async () => {
    const fixture = await checkoutWithGateways(null, false, [], 1234.5);
    const dialog = fixture.nativeElement.querySelector('.checkout-dialog') as HTMLElement;
    const cash = [...dialog.querySelectorAll<HTMLElement>('.payment-option')]
      .find((option) => option.textContent?.includes('Efectivo'))!
      .querySelector<HTMLInputElement>('.payment-amount')!;
    expect(cash.value).toBe('1.234,50');
    cash.focus();
    cash.value = '1.234';
    cash.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    expect(dialog.textContent).toContain('Completá cada medio de pago con un importe válido.');

    cash.value = '1234.50';
    cash.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    cash.blur();
    expect(cash.value).toBe('1.234,50');
    expect(dialog.textContent).not.toContain('Completá cada medio de pago con un importe válido.');
  });

  it('keeps manual methods but fails closed for new integrated payments if availability fails', async () => {
    const fixture = await checkoutWithGateways(null, true);
    const dialog = fixture.nativeElement.querySelector('.checkout-dialog') as HTMLElement;
    expect(dialog.textContent).toContain('Efectivo');
    expect(dialog.textContent).toContain('No se pudieron consultar las pasarelas');
    expect(dialog.querySelector('.payment-methods')?.textContent).not.toContain('Mercado Pago');
  });

  it('keeps an existing provider order visible and locked after register configuration changes', async () => {
    const fixture = await checkoutWithGateways({
      id: 'intent-id', mode: 'point', amount: 2500, status: 'Pending',
      providerOrderId: 'order-id', qrData: null, approved: false,
      createdAtUtc: '2026-10-13T15:00:00Z',
    });
    const option = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.payment-option'))
      .find((element) => element.textContent?.includes('Mercado Pago'))!;
    expect(option.querySelector('input[type="checkbox"]')?.hasAttribute('disabled')).toBe(true);
    expect(fixture.nativeElement.querySelector('#mercado-pago-mode')).not.toBeNull();
  });

  it('offers the permanent QR alongside the dynamic QR and recovers it after reopening checkout', async () => {
    const fixture = await checkoutWithGateways({
      id: 'intent-id', mode: 'qrStatic', amount: 2500, status: 'Pending',
      providerOrderId: 'order-id', qrData: '000201STATIC', approved: false,
      createdAtUtc: '2026-10-14T15:00:00Z',
    }, false, ['qr', 'qrStatic', 'point']);
    const selector = fixture.nativeElement.querySelector('#mercado-pago-mode') as HTMLSelectElement;
    expect(Array.from(selector.options).map((option) => option.textContent))
      .toEqual(['QR dinámico', 'QR fijo de la caja', 'Terminal Point']);
    expect(selector.value).toBe('qrStatic');
    expect(fixture.nativeElement.querySelector('.mercado-pago-panel').textContent)
      .toContain('QR fijo de esta caja');
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('.mercado-pago-qr')).not.toBeNull();
    });
    expect(selector.disabled).toBe(true);
  });

  it('requests a static QR order when the cashier selects the permanent code', async () => {
    const fixture = await checkoutWithGateways(null, false, ['qr', 'qrStatic']);
    const start = vi.spyOn(TestBed.inject(MercadoPagoClient), 'executeCheckout').mockReturnValue(NEVER);
    const options = fixture.nativeElement.querySelectorAll('.payment-option input[type="checkbox"]') as NodeListOf<HTMLInputElement>;
    options[0].click();
    options[4].click();
    fixture.detectChanges();
    const selector = fixture.nativeElement.querySelector('#mercado-pago-mode') as HTMLSelectElement;
    selector.value = 'qrStatic';
    selector.dispatchEvent(new Event('change', { bubbles: true }));
    (fixture.nativeElement.querySelector('.mercado-pago-actions button') as HTMLButtonElement).click();
    await fixture.whenStable();
    expect(start).toHaveBeenCalledWith('draft-id', 'checkout-id', expect.any(String), 'qrStatic', 2500);
  });

  it('never calls Mercado Pago when the durable checkout commit fails', async () => {
    const fixture = await checkoutWithGateways(null, false, ['qr']);
    const checkout = TestBed.inject(SaleCheckoutClient);
    const commit = vi.spyOn(checkout, 'commit').mockReturnValue(throwError(() =>
      new HttpErrorResponse({ status: 503 })));
    const execute = vi.spyOn(TestBed.inject(MercadoPagoClient), 'executeCheckout');
    const options = fixture.nativeElement.querySelectorAll('.payment-option input[type="checkbox"]') as NodeListOf<HTMLInputElement>;
    options[0].click();
    options[4].click();
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('.mercado-pago-actions button') as HTMLButtonElement).click();
    await fixture.whenStable();

    expect(vi.mocked(checkout.prepare)).toHaveBeenCalledOnce();
    expect(commit).toHaveBeenCalledOnce();
    expect(execute).not.toHaveBeenCalled();
  });

  it('restores independent repeated-method payments from the saved checkout after reopening', async () => {
    const recovered: SaleCheckout = {
      id: 'checkout-id', saleDraftId: 'draft-id', status: 'PendingBalance', revision: 1,
      total: 2500, planHash: 'hash', commitKey: 'commit-key', approvedAmount: 1000,
      remainingAmount: 1500, confirmedSaleId: null,
      terms: { customerId: null, accountChargeAmount: 0, creditAppliedAmount: 0,
        accountChargeConfirmed: false, documentType: 'nonFiscalTicket', recipientTaxStatus: 'finalConsumer' },
      instruments: [
        { id: 'operation-1', method: 'transfer', amount: 1000, status: 'Approved',
          reference: 'transfer-001', details: { entity: 'Banco Nación', date: '2026-11-11',
            reference: 'transfer-001' }, pointPaymentIntentId: null, approvedAtUtc: '2026-10-13T15:01:00Z' },
        { id: 'operation-2', method: 'transfer', amount: 1500, status: 'Prepared',
          reference: null, pointPaymentIntentId: null, approvedAtUtc: null },
      ],
    };
    const fixture = await checkoutWithGateways(null, false, [], 2500, recovered);
    fixture.detectChanges();
    const dialog = fixture.nativeElement.querySelector('.checkout-dialog') as HTMLElement;

    expect(dialog.textContent).toContain('1.000,00');
    expect(dialog.textContent).toContain('1.500,00');
    expect(dialog.querySelectorAll('.payment-option[data-payment-method="transfer"] .payment-instrument')).toHaveLength(2);
    const transfer = [...dialog.querySelectorAll<HTMLElement>('.payment-option')]
      .find((option) => option.textContent?.includes('Transferencia'))!;
    expect(transfer.querySelector<HTMLInputElement>('[data-instrument-field="reference"]')?.value).toBe('transfer-001');
    expect((dialog.querySelector('.payment-option input[type="checkbox"]') as HTMLInputElement).disabled).toBe(true);
  });

  beforeEach(() => {
    window.localStorage.clear();
    TestBed.configureTestingModule({ providers: [
      { provide: PosTerminalClient, useValue: { current: () => of({
        id: 'terminal-id', name: 'Caja 1', companyId: 'company-id', branchId: 'branch-id',
      }) } },
      { provide: CashierShiftClient, useValue: { current: () => of({
        id: 'shift-id', openingCash: 0, openedAtUtc: '2026-10-06T15:00:00Z', closedAtUtc: null,
      }) } },
      { provide: InventoryPieceClient, useValue: { lookup: () => throwError(() => new HttpErrorResponse({ status: 404 })) } },
      { provide: CreditCustomerClient, useValue: { searchFiscal: () => of([]),
        lookupFiscal: () => of({ source: 'manual', customerId: null, customerCode: null,
          name: null, documentNumber: '20164755100', address: null,
          locality: null, province: null, taxStatus: null, notice: null }),
        search: () => of([]) } },
      { provide: FiscalDocumentClient, useValue: { issue: () => NEVER } },
      { provide: MercadoPagoClient, useValue: { get: () => of(null),
        start: () => NEVER, executeCheckout: () => NEVER,
        check: () => NEVER, cancel: () => NEVER } },
      { provide: SaleCheckoutClient, useValue: checkoutClientMock() },
      { provide: IntegratedGatewayClient, useValue: { available: () => of({ gateways: [
        { provider: 'mercadoPago', modes: ['qr', 'point'] },
      ] }) } },
    ] });
  });

  it('loads an exact local CUIT profile without changing the open ticket', async () => {
    const fixture = await checkoutWithGateways(null, false, [], 2500, undefined, false, false);
    const client = TestBed.inject(CreditCustomerClient);
    const lookup = vi.spyOn(client, 'lookupFiscal').mockReturnValue(of({
      source: 'local', customerId: 'customer-1', customerCode: 'CLI-1', name: 'Cliente fiscal',
      documentNumber: '20164755100', address: 'Calle 123', locality: 'Córdoba',
      province: 'Córdoba', taxStatus: 'registered', notice: null,
    }));
    const kind = fixture.nativeElement.querySelector('#checkout-customer-kind') as HTMLSelectElement;
    kind.value = 'other';
    kind.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();
    const cuit = fixture.nativeElement.querySelector('#checkout-cuit') as HTMLInputElement;
    cuit.value = '20164755100';
    cuit.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('.customer-continue-button') as HTMLButtonElement).click();
    fixture.detectChanges();
    const document = fixture.nativeElement.querySelector('#checkout-document-type') as HTMLSelectElement;
    document.value = 'electronicInvoice';
    document.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();

    expect(lookup).toHaveBeenCalledWith('20164755100');
    expect((fixture.nativeElement.querySelector('#sale-recipient-name') as HTMLInputElement).value).toBe('Cliente fiscal');
    expect((fixture.nativeElement.querySelector('#sale-recipient-address') as HTMLInputElement).value).toBe('Calle 123');
    expect(fixture.nativeElement.querySelector('#sale-fiscal-customer')).toBeNull();
    expect(fixture.nativeElement.querySelector('.ticket-lines')?.textContent).toContain('2.500,00');
  });

  it('keeps manual fiscal fields when registry lookup fails', async () => {
    const fixture = await checkoutWithGateways(null, false, [], 2500, undefined, false, false);
    vi.spyOn(TestBed.inject(CreditCustomerClient), 'lookupFiscal')
      .mockReturnValue(throwError(() => new HttpErrorResponse({ status: 503 })));
    const kind = fixture.nativeElement.querySelector('#checkout-customer-kind') as HTMLSelectElement;
    kind.value = 'other';
    kind.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();
    const cuit = fixture.nativeElement.querySelector('#checkout-cuit') as HTMLInputElement;
    cuit.value = '20164755100';
    cuit.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('.customer-continue-button') as HTMLButtonElement).click();
    fixture.detectChanges();
    const document = fixture.nativeElement.querySelector('#checkout-document-type') as HTMLSelectElement;
    document.value = 'electronicInvoice';
    document.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();
    const name = fixture.nativeElement.querySelector('#sale-recipient-name') as HTMLInputElement;
    name.value = 'Nombre manual';
    name.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();

    expect(name.value).toBe('Nombre manual');
    expect(fixture.nativeElement.querySelector('#checkout-recipient-tax-status')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('.fiscal-lookup-notice')?.textContent)
      .toContain('No se pudo consultar el CUIT');
  });

  it('does not overwrite fiscal data edited while ARCA is responding', async () => {
    const fixture = await checkoutWithGateways(null, false, [], 2500, undefined, false, false);
    const pending = new Subject<FiscalLookupResult>();
    vi.spyOn(TestBed.inject(CreditCustomerClient), 'lookupFiscal')
      .mockReturnValue(pending.asObservable());
    const kind = fixture.nativeElement.querySelector('#checkout-customer-kind') as HTMLSelectElement;
    kind.value = 'other';
    kind.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();
    const cuit = fixture.nativeElement.querySelector('#checkout-cuit') as HTMLInputElement;
    cuit.value = '20164755100';
    cuit.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('.customer-continue-button') as HTMLButtonElement).click();
    fixture.detectChanges();
    const document = fixture.nativeElement.querySelector('#checkout-document-type') as HTMLSelectElement;
    document.value = 'electronicInvoice';
    document.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();
    const name = fixture.nativeElement.querySelector('#sale-recipient-name') as HTMLInputElement;
    name.value = 'Nombre corregido';
    name.dispatchEvent(new Event('input', { bubbles: true }));
    pending.next({ source: 'arca', customerId: null, customerCode: null,
      name: 'Nombre anterior', documentNumber: '20164755100', address: 'Calle 123',
      locality: null, province: null, taxStatus: 'registered', notice: null });
    fixture.detectChanges();

    expect(name.value).toBe('Nombre corregido');
    expect(fixture.nativeElement.querySelector('.fiscal-customer-lookup')).toBeNull();
  });

  it('requires fiscal recipient details before confirming an electronic invoice and keeps it pending', async () => {
    const issued = new Subject<AuthorizedFiscalDocument>();
    const receiptPdf = vi.fn().mockResolvedValue('C:\\Users\\cajero\\Downloads\\factura.pdf');
    const session: CurrentSession = {
      userId: 'user-id', username: 'cajero', expiresAtUtc: '2026-10-06T20:00:00Z',
      context: { userId: 'user-id', companyId: 'company-id', companyName: 'Empresa',
        branchId: 'branch-id', branchName: 'Sucursal', permissions: [], sessionId: 'session-id' },
    };
    const confirm = vi.fn().mockReturnValue(of({
      id: 'sale-id', total: 2500, changeAmount: 0, confirmedAtUtc: '2026-10-09T16:00:00Z',
      documentType: 'electronicInvoice', recipientTaxStatus: 'registered', recipientName: 'Cliente',
      accountChargeAmount: 0, creditAppliedAmount: 0, customerId: null, customerCode: null,
      customerName: null, lines: [], payments: [{ method: 'cash', tenderedAmount: 2500, appliedAmount: 2500 }],
    }));
    TestBed.configureTestingModule({ providers: [
      provideRouter([]),
      { provide: FiscalDocumentClient, useValue: { issue: () => issued.asObservable() } },
      { provide: ReceiptPdfClient, useValue: { save: receiptPdf } },
      { provide: SessionClient, useValue: { current: () => of(session) } },
      { provide: CatalogClient, useValue: { priceLists: () => of([{ id: 'list-id', name: 'Mostrador' }]),
        categories: () => of([]) } },
      { provide: SaleDraftClient, useValue: { list: () => of([]), current: () => of({
        id: 'draft-id', ticketSlot: 'A', priceListId: 'list-id', updatedAtUtc: '2026-10-09T15:00:00Z',
        lines: [{ id: 'line-id', productId: 'product-id', productCode: '1001', productName: 'Asado',
          unit: 'kg', saleMode: 'weight', quantity: 1, unitPrice: 2500 }],
      }), confirm } },
      { provide: InventoryClient, useValue: { stock: () => of([]) } },
    ] });
    const fixture = TestBed.createComponent(PosPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const select = (id: string, value: string) => {
      const element = fixture.nativeElement.querySelector(`#${id}`) as HTMLSelectElement;
      element.value = value;
      element.dispatchEvent(new Event('change', { bubbles: true }));
      fixture.detectChanges();
    };
    const input = (id: string, value: string) => {
      const element = fixture.nativeElement.querySelector(`#${id}`) as HTMLInputElement;
      element.value = value;
      element.dispatchEvent(new Event('input', { bubbles: true }));
      fixture.detectChanges();
    };
    expect(fixture.nativeElement.querySelector('.sale-metadata #sale-document-type')).toBeNull();
    expect(fixture.nativeElement.querySelector('.sale-metadata #sale-recipient-tax-status')).toBeNull();
    (fixture.nativeElement.querySelector('.sale-footer .finish-button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect((fixture.nativeElement.querySelector('#checkout-customer-kind') as HTMLSelectElement).value).toBe('finalConsumer');
    select('checkout-customer-kind', 'other');
    expect((fixture.nativeElement.querySelector('.customer-continue-button') as HTMLButtonElement).disabled).toBe(true);
    input('checkout-cuit', '20164755100');
    expect((fixture.nativeElement.querySelector('.customer-continue-button') as HTMLButtonElement).disabled).toBe(false);
    (fixture.nativeElement.querySelector('.customer-continue-button') as HTMLButtonElement).click();
    fixture.detectChanges();
    select('checkout-document-type', 'electronicInvoice');
    expect(fixture.nativeElement.querySelector('.checkout-dialog #sale-document-type')).toBeNull();
    expect(fixture.nativeElement.querySelector('.sale-document').textContent).toContain('Factura electr');
    (fixture.nativeElement.querySelector('.checkout-dialog .dialog-close') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#sale-document-type')).toBeNull();
    (fixture.nativeElement.querySelector('.sale-footer .finish-button') as HTMLButtonElement).click();
    fixture.detectChanges();
    select('checkout-customer-kind', 'other');
    input('checkout-cuit', '20164755100');
    (fixture.nativeElement.querySelector('.customer-continue-button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect((fixture.nativeElement.querySelector('#checkout-document-type') as HTMLSelectElement).value).toBe('electronicInvoice');
    expect((fixture.nativeElement.querySelector('.document-continue-button') as HTMLButtonElement).disabled).toBe(true);
    expect(confirm).not.toHaveBeenCalled();
    select('checkout-recipient-tax-status', 'registered');
    input('sale-recipient-name', 'Cliente');
    input('sale-recipient-address', 'Calle 123');
    expect((fixture.nativeElement.querySelector('.document-continue-button') as HTMLButtonElement).disabled).toBe(false);
    (fixture.nativeElement.querySelector('.document-continue-button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect((fixture.nativeElement.querySelector('.checkout-dialog .finish-button') as HTMLButtonElement).disabled).toBe(false);
    (fixture.nativeElement.querySelector('.checkout-dialog .finish-button') as HTMLButtonElement).click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(confirm).toHaveBeenCalledWith('draft-id', [{ method: 'cash', amount: 2500 }], undefined,
      { documentType: 'electronicInvoice', recipientTaxStatus: 'registered', recipientName: 'Cliente',
        recipientDocumentNumber: '20164755100', recipientAddress: 'Calle 123' }, undefined);
    expect(fixture.nativeElement.querySelector('#sale-document-type')).toBeNull();
    expect(fixture.nativeElement.querySelector('#sale-recipient-tax-status')).toBeNull();
    expect(fixture.nativeElement.querySelector('.sale-receipt').textContent).toContain('pendiente de emisión');
    expect(fixture.nativeElement.querySelector('.serial-print-button')).toBeNull();
    expect(fixture.nativeElement.querySelector('.save-pdf-button')).toBeNull();
    issued.next({
      saleId: 'sale-id', status: 'Authorized', saleDocumentType: 'ElectronicInvoice',
      issuerCuit: '30000000001', issuerName: 'Emisor', issuerAddress: 'Calle 1',
      issuerIibb: null, issuerActivityStartDate: null, pointOfSale: 99, voucherType: 1,
      number: 1, issueDate: '2026-10-09', total: 2500, receiverName: 'Cliente',
      receiverAddress: 'Calle 123', receiverTaxStatus: 'registered', receiverDocumentType: 80,
      receiverDocumentNumber: 20000000001, vatBreakdown: [], exemptAmount: 0,
      notTaxedAmount: 0, cae: '12345678901234', caeExpiry: '2026-10-19', errorCodes: null,
    });
    fixture.detectChanges();
    const download = fixture.nativeElement.querySelector('.save-pdf-button') as HTMLButtonElement;
    expect(download.textContent).toContain('Descargar comprobante');
    expect(receiptPdf).not.toHaveBeenCalled();
    download.click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(receiptPdf).toHaveBeenCalledExactlyOnceWith(expect.objectContaining({ id: 'sale-id' }),
      'Sucursal', 'Caja 1', 'cajero', expect.objectContaining({ cae: '12345678901234' }));
    expect(fixture.nativeElement.querySelector('.sale-receipt').textContent)
      .not.toContain('C:\\Users\\cajero\\Downloads');
  });

  it('switches independent saved tickets and restores the active ticket after remount', async () => {
    const session: CurrentSession = {
      userId: 'user-id', username: 'cajero', expiresAtUtc: '2026-10-06T20:00:00Z',
      context: { userId: 'user-id', companyId: 'company-id', companyName: 'Empresa',
        branchId: 'branch-id', branchName: 'Sucursal', permissions: [], sessionId: 'session-id' },
    };
    const drafts = {
      A: { id: 'draft-a', ticketSlot: 'A', priceListId: 'list-id', updatedAtUtc: '2026-10-08T16:00:00Z',
        lines: [{ id: 'line-a', productId: 'asado', productCode: '1002', productName: 'Asado',
          unit: 'kg', saleMode: 'weight', quantity: 1, unitPrice: 1000 }] },
      B: { id: 'draft-b', ticketSlot: 'B', priceListId: 'list-id', updatedAtUtc: '2026-10-08T16:00:00Z',
        lines: [{ id: 'line-b', productId: 'bondiola', productCode: '2002', productName: 'Bondiola',
          unit: 'kg', saleMode: 'weight', quantity: 2, unitPrice: 2000 }] },
      C: { id: 'draft-c', ticketSlot: 'C', priceListId: 'list-id', updatedAtUtc: '2026-10-08T16:00:00Z',
        lines: [{ id: 'line-c', productId: 'pollo', productCode: '3001', productName: 'Pollo',
          unit: 'kg', saleMode: 'weight', quantity: 3, unitPrice: 3000 }] },
      D: { id: 'draft-d', ticketSlot: 'D', priceListId: 'list-id', updatedAtUtc: '2026-10-08T16:00:00Z',
        lines: [{ id: 'line-d', productId: 'pan', productCode: '8001', productName: 'Pan',
          unit: 'unidad', saleMode: 'unit', quantity: 4, unitPrice: 4000 }] },
    };
    TestBed.configureTestingModule({ providers: [
      provideRouter([]),
      { provide: SessionClient, useValue: { current: () => of(session) } },
      { provide: CatalogClient, useValue: {
        priceLists: () => of([{ id: 'list-id', name: 'Mostrador' }]), categories: () => of([]),
      } },
      { provide: SaleDraftClient, useValue: {
        list: () => of(Object.values(drafts)),
        current: (slot = 'A') => of(drafts[slot as keyof typeof drafts] ?? null),
      } },
      { provide: InventoryClient, useValue: { stock: () => of([]) } },
    ] });

    const fixture = TestBed.createComponent(PosPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.ticket-lines').textContent).toContain('Asado');
    expect(fixture.nativeElement.querySelector('.catalog-footer .ticket-strip')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('.catalog-toolbar .ticket-strip')).toBeNull();
    expect((fixture.nativeElement.querySelector('.ticket-tab-saved') as HTMLElement).textContent).toBe('Guardado');
    expect(fixture.nativeElement.querySelector('.ticket-panel .ticket-strip')).toBeNull();

    const choose = (id: string, value: string) => {
      const element = fixture.nativeElement.querySelector(`#${id}`) as HTMLSelectElement;
      element.value = value;
      element.dispatchEvent(new Event('change', { bubbles: true }));
      fixture.detectChanges();
    };
    (fixture.nativeElement.querySelector('.sale-footer .finish-button') as HTMLButtonElement).click();
    fixture.detectChanges();
    continueToDocument(fixture);
    choose('checkout-document-type', 'electronicInvoice');
    (fixture.nativeElement.querySelector('.checkout-dialog .dialog-close') as HTMLButtonElement).click();
    fixture.detectChanges();

    (fixture.nativeElement.querySelectorAll('.ticket-tab')[1] as HTMLButtonElement).click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.ticket-lines').textContent).toContain('Bondiola');
    expect(fixture.nativeElement.querySelector('.ticket-lines').textContent).not.toContain('Asado');
    (fixture.nativeElement.querySelector('.sale-footer .finish-button') as HTMLButtonElement).click();
    fixture.detectChanges();
    continueToDocument(fixture);
    expect((fixture.nativeElement.querySelector('#checkout-document-type') as HTMLSelectElement).value).toBe('nonFiscalTicket');
    choose('checkout-document-type', 'fiscalTicket');
    (fixture.nativeElement.querySelector('.checkout-dialog .dialog-close') as HTMLButtonElement).click();
    fixture.detectChanges();
    (fixture.nativeElement.querySelectorAll('.ticket-tab')[2] as HTMLButtonElement).click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.ticket-lines').textContent).toContain('Pollo');
    (fixture.nativeElement.querySelectorAll('.ticket-tab')[3] as HTMLButtonElement).click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.ticket-lines').textContent).toContain('Pan');
    fixture.destroy();

    const reopened = TestBed.createComponent(PosPage);
    reopened.detectChanges();
    await reopened.whenStable();
    reopened.detectChanges();
    expect(reopened.nativeElement.querySelector('.ticket-lines').textContent).toContain('Pan');
    expect((reopened.nativeElement.querySelectorAll('.ticket-tab')[3] as HTMLButtonElement)
      .getAttribute('aria-current')).toBe('page');
    (reopened.nativeElement.querySelectorAll('.ticket-tab')[0] as HTMLButtonElement).click();
    await reopened.whenStable();
    reopened.detectChanges();
    expect(reopened.nativeElement.querySelector('#sale-document-type')).toBeNull();
    expect(reopened.nativeElement.querySelector('#sale-recipient-tax-status')).toBeNull();
    (reopened.nativeElement.querySelector('.sale-footer .finish-button') as HTMLButtonElement).click();
    reopened.detectChanges();
    expect((reopened.nativeElement.querySelector('#checkout-customer-kind') as HTMLSelectElement).value).toBe('finalConsumer');
    continueToDocument(reopened);
    expect((reopened.nativeElement.querySelector('#checkout-document-type') as HTMLSelectElement).value).toBe('electronicInvoice');
    (reopened.nativeElement.querySelector('.checkout-dialog .dialog-close') as HTMLButtonElement).click();
    reopened.detectChanges();
    (reopened.nativeElement.querySelectorAll('.ticket-tab')[1] as HTMLButtonElement).click();
    await reopened.whenStable();
    reopened.detectChanges();
    (reopened.nativeElement.querySelector('.sale-footer .finish-button') as HTMLButtonElement).click();
    reopened.detectChanges();
    continueToDocument(reopened);
    expect((reopened.nativeElement.querySelector('#checkout-document-type') as HTMLSelectElement).value).toBe('fiscalTicket');
  });

  it('shows which tickets were saved after reopening the POS', async () => {
    const session: CurrentSession = {
      userId: 'user-id', username: 'cajero', expiresAtUtc: '2026-10-06T20:00:00Z',
      context: { userId: 'user-id', companyId: 'company-id', companyName: 'Empresa',
        branchId: 'branch-id', branchName: 'Sucursal', permissions: [], sessionId: 'session-id' },
    };
    const draftA = { id: 'draft-a', ticketSlot: 'A', priceListId: 'list-id',
      updatedAtUtc: '2026-10-08T16:00:00Z', lines: [{ id: 'line-a', productId: 'asado',
        productCode: '1002', productName: 'Asado', unit: 'kg', saleMode: 'weight',
        quantity: 1, unitPrice: 1000 }] };
    const draftC = { ...draftA, id: 'draft-c', ticketSlot: 'C' };
    TestBed.configureTestingModule({ providers: [
      provideRouter([]),
      { provide: SessionClient, useValue: { current: () => of(session) } },
      { provide: CatalogClient, useValue: {
        priceLists: () => of([{ id: 'list-id', name: 'Mostrador' }]), categories: () => of([]),
      } },
      { provide: SaleDraftClient, useValue: {
        list: () => of([draftA, draftC]),
        current: (slot = 'A') => of(slot === 'A' ? draftA : slot === 'C' ? draftC : null),
      } },
      { provide: InventoryClient, useValue: { stock: () => of([]) } },
    ] });
    const fixture = TestBed.createComponent(PosPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const labels = [...fixture.nativeElement.querySelectorAll('.ticket-tab') as NodeListOf<HTMLButtonElement>]
      .map((button) => button.getAttribute('aria-label'));
    expect(labels).toEqual(['Ticket A, guardado', 'Ticket B', 'Ticket C, guardado', 'Ticket D']);
  });

  it('does not guess which tickets are saved when the list fails, and allows retry', async () => {
    const session: CurrentSession = {
      userId: 'user-id', username: 'cajero', expiresAtUtc: '2026-10-06T20:00:00Z',
      context: { userId: 'user-id', companyId: 'company-id', companyName: 'Empresa',
        branchId: 'branch-id', branchName: 'Sucursal', permissions: [], sessionId: 'session-id' },
    };
    const savedDraft = { id: 'draft-b', ticketSlot: 'B', priceListId: 'list-id',
      updatedAtUtc: '2026-10-08T16:00:00Z', lines: [] };
    let listAttempts = 0;
    TestBed.configureTestingModule({ providers: [
      provideRouter([]),
      { provide: SessionClient, useValue: { current: () => of(session) } },
      { provide: CatalogClient, useValue: {
        priceLists: () => of([{ id: 'list-id', name: 'Mostrador' }]), categories: () => of([]),
      } },
      { provide: SaleDraftClient, useValue: {
        list: () => ++listAttempts === 1
          ? throwError(() => new HttpErrorResponse({ status: 503 })) : of([savedDraft]),
        current: () => of(null),
      } },
      { provide: InventoryClient, useValue: { stock: () => of([]) } },
    ] });
    const fixture = TestBed.createComponent(PosPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const retry = fixture.nativeElement.querySelector('.ticket-strip-retry') as HTMLButtonElement;
    expect(retry.textContent).toContain('Reintentar');
    expect(fixture.nativeElement.querySelector('.ticket-tab-saved')).toBeNull();
    retry.click();
    fixture.detectChanges();
    expect((fixture.nativeElement.querySelectorAll('.ticket-tab')[1] as HTMLButtonElement)
      .getAttribute('aria-label')).toContain('guardado');
    expect(fixture.nativeElement.querySelector('.ticket-strip-retry')).toBeNull();
  });

  it('does not switch tickets while a changed ticket is still being saved', async () => {
    const session: CurrentSession = {
      userId: 'user-id', username: 'cajero', expiresAtUtc: '2026-10-06T20:00:00Z',
      context: { userId: 'user-id', companyId: 'company-id', companyName: 'Empresa',
        branchId: 'branch-id', branchName: 'Sucursal', permissions: [], sessionId: 'session-id' },
    };
    const pendingSave = new Subject<unknown>();
    const current = vi.fn().mockReturnValue(of(null));
    const savedDraft = { id: 'draft-a', ticketSlot: 'A', priceListId: 'list-id',
      updatedAtUtc: '2026-10-08T16:00:00Z', lines: [{ id: 'line-a', productId: 'asado',
        productCode: '1002', productName: 'Asado', unit: 'unidad', saleMode: 'unit',
        quantity: 1, unitPrice: 1000 }] };
    let saved = false;
    TestBed.configureTestingModule({ providers: [
      provideRouter([]),
      { provide: SessionClient, useValue: { current: () => of(session) } },
      { provide: CatalogClient, useValue: {
        priceLists: () => of([{ id: 'list-id', name: 'Mostrador' }]),
        categories: () => of([{ id: 'meat', name: 'Carnes', productCount: 1 }]),
        products: () => of({ items: [{ id: 'asado', code: '1002', name: 'Asado', categoryId: 'meat',
          saleMode: 'unit', unit: 'unidad', price: 1000, availableStock: 10 }],
          page: 1, pageSize: 50, totalItems: 1 }),
      } },
      { provide: SaleDraftClient, useValue: {
        list: () => of(saved ? [savedDraft] : []), current, save: () => pendingSave,
      } },
      { provide: InventoryClient, useValue: { stock: () => of([{ productId: 'asado', available: 10 }]) } },
    ] });
    const fixture = TestBed.createComponent(PosPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('.product-card') as HTMLButtonElement).click();
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('.pos-dialog .finish-button') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect((fixture.nativeElement.querySelectorAll('.ticket-tab')[1] as HTMLButtonElement).disabled).toBe(true);
    expect(current).toHaveBeenCalledTimes(1);
    saved = true;
    pendingSave.next(savedDraft);
    pendingSave.complete();
    fixture.detectChanges();
    expect((fixture.nativeElement.querySelectorAll('.ticket-tab')[0] as HTMLButtonElement)
      .getAttribute('aria-label')).toContain('guardado');
    expect((fixture.nativeElement.querySelectorAll('.ticket-tab')[1] as HTMLButtonElement).disabled).toBe(false);
    (fixture.nativeElement.querySelectorAll('.ticket-tab')[1] as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(current).toHaveBeenLastCalledWith('B');
    expect(fixture.nativeElement.querySelector('.ticket-lines')).toBeNull();
  });

  it('ignores a late barcode result from the previous ticket', async () => {
    const session: CurrentSession = {
      userId: 'user-id', username: 'cajero', expiresAtUtc: '2026-10-06T20:00:00Z',
      context: { userId: 'user-id', companyId: 'company-id', companyName: 'Empresa',
        branchId: 'branch-id', branchName: 'Sucursal', permissions: [], sessionId: 'session-id' },
    };
    const pendingSearch = new Subject<unknown>();
    TestBed.configureTestingModule({ providers: [
      provideRouter([]),
      { provide: SessionClient, useValue: { current: () => of(session) } },
      { provide: CatalogClient, useValue: {
        priceLists: () => of([{ id: 'list-id', name: 'Mostrador' }]), categories: () => of([]),
        products: () => pendingSearch,
      } },
      { provide: SaleDraftClient, useValue: { list: () => of([]), current: () => of(null) } },
      { provide: InventoryClient, useValue: { stock: () => of([]) } },
    ] });
    const fixture = TestBed.createComponent(PosPage);
    fixture.detectChanges();
    await fixture.whenStable();
    const search = fixture.nativeElement.querySelector('#product-search') as HTMLInputElement;
    search.value = '1002';
    search.dispatchEvent(new Event('input', { bubbles: true }));
    search.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
    fixture.detectChanges();
    (fixture.nativeElement.querySelectorAll('.ticket-tab')[1] as HTMLButtonElement).click();
    fixture.detectChanges();
    pendingSearch.next({ items: [{ id: 'asado', code: '1002', name: 'Asado', categoryId: 'meat',
      saleMode: 'weight', unit: 'kg', price: 1000, availableStock: 10 }],
      page: 1, pageSize: 50, totalItems: 1 });
    fixture.detectChanges();

    expect((fixture.nativeElement.querySelectorAll('.ticket-tab')[1] as HTMLButtonElement)
      .getAttribute('aria-current')).toBe('page');
    expect(fixture.nativeElement.querySelector('#product-dialog-title')).toBeNull();
  });

  it('cancels only the current draft when its last line is removed', async () => {
    const session: CurrentSession = {
      userId: 'user-id', username: 'cajero', expiresAtUtc: '2026-10-06T20:00:00Z',
      context: { userId: 'user-id', companyId: 'company-id', companyName: 'Empresa',
        branchId: 'branch-id', branchName: 'Sucursal', permissions: [], sessionId: 'session-id' },
    };
    const cancel = vi.fn().mockReturnValue(of(undefined));
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true);
    TestBed.configureTestingModule({ providers: [
      provideRouter([]),
      { provide: SessionClient, useValue: { current: () => of(session) } },
      { provide: CatalogClient, useValue: {
        priceLists: () => of([{ id: 'list-id', name: 'Mostrador' }]), categories: () => of([]),
      } },
      { provide: SaleDraftClient, useValue: {
        list: () => of([]),
        current: (slot = 'A') => of(slot === 'A' ? { id: 'draft-a', ticketSlot: 'A',
          priceListId: 'list-id', updatedAtUtc: '2026-10-08T16:00:00Z', lines: [{ id: 'line-a',
            productId: 'asado', productCode: '1002', productName: 'Asado', unit: 'kg',
            saleMode: 'weight', quantity: 1, unitPrice: 1000 }] } : null), cancel,
      } },
      { provide: InventoryClient, useValue: { stock: () => of([]) } },
    ] });
    try {
      const fixture = TestBed.createComponent(PosPage);
      fixture.detectChanges();
      await fixture.whenStable();
      (fixture.nativeElement.querySelector('.sale-footer .finish-button') as HTMLButtonElement).click();
      fixture.detectChanges();
      continueToDocument(fixture);
      const documentType = fixture.nativeElement.querySelector('#checkout-document-type') as HTMLSelectElement;
      documentType.value = 'electronicInvoice';
      documentType.dispatchEvent(new Event('change', { bubbles: true }));
      fixture.detectChanges();
      (fixture.nativeElement.querySelector('.checkout-dialog .dialog-close') as HTMLButtonElement).click();
      fixture.detectChanges();
      (fixture.nativeElement.querySelector('.remove-line-button') as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(cancel).toHaveBeenCalledExactlyOnceWith('A');
      expect(fixture.nativeElement.querySelector('.ticket-lines')).toBeNull();
      expect(Object.keys(window.localStorage).filter((key) => key.includes('ticket-document'))).toHaveLength(0);
      expect((fixture.nativeElement.querySelectorAll('.ticket-tab')[1] as HTMLButtonElement).disabled).toBe(false);
    } finally {
      confirm.mockRestore();
    }
  });

  it.each([
    [401, 'credencial vigente'],
    [503, 'conexión con el servidor'],
  ])('explains terminal verification failures with HTTP %i', async (status, message) => {
    const session: CurrentSession = {
      userId: 'user-id', username: 'cajero', expiresAtUtc: '2026-10-06T20:00:00Z',
      context: { userId: 'user-id', companyId: 'company-id', companyName: 'Empresa',
        branchId: 'branch-id', branchName: 'Sucursal', permissions: [], sessionId: 'session-id' },
    };
    TestBed.configureTestingModule({ providers: [
      provideRouter([]),
      { provide: SessionClient, useValue: { current: () => of(session) } },
      { provide: PosTerminalClient, useValue: { current: () => throwError(() => new HttpErrorResponse({ status })) } },
    ] });

    const fixture = TestBed.createComponent(PosPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain(message);
    expect(fixture.nativeElement.querySelector('#product-search')).toBeNull();
  });

  it('blocks the catalogue and ticket until this cashier opens a shift', async () => {
    const session: CurrentSession = {
      userId: 'user-id', username: 'cajero', expiresAtUtc: '2026-10-06T20:00:00Z',
      context: { userId: 'user-id', companyId: 'company-id', companyName: 'Empresa',
        branchId: 'branch-id', branchName: 'Sucursal', permissions: [], sessionId: 'session-id' },
    };
    let saved = false;
    let opened = false;
    TestBed.configureTestingModule({ providers: [
      provideRouter([]),
      { provide: SessionClient, useValue: { current: () => of(session) } },
      { provide: CashierShiftClient, useValue: {
        current: () => of(null), lastClosed: () => of(null),
        open: () => { opened = true; return of({
          id: 'new-shift', openingCash: 0, openedAtUtc: '2026-10-06T15:00:00Z', closedAtUtc: null,
        }); },
      } },
      { provide: CatalogClient, useValue: { priceLists: () => of([]) } },
      { provide: SaleDraftClient, useValue: { list: () => of([]), current: () => of(null),
        save: () => { saved = true; return of(null); } } },
      { provide: InventoryClient, useValue: { stock: () => of([]) } },
    ] });

    const fixture = TestBed.createComponent(PosPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Caja 1');
    expect(fixture.nativeElement.textContent).toContain('Abrí el turno');
    expect(fixture.nativeElement.querySelector('#product-search')).toBeNull();
    expect(fixture.nativeElement.querySelector('.sale-footer')).toBeNull();
    expect(fixture.nativeElement.querySelector('.ticket-strip')).toBeNull();
    expect(saved).toBe(false);
    for (const key of ['8', '0', '0', '1', 'Enter'])
      window.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }));
    expect(fixture.nativeElement.querySelector('#product-dialog-title')).toBeNull();

    (fixture.nativeElement.querySelector('.shift-gate .finish-button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#shift-title').textContent).toContain('Abrir turno');
    (fixture.nativeElement.querySelector('.shift-dialog .finish-button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(opened).toBe(true);
    expect(fixture.nativeElement.querySelector('#product-search')).not.toBeNull();
  });

  it('loads the branch catalog automatically and persists an added sale line', async () => {
    const session: CurrentSession = {
      userId: 'user-id',
      username: 'cajero',
      expiresAtUtc: '2026-10-06T20:00:00Z',
      context: {
        userId: 'user-id',
        companyId: 'company-id',
        companyName: 'Empresa',
        branchId: 'branch-id',
        branchName: 'Sucursal',
        permissions: [],
        sessionId: 'session-id',
      },
    };
    let saveAttempts = 0;
    let confirmationAttempts = 0;
    let printerAssigned = true;
    const receiptPdf = vi.fn().mockResolvedValue('C:\\Users\\cajero\\Downloads\\ticket-test.pdf');
    const serialPrint = vi.fn().mockResolvedValue({ port: 'COM3', bytesWritten: 480,
      confirmation: 'write-only' });
    const deviceAvailability = vi.fn(async () => ({ scale: null,
      printer: printerAssigned ? { kind: 'text' as const, port: 'COM3', baudRate: 9600 } : null }));
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: ReceiptPdfClient, useValue: { save: receiptPdf } },
        { provide: PosDeviceClient, useValue: { print: serialPrint, deviceAvailability } },
        { provide: SessionClient, useValue: { current: () => of(session) } },
        { provide: CatalogClient, useValue: {
          priceLists: () => of([{ id: 'list-id', name: 'Mostrador (datos ficticios)' }]),
          categories: () => of([{ id: 'category-id', name: 'Almacén', productCount: 1 }]),
          products: () => of({ items: [{
            id: 'product-id', code: '8001', name: 'Pan rallado 1 kg', categoryId: 'category-id',
            saleMode: 'unit' as const, price: 2400, availableStock: 40,
          }], page: 1, pageSize: 50, totalItems: 1 }),
        } },
        { provide: SaleDraftClient, useValue: {
          list: () => of([]),
          current: () => of(null),
          save: (priceListId: string, lines: readonly { productId: string; quantity: number }[]) => {
            saveAttempts++;
            return of({ id: 'draft-id', priceListId, updatedAtUtc: '2026-10-06T16:00:00Z', lines: [{
              productId: lines[0].productId, productCode: '8001', productName: 'Pan rallado 1 kg',
              unit: 'unidad', saleMode: 'unit' as const, quantity: lines[0].quantity, unitPrice: 2400,
            }] });
          },
          confirm: () => {
            confirmationAttempts++;
            return of({
              id: 'sale-id', total: 2400, changeAmount: 0, confirmedAtUtc: '2026-10-06T16:00:00Z',
              lines: [{ code: '8001', name: 'Pan rallado 1 kg', unit: 'unidad', quantity: 1, unitPrice: 2400, lineTotal: 2400 }],
              payments: [{ method: 'cash' as const, tenderedAmount: 2400, appliedAmount: 2400 }],
            });
          },
          cancel: () => of(null),
        } },
        { provide: CashierShiftClient, useValue: { current: () => of({
          id: 'shift-id', openingCash: 0, openedAtUtc: '2026-10-06T15:00:00Z', closedAtUtc: null,
        }) } },
        { provide: InventoryClient, useValue: { stock: () => of([{ productId: 'product-id', available: 40 }]),
          adjust: () => of(null) } },
      ],
    });
    const fixture = TestBed.createComponent(PosPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    fixture.detectChanges();

    const search = fixture.nativeElement.querySelector('#product-search') as HTMLInputElement;
    const priceList = fixture.nativeElement.querySelector('.price-list-field select') as HTMLSelectElement;
    expect(priceList.value).toBe('list-id');
    expect(search.disabled).toBe(false);
    expect(fixture.nativeElement.querySelector('.product-card').textContent).toContain('Pan rallado');
    expect(fixture.nativeElement.querySelector('.product-card').textContent).toContain('/ unidad');
    expect(fixture.nativeElement.querySelector('.product-card').textContent).toContain('Disponible: 40 unidad');
    expect(fixture.nativeElement.querySelector('.category-icon').textContent).toContain('🧺');
    search.dispatchEvent(new KeyboardEvent('keydown', { key: '8', bubbles: true }));
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#product-dialog-title')).toBeNull();
    for (const key of ['8', '0', '0', '1', 'Enter'])
      window.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#product-dialog-title').textContent).toContain('Pan rallado');
    (fixture.nativeElement.querySelector('.pos-dialog .dialog-close') as HTMLButtonElement).click();
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('.product-card') as HTMLButtonElement).click();
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('.pos-dialog .finish-button') as HTMLButtonElement).click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(saveAttempts).toBe(1);
    expect(fixture.nativeElement.querySelector('.ticket-lines').textContent).toContain('Pan rallado');
    expect(fixture.nativeElement.querySelector('.price-list-lock').textContent).toContain('Borrador guardado');
    expect(priceList.disabled).toBe(true);
    (fixture.nativeElement.querySelector('.sale-footer .finish-button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#checkout-title').textContent).toContain('Cobrar venta');
    continueAsFinalConsumer(fixture);
    const cashAmount = fixture.nativeElement.querySelector('.payment-amount') as HTMLInputElement;
    cashAmount.value = '1000';
    cashAmount.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.payment-balance').textContent).toContain('1.400,00');
    (fixture.nativeElement.querySelector('.checkout-dialog .finish-button') as HTMLButtonElement).click();
    expect(confirmationAttempts).toBe(0);
    cashAmount.value = '2400';
    cashAmount.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('.checkout-dialog .finish-button') as HTMLButtonElement).click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(confirmationAttempts).toBe(1);
    const receipt = fixture.nativeElement.querySelector('.sale-receipt') as HTMLElement;
    expect(receipt.textContent).toContain('Venta confirmada');
    expect(receipt.textContent).toContain('sale-id');
    expect(receipt.textContent).toContain('2.400,00');
    expect(receipt.textContent).toContain('Efectivo');
    await fixture.whenStable();
    fixture.detectChanges();
    expect(serialPrint).toHaveBeenCalledExactlyOnceWith(expect.objectContaining({ id: 'sale-id' }),
      'Sucursal', 'Caja 1', 'cajero');
    expect(receipt.textContent).toContain('COM3');
    expect(receipt.textContent).not.toContain('COM1');
    expect((receipt.querySelector('.serial-print-button') as HTMLButtonElement).disabled).toBe(true);
    expect(receipt.textContent).not.toContain('ticket-test.pdf');
    expect((receipt.querySelector('.save-pdf-button') as HTMLButtonElement).textContent)
      .toContain('Descargar comprobante');
    (receipt.querySelector('.save-pdf-button') as HTMLButtonElement).click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(receiptPdf).toHaveBeenCalledWith(expect.objectContaining({ id: 'sale-id' }), 'Sucursal', 'Caja 1', 'cajero');
    expect(receipt.textContent).toContain('Comprobante descargado en Descargas.');
    expect(receipt.textContent).not.toContain('ticket-test.pdf');

    (receipt.querySelector('.finish-button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.sale-receipt')).toBeNull();
    (fixture.nativeElement.querySelector('.product-card') as HTMLButtonElement).click();
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('.pos-dialog .finish-button') as HTMLButtonElement).click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(saveAttempts).toBe(2);
    expect(fixture.nativeElement.querySelector('.price-list-lock').textContent).toContain('Borrador guardado');
    (fixture.nativeElement.querySelector('.sale-footer .finish-button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#checkout-title').textContent).toContain('Cobrar venta');
    continueAsFinalConsumer(fixture);
    printerAssigned = false;
    const secondCash = fixture.nativeElement.querySelector('.payment-amount') as HTMLInputElement;
    secondCash.value = '2400';
    secondCash.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('.checkout-dialog .finish-button') as HTMLButtonElement).click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(confirmationAttempts).toBe(2);
    expect(serialPrint).toHaveBeenCalledTimes(1);
    expect(fixture.nativeElement.querySelector('.sale-receipt').textContent)
      .toContain('no tiene impresora asignada');
    expect(fixture.nativeElement.querySelector('.serial-print-button')).toBeNull();
  });

  it('fills a weighed product from the serial scale without changing a sale or a unit item', async () => {
    const session: CurrentSession = {
      userId: 'user-id', username: 'cajero', expiresAtUtc: '2026-10-06T20:00:00Z',
      context: { userId: 'user-id', companyId: 'company-id', companyName: 'Empresa',
        branchId: 'branch-id', branchName: 'Sucursal', permissions: [], sessionId: 'session-id' },
    };
    const readScale = vi.fn().mockResolvedValue(0.75);
    TestBed.configureTestingModule({ providers: [
      provideRouter([]),
      { provide: SessionClient, useValue: { current: () => of(session) } },
      { provide: PosDeviceClient, useValue: { readScale,
        deviceAvailability: async () => ({ scale: { port: 'COM6', baudRate: 9600 }, printer: null }) } },
      { provide: CatalogClient, useValue: {
        priceLists: () => of([{ id: 'list-id', name: 'Mostrador' }]),
        categories: () => of([{ id: 'res', name: 'Carne de res', productCount: 1 }]),
        products: () => of({ items: [{ id: 'asado', code: '1002', name: 'Asado',
          categoryId: 'res', saleMode: 'weight' as const, price: 4900, availableStock: 10 }],
          page: 1, pageSize: 50, totalItems: 1 }),
      } },
      { provide: SaleDraftClient, useValue: { list: () => of([]), current: () => of(null) } },
      { provide: InventoryClient, useValue: { stock: () => of([{ productId: 'asado', available: 10 }]) } },
    ] });
    const fixture = TestBed.createComponent(PosPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('.product-card') as HTMLButtonElement).click();
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('.scale-read-button') as HTMLButtonElement).click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(readScale).toHaveBeenCalledOnce();
    expect((fixture.nativeElement.querySelector('#product-quantity') as HTMLInputElement).value).toBe('0.75');
    expect(fixture.nativeElement.querySelector('.scale-status[role="status"]').textContent).toContain('0,750 kg');
    expect(fixture.nativeElement.querySelector('.sale-receipt')).toBeNull();
  });

  it('keeps manual weight entry but offers no scale reading without an assignment', async () => {
    const session: CurrentSession = {
      userId: 'user-id', username: 'cajero', expiresAtUtc: '2026-10-06T20:00:00Z',
      context: { userId: 'user-id', companyId: 'company-id', companyName: 'Empresa',
        branchId: 'branch-id', branchName: 'Sucursal', permissions: [], sessionId: 'session-id' },
    };
    const readScale = vi.fn();
    TestBed.configureTestingModule({ providers: [
      provideRouter([]),
      { provide: SessionClient, useValue: { current: () => of(session) } },
      { provide: PosDeviceClient, useValue: { readScale,
        deviceAvailability: async () => ({ scale: null, printer: null }) } },
      { provide: CatalogClient, useValue: {
        priceLists: () => of([{ id: 'list-id', name: 'Mostrador' }]),
        categories: () => of([{ id: 'res', name: 'Carne de res', productCount: 1 }]),
        products: () => of({ items: [{ id: 'asado', code: '1002', name: 'Asado',
          categoryId: 'res', saleMode: 'weight' as const, price: 4900, availableStock: 10 }],
          page: 1, pageSize: 50, totalItems: 1 }),
      } },
      { provide: SaleDraftClient, useValue: { list: () => of([]), current: () => of(null) } },
      { provide: InventoryClient, useValue: { stock: () => of([{ productId: 'asado', available: 10 }]) } },
    ] });
    const fixture = TestBed.createComponent(PosPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('.product-card') as HTMLButtonElement).click();
    fixture.detectChanges();
    await new Promise<void>((resolve) => setTimeout(resolve, 0));

    expect(fixture.nativeElement.querySelector('.scale-read-button')).toBeNull();
    expect(fixture.nativeElement.querySelector('.pos-dialog').textContent)
      .toContain('peso manualmente');
    const quantity = fixture.nativeElement.querySelector('#product-quantity') as HTMLInputElement;
    expect(quantity.type).toBe('text');
    expect(document.activeElement).toBe(quantity);
    expect(quantity.selectionStart).toBe(0);
    expect(quantity.selectionEnd).toBe(quantity.value.length);
    expect(readScale).not.toHaveBeenCalled();
    (fixture.nativeElement.querySelector('.pos-dialog .dialog-close') as HTMLButtonElement).click();
    fixture.detectChanges();
    await new Promise<void>((resolve) => setTimeout(resolve, 0));
    const scan = fixture.nativeElement.querySelector('#product-search') as HTMLInputElement;
    expect(document.activeElement).toBe(scan);
    scan.value = '1002';
    scan.blur();
    scan.focus();
    expect(scan.selectionStart).toBe(0);
    expect(scan.selectionEnd).toBe(scan.value.length);
  });

  it('offers lists from the active branch without displaying demo prices as real prices', () => {
    const session: CurrentSession = {
      userId: 'user-id', username: 'cajero', expiresAtUtc: '2026-10-06T20:00:00Z',
      context: {
        userId: 'user-id', companyId: 'company-id', companyName: 'Empresa',
        branchId: 'branch-id', branchName: 'Sucursal', permissions: [], sessionId: 'session-id',
      },
    };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: SessionClient, useValue: { current: () => of(session) } },
        { provide: CatalogClient, useValue: { priceLists: () => of([{ id: 'list-id', name: 'Mayorista' }]), categories: () => of([]), products: () => of({ items: [], page: 1, pageSize: 50, totalItems: 0 }) } },
        { provide: SaleDraftClient, useValue: { list: () => of([]), current: () => of(null),
          save: () => of(null), cancel: () => of(null) } },
        { provide: InventoryClient, useValue: { stock: () => of([]), adjust: () => of(null) } },
      ],
    });
    const fixture = TestBed.createComponent(PosPage);
    fixture.detectChanges();

    const priceList = fixture.nativeElement.querySelector('.price-list-field select') as HTMLSelectElement;
    expect(priceList.textContent).toContain('Mayorista');
    expect(priceList.value).toBe('list-id');
    expect(fixture.nativeElement.querySelector('.catalog-badge').textContent).toContain('Catálogo de la sucursal');
    priceList.value = 'list-id';
    priceList.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelectorAll('.product-card')).toHaveLength(0);
    expect(fixture.nativeElement.querySelector('.catalog-empty').textContent).toContain('Sin productos');
    expect((fixture.nativeElement.querySelector('#product-search') as HTMLInputElement).disabled).toBe(false);
  });

  it('restores a persisted ticket using its frozen description and price', async () => {
    const session: CurrentSession = {
      userId: 'user-id', username: 'cajero', expiresAtUtc: '2026-10-06T20:00:00Z',
      context: {
        userId: 'user-id', companyId: 'company-id', companyName: 'Empresa',
        branchId: 'branch-id', branchName: 'Sucursal', permissions: [], sessionId: 'session-id',
      },
    };
    const savedDraft = {
      id: 'draft-id', priceListId: 'list-id', updatedAtUtc: '2026-10-06T16:00:00Z',
      lines: [{
        productId: 'product-id', productCode: 'CAR-01', productName: 'Corte guardado',
        unit: 'kg', saleMode: 'weight' as const, quantity: 1.25, unitPrice: 12345.67,
      }],
    };
    let confirmedDraftId = '';
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: SessionClient, useValue: { current: () => of(session) } },
        { provide: CatalogClient, useValue: { priceLists: () => of([{ id: 'list-id', name: 'Mayorista' }]), categories: () => of([]), products: () => of({ items: [], page: 1, pageSize: 50, totalItems: 0 }) } },
        { provide: SaleDraftClient, useValue: {
          list: () => of([]),
          current: () => of(savedDraft), save: () => of(savedDraft), cancel: () => of(null),
          confirm: (draftId: string) => {
            confirmedDraftId = draftId;
            return of({ id: 'sale-id', total: 15432.09, changeAmount: 0,
              confirmedAtUtc: '2026-10-06T16:01:00Z', lines: [], payments: [] });
          },
        } },
        { provide: CashierShiftClient, useValue: { current: () => of({
          id: 'shift-id', openingCash: 0, openedAtUtc: '2026-10-06T15:00:00Z', closedAtUtc: null,
        }) } },
        { provide: InventoryClient, useValue: { stock: () => of([]), adjust: () => of(null) } },
      ],
    });

    const fixture = TestBed.createComponent(PosPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect((fixture.componentInstance as unknown as { selectedPriceListId: () => string }).selectedPriceListId()).toBe('list-id');
    expect(fixture.nativeElement.querySelector('.price-list-field select').textContent).toContain('Mayorista');
    const ticketTable = fixture.nativeElement.querySelector('table.ticket-lines') as HTMLTableElement;
    expect(ticketTable).not.toBeNull();
    expect(ticketTable.querySelectorAll('thead th')).toHaveLength(6);
    expect(ticketTable.querySelector('.ticket-line-quantity-heading .heading-original')?.textContent)
      .toContain('Peso / Cant.');
    expect(ticketTable.querySelector('.ticket-line-quantity-heading .heading-nocturno')?.textContent)
      .toContain('Cantidad');
    expect(ticketTable.querySelector('.ticket-line-total-heading .heading-original')?.textContent)
      .toContain('Total');
    expect(ticketTable.querySelector('.ticket-line-total-heading .heading-nocturno')?.textContent)
      .toContain('Importe');
    const compactLine = ticketTable.querySelector('tbody tr.ticket-line') as HTMLTableRowElement;
    expect(compactLine).not.toBeNull();
    expect(compactLine.cells).toHaveLength(6);
    expect(compactLine.textContent).toContain('Corte guardado');
    expect(compactLine.textContent).toContain('CAR-01');
    expect(compactLine.cells[2].textContent).toContain('12.345,67');
    expect(compactLine.cells[2].textContent).toContain('Desc. 0,00%');
    expect(compactLine.cells[5].querySelector('.remove-line-button')).not.toBeNull();
    const ticketBody = fixture.nativeElement.querySelector('.ticket-body-scroll') as HTMLElement;
    expect(ticketBody.querySelector('.sale-metadata')).not.toBeNull();
    expect(ticketBody.querySelector('.ticket-line')).not.toBeNull();
    expect(ticketBody.querySelector('.sale-footer')).toBeNull();
    expect(fixture.nativeElement.querySelector('.ticket-lines').textContent).toContain('Corte guardado');
    expect(fixture.nativeElement.querySelector('.ticket-lines').textContent).toContain('12.345,67');
    expect((fixture.nativeElement.querySelector('.quantity-input') as HTMLInputElement).value).toBe('1,250');
    (fixture.nativeElement.querySelector('.sale-footer .finish-button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#checkout-title').textContent).toContain('Cobrar venta');
    continueAsFinalConsumer(fixture);
    (fixture.nativeElement.querySelector('.checkout-dialog .finish-button') as HTMLButtonElement).click();
    await fixture.whenStable();
    expect(confirmedDraftId).toBe('draft-id');
  });

  it('keeps the unsaved ticket visible and offers retry for a server failure', async () => {
    const session: CurrentSession = {
      userId: 'user-id', username: 'cajero', expiresAtUtc: '2026-10-06T20:00:00Z',
      context: {
        userId: 'user-id', companyId: 'company-id', companyName: 'Empresa',
        branchId: 'branch-id', branchName: 'Sucursal', permissions: [], sessionId: 'session-id',
      },
    };
    let saveAttempts = 0;
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: SessionClient, useValue: { current: () => of(session) } },
        { provide: CatalogClient, useValue: {
          priceLists: () => of([{ id: 'list-id', name: 'Mostrador' }]),
          categories: () => of([{ id: 'category-id', name: 'Vacuno', productCount: 1 }]),
          products: () => of({ items: [{
            id: 'product-id', code: '1001', name: 'Bife', categoryId: 'category-id',
            saleMode: 'unit' as const, price: 1000, availableStock: 10,
          }], page: 1, pageSize: 50, totalItems: 1 }),
        } },
        { provide: SaleDraftClient, useValue: {
          list: () => of([]),
          current: () => of(null), save: () => {
            saveAttempts++;
            if (saveAttempts === 1) return throwError(() => new HttpErrorResponse({
              status: 500,
            }));
            return of({ id: 'draft-id', priceListId: 'list-id', updatedAtUtc: '2026-10-06T16:00:00Z', lines: [{
              productId: 'product-id', productCode: '1001', productName: 'Bife', unit: 'unidad',
              saleMode: 'unit' as const, quantity: 1, unitPrice: 1000,
            }] });
          },
          cancel: () => of(null),
        } },
        { provide: InventoryClient, useValue: { stock: () => of([{ productId: 'product-id', available: 10 }]),
          adjust: () => of(null) } },
      ],
    });

    const fixture = TestBed.createComponent(PosPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const priceList = fixture.nativeElement.querySelector('.price-list-field select') as HTMLSelectElement;
    priceList.value = 'list-id';
    priceList.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('.product-card') as HTMLButtonElement).click();
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('.pos-dialog .finish-button') as HTMLButtonElement).click();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.ticket-lines').textContent).toContain('Bife');
    const retry = fixture.nativeElement.querySelector('.draft-retry') as HTMLButtonElement;
    expect(retry).not.toBeNull();
    expect(fixture.nativeElement.querySelector('.price-list-lock[role="alert"]').textContent).toContain('No se pudo guardar el ticket');
    expect(fixture.nativeElement.querySelector('.price-list-lock[role="alert"]').textContent).not.toContain('No se pudo sincronizar');
    expect(fixture.nativeElement.querySelector('.pos-error')).toBeNull();
    (fixture.nativeElement.querySelector('.sale-footer .finish-button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#checkout-title')).toBeNull();
    expect(fixture.nativeElement.querySelector('.price-list-lock[role="alert"]').textContent).toContain('No se pudo guardar el ticket');
    expect(fixture.nativeElement.querySelector('.pos-error')).toBeNull();
    retry.click();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
    expect(saveAttempts).toBe(2);
    expect(fixture.nativeElement.querySelector('.ticket-lines').textContent).toContain('Bife');
    expect(fixture.nativeElement.querySelector('.price-list-lock').textContent).toContain('Borrador guardado');
    expect(fixture.nativeElement.querySelector('.pos-error')).toBeNull();
  });

  it('checks kilograms and catalog-defined packages before adding a product', async () => {
    const session: CurrentSession = {
      userId: 'user-id', username: 'cajero', expiresAtUtc: '2026-10-06T20:00:00Z',
      context: { userId: 'user-id', companyId: 'company-id', companyName: 'Empresa',
        branchId: 'branch-id', branchName: 'Sucursal', permissions: [], sessionId: 'session-id' },
    };
    let savedLines: readonly { productId: string; quantity: number; pieceCount?: number }[] = [];
    TestBed.configureTestingModule({ providers: [
      provideRouter([]),
      { provide: SessionClient, useValue: { current: () => of(session) } },
      { provide: CatalogClient, useValue: {
        priceLists: () => of([{ id: 'list-id', name: 'Mostrador' }]),
        categories: () => of([{ id: 'category-id', name: 'Productos', productCount: 2 }]),
        products: () => of({ items: [
          { id: 'weight-id', code: '1001', name: 'Asado', categoryId: 'category-id',
            unit: 'kg', saleMode: 'weight', price: 1000, availableStock: 0.5 },
          { id: 'pack-id', code: '7001', name: 'Hamburguesas x 4', categoryId: 'category-id',
            unit: 'paquete', saleMode: 'unit', price: 2000, availableStock: 2 },
        ], page: 1, pageSize: 50, totalItems: 2 }),
      } },
      { provide: SaleDraftClient, useValue: {
        list: () => of([]),
        current: () => of(null),
        save: (_: string, lines: readonly { productId: string; quantity: number; pieceCount?: number }[]) => {
          savedLines = lines;
          return of({ id: 'draft-id', priceListId: 'list-id', updatedAtUtc: '2026-10-06T16:00:00Z',
            lines: lines.map((line) => ({ productId: line.productId, productCode: line.productId,
              productName: line.productId, unit: line.productId === 'weight-id' ? 'kg' : 'paquete',
              saleMode: line.productId === 'weight-id' ? 'weight' : 'unit', quantity: line.quantity,
              pieceCount: line.pieceCount, unitPrice: 1000 })) });
        },
      } },
      { provide: InventoryClient, useValue: { stock: () => of([
        { productId: 'weight-id', available: 0.5 }, { productId: 'pack-id', available: 2 },
      ]) } },
    ] });
    const fixture = TestBed.createComponent(PosPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    (fixture.nativeElement.querySelectorAll('.product-card')[0] as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.pos-dialog [role="alert"]').textContent).toContain('0,5 kg');
    expect((fixture.nativeElement.querySelector('.pos-dialog .finish-button') as HTMLButtonElement).disabled).toBe(true);
    let quantity = fixture.nativeElement.querySelector('#product-quantity') as HTMLInputElement;
    quantity.value = '0.25';
    quantity.dispatchEvent(new Event('input', { bubbles: true }));
    const pieces = fixture.nativeElement.querySelector('#product-piece-count') as HTMLInputElement;
    pieces.value = '2';
    pieces.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('.pos-dialog .finish-button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(savedLines).toEqual([{ productId: 'weight-id', quantity: 0.25, pieceCount: 2 }]);
    expect(fixture.nativeElement.querySelector('.ticket-lines').textContent).toContain('2 piezas');

    (fixture.nativeElement.querySelectorAll('.product-card')[1] as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.product-facts').textContent).toContain('/ paquete');
    quantity = fixture.nativeElement.querySelector('#product-quantity') as HTMLInputElement;
    expect(quantity.inputMode).toBe('numeric');
    quantity.value = '3';
    quantity.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.pos-dialog [role="alert"]').textContent).toContain('2 paquete');
    expect((fixture.nativeElement.querySelector('.pos-dialog .finish-button') as HTMLButtonElement).disabled).toBe(true);
    quantity.value = '1.5';
    quantity.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.pos-dialog [role="alert"]').textContent).toContain('entera');
    expect(savedLines).toEqual([{ productId: 'weight-id', quantity: 0.25, pieceCount: 2 }]);
  });

  it('removes a rejected addition when stock changed after the catalog loaded', async () => {
    const session: CurrentSession = {
      userId: 'user-id', username: 'cajero', expiresAtUtc: '2026-10-06T20:00:00Z',
      context: { userId: 'user-id', companyId: 'company-id', companyName: 'Empresa',
        branchId: 'branch-id', branchName: 'Sucursal', permissions: [], sessionId: 'session-id' },
    };
    TestBed.configureTestingModule({ providers: [
      provideRouter([]),
      { provide: SessionClient, useValue: { current: () => of(session) } },
      { provide: CatalogClient, useValue: {
        priceLists: () => of([{ id: 'list-id', name: 'Mostrador' }]),
        categories: () => of([{ id: 'category-id', name: 'Vacuno', productCount: 1 }]),
        products: () => of({ items: [{ id: 'product-id', code: '1001', name: 'Asado',
          categoryId: 'category-id', unit: 'kg', saleMode: 'weight', price: 1000, availableStock: 10 }],
          page: 1, pageSize: 50, totalItems: 1 }),
      } },
      { provide: SaleDraftClient, useValue: {
        list: () => of([]),
        current: () => of(null),
        save: () => throwError(() => new HttpErrorResponse({ status: 409,
          error: { error: { code: 'INSUFFICIENT_STOCK' } } })),
      } },
      { provide: InventoryClient, useValue: { stock: () => of([{ productId: 'product-id', available: 10 }]) } },
    ] });
    const fixture = TestBed.createComponent(PosPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('.product-card') as HTMLButtonElement).click();
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('.pos-dialog .finish-button') as HTMLButtonElement).click();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.ticket-lines')).toBeNull();
    expect(fixture.nativeElement.querySelector('.pos-error[role="alert"]').textContent).toContain('No hay stock suficiente');
    expect((fixture.nativeElement.querySelector('.sale-footer .finish-button') as HTMLButtonElement).disabled).toBe(true);
  });

  it('reloads the persisted ticket after an administrator releases one piece', async () => {
    const session: CurrentSession = {
      userId: 'user-id', username: 'cajero', expiresAtUtc: '2026-10-06T20:00:00Z',
      context: { userId: 'user-id', companyId: 'company-id', companyName: 'Empresa',
        branchId: 'branch-id', branchName: 'Sucursal', permissions: [], sessionId: 'session-id' },
    };
    const remainingLine = { id: 'other-line', productId: 'other-id', productCode: '1002',
      productName: 'Otro corte', unit: 'kg', saleMode: 'weight' as const,
      quantity: 1, unitPrice: 500 };
    const staleDraft = { id: 'draft-id', ticketSlot: 'A' as const, priceListId: 'list-id',
      updatedAtUtc: '2026-10-06T16:00:00Z', lines: [
        { ...remainingLine, id: 'piece-line', productId: 'piece-product', productCode: '1001',
          productName: 'Pieza liberada', inventoryPieceId: 'piece-id' }, remainingLine,
      ] };
    const current = vi.fn().mockReturnValueOnce(of(staleDraft))
      .mockReturnValue(of({ ...staleDraft, lines: [remainingLine] }));
    TestBed.configureTestingModule({ providers: [
      provideRouter([]),
      { provide: SessionClient, useValue: { current: () => of(session) } },
      { provide: CatalogClient, useValue: {
        priceLists: () => of([{ id: 'list-id', name: 'Mostrador' }]),
        categories: () => of([{ id: 'category-id', name: 'Carnes', productCount: 1 }]),
        products: () => of({ items: [{ id: 'new-id', code: '1003', name: 'Nuevo corte',
          categoryId: 'category-id', unit: 'kg', saleMode: 'weight', price: 800,
          availableStock: 10 }], page: 1, pageSize: 50, totalItems: 1 }),
      } },
      { provide: SaleDraftClient, useValue: { list: () => of([]), current,
        save: () => throwError(() => new HttpErrorResponse({ status: 409,
          error: { error: { code: 'PIECE_RELEASED_BY_ADMIN' } } })),
      } },
      { provide: InventoryClient, useValue: { stock: () => of([{ productId: 'new-id', available: 10 }]) } },
    ] });
    const fixture = TestBed.createComponent(PosPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.ticket-lines').textContent).toContain('Pieza liberada');

    (fixture.nativeElement.querySelector('.product-card') as HTMLButtonElement).click();
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('.pos-dialog .finish-button') as HTMLButtonElement).click();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();

    const ticket = fixture.nativeElement.querySelector('.ticket-lines') as HTMLElement;
    expect(current).toHaveBeenCalledTimes(2);
    expect(ticket.textContent).not.toContain('Pieza liberada');
    expect(ticket.textContent).toContain('Otro corte');
    expect(ticket.textContent).not.toContain('Nuevo corte');
    expect(fixture.nativeElement.querySelector('.pos-error[role="alert"]').textContent)
      .toContain('Administración liberó una pieza');
  });

  it('does not increase a saved line beyond the available stock plus its own reservation', async () => {
    const session: CurrentSession = {
      userId: 'user-id', username: 'cajero', expiresAtUtc: '2026-10-06T20:00:00Z',
      context: { userId: 'user-id', companyId: 'company-id', companyName: 'Empresa',
        branchId: 'branch-id', branchName: 'Sucursal', permissions: [], sessionId: 'session-id' },
    };
    const draft = { id: 'draft-id', priceListId: 'list-id', updatedAtUtc: '2026-10-06T16:00:00Z',
      lines: [{ productId: 'product-id', productCode: '1001', productName: 'Asado', unit: 'kg',
        saleMode: 'weight', quantity: 1, unitPrice: 1000 }] };
    let savedQuantity = 1;
    TestBed.configureTestingModule({ providers: [
      provideRouter([]),
      { provide: SessionClient, useValue: { current: () => of(session) } },
      { provide: CatalogClient, useValue: {
        priceLists: () => of([{ id: 'list-id', name: 'Mostrador' }]),
        categories: () => of([]),
      } },
      { provide: SaleDraftClient, useValue: {
        list: () => of([]),
        current: () => of(draft),
        save: (_: string, lines: readonly { quantity: number }[]) => {
          savedQuantity = lines[0].quantity;
          return of({ ...draft, lines: [{ ...draft.lines[0], quantity: savedQuantity }] });
        },
      } },
      { provide: InventoryClient, useValue: { stock: () => of([{
        productId: 'product-id', code: '1001', name: 'Asado', onHand: 3, reserved: 1, available: 2,
      }]) } },
    ] });
    const fixture = TestBed.createComponent(PosPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const input = fixture.nativeElement.querySelector('.quantity-input') as HTMLInputElement;

    input.value = '4';
    input.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();
    expect(input.value).toBe('1');
    expect(savedQuantity).toBe(1);
    expect(fixture.nativeElement.querySelector('.pos-error[role="alert"]').textContent).toContain('Stock insuficiente');

    input.value = '3';
    input.dispatchEvent(new Event('change', { bubbles: true }));
    await fixture.whenStable();
    fixture.detectChanges();
    expect(savedQuantity).toBe(3);
    expect(fixture.nativeElement.querySelector('.pos-error')).toBeNull();
  });

  it('keeps checkout blocked until the latest ticket change is saved', async () => {
    const session: CurrentSession = {
      userId: 'user-id', username: 'cajero', expiresAtUtc: '2026-10-06T20:00:00Z',
      context: { userId: 'user-id', companyId: 'company-id', companyName: 'Empresa',
        branchId: 'branch-id', branchName: 'Sucursal', permissions: [], sessionId: 'session-id' },
    };
    const saves: Subject<unknown>[] = [];
    TestBed.configureTestingModule({ providers: [
      provideRouter([]),
      { provide: SessionClient, useValue: { current: () => of(session) } },
      { provide: CatalogClient, useValue: {
        priceLists: () => of([{ id: 'list-id', name: 'Mostrador' }]),
        categories: () => of([{ id: 'category-id', name: 'Vacuno', productCount: 1 }]),
        products: () => of({ items: [{ id: 'product-id', code: '1001', name: 'Bife',
          categoryId: 'category-id', saleMode: 'unit', price: 1000, availableStock: 10 }],
          page: 1, pageSize: 50, totalItems: 1 }),
      } },
      { provide: SaleDraftClient, useValue: {
        list: () => of([]),
        current: () => of(null),
        save: () => { const response = new Subject<unknown>(); saves.push(response); return response; },
        cancel: () => of(null),
      } },
      { provide: CashierShiftClient, useValue: { current: () => of({
        id: 'shift-id', openingCash: 0, openedAtUtc: '2026-10-06T15:00:00Z', closedAtUtc: null,
      }) } },
      { provide: InventoryClient, useValue: { stock: () => of([{ productId: 'product-id', available: 10 }]),
        adjust: () => of(null) } },
    ] });
    const fixture = TestBed.createComponent(PosPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('.product-card') as HTMLButtonElement).click();
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('.pos-dialog .finish-button') as HTMLButtonElement).click();
    fixture.detectChanges();
    const quantity = fixture.nativeElement.querySelector('.quantity-input') as HTMLInputElement;
    quantity.value = '2';
    quantity.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();
    saves[0].next({ id: 'draft-id', priceListId: 'list-id', updatedAtUtc: '2026-10-06T16:00:00Z',
      lines: [{ productId: 'product-id', productCode: '1001', productName: 'Bife', unit: 'unidad',
        saleMode: 'unit', quantity: 1, unitPrice: 1000 }] });
    saves[0].complete();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.price-list-lock').textContent).toContain('Guardando ticket');
    (fixture.nativeElement.querySelector('.sale-footer .finish-button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#checkout-title')).toBeNull();
    expect(fixture.nativeElement.querySelector('.pos-error').textContent).toContain('termine de guardarse');
    saves[1].next({ id: 'draft-id', priceListId: 'list-id', updatedAtUtc: '2026-10-06T16:00:01Z',
      lines: [{ productId: 'product-id', productCode: '1001', productName: 'Bife', unit: 'unidad',
        saleMode: 'unit', quantity: 2, unitPrice: 1000 }] });
    saves[1].complete();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.price-list-lock').textContent).toContain('Borrador guardado');
  });

  it('returns to login after closing and preserves the cash summary for a fresh login', async () => {
    const session: CurrentSession = {
      userId: 'user-id', username: 'cajero', expiresAtUtc: '2026-10-06T20:00:00Z',
      context: { userId: 'user-id', companyId: 'company-id', companyName: 'Empresa',
        branchId: 'branch-id', branchName: 'Sucursal', permissions: [], sessionId: 'session-id' },
    };
    const openShift = { id: 'shift-id', openingCash: 100, cashSales: 1000,
      nonCashSales: 500, accountSales: 0, creditApplied: 0,
      cashCollections: 0, nonCashCollections: 0, salesTotal: 1500, cashBalance: 1100,
      paymentBreakdown: [{ method: 'cash', amount: 1000 }, { method: 'debit', amount: 500 }],
      openedAtUtc: '2026-10-06T15:00:00Z', closedAtUtc: null };
    const closedShift = { ...openShift, cashierName: 'cajero-anterior',
      closedAtUtc: '2026-10-06T17:00:00Z', report: {
        shiftId: 'shift-id', companyName: 'Empresa', branchName: 'Sucursal', terminalName: 'Caja A',
        cashierName: 'cajero-anterior', openedAtUtc: '2026-10-06T15:00:00Z',
        closedAtUtc: '2026-10-06T17:00:00Z', openingCash: 100, salesTotal: 1500,
        cashSales: 1000, nonCashSales: 500, cashCollections: 0, nonCashCollections: 0,
        cashWithdrawals: 0, expectedCash: 1100, countedCash: 1090, handedCash: 1090,
        cashDifference: -10,
      } };
    let isOpen = true;
    let reopenedCash: number | null = null;
    let closingAmounts: [number, number] | null = null;
    TestBed.configureTestingModule({ providers: [
      provideRouter([]),
      { provide: SessionClient, useValue: { current: () => of(session) } },
      { provide: CatalogClient, useValue: { priceLists: () => of([]) } },
      { provide: SaleDraftClient, useValue: { list: () => of([]), current: () => of(null) } },
      { provide: CashierShiftClient, useValue: {
        current: () => of(isOpen ? openShift : null),
        lastClosed: () => of(isOpen ? null : closedShift),
        close: (countedCash: number, handedCash: number) => {
          closingAmounts = [countedCash, handedCash];
          isOpen = false; return of(closedShift);
        },
        open: (openingCash: number) => {
          reopenedCash = openingCash;
          isOpen = true;
          return of({ ...openShift, openingCash });
        },
      } },
      { provide: InventoryClient, useValue: { stock: () => of([]) } },
    ] });
    const fixture = TestBed.createComponent(PosPage);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    fixture.detectChanges();
    await fixture.whenStable();
    (fixture.nativeElement.querySelector('.shift-status') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.shift-summary').textContent).toContain('1.100,00');
    expect(fixture.nativeElement.querySelector('.shift-summary').textContent).toContain('Débito');
    expect(fixture.nativeElement.querySelector('.shift-summary').textContent).toContain('500,00');
    for (const [id, value] of [['shift-counted-cash', '1090'], ['shift-handed-cash', '1090']]) {
      const input = fixture.nativeElement.querySelector(`#${id}`) as HTMLInputElement;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    }
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('#close-shift-button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(closingAmounts).toEqual([1090, 1090]);
    expect(fixture.nativeElement.querySelector('.closed-shift-summary').textContent).toContain('Diferencia-$');
    expect(navigate).not.toHaveBeenCalledWith('/');
    (fixture.nativeElement.querySelector('.shift-dialog .finish-button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(navigate).toHaveBeenCalledWith('/');
    expect(fixture.nativeElement.querySelector('.shift-dialog')).toBeNull();

    fixture.destroy();
    const reopened = TestBed.createComponent(PosPage);
    reopened.detectChanges();
    await reopened.whenStable();
    (reopened.nativeElement.querySelector('.shift-status') as HTMLButtonElement).click();
    reopened.detectChanges();
    const previous = reopened.nativeElement.querySelector('.closed-shift-summary').textContent;
    expect(previous).toContain('1.100,00');
    expect(previous).toContain('cajero-anterior');
    expect(previous).toContain('06/10/2026');
    const opening = reopened.nativeElement.querySelector('#opening-cash') as HTMLInputElement;
    opening.value = '44';
    opening.dispatchEvent(new Event('input'));
    reopened.detectChanges();
    (reopened.nativeElement.querySelector('.shift-dialog .finish-button') as HTMLButtonElement).click();
    expect(reopenedCash).toBe(44);
  });

  it('keeps a pending withdrawal visible and reuses its operation id after a network failure', async () => {
    const session: CurrentSession = {
      userId: 'cashier-id', username: 'cajero', expiresAtUtc: '2026-11-04T22:00:00Z',
      context: { userId: 'cashier-id', companyId: 'company-id', companyName: 'Empresa',
        branchId: 'branch-id', branchName: 'Sucursal', permissions: [], sessionId: 'session-id' },
    };
    const shift = { id: 'shift-id', openingCash: 1000, cashSales: 0, nonCashSales: 0,
      accountSales: 0, creditApplied: 0, cashCollections: 0, nonCashCollections: 0,
      salesTotal: 0, cashBalance: 900, cashWithdrawals: 100,
      openedAtUtc: '2026-11-04T18:00:00Z', closedAtUtc: null, paymentBreakdown: [] };
    const pending = { id: 'withdrawal-id', operationId: 'old-operation', cashierShiftId: 'shift-id',
      method: 'cash' as const, amount: 100, reason: 'Rendición', status: 'pending' as const,
      requestedByUserId: 'cashier-id', requesterName: 'cajero',
      requestedAtUtc: '2026-11-04T18:30:00Z', decidedByUserId: null,
      decisionByName: null, decidedAtUtc: null, decisionReason: null, ledgerMovementId: null };
    const request = vi.fn().mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 0 })))
      .mockReturnValueOnce(of({ ...pending, id: 'new-withdrawal' }));
    TestBed.configureTestingModule({ providers: [
      provideRouter([]),
      { provide: SessionClient, useValue: { current: () => of(session) } },
      { provide: CatalogClient, useValue: { priceLists: () => of([]) } },
      { provide: SaleDraftClient, useValue: { list: () => of([]), current: () => of(null) } },
      { provide: CashierShiftClient, useValue: { current: () => of(shift), lastClosed: () => of(null) } },
      { provide: CashWithdrawalClient, useValue: { current: () => of([pending]), request } },
      { provide: InventoryClient, useValue: { stock: () => of([]) } },
    ] });
    const fixture = TestBed.createComponent(PosPage);
    fixture.detectChanges();
    await fixture.whenStable();
    (fixture.nativeElement.querySelector('.shift-status') as HTMLButtonElement).click();
    fixture.detectChanges();
    const dialog = fixture.nativeElement.querySelector('.shift-dialog') as HTMLElement;
    expect(dialog.textContent).toContain('Rendición');
    expect(dialog.textContent).toContain('100,00');
    expect(dialog.querySelector<HTMLButtonElement>('#close-shift-button')!.disabled).toBe(true);
    const amount = dialog.querySelector<HTMLInputElement>('#withdrawal-amount')!;
    amount.value = '10,50';
    amount.dispatchEvent(new Event('input', { bubbles: true }));
    const reason = dialog.querySelector<HTMLInputElement>('#withdrawal-reason')!;
    reason.value = 'Nuevo retiro';
    reason.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    const send = [...dialog.querySelectorAll<HTMLButtonElement>('.shift-withdrawals .finish-button')][0];
    send.click();
    fixture.detectChanges();
    expect(dialog.textContent).toContain('La solicitud puede haberse guardado');
    send.click();
    fixture.detectChanges();
    expect(request).toHaveBeenCalledTimes(2);
    expect(request.mock.calls[0][0]).toBe(request.mock.calls[1][0]);
    expect(request.mock.calls[0].slice(1)).toEqual([10.5, 'Nuevo retiro']);
  });

  it('prefills kilograms from a received piece barcode before adding the product', async () => {
    const session: CurrentSession = {
      userId: 'user-id', username: 'cajero1', expiresAtUtc: '2026-10-08T20:00:00Z',
      context: { userId: 'user-id', companyId: 'company-id', companyName: 'Empresa',
        branchId: 'branch-id', branchName: 'Sucursal', permissions: [], sessionId: 'session-id' },
    };
    const asado = { id: 'product-id', code: '1002', name: 'Asado', categoryId: 'category-id',
      saleMode: 'weight' as const, unit: 'kg', price: 11500, availableStock: 79.5 };
    const save = vi.fn().mockImplementation((_listId, lines: readonly { id: string; productId: string; quantity: number; inventoryPieceId?: string }[]) =>
      of({ id: 'draft-id', priceListId: 'list-id', updatedAtUtc: '2026-10-08T16:00:00Z',
        lines: lines.map(line => ({ ...line, productCode: asado.code, productName: asado.name,
          unit: 'kg', saleMode: 'weight', unitPrice: asado.price,
          pieceIdentifier: line.inventoryPieceId === 'piece-id' ? '999001' : '999002' })) }));
    TestBed.configureTestingModule({ providers: [
      provideRouter([]),
      { provide: SessionClient, useValue: { current: () => of(session) } },
      { provide: CatalogClient, useValue: {
        priceLists: () => of([{ id: 'list-id', name: 'Mostrador' }]),
        categories: () => of([{ id: 'category-id', name: 'Carnes', productCount: 1 }]),
        products: (query: { code?: string }) => {
          const items = !query.code || query.code === asado.code ? [asado] : [];
          return of({ items, page: 1, pageSize: 50, totalItems: items.length });
        },
      } },
       { provide: InventoryPieceClient, useValue: { lookup: (code: string) => code === '2999003009000'
         ? throwError(() => new HttpErrorResponse({ status: 409,
           error: { error: { code: 'PIECE_RESERVED' } } }))
         : code === '2999001005009' || code === '2999002007507'
           ? of({ id: code === '2999001005009' ? 'piece-id' : 'piece-id-2', productId: asado.id, productCode: asado.code,
             externalIdentifier: code === '2999001005009' ? '999001' : '999002',
             receivedWeightKg: code === '2999001005009' ? 0.5 : 0.75, rawBarcode: code })
           : throwError(() => new HttpErrorResponse({ status: 404 })) } },
      { provide: SaleDraftClient, useValue: { list: () => of([]), current: () => of(null), save } },
      { provide: InventoryClient, useValue: { stock: () => of([{ productId: asado.id, available: 79.5 }]) } },
    ] });
    const fixture = TestBed.createComponent(PosPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const search = fixture.nativeElement.querySelector('#product-search') as HTMLInputElement;
    search.value = '2999001005009';
    search.dispatchEvent(new Event('input', { bubbles: true }));
    search.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('#product-dialog-title').textContent).toContain('Asado');
    const measuredQuantity = fixture.nativeElement.querySelector('#product-quantity') as HTMLInputElement;
    expect(measuredQuantity.value).toBe('0,500');
    expect(measuredQuantity.readOnly).toBe(true);
    (fixture.nativeElement.querySelector('.pos-dialog .finish-button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(save).toHaveBeenCalledWith('list-id', [expect.objectContaining({ productId: asado.id, quantity: 0.5, inventoryPieceId: 'piece-id' })], 'A', 0, null);
    expect((fixture.nativeElement.querySelector('.quantity-input') as HTMLInputElement).value).toBe('0,500');
    expect(fixture.nativeElement.querySelector('.ticket-lines').textContent).toContain('5.750,00');

    search.value = '2999002007507';
    search.dispatchEvent(new Event('input', { bubbles: true }));
    search.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('.pos-dialog .finish-button') as HTMLButtonElement).click();
    fixture.detectChanges();
    const ticketRows = fixture.nativeElement.querySelectorAll('.ticket-lines .ticket-line');
    expect(ticketRows).toHaveLength(2);
    expect(ticketRows[0].textContent).toContain('999001');
    expect(ticketRows[1].textContent).toContain('999002');
    expect((ticketRows[0].querySelector('.quantity-input') as HTMLInputElement).value).toBe('0,500');
    expect((ticketRows[1].querySelector('.quantity-input') as HTMLInputElement).value).toBe('0,750');
    expect(save).toHaveBeenLastCalledWith('list-id', [
      expect.objectContaining({ productId: asado.id, quantity: 0.5, inventoryPieceId: 'piece-id' }),
      expect.objectContaining({ productId: asado.id, quantity: 0.75, inventoryPieceId: 'piece-id-2' }),
    ], 'A', 0, null);

    search.value = '2999001005009';
    search.dispatchEvent(new Event('input', { bubbles: true }));
    search.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('.pos-dialog .finish-button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('.ticket-lines .ticket-line')).toHaveLength(2);
    expect(fixture.nativeElement.querySelector('.pos-error').textContent).toContain('ya está en el ticket');
    (fixture.nativeElement.querySelector('.pos-dialog .dialog-close') as HTMLButtonElement).click();

    search.value = '2999001005999';
    search.dispatchEvent(new Event('input', { bubbles: true }));
    search.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#product-dialog-title')).toBeNull();
    expect(save).toHaveBeenCalledTimes(2);
    expect(fixture.nativeElement.querySelector('.pos-error').textContent)
      .toContain('no corresponde a un artículo ni a una pieza');

    search.value = '2999003009000';
    search.dispatchEvent(new Event('input', { bubbles: true }));
    search.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.pos-error').textContent)
      .toContain('reservada en un ticket');
    expect(save).toHaveBeenCalledTimes(2);
  });

  it.each([
    ['Rejected', 'Rechazado'],
    ['Canceled', 'Cancelado'],
  ])('allows another Mercado Pago order after %s and labels it %s', async (status, label) => {
    const session: CurrentSession = {
      userId: 'user-id', username: 'cajero', expiresAtUtc: '2026-10-09T20:00:00Z',
      context: { userId: 'user-id', companyId: 'company-id', companyName: 'Empresa',
        branchId: 'branch-id', branchName: 'Sucursal', permissions: [], sessionId: 'session-id' },
    };
    const paymentMode = status === 'Canceled' ? 'qr' : 'point';
    const pending = { id: 'intent-id', mode: paymentMode, amount: 2500, status: 'Pending',
      providerOrderId: 'order-id', qrData: paymentMode === 'qr' ? 'https://mpago.la/test' : null,
      approved: false, createdAtUtc: '2026-10-09T16:00:00Z' };
    const start = vi.fn().mockReturnValue(of(pending));
    const check = vi.fn().mockReturnValue(of({ ...pending, status }));
    TestBed.configureTestingModule({ providers: [
      provideRouter([]),
      { provide: SessionClient, useValue: { current: () => of(session) } },
      { provide: CatalogClient, useValue: { priceLists: () => of([{ id: 'list-id', name: 'Mostrador' }]),
        categories: () => of([]) } },
      { provide: SaleDraftClient, useValue: { list: () => of([]), current: () => of({
        id: 'draft-id', ticketSlot: 'A', priceListId: 'list-id', updatedAtUtc: '2026-10-09T15:00:00Z',
        lines: [{ id: 'line-id', productId: 'product-id', productCode: '1001', productName: 'Asado',
          unit: 'kg', saleMode: 'weight', quantity: 1, unitPrice: 2500 }],
      }) } },
      { provide: MercadoPagoClient, useValue: { get: () => of(null), start,
        executeCheckout: start, check,
        cancel: () => NEVER } },
      { provide: InventoryClient, useValue: { stock: () => of([]) } },
    ] });
    const fixture = TestBed.createComponent(PosPage);
    fixture.detectChanges();
    await fixture.whenStable();
    (fixture.nativeElement.querySelector('.sale-footer .finish-button') as HTMLButtonElement).click();
    fixture.detectChanges();
    continueAsFinalConsumer(fixture);
    const options = fixture.nativeElement.querySelectorAll('.payment-option input[type="checkbox"]') as NodeListOf<HTMLInputElement>;
    options[0].click();
    options[4].click();
    fixture.detectChanges();
    const mode = fixture.nativeElement.querySelector('#mercado-pago-mode') as HTMLSelectElement;
    mode.value = paymentMode;
    mode.dispatchEvent(new Event('change', { bubbles: true }));
    const request = fixture.nativeElement.querySelector('.mercado-pago-actions button') as HTMLButtonElement;
    request.click();
    await fixture.whenStable();
    fixture.detectChanges();
    if (paymentMode === 'qr') await vi.waitFor(() => {
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('.mercado-pago-qr')).not.toBeNull();
    });
    expect(request.disabled).toBe(true);
    (fixture.nativeElement.querySelectorAll('.mercado-pago-actions button')[1] as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.mercado-pago-state').textContent).toContain(label);
    if (paymentMode === 'qr') expect(fixture.nativeElement.querySelector('.mercado-pago-qr')).toBeNull();
    expect(request.disabled).toBe(false);
    expect(mode.disabled).toBe(false);
    expect(start).toHaveBeenCalledWith('draft-id', 'checkout-id', expect.any(String), paymentMode, 2500);
    expect(check).toHaveBeenCalledWith('draft-id', 'intent-id');
  });

  it('reconciles a pending Mercado Pago payment automatically while checkout is open', async () => {
    const session: CurrentSession = {
      userId: 'user-id', username: 'cajero', expiresAtUtc: '2026-10-09T20:00:00Z',
      context: { userId: 'user-id', companyId: 'company-id', companyName: 'Empresa',
        branchId: 'branch-id', branchName: 'Sucursal', permissions: [], sessionId: 'session-id' },
    };
    const pending = { id: 'intent-id', mode: 'qr' as const, amount: 2500, status: 'Pending' as const,
      providerOrderId: 'order-id', qrData: 'https://mpago.la/test', approved: false,
      createdAtUtc: '2026-10-09T16:00:00Z' };
    const check = vi.fn().mockReturnValueOnce(of(pending))
      .mockReturnValue(of({ ...pending, status: 'Approved', approved: true }));
    TestBed.configureTestingModule({ providers: [
      provideRouter([]),
      { provide: SessionClient, useValue: { current: () => of(session) } },
      { provide: CatalogClient, useValue: { priceLists: () => of([{ id: 'list-id', name: 'Mostrador' }]),
        categories: () => of([]) } },
      { provide: SaleDraftClient, useValue: { list: () => of([]), current: () => of({
        id: 'draft-id', ticketSlot: 'A', priceListId: 'list-id', updatedAtUtc: '2026-10-09T15:00:00Z',
        lines: [{ id: 'line-id', productId: 'product-id', productCode: '1001', productName: 'Asado',
          unit: 'kg', saleMode: 'weight', quantity: 1, unitPrice: 2500 }],
      }) } },
      { provide: MercadoPagoClient, useValue: { get: () => of(pending), start: () => NEVER, check,
        cancel: () => NEVER } },
      { provide: InventoryClient, useValue: { stock: () => of([]) } },
    ] });
    const fixture = TestBed.createComponent(PosPage);
    fixture.detectChanges();
    await fixture.whenStable();
    vi.useFakeTimers();
    try {
      (fixture.nativeElement.querySelector('.sale-footer .finish-button') as HTMLButtonElement).click();
      fixture.detectChanges();
      continueAsFinalConsumer(fixture);
      expect(fixture.nativeElement.querySelector('.mercado-pago-state').textContent).toContain('Pendiente');
      await vi.advanceTimersByTimeAsync(5000);
      fixture.detectChanges();
      expect(check).toHaveBeenCalledExactlyOnceWith('draft-id', 'intent-id');
      expect(fixture.nativeElement.querySelector('.mercado-pago-state').textContent).toContain('Pendiente');
      await vi.advanceTimersByTimeAsync(5000);
      fixture.detectChanges();
      expect(check).toHaveBeenCalledTimes(2);
      expect(fixture.nativeElement.querySelector('.mercado-pago-state').textContent).toContain('Acreditado');
      await vi.advanceTimersByTimeAsync(15000);
      expect(check).toHaveBeenCalledTimes(2);
    } finally {
      fixture.destroy();
      vi.useRealTimers();
    }
  });

  it('stops checking Mercado Pago when the cashier closes checkout', async () => {
    const session: CurrentSession = {
      userId: 'user-id', username: 'cajero', expiresAtUtc: '2026-10-09T20:00:00Z',
      context: { userId: 'user-id', companyId: 'company-id', companyName: 'Empresa',
        branchId: 'branch-id', branchName: 'Sucursal', permissions: [], sessionId: 'session-id' },
    };
    const pending = { id: 'intent-id', mode: 'qr' as const, amount: 2500, status: 'Pending' as const,
      providerOrderId: 'order-id', qrData: 'https://mpago.la/test', approved: false,
      createdAtUtc: '2026-10-09T16:00:00Z' };
    const check = vi.fn().mockReturnValue(of(pending));
    TestBed.configureTestingModule({ providers: [
      provideRouter([]),
      { provide: SessionClient, useValue: { current: () => of(session) } },
      { provide: CatalogClient, useValue: { priceLists: () => of([{ id: 'list-id', name: 'Mostrador' }]),
        categories: () => of([]) } },
      { provide: SaleDraftClient, useValue: { list: () => of([]), current: () => of({
        id: 'draft-id', ticketSlot: 'A', priceListId: 'list-id', updatedAtUtc: '2026-10-09T15:00:00Z',
        lines: [{ id: 'line-id', productId: 'product-id', productCode: '1001', productName: 'Asado',
          unit: 'kg', saleMode: 'weight', quantity: 1, unitPrice: 2500 }],
      }) } },
      { provide: MercadoPagoClient, useValue: { get: () => of(pending), start: () => NEVER, check,
        cancel: () => NEVER } },
      { provide: InventoryClient, useValue: { stock: () => of([]) } },
    ] });
    const fixture = TestBed.createComponent(PosPage);
    fixture.detectChanges();
    await fixture.whenStable();
    vi.useFakeTimers();
    try {
      (fixture.nativeElement.querySelector('.sale-footer .finish-button') as HTMLButtonElement).click();
      fixture.detectChanges();
      await vi.advanceTimersByTimeAsync(5000);
      expect(check).toHaveBeenCalledTimes(1);
      (fixture.nativeElement.querySelector('.pos-dialog .dialog-close') as HTMLButtonElement).click();
      await vi.advanceTimersByTimeAsync(15000);
      expect(check).toHaveBeenCalledTimes(1);
    } finally {
      fixture.destroy();
      vi.useRealTimers();
    }
  });

  async function liveCatalogFixture() {
    const session: CurrentSession = {
      userId: 'user-id', username: 'cajero', expiresAtUtc: '2026-10-14T20:00:00Z',
      context: { userId: 'user-id', companyId: 'company-id', companyName: 'Empresa',
        branchId: 'branch-id', branchName: 'Sucursal', permissions: [], sessionId: 'session-id' },
    };
    let lists = [{ id: 'list-id', name: 'Mostrador' }, { id: 'other-list', name: 'Mayorista' }];
    let available = 10;
    let allowSaleWithoutStock = false;
    let hasProtectedPieces = false;
    let stockMissing = false;
    let stockFails = false;
    let listsFail = false;
    let categoriesFail = false;
    let productsFail = false;
    let asadoPrice = 1000;
    let categoryRows = [{ id: 'meat', name: 'Carnes', productCount: 1 }];
    const priceLists = vi.fn(() => listsFail
      ? throwError(() => new HttpErrorResponse({ status: 503 })) : of(lists));
    const categories = vi.fn(() => categoriesFail
      ? throwError(() => new HttpErrorResponse({ status: 503 })) : of(categoryRows));
    const products = vi.fn((filters: { categoryId?: string; code?: string }) => {
      if (productsFail) return throwError(() => new HttpErrorResponse({ status: 503 }));
      const items = filters.categoryId === 'pork' || filters.code === '2001'
        ? [{ id: 'bondiola', code: '2001', name: 'Bondiola', categoryId: 'pork',
          saleMode: 'unit', unit: 'unidad', price: 1200, availableStock: 10 }]
        : [{ id: 'asado', code: '1002', name: 'Asado', categoryId: 'meat',
          saleMode: 'unit', unit: 'unidad', price: asadoPrice, availableStock: 10 }];
      return of({ items, page: 1, pageSize: 50, totalItems: items.length });
    });
    const stock = vi.fn(() => stockFails
      ? throwError(() => new HttpErrorResponse({ status: 503 }))
      : of(stockMissing ? [] : [{ productId: 'asado', available,
        genericAvailable: available, protectedAvailable: hasProtectedPieces ? 2 : 0,
        allowSaleWithoutStock, hasProtectedPieces }]));
    const current = vi.fn(() => of(null));
    const save = vi.fn(() => NEVER);
    TestBed.configureTestingModule({ providers: [
      provideRouter([]),
      { provide: SessionClient, useValue: { current: () => of(session) } },
      { provide: CatalogClient, useValue: { priceLists,
        categories, products } },
      { provide: SaleDraftClient, useValue: { list: () => of([]), current, save } },
      { provide: InventoryClient, useValue: { stock } },
    ] });
    const fixture = TestBed.createComponent(PosPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, priceLists, categories, products, stock, current, save,
      setLists: (value: typeof lists) => { lists = value; },
      setCategories: (value: typeof categoryRows) => { categoryRows = value; },
      failCategories: (value: boolean) => { categoriesFail = value; },
      setAsadoPrice: (value: number) => { asadoPrice = value; },
      failProducts: (value: boolean) => { productsFail = value; },
      setAvailable: (value: number) => { available = value; stockMissing = false; },
      setStockPolicy: (allowed: boolean, protectedPieces = false) => {
        allowSaleWithoutStock = allowed; hasProtectedPieces = protectedPieces;
      },
      omitStock: () => { stockMissing = true; },
      failStock: () => { stockFails = true; },
      failLists: () => { listsFail = true; } };
  }

  it('provides both visual identities without duplicating the ticket switcher', async () => {
    const { fixture } = await liveCatalogFixture();
    const page = fixture.nativeElement as HTMLElement;
    expect(page.querySelector('.brand-original')?.textContent).toContain('Carnicerías');
    expect(page.querySelector('.brand-nocturno')?.textContent).toContain('NOCTURNA');
    expect(page.querySelectorAll('.ticket-strip')).toHaveLength(1);
    expect(page.querySelectorAll('.ticket-strip .ticket-tab')).toHaveLength(4);
  });

  it('keeps the Original stock amount and a separate discount column in the ticket', async () => {
    const { fixture } = await liveCatalogFixture();
    const page = fixture.nativeElement as HTMLElement;
    expect(page.querySelector('.product-stock')?.textContent).toContain('Disponible: 10 unidad');

    page.querySelector<HTMLButtonElement>('.product-card')!.click();
    fixture.detectChanges();
    page.querySelector<HTMLButtonElement>('.pos-dialog .finish-button')!.click();
    fixture.detectChanges();
    expect(page.querySelector('.ticket-line-discount-heading')?.textContent).toContain('Desc.');
    expect(page.querySelector('.ticket-line-discount')?.textContent).toContain('0,00%');
  });

  it('preserves the active ticket when the theme identifier changes', async () => {
    const { fixture } = await liveCatalogFixture();
    const page = fixture.nativeElement as HTMLElement;
    page.querySelector<HTMLButtonElement>('.product-card')!.click();
    fixture.detectChanges();
    page.querySelector<HTMLButtonElement>('.pos-dialog .finish-button')!.click();
    fixture.detectChanges();
    const originalTotal = page.querySelector('.total-field strong')?.textContent;
    try {
      document.documentElement.dataset['visualTheme'] = 'nocturno';
      fixture.detectChanges();
      expect(page.querySelectorAll('.ticket-line')).toHaveLength(1);
      expect(page.querySelector('.total-field strong')?.textContent).toBe(originalTotal);
      expect(page.querySelectorAll('.ticket-strip')).toHaveLength(1);
    } finally {
      delete document.documentElement.dataset['visualTheme'];
    }
  });

  it('refreshes stock and price lists every five seconds, on focus and on reconnect, then stops on destroy', async () => {
    vi.useFakeTimers();
    let fixture: { destroy(): void } | undefined;
    try {
      const setup = await liveCatalogFixture();
      fixture = setup.fixture;
      const { priceLists, products, stock, current } = setup;
      expect(priceLists).toHaveBeenCalledTimes(1);
      expect(products).toHaveBeenCalledTimes(1);
      expect(stock).toHaveBeenCalledTimes(1);
      await vi.advanceTimersByTimeAsync(5000);
      expect(priceLists).toHaveBeenCalledTimes(2);
      expect(stock).toHaveBeenCalledTimes(2);
      window.dispatchEvent(new Event('focus'));
      window.dispatchEvent(new Event('online'));
      expect(priceLists).toHaveBeenCalledTimes(4);
      expect(stock).toHaveBeenCalledTimes(4);
      expect(current).toHaveBeenCalledTimes(1);
      fixture.destroy();
      await vi.advanceTimersByTimeAsync(10000);
      expect(priceLists).toHaveBeenCalledTimes(4);
      expect(stock).toHaveBeenCalledTimes(4);
      expect(products).toHaveBeenCalledTimes(1);
    } finally {
      fixture?.destroy();
      vi.useRealTimers();
    }
  });

  it('refreshes renamed and enabled categories without resetting the ticket or selected product', async () => {
    vi.useFakeTimers();
    let fixture: { destroy(): void } | undefined;
    try {
      const setup = await liveCatalogFixture();
      fixture = setup.fixture;
      const page = setup.fixture.nativeElement as HTMLElement;
      page.querySelector<HTMLButtonElement>('.product-card')!.click();
      setup.fixture.detectChanges();
      page.querySelector<HTMLButtonElement>('.pos-dialog .finish-button')!.click();
      setup.fixture.detectChanges();
      page.querySelector<HTMLButtonElement>('.product-card')!.click();
      setup.fixture.detectChanges();
      expect(page.querySelector('#product-dialog-title')?.textContent).toContain('Asado');
      setup.setCategories([
        { id: 'meat', name: 'Vacuno', productCount: 1 },
        { id: 'pork', name: 'Porcino', productCount: 1 },
      ]);

      await vi.advanceTimersByTimeAsync(10000);
      expect(setup.categories).toHaveBeenCalledTimes(1);
      await vi.advanceTimersByTimeAsync(5000);
      setup.fixture.detectChanges();
      expect(setup.categories).toHaveBeenCalledTimes(2);
      expect(page.querySelector('.category-rail')?.textContent).toContain('Vacuno');
      expect(page.querySelector('.category-rail')?.textContent).toContain('Porcino');
      expect(page.querySelector('.category-button.active')?.textContent).toContain('Vacuno');
      expect(page.querySelector('.ticket-lines')?.textContent).toContain('Asado');
      expect(page.querySelector('#product-dialog-title')?.textContent).toContain('Asado');
      expect(setup.products).toHaveBeenCalledTimes(2);
      await vi.advanceTimersByTimeAsync(5000);
      expect(setup.categories).toHaveBeenCalledTimes(2);
    } finally {
      fixture?.destroy();
      vi.useRealTimers();
    }
  });

  it('removes a disabled active category and shows another while preserving an open product', async () => {
    vi.useFakeTimers();
    let fixture: { destroy(): void } | undefined;
    try {
      const setup = await liveCatalogFixture();
      fixture = setup.fixture;
      const page = setup.fixture.nativeElement as HTMLElement;
      page.querySelector<HTMLButtonElement>('.product-card')!.click();
      setup.fixture.detectChanges();
      setup.setCategories([{ id: 'pork', name: 'Porcino', productCount: 1 }]);
      await vi.advanceTimersByTimeAsync(15000);
      setup.fixture.detectChanges();
      expect(page.querySelector('.category-rail')?.textContent).not.toContain('Carnes');
      expect(page.querySelector('.category-button.active')?.textContent).toContain('Porcino');
      expect(page.querySelector('#product-dialog-title')?.textContent).toContain('Asado');
      expect(page.querySelector('.product-grid')?.textContent).toContain('Bondiola');
      setup.setCategories([
        { id: 'meat', name: 'Carnes', productCount: 1 },
        { id: 'pork', name: 'Porcino', productCount: 1 },
      ]);
      await vi.advanceTimersByTimeAsync(15000);
      setup.fixture.detectChanges();
      expect(page.querySelector('.category-rail')?.textContent).toContain('Carnes');
      expect(page.querySelector('.category-button.active')?.textContent).toContain('Porcino');
      expect(page.querySelector('#product-dialog-title')?.textContent).toContain('Asado');
    } finally {
      fixture?.destroy();
      vi.useRealTimers();
    }
  });

  it('keeps category navigation during a failed refresh and retries on the next interval', async () => {
    vi.useFakeTimers();
    let fixture: { destroy(): void } | undefined;
    try {
      const setup = await liveCatalogFixture();
      fixture = setup.fixture;
      const page = setup.fixture.nativeElement as HTMLElement;
      setup.failCategories(true);
      await vi.advanceTimersByTimeAsync(15000);
      setup.fixture.detectChanges();
      expect(setup.categories).toHaveBeenCalledTimes(2);
      expect(page.querySelector('.category-rail')?.textContent).toContain('Carnes');
      expect(page.querySelector('.product-grid')?.textContent).toContain('Asado');
      setup.failCategories(false);
      setup.setCategories([{ id: 'meat', name: 'Vacuno', productCount: 1 }]);
      await vi.advanceTimersByTimeAsync(15000);
      setup.fixture.detectChanges();
      expect(setup.categories).toHaveBeenCalledTimes(3);
      expect(page.querySelector('.category-rail')?.textContent).toContain('Vacuno');
      expect(page.querySelector('.product-grid')?.textContent).toContain('Asado');
      expect(setup.products).toHaveBeenCalledTimes(3);
    } finally {
      fixture?.destroy();
      vi.useRealTimers();
    }
  });

  it('refreshes two open POS instances from the same category catalog independently', async () => {
    vi.useFakeTimers();
    let first: { destroy(): void } | undefined;
    let second: { destroy(): void } | undefined;
    try {
      const setup = await liveCatalogFixture();
      first = setup.fixture;
      const otherBox = TestBed.createComponent(PosPage);
      second = otherBox;
      otherBox.detectChanges();
      await otherBox.whenStable();
      expect(setup.categories).toHaveBeenCalledTimes(2);
      setup.setCategories([
        { id: 'meat', name: 'Vacuno', productCount: 1 },
        { id: 'pork', name: 'Porcino', productCount: 1 },
      ]);

      await vi.advanceTimersByTimeAsync(15000);
      setup.fixture.detectChanges();
      otherBox.detectChanges();
      expect(setup.categories).toHaveBeenCalledTimes(4);
      expect((setup.fixture.nativeElement as HTMLElement).querySelector('.category-rail')?.textContent)
        .toContain('Porcino');
      expect((otherBox.nativeElement as HTMLElement).querySelector('.category-rail')?.textContent)
        .toContain('Porcino');
    } finally {
      first?.destroy();
      second?.destroy();
      vi.useRealTimers();
    }
  });

  it('recovers category navigation after a barcode-selected product loses its category', async () => {
    vi.useFakeTimers();
    let fixture: { destroy(): void } | undefined;
    try {
      const setup = await liveCatalogFixture();
      fixture = setup.fixture;
      const page = setup.fixture.nativeElement as HTMLElement;
      const search = page.querySelector<HTMLInputElement>('#product-search')!;
      search.value = '1002';
      search.dispatchEvent(new Event('input'));
      search.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
      setup.fixture.detectChanges();
      expect(page.querySelector('#product-dialog-title')?.textContent).toContain('Asado');
      setup.setCategories([{ id: 'pork', name: 'Porcino', productCount: 1 }]);

      await vi.advanceTimersByTimeAsync(15000);
      setup.fixture.detectChanges();
      expect(page.querySelector('#product-dialog-title')?.textContent).toContain('Asado');
      expect(page.querySelector('.category-button.active')?.textContent).toContain('Porcino');
      expect(page.querySelector('.product-grid')?.textContent).toContain('Bondiola');
    } finally {
      fixture?.destroy();
      vi.useRealTimers();
    }
  });

  it('refreshes visible prices without changing the price or total of an open ticket', async () => {
    vi.useFakeTimers();
    let fixture: { destroy(): void } | undefined;
    try {
      const setup = await liveCatalogFixture();
      fixture = setup.fixture;
      const page = setup.fixture.nativeElement as HTMLElement;
      page.querySelector<HTMLButtonElement>('.product-card')!.click();
      setup.fixture.detectChanges();
      page.querySelector<HTMLButtonElement>('.pos-dialog .finish-button')!.click();
      setup.fixture.detectChanges();
      expect(page.querySelector('.ticket-lines')?.textContent).toContain('1.000');
      setup.setAsadoPrice(1450);

      await vi.advanceTimersByTimeAsync(10000);
      expect(setup.products).toHaveBeenCalledTimes(1);
      await vi.advanceTimersByTimeAsync(5000);
      setup.fixture.detectChanges();
      expect(setup.products).toHaveBeenCalledTimes(2);
      expect(page.querySelector('.product-card .product-price')?.textContent).toContain('1.450');
      expect(page.querySelector('.ticket-lines')?.textContent).toContain('1.000');
      expect(page.querySelector('.ticket-lines')?.textContent).not.toContain('1.450');
      expect(page.querySelector('.sale-footer .total-field')?.textContent).toContain('1.000');
      expect(page.querySelector('.category-button.active')?.textContent).toContain('Carnes');
      expect(page.querySelector('.ticket-tab[aria-current="page"]')?.textContent).toContain('A');
      expect(setup.save).toHaveBeenCalledTimes(1);
      await vi.advanceTimersByTimeAsync(5000);
      expect(setup.products).toHaveBeenCalledTimes(2);
    } finally {
      fixture?.destroy();
      vi.useRealTimers();
    }
  });

  it('updates a selected product before adding it while preserving its entered quantity', async () => {
    vi.useFakeTimers();
    let fixture: { destroy(): void } | undefined;
    try {
      const setup = await liveCatalogFixture();
      fixture = setup.fixture;
      const page = setup.fixture.nativeElement as HTMLElement;
      page.querySelector<HTMLButtonElement>('.product-card')!.click();
      setup.fixture.detectChanges();
      const quantity = page.querySelector<HTMLInputElement>('#product-quantity')!;
      quantity.value = '2';
      quantity.dispatchEvent(new Event('input'));
      setup.setAsadoPrice(1450);

      await vi.advanceTimersByTimeAsync(15000);
      setup.fixture.detectChanges();
      expect(page.querySelector('#product-dialog-title')?.textContent).toContain('Asado');
      expect(page.querySelector('.product-facts')?.textContent).toContain('1.450');
      expect(page.querySelector<HTMLInputElement>('#product-quantity')?.value).toBe('2');
      page.querySelector<HTMLButtonElement>('.pos-dialog .finish-button')!.click();
      setup.fixture.detectChanges();
      expect(page.querySelector('.ticket-lines')?.textContent).toContain('2.900');
    } finally {
      fixture?.destroy();
      vi.useRealTimers();
    }
  });

  it('keeps available prices and selling usable after a failed refresh, then retries', async () => {
    vi.useFakeTimers();
    let fixture: { destroy(): void } | undefined;
    try {
      const setup = await liveCatalogFixture();
      fixture = setup.fixture;
      const page = setup.fixture.nativeElement as HTMLElement;
      setup.failProducts(true);
      await vi.advanceTimersByTimeAsync(15000);
      setup.fixture.detectChanges();
      expect(setup.products).toHaveBeenCalledTimes(2);
      expect(page.querySelector('.product-card .product-price')?.textContent).toContain('1.000');
      expect(page.querySelector('.product-card')).not.toBeNull();
      page.querySelector<HTMLButtonElement>('.product-card')!.click();
      setup.fixture.detectChanges();
      expect(page.querySelector('#product-dialog-title')?.textContent).toContain('Asado');
      page.querySelector<HTMLButtonElement>('.pos-dialog .dialog-close')!.click();
      setup.setAsadoPrice(1450);
      setup.failProducts(false);

      await vi.advanceTimersByTimeAsync(15000);
      setup.fixture.detectChanges();
      expect(setup.products).toHaveBeenCalledTimes(3);
      expect(page.querySelector('.product-card .product-price')?.textContent).toContain('1.450');
    } finally {
      fixture?.destroy();
      vi.useRealTimers();
    }
  });

  it('updates prices in two open POS instances without reloading either ticket view', async () => {
    vi.useFakeTimers();
    let first: { destroy(): void } | undefined;
    let second: { destroy(): void } | undefined;
    try {
      const setup = await liveCatalogFixture();
      first = setup.fixture;
      const otherBox = TestBed.createComponent(PosPage);
      second = otherBox;
      otherBox.detectChanges();
      await otherBox.whenStable();
      expect(setup.products).toHaveBeenCalledTimes(2);
      setup.setAsadoPrice(1450);

      await vi.advanceTimersByTimeAsync(15000);
      setup.fixture.detectChanges();
      otherBox.detectChanges();
      expect(setup.products).toHaveBeenCalledTimes(4);
      for (const box of [setup.fixture, otherBox]) {
        const page = box.nativeElement as HTMLElement;
        expect(page.querySelector('.product-card .product-price')?.textContent).toContain('1.450');
        expect(page.querySelector('.category-button.active')?.textContent).toContain('Carnes');
      }
    } finally {
      first?.destroy();
      second?.destroy();
      vi.useRealTimers();
    }
  });

  it('refreshes the price of a barcode-selected product without closing its dialog', async () => {
    vi.useFakeTimers();
    let fixture: { destroy(): void } | undefined;
    try {
      const setup = await liveCatalogFixture();
      fixture = setup.fixture;
      const page = setup.fixture.nativeElement as HTMLElement;
      const search = page.querySelector<HTMLInputElement>('#product-search')!;
      search.value = '1002';
      search.dispatchEvent(new Event('input'));
      search.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
      setup.fixture.detectChanges();
      expect(page.querySelector('.product-facts')?.textContent).toContain('1.000');
      setup.setAsadoPrice(1450);

      await vi.advanceTimersByTimeAsync(15000);
      setup.fixture.detectChanges();
      expect(setup.products).toHaveBeenLastCalledWith({ priceListId: 'list-id', code: '1002' });
      expect(page.querySelector('#product-dialog-title')?.textContent).toContain('Asado');
      expect(page.querySelector('.product-facts')?.textContent).toContain('1.450');
    } finally {
      fixture?.destroy();
      vi.useRealTimers();
    }
  });

  it('ignores a late price response after the cashier changes category', async () => {
    vi.useFakeTimers();
    let fixture: { destroy(): void } | undefined;
    try {
      const setup = await liveCatalogFixture();
      fixture = setup.fixture;
      const page = setup.fixture.nativeElement as HTMLElement;
      setup.setCategories([
        { id: 'meat', name: 'Carnes', productCount: 1 },
        { id: 'pork', name: 'Cerdo', productCount: 1 },
      ]);
      await vi.advanceTimersByTimeAsync(15000);
      setup.fixture.detectChanges();
      [...page.querySelectorAll<HTMLButtonElement>('.category-button')]
        .find((button) => button.textContent?.includes('Cerdo'))!.click();
      setup.fixture.detectChanges();
      expect(page.querySelector('.product-grid')?.textContent).toContain('Bondiola');
      const stalePage = { items: [{ id: 'bondiola', code: '2001', name: 'Bondiola', categoryId: 'pork',
        saleMode: 'unit', unit: 'unidad', price: 9999, availableStock: 10 }],
        page: 1, pageSize: 50, totalItems: 1 };
      const pending = new Subject<typeof stalePage>();
      setup.products.mockImplementationOnce(() => pending);
      await vi.advanceTimersByTimeAsync(15000);
      [...page.querySelectorAll<HTMLButtonElement>('.category-button')]
        .find((button) => button.textContent?.includes('Carnes'))!.click();
      pending.next(stalePage);
      pending.complete();
      setup.fixture.detectChanges();
      expect(page.querySelector('.category-button.active')?.textContent).toContain('Carnes');
      expect(page.querySelector('.product-grid')?.textContent).toContain('Asado');
      expect(page.querySelector('.product-grid')?.textContent).not.toContain('Bondiola');
    } finally {
      fixture?.destroy();
      vi.useRealTimers();
    }
  });

  it('shows refreshed stock and blocks adding products while stock cannot be verified', async () => {
    const { fixture, setAvailable, omitStock, failStock } = await liveCatalogFixture();
    setAvailable(0);
    window.dispatchEvent(new Event('focus'));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.product-availability').textContent).toContain('Sin stock');
    expect(fixture.nativeElement.querySelector('.product-stock')).toBeNull();
    (fixture.nativeElement.querySelector('.product-card') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#product-dialog-title')).toBeNull();
    omitStock();
    window.dispatchEvent(new Event('focus'));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.product-availability').textContent).toContain('Sin stock');
    setAvailable(10);
    window.dispatchEvent(new Event('focus'));
    failStock();
    window.dispatchEvent(new Event('focus'));
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Stock desactualizado');
    expect(fixture.nativeElement.querySelector('.product-card')).toBeNull();
  });

  it('offers generic sale with zero stock only when enabled and no protected piece exists', async () => {
    const { fixture, setAvailable, setStockPolicy, save } = await liveCatalogFixture();
    setAvailable(-5);
    setStockPolicy(true);
    window.dispatchEvent(new Event('focus'));
    fixture.detectChanges();
    const page = fixture.nativeElement as HTMLElement;
    expect(page.querySelector('.product-availability')?.textContent).toContain('Venta sin stock habilitada');
    page.querySelector<HTMLButtonElement>('.product-card')!.click();
    fixture.detectChanges();
    expect(page.querySelector('#product-dialog-title')?.textContent).toContain('Asado');
    page.querySelector<HTMLButtonElement>('.pos-dialog .finish-button')!.click();
    fixture.detectChanges();
    expect(save).toHaveBeenCalledTimes(1);
    setStockPolicy(true, true);
    window.dispatchEvent(new Event('focus'));
    fixture.detectChanges();
    expect(page.querySelector('.product-availability')?.textContent).toContain('Sin stock');
  });

  it('switches away from a disabled list only when the current ticket is empty', async () => {
    const { fixture, setLists, current } = await liveCatalogFixture();
    setLists([{ id: 'other-list', name: 'Mayorista' }]);
    window.dispatchEvent(new Event('focus'));
    fixture.detectChanges();
    expect((fixture.nativeElement.querySelector('.price-list-field select') as HTMLSelectElement).value)
      .toBe('other-list');
    expect(fixture.nativeElement.textContent).toContain('Mostrador ya no está habilitada');
    expect(current).toHaveBeenCalledTimes(1);
  });

  it('preserves an unsaved ticket and its prices if its list is disabled, but prevents checkout', async () => {
    const { fixture, setLists, current, save } = await liveCatalogFixture();
    (fixture.nativeElement.querySelector('.product-card') as HTMLButtonElement).click();
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('.pos-dialog .finish-button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(save).toHaveBeenCalledTimes(1);
    expect(fixture.nativeElement.querySelector('.ticket-lines').textContent).toContain('Asado');
    setLists([{ id: 'other-list', name: 'Mayorista' }]);
    window.dispatchEvent(new Event('focus'));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.ticket-lines').textContent).toContain('Asado');
    expect(fixture.nativeElement.querySelector('.ticket-lines').textContent).toContain('1.000');
    expect((fixture.nativeElement.querySelector('.price-list-field select') as HTMLSelectElement).value)
      .toBe('list-id');
    expect(fixture.nativeElement.textContent).toContain('Mostrador ya no está habilitada');
    expect((fixture.nativeElement.querySelector('.sale-footer .finish-button') as HTMLButtonElement).disabled)
      .toBe(true);
    expect((fixture.nativeElement.querySelector('.remove-line-button') as HTMLButtonElement).disabled).toBe(true);
    expect((fixture.nativeElement.querySelector('.quantity-input') as HTMLInputElement).disabled).toBe(true);
    expect(current).toHaveBeenCalledTimes(1);
    expect(save).toHaveBeenCalledTimes(1);
  });
});
