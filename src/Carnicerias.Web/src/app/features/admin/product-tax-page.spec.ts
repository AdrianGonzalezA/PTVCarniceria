import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { ProductTaxPage } from './product-tax-page';

describe('ProductTaxPage', () => {
  beforeEach(() => TestBed.configureTestingModule({
    imports: [ProductTaxPage],
    providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
  }));
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    vi.restoreAllMocks();
  });

  it('keeps an unconfigured article explicit and saves a rule without treating it as an invoice', () => {
    const fixture = TestBed.createComponent(ProductTaxPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/sessions/current').flush({
      userId: 'admin-id', username: 'visual-admin', expiresAtUtc: '2026-10-09T12:00:00Z',
      context: { userId: 'admin-id', companyId: 'company-id', companyName: 'Empresa Visual',
        branchId: 'branch-id', branchName: 'Sucursal Visual',
        permissions: ['catalog.manage'], sessionId: 'session-id' },
    });
    const product = { id: 'product-id', code: 'ADM-TEST', name: 'Artículo inactivo',
      isActive: false, currentRule: null };
    http.match((request) => request.url === '/api/admin/product-tax-rules' &&
      request.params.get('page') === '1').forEach((request) => request.flush({
      page: 1, pageSize: 25, total: 1, items: [product],
    }));
    http.expectOne((request) => request.url === '/api/admin/taxes').flush({
      page: 1, pageSize: 25, total: 0, items: [],
    });
    http.expectOne('/api/admin/taxes/active').flush([]);
    http.expectOne('/api/admin/tax-assignments/count').flush({ total: 1 });
    fixture.detectChanges();
    const page = fixture.nativeElement as HTMLElement;
    expect(page.textContent).toContain('Sin configurar');
    expect(page.textContent).toContain('las ventas anteriores no cambian');
    (page.querySelector('.history-panel tbody tr button') as HTMLButtonElement).click();
    http.expectOne('/api/admin/product-tax-rules/product-id').flush([]);
    http.expectOne('/api/admin/taxes/options').flush([]);
    fixture.detectChanges();
    (page.querySelector('#tax-treatment') as HTMLSelectElement).value = 'exempt';
    page.querySelector('#tax-treatment')?.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    const saveButton = [...page.querySelectorAll('.product-tax-editor button')]
      .find((button) => button.textContent?.includes('Guardar regla')) as HTMLButtonElement;
    saveButton.click();
    const save = http.expectOne('/api/admin/product-tax-rules/product-id');
    expect(save.request.method).toBe('PUT');
    expect(save.request.body).toEqual({ treatment: 'exempt', ratePercent: 0 });
    expect(save.request.withCredentials).toBe(true);
    save.flush({ id: 'rule-id', productId: 'product-id', treatment: 'exempt', ratePercent: 0,
      effectiveFromUtc: '2026-10-09T11:00:00Z', effectiveToUtc: null,
      changedByUserId: 'admin-id' });
    http.expectOne('/api/admin/product-tax-rules/product-id').flush([]);
    http.expectOne((request) => request.url === '/api/admin/product-tax-rules').flush({
      page: 1, pageSize: 25, total: 1, items: [product],
    });
    fixture.detectChanges();
    expect(page.textContent).toContain('Regla guardada');
  });

  it('assigns a catalog IVA entry rather than typing the rate into each article', () => {
    const fixture = TestBed.createComponent(ProductTaxPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/sessions/current').flush({
      userId: 'admin-id', username: 'visual-admin', expiresAtUtc: '2026-10-09T12:00:00Z',
      context: { userId: 'admin-id', companyId: 'company-id', companyName: 'Empresa Visual',
        branchId: 'branch-id', branchName: 'Sucursal Visual',
        permissions: ['catalog.manage'], sessionId: 'session-id' },
    });
    const product = { id: 'product-id', code: 'ASADO', name: 'Asado',
      isActive: true, currentRule: null };
    http.match((request) => request.url === '/api/admin/product-tax-rules').forEach((request) =>
      request.flush({ page: 1, pageSize: 25, total: 1, items: [product] }));
    http.expectOne((request) => request.url === '/api/admin/taxes').flush({
      page: 1, pageSize: 25, total: 0, items: [],
    });
    http.expectOne('/api/admin/taxes/active').flush([]);
    http.expectOne('/api/admin/tax-assignments/count').flush({ total: 1 });
    fixture.detectChanges();
    const page = fixture.nativeElement as HTMLElement;
    (page.querySelector('.history-panel tbody tr button') as HTMLButtonElement).click();
    http.expectOne('/api/admin/product-tax-rules/product-id').flush([]);
    http.expectOne('/api/admin/taxes/options').flush([
      { id: 'iva-id', code: 'IVA_21_00', name: 'IVA 21 %', ratePercent: 21 },
    ]);
    fixture.detectChanges();
    (page.querySelector('#tax-catalog-entry') as HTMLSelectElement).value = 'iva-id';
    page.querySelector('#tax-catalog-entry')?.dispatchEvent(new Event('change'));
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    fixture.detectChanges();
    const saveButton = [...page.querySelectorAll('.product-tax-editor button')]
      .find((button) => button.textContent?.includes('Guardar regla')) as HTMLButtonElement;
    saveButton.click();
    const save = http.expectOne('/api/admin/product-tax-rules/product-id');
    expect(save.request.body).toEqual({ treatment: 'taxed', ratePercent: 21,
      taxCatalogEntryId: 'iva-id' });
    save.flush({ id: 'rule-id', productId: 'product-id', treatment: 'taxed', ratePercent: 21,
      taxCatalogEntryId: 'iva-id', effectiveFromUtc: '2026-10-09T11:00:00Z',
      effectiveToUtc: null, changedByUserId: 'admin-id' });
    http.expectOne('/api/admin/product-tax-rules/product-id').flush([]);
    http.expectOne((request) => request.url === '/api/admin/product-tax-rules').flush({
      page: 1, pageSize: 25, total: 1, items: [product],
    });
  });
});
