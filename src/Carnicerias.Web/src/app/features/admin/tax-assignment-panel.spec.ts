import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { TaxAssignmentPanel } from './tax-assignment-panel';

describe('TaxAssignmentPanel', () => {
  beforeEach(() => TestBed.configureTestingModule({
    imports: [TaxAssignmentPanel],
    providers: [provideHttpClient(), provideHttpClientTesting()],
  }));
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    vi.restoreAllMocks();
  });

  it('confirms the server count before applying an additional levy to all existing articles', () => {
    const fixture = TestBed.createComponent(TaxAssignmentPanel);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/admin/taxes/active').flush([
      { id: 'other-id', code: 'IIBB_3', name: 'Percepción IIBB', kind: 'otro',
        ratePercent: 3, isActive: true, createdAtUtc: '2026-10-09T11:00:00Z', deactivatedAtUtc: null },
    ]);
    http.expectOne('/api/admin/tax-assignments/count').flush({ total: 2 });
    http.expectOne((request) => request.url === '/api/admin/product-tax-rules').flush({
      page: 1, pageSize: 25, total: 2,
      items: [{ id: 'p1', code: 'P1', name: 'Asado', isActive: true, currentRule: null },
        { id: 'p2', code: 'P2', name: 'Artículo inactivo', isActive: false, currentRule: null }],
    });
    fixture.detectChanges();
    const page = fixture.nativeElement as HTMLElement;
    (page.querySelector('.refresh-taxes') as HTMLButtonElement).click();
    http.expectOne('/api/admin/taxes/active').flush([
      { id: 'other-id', code: 'IIBB_3', name: 'Percepción IIBB', kind: 'otro',
        ratePercent: 3, isActive: true, createdAtUtc: '2026-10-09T11:00:00Z', deactivatedAtUtc: null },
    ]);
    (page.querySelector('#assignment-tax') as HTMLSelectElement).value = 'other-id';
    page.querySelector('#assignment-tax')?.dispatchEvent(new Event('change'));
    (page.querySelector('#assignment-all') as HTMLInputElement).click();
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true);
    (page.querySelector('#assignment-apply') as HTMLButtonElement).click();
    http.expectOne('/api/admin/tax-assignments/count').flush({ total: 2 });
    expect(confirm).toHaveBeenCalledWith(expect.stringContaining('2 artículos'));
    const save = http.expectOne('/api/admin/tax-assignments');
    expect(save.request.body).toEqual({ taxCatalogEntryId: 'other-id', isAssigned: true,
      scope: 'all', expectedProductCount: 2 });
    save.flush({ affectedCount: 2, changedCount: 2 });
    fixture.detectChanges();
    expect(page.textContent).toContain('2 artículos');
    expect(page.textContent).toContain('No calculado');
  });

  it('removes a levy from multiple selected articles and shows its version history', () => {
    const fixture = TestBed.createComponent(TaxAssignmentPanel);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/admin/taxes/active').flush([
      { id: 'other-id', code: 'IIBB_3', name: 'Percepción IIBB', kind: 'otro',
        ratePercent: 3, isActive: true, createdAtUtc: '2026-10-09T11:00:00Z', deactivatedAtUtc: null },
    ]);
    http.expectOne('/api/admin/tax-assignments/count').flush({ total: 2 });
    http.expectOne((request) => request.url === '/api/admin/product-tax-rules').flush({
      page: 1, pageSize: 25, total: 2,
      items: [{ id: 'p1', code: 'P1', name: 'Asado', isActive: true, currentRule: null },
        { id: 'p2', code: 'P2', name: 'Entraña', isActive: true, currentRule: null }],
    });
    fixture.detectChanges();
    const page = fixture.nativeElement as HTMLElement;
    (page.querySelector('#assignment-tax') as HTMLSelectElement).value = 'other-id';
    page.querySelector('#assignment-tax')?.dispatchEvent(new Event('change'));
    (page.querySelector('#assignment-action') as HTMLSelectElement).value = 'remove';
    page.querySelector('#assignment-action')?.dispatchEvent(new Event('change'));
    page.querySelectorAll<HTMLInputElement>('tbody input[type="checkbox"]').forEach((checkbox) => checkbox.click());
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    (page.querySelector('#assignment-apply') as HTMLButtonElement).click();
    const save = http.expectOne('/api/admin/tax-assignments');
    expect(save.request.body).toEqual({ taxCatalogEntryId: 'other-id', isAssigned: false,
      scope: 'selected', productIds: ['p1', 'p2'] });
    save.flush({ affectedCount: 2, changedCount: 2 });
    (page.querySelector('tbody button') as HTMLButtonElement).click();
    http.expectOne('/api/admin/tax-assignments/p1').flush([{
      id: 'assignment-id', productId: 'p1', taxCatalogEntryId: 'other-id',
      taxName: 'Percepción IIBB', ratePercent: 3,
      effectiveFromUtc: '2026-10-09T11:00:00Z', effectiveToUtc: '2026-10-09T12:00:00Z',
      assignedByUserId: 'admin-id', removedByUserId: 'admin-id',
    }]);
    fixture.detectChanges();
    expect(page.textContent).toContain('Percepción IIBB · No calculado');
  });
});
