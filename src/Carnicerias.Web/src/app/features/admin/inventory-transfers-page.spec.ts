import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { InventoryTransfersPage } from './inventory-transfers-page';

describe('InventoryTransfersPage', () => {
  beforeEach(() => TestBed.configureTestingModule({
    imports: [InventoryTransfersPage],
    providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
  }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  function load() {
    const fixture = TestBed.createComponent(InventoryTransfersPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/sessions/current').flush({
      userId: 'dispatcher', username: 'despachante', expiresAtUtc: '2026-11-06T00:00:00Z',
      context: { userId: 'dispatcher', companyId: 'company', companyName: 'Carnicería',
        branchId: 'origin', branchName: 'Origen', sessionId: 'session',
        permissions: ['inventory.transfers.manage'] },
    });
    http.expectOne('/api/inventory/transfers/product-options').flush([
      { productId: 'product', code: 'A1', name: 'Asado', unit: 'kg', saleMode: 'weight' },
    ]);
    http.expectOne('/api/inventory/transfers/branch-options').flush([
      { id: 'destination', name: 'Sucursal Norte' },
    ]);
    http.expectOne(request => request.url === '/api/inventory/transfers').flush({
      items: [], page: 1, pageSize: 20, totalItems: 0, totalPages: 0,
    });
    fixture.detectChanges();
    return { fixture, http, host: fixture.nativeElement as HTMLElement };
  }

  const draft = { id: 'transfer', operationId: 'operation', branchId: 'origin',
    destinationBranchId: null, destinationName: 'Destino externo',
    documentNumber: 'TR-operation', documentDate: '2026-11-05',
    status: 'Draft', version: 1, createdAtUtc: '2026-11-05T12:00:00Z',
    dispatchedAtUtc: null, canceledAtUtc: null, lines: [] };

  it('lets an external destination be dispatched without requiring receipt', () => {
    const { fixture, http, host } = load();
    const destination = host.querySelector<HTMLInputElement>('#transfer-destination')!;
    destination.value = 'Destino externo';
    destination.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    host.querySelector<HTMLFormElement>('.receipt-form')!
      .dispatchEvent(new Event('submit'));
    const create = http.expectOne('/api/inventory/transfers');
    expect(create.request.body.destinationBranchId).toBeNull();
    expect(create.request.body.destinationName).toBe('Destino externo');
    create.flush(draft);
    http.expectOne(request => request.url === '/api/inventory/transfers').flush({
      items: [], page: 1, pageSize: 20, totalItems: 0, totalPages: 0,
    });
    fixture.detectChanges();

    const product = host.querySelector<HTMLSelectElement>('#transfer-product')!;
    product.value = 'product';
    product.dispatchEvent(new Event('change'));
    const quantity = host.querySelector<HTMLInputElement>('#transfer-quantity')!;
    quantity.value = '2,500';
    quantity.dispatchEvent(new Event('input'));
    host.querySelector<HTMLButtonElement>('.transfer-add-line button')!.click();
    fixture.detectChanges();
    expect(host.querySelector<HTMLButtonElement>('.receipt-actions button:nth-child(2)')!.disabled)
      .toBe(true);
    host.querySelector<HTMLButtonElement>('.receipt-actions button:first-child')!.click();
    const save = http.expectOne('/api/inventory/transfers/transfer/lines');
    expect(save.request.body).toEqual({ version: 1, lines: [
      { productId: 'product', quantity: 2.5, pieceId: null },
    ] });
    const line = { id: 'line', productId: 'product', productCode: 'A1',
      productName: 'Asado', unit: 'kg', saleMode: 'weight', quantity: 2.5,
      pieceId: null, inventoryMovementId: null };
    save.flush({ ...draft, version: 2, lines: [line] });
    fixture.detectChanges();
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true);
    host.querySelector<HTMLButtonElement>('.receipt-actions button:nth-child(2)')!.click();
    const dispatch = http.expectOne('/api/inventory/transfers/transfer/dispatch');
    expect(dispatch.request.body).toEqual({ version: 2 });
    dispatch.flush({ ...draft, version: 4, status: 'Dispatched',
      dispatchedAtUtc: '2026-11-05T12:01:00Z', lines: [
        { ...line, inventoryMovementId: 'movement' },
      ] });
    http.expectOne(request => request.url === '/api/inventory/transfers').flush({
      items: [], page: 1, pageSize: 20, totalItems: 0, totalPages: 0,
    });
    fixture.detectChanges();
    expect(host.textContent).toContain('Despacho confirmado');
    expect(host.textContent).toContain('2,500');
    expect(confirm).toHaveBeenCalled();
    confirm.mockRestore();
    http.expectNone(request => request.url.startsWith('/api/inventory/receipts'));
  });

  it('selects a destination branch but keeps dispatch independent of that branch', () => {
    const { fixture, http, host } = load();
    const branch = host.querySelector<HTMLSelectElement>('#transfer-branch')!;
    branch.value = 'destination';
    branch.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(host.querySelector<HTMLInputElement>('#transfer-destination')!.value)
      .toBe('Sucursal Norte');
    host.querySelector<HTMLFormElement>('.receipt-form')!
      .dispatchEvent(new Event('submit'));
    const create = http.expectOne('/api/inventory/transfers');
    expect(create.request.body.destinationBranchId).toBe('destination');
    create.flush({ ...draft, destinationBranchId: 'destination',
      destinationName: 'Sucursal Norte' });
    http.expectOne(request => request.url === '/api/inventory/transfers').flush({
      items: [], page: 1, pageSize: 20, totalItems: 0, totalPages: 0,
    });
    fixture.detectChanges();
    expect(host.textContent).toContain('Sucursal Norte');
  });
});
