import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { HistoryPage } from './history-page';

describe('HistoryPage', () => {
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
});
