import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AccountsPage } from './accounts-page';

describe('AccountsPage', () => {
  beforeEach(() => TestBed.configureTestingModule({
    imports: [AccountsPage],
    providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
  }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('shows net debt, receipts and applied credit without offering correction without a terminal', () => {
    const fixture = TestBed.createComponent(AccountsPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/sessions/current').flush({
      userId: 'admin-id', username: 'visual-admin', expiresAtUtc: '2026-10-09T12:00:00Z',
      context: { userId: 'admin-id', companyId: 'company-id', companyName: 'Empresa Visual',
        branchId: 'branch-id', branchName: 'Sucursal Visual',
        permissions: ['organization.manage', 'pos.account.correct'], sessionId: 'session-id' },
    });
    http.expectOne('/api/pos-terminals/current').flush({ error: { code: 'POS_TERMINAL_REQUIRED' } },
      { status: 409, statusText: 'Conflict' });
    http.expectOne((request) => request.url === '/api/admin/accounts' &&
      request.params.get('page') === '1').flush({ page: 1, pageSize: 25, total: 1,
      items: [{ id: 'customer-id', code: 'CLI-1', name: 'Cliente de prueba',
        isActive: true, creditEnabled: true, totalDebt: 120, creditAvailable: 30 }] });
    fixture.detectChanges();
    const page = fixture.nativeElement as HTMLElement;
    expect(page.textContent).toContain('Cliente de prueba');
    expect(page.textContent).toContain('$ 120,00');
    expect(page.textContent).toContain('$ 30,00');
    (page.querySelector('tbody tr button') as HTMLButtonElement).click();
    http.expectOne((request) => request.url === '/api/admin/accounts/customer-id').flush({
      customerId: 'customer-id', code: 'CLI-1', name: 'Cliente de prueba',
      isActive: true, creditEnabled: true, totalDebt: 120, creditAvailable: 30,
      sales: { page: 1, pageSize: 25, total: 0, items: [] },
      receipts: { page: 1, pageSize: 25, total: 1, items: [{
        id: 'receipt-id', receiptNumber: 3, branchId: 'branch-id', cashierShiftId: 'shift-id',
        createdAtUtc: '2026-10-09T10:00:00Z', amount: 150, creditAmount: 30,
        method: 'cash', origin: 'cashReceived', isVoided: false, replacesReceiptId: null,
        correction: null, allocations: [{ saleId: 'sale-id', amount: 120 }],
      }] },
      creditApplications: { page: 1, pageSize: 25, total: 1, items: [{
        id: 'credit-id', saleId: 'sale-id', branchId: 'branch-id', cashierShiftId: 'shift-id',
        amount: 50, createdAtUtc: '2026-10-09T10:30:00Z',
      }] },
    });
    fixture.detectChanges();
    expect(page.textContent).toContain('Recibos internos · 1');
    expect(page.querySelector('dialog[open] .account-detail-panel')).not.toBeNull();
    expect(page.textContent).toContain('Anticipos aplicados a ventas · 1');
    expect(page.textContent).toContain('ingresá desde una caja Electron');
    expect(page.querySelector('.account-detail-panel button.secondary-button')?.textContent)
      .toContain('Cerrar detalle');
    expect([...page.querySelectorAll('button')].some((button) => button.textContent?.trim() === 'Corregir'))
      .toBe(false);
  });
});
