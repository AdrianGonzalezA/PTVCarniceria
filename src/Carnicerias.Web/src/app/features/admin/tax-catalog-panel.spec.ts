import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { TaxCatalogPanel } from './tax-catalog-panel';

describe('TaxCatalogPanel', () => {
  beforeEach(() => TestBed.configureTestingModule({
    imports: [TaxCatalogPanel],
    providers: [provideHttpClient(), provideHttpClientTesting()],
  }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('themes the catalog form, table and validation without changing its entries', () => {
    const root = document.documentElement;
    const previousTheme = root.getAttribute('data-visual-theme');
    try {
      root.dataset['visualTheme'] = 'original';
      const fixture = TestBed.createComponent(TaxCatalogPanel);
      fixture.detectChanges();
      const http = TestBed.inject(HttpTestingController);
      http.expectOne((request) => request.url === '/api/admin/taxes').flush({
        page: 1, pageSize: 25, total: 1,
        items: [{ id: 'vat', code: 'IVA_21_00', name: 'IVA 21 %', kind: 'iva',
          ratePercent: 21, isActive: true, createdAtUtc: '', deactivatedAtUtc: null }],
      });
      fixture.detectChanges();
      const host = fixture.nativeElement as HTMLElement;
      expect(getComputedStyle(host).getPropertyValue('--admin-tax-control').trim()).toBe('#fff');
      expect(host.querySelectorAll('tbody tr')).toHaveLength(1);

      root.dataset['visualTheme'] = 'nocturno';
      expect(getComputedStyle(host).getPropertyValue('--admin-tax-control').trim()).toBe('#181b20');
      expect(getComputedStyle(host).getPropertyValue('--admin-tax-focus').trim()).toBe('#e5b27f');
      expect(host.querySelectorAll('tbody tr')).toHaveLength(1);
      host.querySelector<HTMLButtonElement>('#tax-create')!.click();
      fixture.detectChanges();
      expect(host.querySelector('[role="alert"]')?.textContent).toContain('código');
      http.expectNone((request) => request.method === 'POST');
      fixture.destroy();
    } finally {
      if (previousTheme === null) root.removeAttribute('data-visual-theme');
      else root.setAttribute('data-visual-theme', previousTheme);
    }
  });

  it('keeps empty and consultation-error states readable in Nocturno', () => {
    const root = document.documentElement;
    const previousTheme = root.getAttribute('data-visual-theme');
    try {
      root.dataset['visualTheme'] = 'nocturno';
      const fixture = TestBed.createComponent(TaxCatalogPanel);
      fixture.detectChanges();
      const http = TestBed.inject(HttpTestingController);
      http.expectOne((request) => request.url === '/api/admin/taxes').flush({
        page: 1, pageSize: 25, total: 0, items: [],
      });
      fixture.detectChanges();
      const host = fixture.nativeElement as HTMLElement;
      expect(host.textContent).toContain('No hay entradas');
      host.querySelector<HTMLButtonElement>('.account-search button[type="submit"]')!.click();
      http.expectOne((request) => request.url === '/api/admin/taxes').flush({}, { status: 500, statusText: 'test' });
      fixture.detectChanges();
      expect(host.querySelector('[role="alert"]')?.textContent).toContain('No se pudo consultar');
      expect(getComputedStyle(host).getPropertyValue('--admin-abm-error-ink').trim()).toBe('#ffb4ab');
      fixture.destroy();
    } finally {
      if (previousTheme === null) root.removeAttribute('data-visual-theme');
      else root.setAttribute('data-visual-theme', previousTheme);
    }
  });

  it('lists existing IVA and creates an additional levy without presenting it as charged', () => {
    const fixture = TestBed.createComponent(TaxCatalogPanel);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    const iva = { id: 'iva-id', code: 'IVA_21_00', name: 'IVA 21 %', kind: 'iva',
      ratePercent: 21, isActive: true, createdAtUtc: '2026-10-09T11:00:00Z', deactivatedAtUtc: null };
    http.expectOne((request) => request.url === '/api/admin/taxes' &&
      request.params.get('page') === '1').flush({ page: 1, pageSize: 25, total: 1, items: [iva] });
    fixture.detectChanges();
    const page = fixture.nativeElement as HTMLElement;
    expect(page.textContent).toContain('IVA 21 %');
    expect(page.textContent).toContain('No calculado');

    (page.querySelector('#new-tax-code') as HTMLInputElement).value = 'IIBB_3';
    page.querySelector('#new-tax-code')?.dispatchEvent(new Event('input'));
    (page.querySelector('#new-tax-name') as HTMLInputElement).value = 'Percepción IIBB';
    page.querySelector('#new-tax-name')?.dispatchEvent(new Event('input'));
    (page.querySelector('#new-tax-kind') as HTMLSelectElement).value = 'otro';
    page.querySelector('#new-tax-kind')?.dispatchEvent(new Event('change'));
    (page.querySelector('#new-tax-rate') as HTMLInputElement).value = '3';
    page.querySelector('#new-tax-rate')?.dispatchEvent(new Event('input'));
    (page.querySelector('#tax-create') as HTMLButtonElement).click();
    const create = http.expectOne('/api/admin/taxes');
    expect(create.request.method).toBe('POST');
    expect(create.request.body).toEqual({ code: 'IIBB_3', name: 'Percepción IIBB',
      kind: 'otro', ratePercent: 3 });
    create.flush({ ...iva, id: 'other-id', code: 'IIBB_3', name: 'Percepción IIBB',
      kind: 'otro', ratePercent: 3 });
    http.expectOne((request) => request.url === '/api/admin/taxes').flush({
      page: 1, pageSize: 25, total: 2,
      items: [iva, { ...iva, id: 'other-id', code: 'IIBB_3', name: 'Percepción IIBB',
        kind: 'otro', ratePercent: 3 }],
    });
    fixture.detectChanges();
    expect(page.textContent).toContain('Percepción IIBB');
  });

  it('confirms tax deactivation in a dialog before sending the request', () => {
    const fixture = TestBed.createComponent(TaxCatalogPanel);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne((request) => request.url === '/api/admin/taxes').flush({
      page: 1, pageSize: 25, total: 1,
      items: [{ id: 'other-id', code: 'IIBB_3', name: 'IIBB', kind: 'otro',
        ratePercent: 3, isActive: true, createdAtUtc: '', deactivatedAtUtc: null }],
    });
    fixture.detectChanges();
    const page = fixture.nativeElement as HTMLElement;
    page.querySelector<HTMLButtonElement>('tbody button')!.click();
    fixture.detectChanges();
    expect(page.querySelector('dialog[aria-labelledby="tax-action-title"]')).not.toBeNull();
    http.expectNone('/api/admin/taxes/other-id');
    page.querySelector<HTMLButtonElement>('.confirm-tax-action')!.click();
    const update = http.expectOne('/api/admin/taxes/other-id');
    expect(update.request.method).toBe('PATCH');
    update.flush({});
    http.expectOne((request) => request.url === '/api/admin/taxes').flush({ page: 1,
      pageSize: 25, total: 1, items: [] });
  });
});
