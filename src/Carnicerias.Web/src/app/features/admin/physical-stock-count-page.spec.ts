import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { PhysicalStockCountPage } from './physical-stock-count-page';

describe('PhysicalStockCountPage', () => {
  beforeEach(() => TestBed.configureTestingModule({
    imports: [PhysicalStockCountPage],
    providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
  }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('shows the captured anatomical group and observed mass without changing stock', () => {
    const fixture = TestBed.createComponent(PhysicalStockCountPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/sessions/current').flush({
      userId: 'admin', username: 'admin', expiresAtUtc: '2026-11-07T00:00:00Z',
      context: { userId: 'admin', companyId: 'company', companyName: 'Carnicería',
        branchId: 'branch', branchName: 'Centro', sessionId: 'session',
        permissions: ['inventory.stock.manage'] },
    });
    http.expectOne('/api/inventory/counts/policy').flush({ categories: [], products: [] });
    http.expectOne(request => request.url === '/api/inventory/counts' &&
      request.params.get('page') === '1').flush({
      items: [{ id: 'count', status: 'Approved', version: 3,
        createdAtUtc: '2026-11-06T00:00:00Z' }],
      page: 1, pageSize: 20, totalItems: 1, totalPages: 1,
    });
    fixture.detectChanges();
    (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('tbody button')!.click();
    http.expectOne('/api/inventory/counts/count').flush({
      id: 'count', status: 'Approved', version: 3,
      createdAtUtc: '2026-11-06T00:00:00Z', massObservedKg: 12.5,
      massLedgerReferenceKg: 13, decisionReason: null,
      lines: [{ id: 'line', productCode: 'P1', productName: 'Lomo',
        anatomicalGroup: 'Cuarto trasero', unit: 'kg', saleMode: 'weight',
        policy: 'MassObservation', systemOnHand: 13, countedQuantity: 12.5,
        delta: -0.5 }],
      anatomicalMasses: [{ group: 'Cuarto trasero', articleCount: 1, observedKg: 12.5 }],
    });
    fixture.detectChanges();
    const dialog = (fixture.nativeElement as HTMLElement).querySelector('dialog')!;
    expect(dialog.textContent).toContain('Cuarto trasero');
    expect(dialog.textContent).toContain('12,500');
    expect(dialog.textContent).toContain('medición, sin ajuste');
    http.expectNone(request => request.method !== 'GET');
    fixture.destroy();
  });
});
