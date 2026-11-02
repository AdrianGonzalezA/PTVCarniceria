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

  it('themes profile/product selection and piece history without receiving a piece', () => {
    const root = document.documentElement;
    const previousTheme = root.getAttribute('data-visual-theme');
    try {
      root.dataset['visualTheme'] = 'original';
      const fixture = TestBed.createComponent(PieceReceiptPage);
      fixture.detectChanges();
      const http = TestBed.inject(HttpTestingController);
      http.expectOne('/api/sessions/current').flush({ userId: 'admin-id', username: 'admin', expiresAtUtc: '',
        context: { userId: 'admin-id', companyId: 'company-id', companyName: 'Empresa Visual',
          branchId: 'branch-id', branchName: 'Sucursal Visual',
          permissions: ['inventory.stock.manage'], sessionId: 'session-id' } });
      http.expectOne('/api/admin/barcode-layouts').flush([{ id: 'profile-id', name: 'Ciclo 2',
        revision: 1, formula: 'pro_identif(6) peso(5)', weightField: 'peso',
        weightDecimals: 3, createdAtUtc: '2026-10-08T13:00:00Z' }]);
      http.expectOne('/api/inventory/stock').flush([{ productId: 'product-id', code: '2546',
        name: 'CORTITO C/FALDA EXP', unit: 'kg', saleMode: 'weight',
        onHand: 0, reserved: 0, available: 0 }]);
      http.expectOne('/api/inventory/pieces?page=1&pageSize=20').flush({
        items: [{ id: 'piece-id', productId: 'product-id', productCode: '2546',
          productName: 'CORTITO C/FALDA EXP', sourceSystem: 'Frigorífico',
          externalIdentifier: '250661', receivedWeightKg: 51.6, rawBarcode: '2250661516008',
          receivedAtUtc: '2026-10-08T14:00:00Z' }], page: 1, pageSize: 20, total: 1,
      });
      fixture.detectChanges();
      const page = fixture.nativeElement as HTMLElement;
      expect(getComputedStyle(page).getPropertyValue('--admin-piece-control').trim()).toBe('#fff');
      expect(page.textContent).toContain('250661');

      root.dataset['visualTheme'] = 'nocturno';
      expect(getComputedStyle(page).getPropertyValue('--admin-piece-control').trim()).toBe('#181b20');
      expect(getComputedStyle(page).getPropertyValue('--admin-piece-muted').trim()).toBe('#bbb8b3');
      expect(page.textContent).toContain('250661');
      http.expectNone((request) => request.method !== 'GET');
      fixture.destroy();
    } finally {
      if (previousTheme === null) root.removeAttribute('data-visual-theme');
      else root.setAttribute('data-visual-theme', previousTheme);
    }
  });

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

  it('shows reservation audit and releases only the selected piece with an admin reason', () => {
    const fixture = TestBed.createComponent(PieceReceiptPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/sessions/current').flush({ userId: 'admin-id', username: 'admin',
      expiresAtUtc: '', context: { userId: 'admin-id', companyId: 'company-id',
        companyName: 'Empresa', branchId: 'branch-id', branchName: 'Sucursal',
        permissions: ['inventory.stock.manage', 'platform.users.manage'], sessionId: 'session-id' } });
    http.expectOne('/api/admin/barcode-layouts').flush([]);
    http.expectOne('/api/inventory/stock').flush([]);
    const reservedPiece = { id: 'piece-id', productId: 'product-id', productCode: '2546',
      productName: 'Asado', sourceSystem: 'Frigorífico', externalIdentifier: '250661',
      receivedWeightKg: 51.6, rawBarcode: '2250661516008',
      receivedAtUtc: '2026-10-08T14:00:00Z', status: 'Reserved',
      reservedBySaleDraftId: 'draft-id', reservedAtUtc: '2026-10-08T15:00:00Z' };
    http.expectOne('/api/inventory/pieces?page=1&pageSize=20')
      .flush({ items: [reservedPiece], page: 1, pageSize: 20, total: 1 });
    fixture.detectChanges();
    const page = fixture.nativeElement as HTMLElement;
    expect(page.textContent).toContain('Reservada');
    page.querySelector<HTMLButtonElement>('table button')!.click();
    http.expectOne('/api/inventory/pieces/piece-id/events').flush([{ id: 'event-id',
      previousStatus: 'Available', status: 'Reserved', kind: 'SaleReserved',
      saleDraftId: 'draft-id', actorUserId: 'cashier-id',
      reason: 'Reserva por borrador de venta', createdAtUtc: '2026-10-08T15:00:00Z' }]);
    fixture.detectChanges();
    expect(page.querySelector('[role="dialog"]')?.textContent).toContain('Reserva por borrador de venta');
    const reason = page.querySelector<HTMLTextAreaElement>('#piece-release-reason')!;
    reason.value = 'Cajero no puede recuperar el ticket';
    reason.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    page.querySelector<HTMLFormElement>('.piece-release-form')!
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    const release = http.expectOne('/api/inventory/pieces/piece-id/release');
    expect(release.request.method).toBe('POST');
    expect(release.request.body).toEqual({ reason: 'Cajero no puede recuperar el ticket' });
    release.flush({ pieceId: 'piece-id', draftId: 'draft-id',
      draftCancelled: false, discountReset: false });
    http.expectOne('/api/inventory/pieces?page=1&pageSize=20')
      .flush({ items: [{ ...reservedPiece, status: 'Available', reservedBySaleDraftId: null,
        reservedAtUtc: null }], page: 1, pageSize: 20, total: 1 });
    fixture.detectChanges();
    expect(page.querySelector('[role="dialog"]')).toBeNull();
    expect(page.textContent).toContain('los demás renglones se conservaron');
  });
});
