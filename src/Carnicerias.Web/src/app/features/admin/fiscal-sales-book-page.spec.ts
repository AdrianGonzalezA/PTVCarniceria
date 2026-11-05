import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { FiscalSalesBookPage } from './fiscal-sales-book-page';

describe('FiscalSalesBookPage', () => {
  const session = {
    userId: 'admin-id', username: 'admin', expiresAtUtc: '2026-11-05T21:00:00Z',
    context: { userId: 'admin-id', companyId: 'company-id', companyName: 'La Rústica',
      branchId: 'branch-id', branchName: 'Alta Córdoba', sessionId: 'session-id',
      permissions: ['organization.manage', 'platform.users.manage'] },
  };
  const complete = {
    fiscalDocumentId: 'fiscal-id', issueDate: '2026-11-04', branchName: 'Alta Córdoba',
    documentType: 'electronicInvoice', voucherType: 6, pointOfSale: 99, number: 25,
    receiverDocument: '20123456789', recipientName: 'Cliente', receiverVatConditionCode: 5,
    cae: '12345678901234', snapshotStatus: 'complete', total: 352.5, taxableBase: 300,
    vatAmount: 52.5, exemptAmount: 0, notTaxedAmount: 0,
    vatBreakdown: [
      { arcaRateCode: 4, ratePercent: 10.5, taxableBase: 100, taxAmount: 10.5 },
      { arcaRateCode: 5, ratePercent: 21, taxableBase: 200, taxAmount: 42 },
    ],
  };
  const legacy = {
    ...complete, fiscalDocumentId: 'legacy-id', number: 24, snapshotStatus: 'legacyIncomplete',
    total: 150, taxableBase: null, vatAmount: null, exemptAmount: null, notTaxedAmount: null,
    vatBreakdown: [],
  };
  const page = {
    from: '2026-11-01', to: '2026-11-05', branchId: null, page: 1, pageSize: 30,
    totalItems: 31, items: [complete, legacy], totals: {
      completeCount: 1, incompleteCount: 1, completeTotal: 352.5, incompleteKnownTotal: 150,
      taxableBase: 300, vatAmount: 52.5, exemptAmount: 0, notTaxedAmount: 0,
      vatBreakdown: [
        { arcaRateCode: 4, ratePercent: 10.5, taxableBase: 100, taxAmount: 10.5 },
        { arcaRateCode: 5, ratePercent: 21, taxableBase: 200, taxAmount: 42 },
      ], otherTaxesStatus: 'notModeled',
    },
  };

  beforeEach(() => TestBed.configureTestingModule({
    imports: [FiscalSalesBookPage],
    providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
  }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  function open() {
    const fixture = TestBed.createComponent(FiscalSalesBookPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/sessions/current').flush(session);
    http.expectOne('/api/admin/companies/company-id/branches').flush([
      { id: 'branch-id', name: 'Alta Córdoba', isActive: true, activeTerminalCount: 2 },
    ]);
    const initial = http.expectOne(request => request.url === '/api/admin/fiscal-sales-book');
    expect(initial.request.params.get('page')).toBe('1');
    initial.flush(page);
    fixture.detectChanges();
    return { fixture, http, host: fixture.nativeElement as HTMLElement };
  }

  it('shows fiscal totals by rate and keeps incomplete history separate', () => {
    const { host } = open();
    expect(host.textContent).toContain('352,50');
    expect(host.textContent).toContain('150,00');
    expect(host.textContent).toContain('Histórico incompleto');
    expect(host.textContent).toContain('no modelados');
    expect(host.querySelectorAll('tbody tr').length).toBe(2);
    expect(host.querySelectorAll('tbody tr')[1].textContent).toContain('—');
    expect(host.querySelectorAll('tbody tr')[0].textContent).toContain('Factura electrónica B');
  });

  it('combines branch and pagination filters without losing the period', () => {
    const { fixture, http, host } = open();
    const branch = host.querySelector<HTMLSelectElement>('#book-branch')!;
    branch.value = 'branch-id';
    branch.dispatchEvent(new Event('change'));
    const filtered = http.expectOne(request => request.url === '/api/admin/fiscal-sales-book');
    expect(filtered.request.params.get('branchId')).toBe('branch-id');
    expect(filtered.request.params.get('page')).toBe('1');
    filtered.flush({ ...page, branchId: 'branch-id' });
    fixture.detectChanges();
    host.querySelectorAll<HTMLButtonElement>('.book-pagination button')[1].click();
    const second = http.expectOne(request => request.url === '/api/admin/fiscal-sales-book');
    expect(second.request.params.get('from')).toBe('2026-11-01');
    expect(second.request.params.get('branchId')).toBe('branch-id');
    expect(second.request.params.get('page')).toBe('2');
    second.flush({ ...page, branchId: 'branch-id', page: 2, items: [] });
  });

  it('exports the applied filters and recovers after a failed request', () => {
    const { fixture, http, host } = open();
    host.querySelector<HTMLButtonElement>('.book-filters button:last-child')!.click();
    const exportRequest = http.expectOne(request => request.url === '/api/admin/fiscal-sales-book/export');
    expect(exportRequest.request.params.get('from')).toBe(page.from);
    expect(exportRequest.request.params.get('to')).toBe(page.to);
    expect(exportRequest.request.params.has('branchId')).toBe(false);
    exportRequest.flush(new Blob(['falló']), { status: 503, statusText: 'Unavailable' });
    fixture.detectChanges();
    expect(host.textContent).toContain('No se pudo descargar el Excel');
    host.querySelector<HTMLButtonElement>('.book-filters .secondary-button')!.click();
    http.expectOne(request => request.url === '/api/admin/fiscal-sales-book').flush(page);
    fixture.detectChanges();
    expect(host.querySelector<HTMLButtonElement>('.book-filters button:last-child')!.disabled).toBe(false);
  });
});
