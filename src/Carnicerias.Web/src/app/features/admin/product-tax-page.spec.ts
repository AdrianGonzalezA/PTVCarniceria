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

  it('themes the IVA editor without changing an existing rule', () => {
    const root = document.documentElement;
    const previousTheme = root.getAttribute('data-visual-theme');
    try {
      root.dataset['visualTheme'] = 'original';
      const fixture = TestBed.createComponent(ProductTaxPage);
      fixture.detectChanges();
      const http = TestBed.inject(HttpTestingController);
      http.expectOne('/api/sessions/current').flush({ userId: 'admin-id', username: 'admin', expiresAtUtc: '',
        context: { userId: 'admin-id', companyId: 'company-id', companyName: 'Empresa Visual',
          branchId: 'branch-id', branchName: 'Sucursal Visual',
          permissions: ['catalog.manage'], sessionId: 'session-id' } });
      const product = { id: 'product-id', code: 'ASADO', name: 'Asado', isActive: true, currentRule: null };
      http.match((request) => request.url === '/api/admin/product-tax-rules').forEach((request) =>
        request.flush({ page: 1, pageSize: 25, total: 1, items: [product] }));
      http.expectOne((request) => request.url === '/api/admin/taxes').flush({
        page: 1, pageSize: 25, total: 0, items: [],
      });
      http.expectOne('/api/admin/taxes/active').flush([]);
      http.expectOne('/api/admin/tax-assignments/count').flush({ total: 1 });
      fixture.detectChanges();
      const page = fixture.nativeElement as HTMLElement;
      expect(getComputedStyle(page).getPropertyValue('--admin-product-tax-control').trim()).toBe('#fff');
      page.querySelector<HTMLButtonElement>('.history-panel tbody tr button')!.click();
      http.expectOne('/api/admin/product-tax-rules/product-id').flush([]);
      http.expectOne('/api/admin/taxes/options').flush([
        { id: 'iva-id', code: 'IVA_21_00', name: 'IVA 21 %', ratePercent: 21 },
      ]);
      fixture.detectChanges();
      expect(page.querySelector<HTMLSelectElement>('#tax-catalog-entry')?.options.length).toBe(2);

      root.dataset['visualTheme'] = 'nocturno';
      expect(getComputedStyle(page).getPropertyValue('--admin-product-tax-control').trim()).toBe('#181b20');
      expect(getComputedStyle(page).getPropertyValue('--admin-product-tax-heading').trim()).toBe('#e5b27f');
      expect(page.querySelector('dialog[open] .product-tax-editor')).not.toBeNull();
      http.expectNone((request) => request.method !== 'GET');
      fixture.destroy();
    } finally {
      if (previousTheme === null) root.removeAttribute('data-visual-theme');
      else root.setAttribute('data-visual-theme', previousTheme);
    }
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
    expect(page.querySelector('dialog[open] .product-tax-editor')).not.toBeNull();
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

  it('removes the current IVA from one article and keeps prior rates in history', () => {
    const fixture = TestBed.createComponent(ProductTaxPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/sessions/current').flush({
      userId: 'admin-id', username: 'rustica-admin', expiresAtUtc: '2026-10-30T18:00:00Z',
      context: { userId: 'admin-id', companyId: 'rustica-id', companyName: 'La Rústica',
        branchId: 'branch-id', branchName: 'Alta Córdoba',
        permissions: ['catalog.manage'], sessionId: 'session-id' },
    });
    const currentRule = { id: 'rule-21', productId: 'product-id', treatment: 'taxed',
      ratePercent: 21, taxCatalogEntryId: 'iva-21', effectiveFromUtc: '2026-10-30T16:00:00Z',
      effectiveToUtc: null, changedByUserId: 'admin-id' };
    const product = { id: 'product-id', code: 'BCARBON', name: 'Carbon 5k',
      isActive: true, currentRule };
    http.match((request) => request.url === '/api/admin/product-tax-rules').forEach((request) =>
      request.flush({ page: 1, pageSize: 25, total: 1, items: [product] }));
    http.expectOne((request) => request.url === '/api/admin/taxes').flush({
      page: 1, pageSize: 25, total: 0, items: [],
    });
    http.expectOne('/api/admin/taxes/active').flush([]);
    http.expectOne('/api/admin/tax-assignments/count').flush({ total: 1 });
    fixture.detectChanges();
    const page = fixture.nativeElement as HTMLElement;
    page.querySelector<HTMLButtonElement>('.history-panel tbody tr button')!.click();
    const previousRule = { ...currentRule, id: 'rule-10', ratePercent: 10.5,
      effectiveFromUtc: '2026-10-20T16:00:00Z', effectiveToUtc: '2026-10-30T16:00:00Z' };
    http.expectOne('/api/admin/product-tax-rules/product-id').flush([currentRule, previousRule]);
    http.expectOne('/api/admin/taxes/options').flush([
      { id: 'iva-21', code: 'IVA21', name: 'IVA 21', ratePercent: 21 },
    ]);
    fixture.detectChanges();
    expect(page.querySelector('.product-tax-editor')?.textContent).toContain('Solo la regla vigente se aplica');
    const remove = [...page.querySelectorAll<HTMLButtonElement>('.product-tax-editor button')]
      .find((button) => button.textContent?.includes('Quitar IVA'))!;
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    remove.click();
    const request = http.expectOne('/api/admin/product-tax-rules/product-id');
    expect(request.request.method).toBe('DELETE');
    expect(request.request.withCredentials).toBe(true);
    request.flush(null, { status: 204, statusText: 'No Content' });
    http.expectOne('/api/admin/product-tax-rules/product-id').flush([
      { ...currentRule, effectiveToUtc: '2026-10-30T16:10:00Z' }, previousRule,
    ]);
    http.expectOne((next) => next.url === '/api/admin/product-tax-rules').flush({
      page: 1, pageSize: 25, total: 1,
      items: [{ ...product, currentRule: null }],
    });
    fixture.detectChanges();
    expect(page.querySelector('.product-tax-editor')?.textContent).toContain('Regla fiscal quitada');
    expect(page.querySelector('.product-tax-editor')?.textContent).toContain('10.5 %');
    expect(page.querySelector('.history-panel tbody')?.textContent).toContain('Sin configurar');
  });

  it('refreshes the fiscal classification table after a bulk IVA change', () => {
    const fixture = TestBed.createComponent(ProductTaxPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/sessions/current').flush({
      userId: 'admin-id', username: 'rustica-admin', expiresAtUtc: '2026-10-30T18:00:00Z',
      context: { userId: 'admin-id', companyId: 'rustica-id', companyName: 'La Rústica',
        branchId: 'branch-id', branchName: 'Alta Córdoba',
        permissions: ['catalog.manage'], sessionId: 'session-id' },
    });
    const product = { id: 'product-id', code: 'BCARBON', name: 'Carbon 5k',
      isActive: true, currentRule: null };
    http.match((request) => request.url === '/api/admin/product-tax-rules').forEach((request) =>
      request.flush({ page: 1, pageSize: 25, total: 1, items: [product] }));
    http.expectOne((request) => request.url === '/api/admin/taxes').flush({
      page: 1, pageSize: 25, total: 0, items: [],
    });
    http.expectOne('/api/admin/taxes/active').flush([
      { id: 'iva-21', code: 'IVA21', name: 'IVA 21', kind: 'iva',
        ratePercent: 21, isActive: true, createdAtUtc: '2026-10-09T11:00:00Z', deactivatedAtUtc: null },
    ]);
    http.expectOne('/api/admin/tax-assignments/count').flush({ total: 1 });
    fixture.detectChanges();
    const page = fixture.nativeElement as HTMLElement;
    const tax = page.querySelector('#assignment-tax') as HTMLSelectElement;
    tax.value = 'iva-21';
    tax.dispatchEvent(new Event('change'));
    (page.querySelector('#assignment-all') as HTMLInputElement).click();
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    (page.querySelector('#assignment-apply') as HTMLButtonElement).click();
    http.expectOne('/api/admin/tax-assignments/count').flush({ total: 1 });
    http.expectOne('/api/admin/tax-assignments').flush({ affectedCount: 1, changedCount: 1 });
    const refreshes = http.match((request) => request.url === '/api/admin/product-tax-rules');
    const updated = { ...product, currentRule: { id: 'rule-21', productId: 'product-id',
      treatment: 'taxed', ratePercent: 21, taxCatalogEntryId: 'iva-21',
      effectiveFromUtc: '2026-10-30T16:00:00Z', effectiveToUtc: null,
      changedByUserId: 'admin-id' } };
    refreshes.forEach((request) => request.flush({ page: 1, pageSize: 25, total: 1,
      items: [updated] }));
    expect(refreshes).toHaveLength(2);
    fixture.detectChanges();
    expect(page.querySelector('.history-panel tbody')?.textContent).toContain('21 %');
  });
});
