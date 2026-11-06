import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { InventoryReceiptsPage } from './inventory-receipts-page';

describe('InventoryReceiptsPage', () => {
  beforeEach(() => TestBed.configureTestingModule({
    imports: [InventoryReceiptsPage],
    providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
  }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  function load() {
    const fixture = TestBed.createComponent(InventoryReceiptsPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/sessions/current').flush({
      userId: 'receiver', username: 'receptor', expiresAtUtc: '2026-11-06T00:00:00Z',
      context: { userId: 'receiver', companyId: 'company', companyName: 'Carnicería',
        branchId: 'branch', branchName: 'Centro', sessionId: 'session',
        permissions: ['inventory.receipts.manage'] },
    });
    http.expectOne('/api/inventory/receipts/supplier-options').flush([
      { id: 'supplier', code: 'F1', name: 'Proveedor uno' },
    ]);
    http.expectOne('/api/inventory/receipts/product-options').flush([
      { productId: 'product', code: 'A1', name: 'Asado', unit: 'kg', saleMode: 'weight' },
    ]);
    http.expectOne('/api/inventory/receipts/barcode-profiles').flush([
      { id: 'profile', name: 'Ciclo 2' },
    ]);
    http.expectOne((request) => request.url === '/api/inventory/receipts').flush({
      items: [], page: 1, pageSize: 20, totalItems: 0, totalPages: 0,
    });
    fixture.detectChanges();
    return { fixture, http, host: fixture.nativeElement as HTMLElement };
  }

  function input(host: HTMLElement, selector: string, value: string) {
    const element = host.querySelector<HTMLInputElement>(selector)!;
    element.value = value;
    element.dispatchEvent(new Event('input'));
  }

  const draft = {
    id: 'receipt', operationId: 'operation', supplierId: 'supplier', supplierCode: 'F1',
    supplierName: 'Proveedor uno', supplierIdentification: null, sourceSystem: null,
    documentType: 'remito', documentNumber: 'R-1', documentDate: '2026-11-05',
    destination: null, externalReference: null, status: 'Draft', version: 1,
    createdAtUtc: '2026-11-05T12:00:00Z', approvedAtUtc: null, canceledAtUtc: null,
    lines: [], pieceReads: [],
  };

  it('keeps a package read separate from editable loose units', () => {
    const { fixture, http, host } = load();
    const supplier = host.querySelector<HTMLSelectElement>('#receipt-supplier')!;
    supplier.value = 'supplier';
    supplier.dispatchEvent(new Event('change'));
    input(host, '#receipt-number', 'R-1');
    host.querySelector<HTMLFormElement>('.receipt-form')!.dispatchEvent(new Event('submit'));
    http.expectOne('/api/inventory/receipts').flush(draft);
    http.expectOne((request) => request.url === '/api/inventory/receipts').flush({
      items: [], page: 1, pageSize: 20, totalItems: 0, totalPages: 0,
    });
    fixture.detectChanges();

    input(host, '#receipt-package-code', 'CAJA6');
    input(host, '#receipt-package-count', '2');
    host.querySelector<HTMLFormElement>('section[aria-labelledby="receipt-package-title"] form')!
      .dispatchEvent(new Event('submit'));
    const scan = http.expectOne('/api/inventory/receipts/receipt/packages');
    expect(scan.request.body).toMatchObject({ version: 1, code: 'CAJA6', packageCount: 2 });
    scan.flush({ ...draft, version: 2, lines: [{ id: 'package-line', productId: 'unit-product',
      productCode: 'U1', productName: 'Chorizo', unit: 'u.', saleMode: 'unit',
      expectedQuantity: null, receivedQuantity: 12, differenceStatus: 'None',
      differenceReason: null, inventoryMovementId: null }], packageReads: [{
      id: 'package-read', lineId: 'package-line', operationId: scan.request.body.operationId,
      productId: 'unit-product', code: 'CAJA6', packageCount: 2,
      unitsPerPackage: 6, unitQuantity: 12, readByUserId: 'receiver',
      readAtUtc: '2026-11-06T12:00:00Z',
    }] });
    fixture.detectChanges();
    expect(host.textContent).toContain('CAJA6');
    expect(host.textContent).toContain('12');
    expect(host.querySelectorAll('.receipt-panel .table-wrap tbody tr').length).toBeGreaterThan(0);
    expect(host.querySelector<HTMLInputElement>('#receipt-package-code')?.value).toBe('');
  });

  it('scans a traceable piece into a draft without changing stock', () => {
    const { fixture, http, host } = load();
    const supplier = host.querySelector<HTMLSelectElement>('#receipt-supplier')!;
    supplier.value = 'supplier';
    supplier.dispatchEvent(new Event('change'));
    input(host, '#receipt-number', 'R-1');
    host.querySelector<HTMLFormElement>('.receipt-form')!.dispatchEvent(new Event('submit'));
    http.expectOne('/api/inventory/receipts').flush(draft);
    http.expectOne((request) => request.url === '/api/inventory/receipts').flush({
      items: [], page: 1, pageSize: 20, totalItems: 0, totalPages: 0,
    });
    fixture.detectChanges();
    const product = host.querySelector<HTMLSelectElement>('#receipt-scan-product')!;
    product.value = 'product';
    product.dispatchEvent(new Event('change'));
    const profile = host.querySelector<HTMLSelectElement>('#receipt-scan-profile')!;
    profile.value = 'profile';
    profile.dispatchEvent(new Event('change'));
    input(host, '#receipt-scan-code', '25066151600');
    host.querySelector<HTMLFormElement>('section[aria-labelledby="receipt-scan-title"] form')!
      .dispatchEvent(new Event('submit'));
    const scan = http.expectOne('/api/inventory/receipts/receipt/pieces');
    expect(scan.request.body).toMatchObject({ version: 1, productId: 'product',
      barcodeProfileId: 'profile', sourceSystem: 'Frigorífico ciclo 2',
      identifierField: 'pro_identif', code: '25066151600' });
    expect(scan.request.body.operationId).toBeTruthy();
    scan.flush({ ...draft, version: 2, lines: [{ id: 'piece-line', productId: 'product',
      productCode: 'A1', productName: 'Asado', unit: 'kg', saleMode: 'weight',
      expectedQuantity: null, receivedQuantity: 51.6, differenceStatus: 'None',
      differenceReason: null, inventoryMovementId: null }],
    pieceReads: [{ id: 'read-1', lineId: 'piece-line', operationId: scan.request.body.operationId,
      barcodeProfileId: 'profile', sourceSystem: 'Frigorífico ciclo 2',
      identifierField: 'pro_identif', externalIdentifier: '250661',
      rawBarcode: '25066151600', weightKg: 51.6, inventoryPieceId: null }] });
    fixture.detectChanges();
    expect(host.textContent).toContain('250661');
    expect(host.textContent).toContain('51,600');
    expect(host.textContent).toContain('En borrador');
    expect(host.querySelector<HTMLInputElement>('#receipt-scan-code')?.value).toBe('');
    http.expectNone((request) => request.url === '/api/inventory/adjustments');
  });

  it('lets a receiver without stock-admin permission save quantities before one approval', () => {
    const { fixture, http, host } = load();
    const supplier = host.querySelector<HTMLSelectElement>('#receipt-supplier')!;
    supplier.value = 'supplier';
    supplier.dispatchEvent(new Event('change'));
    input(host, '#receipt-number', 'R-1');
    fixture.detectChanges();
    host.querySelector<HTMLFormElement>('.receipt-form')!.dispatchEvent(new Event('submit'));
    const create = http.expectOne('/api/inventory/receipts');
    expect(create.request.method).toBe('POST');
    expect(create.request.body.supplierId).toBe('supplier');
    expect(create.request.body.documentNumber).toBe('R-1');
    expect(create.request.body.operationId).toBeTruthy();
    create.flush(draft);
    http.expectOne((request) => request.url === '/api/inventory/receipts').flush({
      items: [], page: 1, pageSize: 20, totalItems: 0, totalPages: 0,
    });
    fixture.detectChanges();

    const product = host.querySelector<HTMLSelectElement>('#line-product')!;
    product.value = 'product';
    product.dispatchEvent(new Event('change'));
    input(host, '#line-expected', '1,500');
    input(host, '#line-received', '1.250');
    host.querySelector<HTMLButtonElement>('.receipt-add-line button')!.click();
    fixture.detectChanges();
    expect(host.querySelector<HTMLButtonElement>('.receipt-actions button:nth-child(2)')!.disabled).toBe(true);
    http.expectNone((request) => request.url === '/api/inventory/adjustments');

    host.querySelector<HTMLButtonElement>('.receipt-actions button:first-child')!.click();
    const save = http.expectOne('/api/inventory/receipts/receipt/lines');
    expect(save.request.method).toBe('PUT');
    expect(save.request.body).toEqual({ version: 1, lines: [{
      productId: 'product', expectedQuantity: 1.5, receivedQuantity: 1.25,
    }] });
    const line = { id: 'line', productId: 'product', productCode: 'A1', productName: 'Asado',
      unit: 'kg', saleMode: 'weight', expectedQuantity: 1.5, receivedQuantity: 1.25,
      differenceStatus: 'None', differenceReason: null, inventoryMovementId: null };
    save.flush({ ...draft, version: 2, lines: [line] });
    fixture.detectChanges();
    host.querySelector<HTMLButtonElement>('.receipt-actions button:nth-child(2)')!.click();
    const approve = http.expectOne('/api/inventory/receipts/receipt/approve');
    expect(approve.request.body).toEqual({ version: 2 });
    approve.flush({ ...draft, status: 'Approved', version: 4, lines: [{ ...line,
      differenceStatus: 'PendingReview', inventoryMovementId: 'movement' }] });
    http.expectOne((request) => request.url === '/api/inventory/receipts').flush({
      items: [], page: 1, pageSize: 20, totalItems: 0, totalPages: 0,
    });
    fixture.detectChanges();
    expect(host.textContent).toContain('Recepción aprobada');
    expect(host.textContent).toContain('Pendiente de revisión');
  });

  it('retains editable quantities when saving fails', () => {
    const { fixture, http, host } = load();
    http.expectNone((request) => request.url === '/api/inventory/adjustments');
    const supplier = host.querySelector<HTMLSelectElement>('#receipt-supplier')!;
    supplier.value = 'supplier';
    supplier.dispatchEvent(new Event('change'));
    input(host, '#receipt-number', 'R-1');
    host.querySelector<HTMLFormElement>('.receipt-form')!.dispatchEvent(new Event('submit'));
    http.expectOne('/api/inventory/receipts').flush(draft);
    http.expectOne((request) => request.url === '/api/inventory/receipts').flush({
      items: [], page: 1, pageSize: 20, totalItems: 0, totalPages: 0,
    });
    fixture.detectChanges();
    const product = host.querySelector<HTMLSelectElement>('#line-product')!;
    product.value = 'product';
    product.dispatchEvent(new Event('change'));
    input(host, '#line-received', '2,250');
    host.querySelector<HTMLButtonElement>('.receipt-add-line button')!.click();
    fixture.detectChanges();
    host.querySelector<HTMLButtonElement>('.receipt-actions button:first-child')!.click();
    http.expectOne('/api/inventory/receipts/receipt/lines').flush(
      { error: { code: 'RECEIPT_VERSION_CHANGED' } },
      { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();
    expect(host.querySelector<HTMLInputElement>('tbody input[aria-label^="Cantidad recibida"]')?.value)
      .toBe('2,250');
    expect(host.querySelector<HTMLButtonElement>('.receipt-actions button:nth-child(2)')!.disabled)
      .toBe(true);
  });

  it('imports Excel into a draft without approving stock', () => {
    const { fixture, http, host } = load();
    const supplier = host.querySelector<HTMLSelectElement>('#receipt-supplier')!;
    supplier.value = 'supplier';
    supplier.dispatchEvent(new Event('change'));
    input(host, '#receipt-number', 'R-Excel');
    host.querySelector<HTMLFormElement>('.receipt-form')!.dispatchEvent(new Event('submit'));
    http.expectOne('/api/inventory/receipts').flush(draft);
    http.expectOne((request) => request.url === '/api/inventory/receipts').flush({
      items: [], page: 1, pageSize: 20, totalItems: 0, totalPages: 0,
    });
    fixture.detectChanges();

    host.querySelector<HTMLButtonElement>('.receipt-import-open')!.click();
    fixture.detectChanges();
    const file = new File(['xlsx'], 'recepcion.xlsx', {
      type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    });
    const inputFile = host.querySelector<HTMLInputElement>('.receipt-import-file input')!;
    Object.defineProperty(inputFile, 'files', { value: [file] });
    inputFile.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    host.querySelector<HTMLButtonElement>('.receipt-import-preview')!.click();
    http.expectOne((request) => request.url ===
      '/api/inventory/receipts/receipt/import/preview').flush({
      canApply: true, createCount: 1, rows: [{ sheet: 'Recepciones', row: 2,
        status: 'create', message: 'A1: 2 kg recibidos.' }], issues: [],
    });
    fixture.detectChanges();
    host.querySelector<HTMLButtonElement>('.receipt-import-apply')!.click();
    const applied = http.expectOne((request) => request.url ===
      '/api/inventory/receipts/receipt/import/apply');
    expect(applied.request.params.get('version')).toBe('1');
    applied.flush({ ...draft, version: 2, lines: [{ id: 'line', productId: 'product',
      productCode: 'A1', productName: 'Asado', unit: 'kg', saleMode: 'weight',
      expectedQuantity: null, receivedQuantity: 2, differenceStatus: 'None',
      differenceReason: null, inventoryMovementId: null }] });
    fixture.detectChanges();
    expect(host.textContent).toContain('guardó en el borrador');
    http.expectNone((request) => request.url.includes('/approve'));
  });
});
