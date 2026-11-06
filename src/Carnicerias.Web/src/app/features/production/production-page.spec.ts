import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ProductionPage } from './production-page';

describe('ProductionPage', () => {
  beforeEach(() => TestBed.configureTestingModule({
    imports: [ProductionPage],
    providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
  }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  const order = { id: 'order', operationId: 'operation', branchId: 'branch',
    kind: 'Standard', status: 'Open', classifierId: 'classifier', correctsOrderId: null,
    version: 1, createdByUserId: 'user', createdAtUtc: '2026-11-06T12:00:00Z',
    closedByUserId: null, closedAtUtc: null, canceledByUserId: null,
    canceledAtUtc: null, inputMassKg: 0, outputMassKg: 0,
    reportedWasteKg: 0, massDifferenceKg: 0, wasteReason: '', lines: [] };

  function load() {
    const fixture = TestBed.createComponent(ProductionPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/sessions/current').flush({ userId: 'user',
      username: 'produccion1', expiresAtUtc: '2026-11-07T00:00:00Z',
      context: { userId: 'user', companyId: 'company', companyName: 'Carnicería',
        branchId: 'branch', branchName: 'Alta Córdoba', sessionId: 'session',
        permissions: ['production.operate'] } });
    http.expectOne(request => request.url === '/api/production/classifiers')
      .flush([{ id: 'classifier', name: 'Desarme', isActive: true }]);
    http.expectOne('/api/production/orders/options').flush({
      products: [{ id: 'product', code: 'A1', name: 'Asado',
        unit: 'kg', saleMode: 'Weight' }], profiles: [],
    });
    http.expectOne('/api/production/orders/summary').flush([]);
    http.expectOne(request => request.url === '/api/production/orders')
      .flush({ items: [], page: 1, pageSize: 20, totalItems: 0, totalPages: 0 });
    fixture.detectChanges();
    return { fixture, http, host: fixture.nativeElement as HTMLElement };
  }

  it('opens an order and persists an input line immediately', () => {
    const { fixture, http, host } = load();
    const classifier = host.querySelector<HTMLSelectElement>('#production-classifier')!;
    classifier.value = 'classifier'; classifier.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    host.querySelector<HTMLFormElement>('.production-new-form')!
      .dispatchEvent(new Event('submit'));
    const create = http.expectOne('/api/production/orders');
    expect(create.request.body.classifierId).toBe('classifier');
    expect(create.request.body.correctsOrderId).toBeNull();
    create.flush(order);
    http.expectOne(request => request.url === '/api/production/orders')
      .flush({ items: [order], page: 1, pageSize: 20, totalItems: 1, totalPages: 1 });
    fixture.detectChanges();

    const product = host.querySelector<HTMLSelectElement>('#production-product')!;
    product.value = 'product'; product.dispatchEvent(new Event('change'));
    const quantity = host.querySelector<HTMLInputElement>('#production-quantity')!;
    quantity.value = '2,500'; quantity.dispatchEvent(new Event('input'));
    const mass = host.querySelector<HTMLInputElement>('#production-mass')!;
    mass.value = '2.500'; mass.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    host.querySelector<HTMLButtonElement>('.production-entry > button')!.click();
    const save = http.expectOne('/api/production/orders/order/lines');
    expect(save.request.body).toEqual({ version: 1, lines: [{
      productId: 'product', direction: 'Input', quantity: 2.5, massKg: 2.5,
      inputPieceId: null, outputBarcodeProfileId: null,
      outputSourceSystem: null, outputRawBarcode: null, outputIdentifierField: null,
    }] });
    save.flush({ ...order, version: 2, inputMassKg: 2.5, massDifferenceKg: 2.5,
      lines: [{ id: 'line', ...save.request.body.lines[0],
        productCode: 'A1', productName: 'Asado', unit: 'kg', saleMode: 'Weight',
        outputExternalIdentifier: null, inventoryMovementId: null,
        outputPieceId: null }] });
    http.expectOne(request => request.url === '/api/production/orders')
      .flush({ items: [], page: 1, pageSize: 20, totalItems: 0, totalPages: 0 });
    fixture.detectChanges();
    expect(host.textContent).toContain('2,500');
    expect(host.textContent).toContain('Orden guardada');
  });

  it('keeps the open order after a reservation conflict', () => {
    const { fixture, http, host } = load();
    const classifier = host.querySelector<HTMLSelectElement>('#production-classifier')!;
    classifier.value = 'classifier'; classifier.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    host.querySelector<HTMLFormElement>('.production-new-form')!
      .dispatchEvent(new Event('submit'));
    http.expectOne('/api/production/orders').flush(order);
    http.expectOne(request => request.url === '/api/production/orders')
      .flush({ items: [], page: 1, pageSize: 20, totalItems: 0, totalPages: 0 });
    fixture.detectChanges();
    const product = host.querySelector<HTMLSelectElement>('#production-product')!;
    product.value = 'product'; product.dispatchEvent(new Event('change'));
    const quantity = host.querySelector<HTMLInputElement>('#production-quantity')!;
    quantity.value = '1,000'; quantity.dispatchEvent(new Event('input'));
    const mass = host.querySelector<HTMLInputElement>('#production-mass')!;
    mass.value = '1,000'; mass.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    host.querySelector<HTMLButtonElement>('.production-entry > button')!.click();
    http.expectOne('/api/production/orders/order/lines').flush({
      error: { code: 'PRODUCTION_PIECE_NOT_AVAILABLE' },
    }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();
    expect(host.textContent).toContain('La pieza ya no está disponible');
    expect(host.querySelector<HTMLElement>('.production-detail')).not.toBeNull();
    expect(host.querySelector<HTMLSelectElement>('#production-product')!.value).toBe('product');
  });
});
