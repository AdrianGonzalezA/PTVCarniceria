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
    http.expectOne((request) => request.url === '/api/admin/product-tax-rules' &&
      request.params.get('page') === '1').flush({ page: 1, pageSize: 25, total: 1,
      items: [product] });
    fixture.detectChanges();
    const page = fixture.nativeElement as HTMLElement;
    expect(page.textContent).toContain('Sin configurar');
    expect(page.textContent).toContain('todavía no emite facturas');
    (page.querySelector('tbody tr button') as HTMLButtonElement).click();
    http.expectOne('/api/admin/product-tax-rules/product-id').flush([]);
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
});
