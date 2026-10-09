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
});
