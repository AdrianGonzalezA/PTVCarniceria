import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { PieceReceiptPage } from './piece-receipt-page';

describe('PieceReceiptPage', () => {
  beforeEach(() => TestBed.configureTestingModule({
    imports: [PieceReceiptPage],
    providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
  }));

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('receives one scanned piece against a saved profile and selected weight product', () => {
    const fixture = TestBed.createComponent(PieceReceiptPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/sessions/current').flush({
      userId: 'admin-id', username: 'visual-admin', expiresAtUtc: '2026-10-08T00:00:00Z',
      context: { userId: 'admin-id', companyId: 'company-id', companyName: 'Empresa Visual',
        branchId: 'branch-id', branchName: 'Sucursal Visual',
        permissions: ['inventory.stock.manage'], sessionId: 'session-id' },
    });
    http.expectOne('/api/admin/barcode-layouts').flush([{ id: 'profile-id', name: 'EAN-13 ciclo 2',
      revision: 1, formula: 'prefijo(1) pro_identif(6) peso(5) control_ean13(1)',
      weightField: 'peso', weightDecimals: 3, createdAtUtc: '2026-10-08T13:00:00Z' }]);
    http.expectOne('/api/inventory/stock').flush([{ productId: 'product-id', code: '2546',
      name: 'CORTITO C/FALDA EXP', unit: 'kg', saleMode: 'weight',
      onHand: 0, reserved: 0, available: 0 }]);
    http.expectOne('/api/inventory/pieces?page=1&pageSize=20')
      .flush({ items: [], page: 1, pageSize: 20, total: 0 });
    fixture.detectChanges();

    const page = fixture.nativeElement as HTMLElement;
    for (const [selector, value] of [
      ['#piece-profile', 'profile-id'], ['#piece-product', 'product-id'],
      ['#piece-source', 'Frigorífico ciclo 2'], ['#piece-code', '2250661516008'],
    ]) {
      const input = page.querySelector<HTMLInputElement | HTMLSelectElement>(selector)!;
      input.value = value;
      input.dispatchEvent(new Event('change'));
      input.dispatchEvent(new Event('input'));
    }
    fixture.detectChanges();
    page.querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit'));

    const receipt = http.expectOne('/api/inventory/pieces');
    expect(receipt.request.method).toBe('POST');
    expect(receipt.request.body).toEqual({
      operationId: expect.any(String), productId: 'product-id', barcodeProfileId: 'profile-id',
      sourceSystem: 'Frigorífico ciclo 2', identifierField: 'pro_identif', code: '2250661516008',
    });
    receipt.flush({ id: 'piece-id', operationId: receipt.request.body.operationId,
      productId: 'product-id', barcodeProfileId: 'profile-id', sourceSystem: 'Frigorífico ciclo 2',
      externalIdentifier: '250661', receivedWeightKg: 51.6, rawBarcode: '2250661516008',
      receivedAtUtc: '2026-10-08T14:00:00Z' });
    http.expectOne('/api/inventory/pieces?page=1&pageSize=20')
      .flush({ items: [{ id: 'piece-id', productId: 'product-id', productCode: '2546',
        productName: 'CORTITO C/FALDA EXP', sourceSystem: 'Frigorífico ciclo 2',
        externalIdentifier: '250661', receivedWeightKg: 51.6, rawBarcode: '2250661516008',
        receivedAtUtc: '2026-10-08T14:00:00Z' }], page: 1, pageSize: 20, total: 1 });
    fixture.detectChanges();

    expect(page.textContent).toContain('250661');
    expect(page.textContent).toContain('51,600 kg');
    expect(page.querySelector<HTMLInputElement>('#piece-code')!.value).toBe('');
  });

  it('retries an uncertain receipt with the same operation id', () => {
    const fixture = TestBed.createComponent(PieceReceiptPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/sessions/current').flush({
      userId: 'admin-id', username: 'visual-admin', expiresAtUtc: '2026-10-08T00:00:00Z',
      context: { userId: 'admin-id', companyId: 'company-id', companyName: 'Empresa Visual',
        branchId: 'branch-id', branchName: 'Sucursal Visual',
        permissions: ['inventory.stock.manage'], sessionId: 'session-id' },
    });
    http.expectOne('/api/admin/barcode-layouts').flush([{ id: 'profile-id', name: 'EAN-13 ciclo 2',
      revision: 1, formula: 'prefijo(1) pro_identif(6) peso(5) control_ean13(1)',
      weightField: 'peso', weightDecimals: 3, createdAtUtc: '2026-10-08T13:00:00Z' }]);
    http.expectOne('/api/inventory/stock').flush([{ productId: 'product-id', code: '2546',
      name: 'CORTITO C/FALDA EXP', unit: 'kg', saleMode: 'weight',
      onHand: 0, reserved: 0, available: 0 }]);
    http.expectOne('/api/inventory/pieces?page=1&pageSize=20')
      .flush({ items: [], page: 1, pageSize: 20, total: 0 });
    fixture.detectChanges();

    const page = fixture.nativeElement as HTMLElement;
    for (const [selector, value] of [
      ['#piece-profile', 'profile-id'], ['#piece-product', 'product-id'],
      ['#piece-source', 'Frigorífico ciclo 2'], ['#piece-code', '2250661516008'],
    ]) {
      const input = page.querySelector<HTMLInputElement | HTMLSelectElement>(selector)!;
      input.value = value;
      input.dispatchEvent(new Event('change'));
      input.dispatchEvent(new Event('input'));
    }
    fixture.detectChanges();
    page.querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit'));
    const first = http.expectOne('/api/inventory/pieces');
    first.flush({ error: { code: 'TEMPORARY_ERROR' } }, { status: 503, statusText: 'Unavailable' });
    fixture.detectChanges();
    expect(page.textContent).toContain('Podés reintentar sin duplicar el ingreso');

    page.querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit'));
    const retry = http.expectOne('/api/inventory/pieces');
    expect(retry.request.body.operationId).toBe(first.request.body.operationId);
    retry.flush({ error: { code: 'PIECE_ALREADY_RECEIVED' } },
      { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();
    expect(page.textContent).toContain('no se sumó stock');
  });
});
