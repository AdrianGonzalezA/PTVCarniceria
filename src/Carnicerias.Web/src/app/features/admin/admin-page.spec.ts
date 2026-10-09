import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AdminPage } from './admin-page';

describe('AdminPage', () => {
  beforeEach(() => TestBed.configureTestingModule({
    imports: [AdminPage],
    providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
  }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('shows the current company and branch on the business landing', () => {
    const fixture = TestBed.createComponent(AdminPage);
    fixture.detectChanges();
    TestBed.inject(HttpTestingController).expectOne('/api/sessions/current').flush({
      userId: 'admin-id', username: 'visual-admin', expiresAtUtc: '2026-10-08T00:00:00Z',
      context: { userId: 'admin-id', companyId: 'company-id', companyName: 'Empresa Visual',
        branchId: 'branch-id', branchName: 'Sucursal Visual',
        permissions: ['platform.users.manage'], sessionId: 'session-id' },
    });
    fixture.detectChanges();

    const page = fixture.nativeElement as HTMLElement;
    expect(page.querySelector('h1')?.textContent).toContain('Negocio');
    expect(page.textContent).toContain('Empresa Visual');
    expect(page.textContent).toContain('Sucursal Visual');
    expect(page.querySelector('a[href="/pos"]')).not.toBeNull();
  });

  it('does not expose business sections without their operational permission', () => {
    const fixture = TestBed.createComponent(AdminPage);
    fixture.detectChanges();
    TestBed.inject(HttpTestingController).expectOne('/api/sessions/current').flush({
      userId: 'admin-id', username: 'visual-admin', expiresAtUtc: '2026-10-08T00:00:00Z',
      context: { userId: 'admin-id', companyId: 'company-id', companyName: 'Empresa Visual',
        branchId: 'branch-id', branchName: 'Sucursal Visual',
        permissions: ['platform.users.manage'], sessionId: 'session-id' },
    });
    fixture.detectChanges();

    const page = fixture.nativeElement as HTMLElement;
    expect(page.querySelector('a[href="/admin/history"]')).toBeNull();
    expect(page.querySelector('a[href="/admin/stock"]')).toBeNull();
    expect(page.querySelector('a[href="/admin/pieces"]')).toBeNull();
  });

  it('shows persisted business amounts separately and filters by branch', () => {
    const fixture = TestBed.createComponent(AdminPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/sessions/current').flush({
      userId: 'admin-id', username: 'visual-admin', expiresAtUtc: '2026-10-08T00:00:00Z',
      context: { userId: 'admin-id', companyId: 'company-id', companyName: 'Empresa Visual',
        branchId: 'branch-id', branchName: 'Sucursal Visual',
        permissions: ['platform.users.manage', 'organization.manage'], sessionId: 'session-id' },
    });
    http.expectOne('/api/admin/companies/company-id/branches').flush([
      { id: 'branch-id', name: 'Sucursal Visual', isActive: true, activeTerminalCount: 1 },
    ]);
    http.expectOne('/api/admin/history/summary').flush({
      fromUtc: '2026-10-08T03:00:00Z', toUtc: '2026-10-09T03:00:00Z', branchId: null,
      saleCount: 2, salesTotal: 2500, immediateSalePayments: 1500,
      paymentsByMethod: [{ method: 'cash', amount: 1500 }],
      newAccountCharges: 1000, registeredAccountCharges: 1000,
    });
    fixture.detectChanges();

    const page = fixture.nativeElement as HTMLElement;
    expect(page.textContent).toContain('Cargos a cuenta acumulados');
    expect(page.textContent).toContain('Importe bruto');
    expect(page.textContent).toContain('Efectivo');
    expect(page.textContent).toContain('$ 2.500,00');
    expect(page.querySelectorAll('.summary-card').length).toBe(4);
    const branch = page.querySelector('#summary-branch') as HTMLSelectElement;
    branch.value = 'branch-id';
    branch.dispatchEvent(new Event('change'));
    const filtered = http.expectOne((request) => request.url === '/api/admin/history/summary' &&
      request.params.get('branchId') === 'branch-id');
    expect(filtered.request.withCredentials).toBe(true);
    filtered.flush({ fromUtc: '2026-10-08T03:00:00Z', toUtc: '2026-10-09T03:00:00Z',
      branchId: 'branch-id', saleCount: 0, salesTotal: 0, immediateSalePayments: 0,
      paymentsByMethod: [], newAccountCharges: 0, registeredAccountCharges: 0 });
  });
});
