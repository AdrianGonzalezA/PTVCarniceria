import { registerLocaleData } from '@angular/common';
import localeEsAr from '@angular/common/locales/es-AR';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { HistoryPage } from './history-page';

describe('HistoryPage', () => {
  beforeAll(() => registerLocaleData(localeEsAr));
  beforeEach(() => TestBed.configureTestingModule({
    imports: [HistoryPage],
    providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
  }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  function load() {
    const fixture = TestBed.createComponent(HistoryPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/sessions/current').flush({
      userId: 'admin-id', username: 'admin', expiresAtUtc: '2026-10-09T00:00:00Z',
      context: { userId: 'admin-id', companyId: 'company-1', companyName: 'Empresa',
        branchId: 'branch-1', branchName: 'Centro', sessionId: 'session-id',
        permissions: ['organization.manage'] },
    });
    http.expectOne('/api/admin/companies/company-1/branches').flush([
      { id: 'branch-1', name: 'Centro', isActive: true, activeTerminalCount: 1 },
    ]);
    const sales = http.expectOne((request) => request.url === '/api/admin/history/sales');
    expect(sales.request.params.get('page')).toBe('1');
    sales.flush({ items: [{ id: 'sale-1', confirmedAtUtc: '2026-10-08T10:00:00Z',
      total: 200, branchId: 'branch-1', branchName: 'Centro', terminalId: 'terminal-1',
      terminalName: 'Caja 1', cashierId: 'cashier-1', cashierName: 'Cajero', shiftId: 'shift-1' }],
    page: 1, pageSize: 30, totalItems: 1 });
    fixture.detectChanges();
    return { fixture, http, page: fixture.nativeElement as HTMLElement };
  }

  it('themes filters, tabs and a read-only ticket in place', () => {
    const root = document.documentElement;
    const previousTheme = root.getAttribute('data-visual-theme');
    try {
      root.dataset['visualTheme'] = 'original';
      const { fixture, http, page } = load();
      expect(getComputedStyle(page).getPropertyValue('--admin-history-control').trim()).toBe('#fff');
      page.querySelector<HTMLButtonElement>('tbody button')!.click();
      http.expectOne('/api/admin/history/sales/sale-1').flush({ id: 'sale-1',
        confirmedAtUtc: '2026-10-08T10:00:00Z', total: 200,
        lines: [], payments: [] });
      fixture.detectChanges();
      expect(page.querySelector('dialog[open] .ticket-detail')).not.toBeNull();

      root.dataset['visualTheme'] = 'nocturno';
      expect(getComputedStyle(page).getPropertyValue('--admin-history-control').trim()).toBe('#181b20');
      expect(getComputedStyle(page).getPropertyValue('--admin-history-detail').trim()).toBe('#24282e');
      expect(page.querySelector('dialog[open]')?.textContent).toContain('Ticket sale-1');
      http.expectNone((request) => request.method !== 'GET');
      fixture.destroy();
    } finally {
      if (previousTheme === null) root.removeAttribute('data-visual-theme');
      else root.setAttribute('data-visual-theme', previousTheme);
    }
  });

  it('shows persisted sales and loads a read-only ticket with payments', () => {
    const { fixture, http, page } = load();
    expect(page.querySelector('tbody')?.textContent).toContain('Caja 1');
    const trigger = page.querySelector<HTMLButtonElement>('tbody button')!;
    trigger.click();
    fixture.detectChanges();
    expect(page.querySelector('dialog[open]')).not.toBeNull();
    expect(page.querySelector('dialog')?.textContent).toContain('Cargando ticket');
    http.expectOne('/api/admin/history/sales/sale-1').flush({ id: 'sale-1',
      confirmedAtUtc: '2026-10-08T10:00:00Z', total: 200,
      lines: [{ code: 'P1', name: 'Asado', unit: 'kg', saleMode: 'weight',
        quantity: 1, unitPrice: 200, lineTotal: 200 }],
      payments: [{ method: 'cash', tenderedAmount: 200, appliedAmount: 200 }] });
    fixture.detectChanges();
    expect(page.querySelector('.ticket-detail')?.textContent).toContain('Asado');
    expect(page.querySelector('.ticket-detail')?.textContent).toContain('Efectivo');
    expect(page.querySelector('.ticket-detail input')).toBeNull();
    page.querySelector<HTMLButtonElement>('dialog button')!.click();
    fixture.detectChanges();
    expect(page.querySelector('dialog')).toBeNull();
    expect(page.querySelector('tbody button')).toBe(trigger);
  });

  it('opens a stored shift closing report and labels older shifts without one', () => {
    const { fixture, http, page } = load();
    const shiftsTab = Array.from(page.querySelectorAll<HTMLButtonElement>('.history-tabs button'))
      .find((button) => button.textContent?.includes('Turnos'))!;
    shiftsTab.click();
    http.expectOne((request) => request.url === '/api/admin/history/shifts').flush({
      items: [
        { id: 'shift-new', openedAtUtc: '2026-11-05T10:00:00Z',
          closedAtUtc: '2026-11-05T18:00:00Z', status: 'closed', openingCash: 100,
          branchName: 'Centro', terminalName: 'Caja 1', cashierName: 'cajero1', hasReport: true },
        { id: 'shift-old', openedAtUtc: '2026-10-05T10:00:00Z',
          closedAtUtc: '2026-10-05T18:00:00Z', status: 'closed', openingCash: 50,
          branchName: 'Centro', terminalName: 'Caja 1', cashierName: 'cajero2', hasReport: false },
      ], page: 1, pageSize: 30, totalItems: 2,
    });
    fixture.detectChanges();
    expect(page.textContent).toContain('Sin snapshot histórico');
    expect(page.querySelectorAll('tbody button')).toHaveLength(1);
    page.querySelector<HTMLButtonElement>('tbody button')!.click();
    http.expectOne('/api/admin/history/shifts/shift-new/report').flush({
      shiftId: 'shift-new', companyName: 'Empresa', branchName: 'Centro', terminalName: 'Caja 1',
      cashierName: 'cajero1', openedAtUtc: '2026-11-05T10:00:00Z',
      closedAtUtc: '2026-11-05T18:00:00Z', openingCash: 100, salesTotal: 400,
      cashSales: 300, nonCashSales: 100, accountSales: 0, creditApplied: 0,
      cashCollections: 20, nonCashCollections: 0, paymentBreakdown: [],
      cashWithdrawals: 10, expectedCash: 410, countedCash: 405,
      handedCash: 400, cashDifference: -5, isComplete: true,
    });
    fixture.detectChanges();
    expect(page.querySelector('dialog[open]')?.textContent).toContain('Efectivo contado');
    expect(page.querySelector('dialog[open]')?.textContent).toContain('405,00');
    expect(page.querySelector('dialog[open]')?.textContent).toContain('Descargar PDF');
  });

  it('distinguishes an issued fiscal snapshot from an older incomplete attempt', () => {
    const { fixture, http, page } = load();
    page.querySelector<HTMLButtonElement>('tbody button')!.click();
    http.expectOne('/api/admin/history/sales/sale-1').flush({ id: 'sale-1',
      confirmedAtUtc: '2026-10-08T10:00:00Z', total: 121,
      lines: [], payments: [], fiscalDocument: {
        id: 'fiscal-1', status: 'Authorized', pointOfSale: 99,
        voucherType: 6, number: 43, issueDate: '2026-10-08', total: 121,
        cae: '12345678901234', snapshotStatus: 'complete', taxableBase: 100,
        taxAmount: 21, exemptAmount: 0, notTaxedAmount: 0,
        vatLines: [{ arcaRateCode: 5, ratePercent: 21, taxableBase: 100, taxAmount: 21 }],
      } });
    fixture.detectChanges();
    expect(page.querySelector('.ticket-detail')?.textContent).toContain('CAE 12345678901234');
    expect(page.querySelector('.ticket-detail')?.textContent).toContain('Base gravada');
    expect(page.querySelector('.ticket-detail')?.textContent).toContain('Estado autorizado');

    page.querySelector<HTMLButtonElement>('dialog button')!.click();
    page.querySelector<HTMLButtonElement>('tbody button')!.click();
    http.expectOne('/api/admin/history/sales/sale-1').flush({ id: 'sale-1',
      confirmedAtUtc: '2026-10-08T10:00:00Z', total: 121,
      lines: [], payments: [], fiscalDocument: {
        id: 'fiscal-old', status: 'Authorized', pointOfSale: 99,
        voucherType: 6, number: 42, issueDate: '2026-10-07', total: 121,
        cae: '12345678901234', snapshotStatus: 'legacyIncomplete',
        taxableBase: null, taxAmount: null, exemptAmount: null,
        notTaxedAmount: null, vatLines: null,
      } });
    fixture.detectChanges();
    expect(page.querySelector('.ticket-detail')?.textContent).toContain('sin snapshot fiscal');
  });

  it('labels account collections and their cash change separately from sales', () => {
    const { fixture, http, page } = load();
    const cashTab = Array.from(page.querySelectorAll<HTMLButtonElement>('.history-tabs button'))
      .find((button) => button.textContent?.includes('Caja y pagos'))!;
    cashTab.click();
    http.expectOne((request) => request.url === '/api/admin/history/cash-movements').flush({
      items: [
        { id: 'cash-1', createdAtUtc: '2026-10-27T17:00:00Z', branchId: 'branch-1',
          branchName: 'Centro', terminalId: 'terminal-1', terminalName: 'Caja 1',
          cashierId: 'cashier-1', cashierName: 'Cajero', shiftId: 'shift-1', saleId: null,
          kind: 'accountCollection', method: 'cash', amountDelta: 120 },
        { id: 'cash-2', createdAtUtc: '2026-10-27T17:00:00Z', branchId: 'branch-1',
          branchName: 'Centro', terminalId: 'terminal-1', terminalName: 'Caja 1',
          cashierId: 'cashier-1', cashierName: 'Cajero', shiftId: 'shift-1', saleId: null,
          kind: 'accountCollectionChange', method: 'cash', amountDelta: -20 },
      ], page: 1, pageSize: 30, totalItems: 2,
    });
    fixture.detectChanges();
    expect(page.querySelector('tbody')?.textContent).toContain('Cobro de cuenta');
    expect(page.querySelector('tbody')?.textContent).toContain('Vuelto de cuenta');
    fixture.destroy();
  });

  it('filters by branch and changes to stock movements without a terminal filter', () => {
    const { fixture, http, page } = load();
    const branch = page.querySelector<HTMLSelectElement>('#history-branch')!;
    branch.value = 'branch-1';
    branch.dispatchEvent(new Event('change'));
    http.expectOne('/api/admin/companies/company-1/branches/branch-1/terminals').flush([
      { id: 'terminal-1', name: 'Caja 1', isActive: true, isHistorical: false, hasCredential: true },
    ]);
    const filtered = http.expectOne((request) => request.url === '/api/admin/history/sales');
    expect(filtered.request.params.get('branchId')).toBe('branch-1');
    filtered.flush({ items: [], page: 1, pageSize: 30, totalItems: 0 });
    fixture.detectChanges();
    const stockTab = Array.from(page.querySelectorAll<HTMLButtonElement>('.history-tabs button'))
      .find((button) => button.textContent?.includes('stock'))!;
    stockTab.click();
    const stock = http.expectOne((request) => request.url === '/api/admin/history/stock-movements');
    expect(stock.request.params.get('branchId')).toBe('branch-1');
    expect(stock.request.params.has('terminalId')).toBe(false);
    stock.flush({ items: [], page: 1, pageSize: 30, totalItems: 0 });
    fixture.detectChanges();
    expect(page.textContent).toContain('No hay registros');
  });

  it('shows weight movements with three decimals and unit movements as integers', () => {
    const { fixture, http, page } = load();
    const stockTab = Array.from(page.querySelectorAll<HTMLButtonElement>('.history-tabs button'))
      .find((button) => button.textContent?.includes('stock'))!;
    stockTab.click();
    http.expectOne((request) => request.url === '/api/admin/history/stock-movements').flush({
      items: [
        { id: 'move-1', createdAtUtc: '2026-10-08T10:00:00Z', branchId: 'branch-1',
          branchName: 'Centro', productId: 'p-1', productName: 'Asado', saleMode: 'weight',
          userId: 'admin-id', username: 'admin', kind: 'adjustment', quantityDelta: 1.5,
          reason: 'Ingreso' },
        { id: 'move-2', createdAtUtc: '2026-10-08T10:00:00Z', branchId: 'branch-1',
          branchName: 'Centro', productId: 'p-2', productName: 'Chorizo', saleMode: 'unit',
          userId: 'admin-id', username: 'admin', kind: 'adjustment', quantityDelta: 2,
          reason: 'Ingreso' },
      ], page: 1, pageSize: 30, totalItems: 2,
    });
    fixture.detectChanges();
    expect(page.querySelector('tbody')?.textContent).toContain('1,500');
    expect(page.querySelector('tbody')?.textContent).not.toContain('2,000');
  });

  it('combines document type with branch, terminal, dates and pagination for sales', () => {
    const { fixture, http, page } = load();
    const branch = page.querySelector<HTMLSelectElement>('#history-branch')!;
    branch.value = 'branch-1';
    branch.dispatchEvent(new Event('change'));
    http.expectOne('/api/admin/companies/company-1/branches/branch-1/terminals').flush([
      { id: 'terminal-1', name: 'Caja 1', isActive: true, isHistorical: false, hasCredential: true },
    ]);
    http.expectOne((request) => request.url === '/api/admin/history/sales').flush({
      items: [], page: 1, pageSize: 30, totalItems: 0,
    });
    fixture.detectChanges();
    const terminal = page.querySelector<HTMLSelectElement>('#history-terminal')!;
    terminal.value = 'terminal-1';
    terminal.dispatchEvent(new Event('change'));
    http.expectOne((request) => request.url === '/api/admin/history/sales').flush({
      items: [], page: 1, pageSize: 30, totalItems: 0,
    });

    const from = page.querySelector<HTMLInputElement>('#history-from')!;
    const to = page.querySelector<HTMLInputElement>('#history-to')!;
    from.value = '2026-10-08';
    from.dispatchEvent(new Event('change'));
    to.value = '2026-10-09';
    to.dispatchEvent(new Event('change'));
    const type = page.querySelector<HTMLSelectElement>('#history-document-type')!;
    type.value = 'fiscalTicket';
    type.dispatchEvent(new Event('change'));
    const first = http.expectOne((request) => request.url === '/api/admin/history/sales');
    expect(first.request.params.get('documentType')).toBe('fiscalTicket');
    expect(first.request.params.get('branchId')).toBe('branch-1');
    expect(first.request.params.get('terminalId')).toBe('terminal-1');
    expect(first.request.params.get('fromUtc')).toBe(new Date('2026-10-08T00:00:00').toISOString());
    expect(first.request.params.get('toUtc')).toBe(new Date('2026-10-10T00:00:00').toISOString());
    expect(first.request.params.get('page')).toBe('1');
    first.flush({ items: [], page: 1, pageSize: 30, totalItems: 31 });
    fixture.detectChanges();
    const next = Array.from(page.querySelectorAll<HTMLButtonElement>('.list-footer button'))
      .find((button) => button.textContent?.includes('Siguiente'))!;
    next.click();
    const second = http.expectOne((request) => request.url === '/api/admin/history/sales');
    expect(second.request.params.get('documentType')).toBe('fiscalTicket');
    expect(second.request.params.get('page')).toBe('2');
    expect(second.request.params.get('terminalId')).toBe('terminal-1');
    second.flush({ items: [], page: 2, pageSize: 30, totalItems: 31 });
    fixture.detectChanges();
    expect(page.textContent).toContain('31 registros');
  });

  it('keeps the old sales request without a type and never sends it to other history tabs', () => {
    const { fixture, http, page } = load();
    const type = page.querySelector<HTMLSelectElement>('#history-document-type')!;
    expect(type.value).toBe('');
    type.value = 'electronicInvoice';
    type.dispatchEvent(new Event('change'));
    const selected = http.expectOne((request) => request.url === '/api/admin/history/sales');
    expect(selected.request.params.get('documentType')).toBe('electronicInvoice');
    selected.flush({ items: [], page: 1, pageSize: 30, totalItems: 0 });
    fixture.detectChanges();

    const shiftsTab = Array.from(page.querySelectorAll<HTMLButtonElement>('.history-tabs button'))
      .find((button) => button.textContent?.includes('Turnos'))!;
    shiftsTab.click();
    const shifts = http.expectOne((request) => request.url === '/api/admin/history/shifts');
    expect(shifts.request.params.has('documentType')).toBe(false);
    shifts.flush({ items: [], page: 1, pageSize: 30, totalItems: 0 });
    fixture.detectChanges();
    expect(page.querySelector('#history-document-type')).toBeNull();

    const salesTab = Array.from(page.querySelectorAll<HTMLButtonElement>('.history-tabs button'))
      .find((button) => button.textContent?.includes('Ventas'))!;
    salesTab.click();
    const restored = http.expectOne((request) => request.url === '/api/admin/history/sales');
    expect(restored.request.params.get('documentType')).toBe('electronicInvoice');
    restored.flush({ items: [], page: 1, pageSize: 30, totalItems: 0 });
    fixture.detectChanges();
    const reset = page.querySelector<HTMLSelectElement>('#history-document-type')!;
    reset.value = '';
    reset.dispatchEvent(new Event('change'));
    const all = http.expectOne((request) => request.url === '/api/admin/history/sales');
    expect(all.request.params.has('documentType')).toBe(false);
    expect(all.request.params.get('page')).toBe('1');
    all.flush({ items: [], page: 1, pageSize: 30, totalItems: 0 });
  });
});
