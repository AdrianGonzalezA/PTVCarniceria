import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { AdminPage } from './admin-page';

describe('administrator areas', () => {
  beforeEach(() => TestBed.configureTestingModule({
    imports: [AdminPage],
    providers: [
      provideRouter([
        { path: 'admin', component: AdminPage, data: { area: 'business' } },
        { path: 'admin/configuracion', component: AdminPage, data: { area: 'configuration' } },
      ]),
      provideHttpClient(), provideHttpClientTesting(),
    ],
  }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  async function open(url: string): Promise<HTMLElement> {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url, AdminPage);
    TestBed.inject(HttpTestingController).expectOne('/api/sessions/current').flush({
      userId: 'admin-id', username: 'visual-admin', expiresAtUtc: '2026-10-08T00:00:00Z',
      context: { userId: 'admin-id', companyId: 'company-id', companyName: 'Empresa Visual',
        branchId: 'branch-id', branchName: 'Sucursal Visual',
        permissions: ['platform.users.manage', 'organization.manage', 'inventory.stock.manage', 'catalog.manage'],
        sessionId: 'session-id' },
    });
    if (url === '/admin') {
      TestBed.inject(HttpTestingController).expectOne('/api/admin/companies/company-id/branches').flush([]);
      TestBed.inject(HttpTestingController).expectOne('/api/admin/history/summary').flush({
        fromUtc: '2026-10-08T03:00:00Z', toUtc: '2026-10-09T03:00:00Z', branchId: null,
        saleCount: 0, salesTotal: 0, immediateSalePayments: 0, paymentsByMethod: [],
        newAccountCharges: 0, registeredAccountCharges: 0, creditApplied: 0,
      });
    }
    harness.detectChanges();
    return harness.routeNativeElement as HTMLElement;
  }

  it('puts history, stock and piece reception under Negocio', async () => {
    const page = await open('/admin');
    expect(page.querySelector('h1')?.textContent).toContain('Negocio');
    expect(page.querySelector('a[href="/admin/history"]')).not.toBeNull();
    expect(page.querySelector('a[href="/admin/stock"]')).not.toBeNull();
    expect(page.querySelector('a[href="/admin/pieces"]')).not.toBeNull();
    expect(page.querySelector('a[href="/users"]')).toBeNull();
    expect(page.querySelector('a[href="/admin"]')?.getAttribute('aria-current')).toBe('page');
  });

  it('puts management and barcode rules under Configuración', async () => {
    const page = await open('/admin/configuracion');
    expect(page.querySelector('h1')?.textContent).toContain('Configuración');
    expect(page.querySelector('a[href="/admin/customers"]')).not.toBeNull();
    expect(page.querySelector('a[href="/admin/barcode-layouts"]')).not.toBeNull();
    expect(page.querySelector('a[href="/users"]')).not.toBeNull();
    expect(page.querySelector('a[href="/admin/history"]')).toBeNull();
    expect(page.querySelector('a[href="/admin/configuracion"]')?.getAttribute('aria-current')).toBe('page');
  });
});
