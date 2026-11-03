import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { PieceReceiptPage } from './piece-receipt-page';

describe('Historical piece reconciliation', () => {
  beforeEach(() => TestBed.configureTestingModule({
    imports: [PieceReceiptPage],
    providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
  }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('finds pending pieces, shows separate stock balances and safely retries an uncertain result', () => {
    const fixture = TestBed.createComponent(PieceReceiptPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/sessions/current').flush({ userId: 'admin-id', username: 'admin',
      expiresAtUtc: '', context: { userId: 'admin-id', companyId: 'company-id',
        companyName: 'Empresa', branchId: 'branch-id', branchName: 'Sucursal',
        permissions: ['inventory.stock.manage', 'platform.users.manage'], sessionId: 'session-id' } });
    http.expectOne('/api/admin/barcode-layouts').flush([]);
    http.expectOne('/api/inventory/stock').flush([{ productId: 'product-id', code: '2546',
      name: 'Asado', unit: 'kg', saleMode: 'weight', onHand: 62, reserved: 1,
      quarantined: 0.75, available: 60.25, reconciliationLocked: false }]);
    http.expectOne('/api/inventory/pieces?page=1&pageSize=20')
      .flush({ items: [], page: 1, pageSize: 20, total: 0 });
    const page = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    page.querySelector<HTMLButtonElement>('#historical-pieces-title + p + button')!.click();
    const historical = { id: 'piece-id', productId: 'product-id', productCode: '2546',
      productName: 'Asado', sourceSystem: 'Frigorifico', externalIdentifier: '999002',
      receivedWeightKg: 0.75, rawBarcode: '2999002000750',
      receivedAtUtc: '2026-10-08T14:00:00Z', status: 'HistoricalUnverified',
      reservedBySaleDraftId: null, reservedAtUtc: null };
    http.expectOne('/api/inventory/pieces?page=1&pageSize=20&status=HistoricalUnverified')
      .flush({ items: [historical], page: 1, pageSize: 20, total: 1 });
    fixture.detectChanges();
    const button = Array.from(page.querySelectorAll<HTMLButtonElement>('button'))
      .find(item => item.textContent?.trim() === 'Conciliar')!;
    button.click();
    http.expectOne('/api/inventory/pieces/piece-id/events').flush([]);
    fixture.detectChanges();
    expect(page.querySelector('.piece-stock-summary')?.textContent).toContain('0,750 kg');
    const start = Array.from(page.querySelectorAll<HTMLButtonElement>('button'))
      .find(item => item.textContent?.trim() === 'Iniciar conteo físico')!;
    start.click();
    start.click();
    const lock = http.expectOne('/api/inventory/pieces/piece-id/reconciliation-lease');
    expect(lock.request.method).toBe('POST');
    lock.flush({ lockId: lock.request.body.lockId, pieceId: 'piece-id',
      startedAtUtc: new Date(Date.now() - 1000).toISOString(),
      expiresAtUtc: new Date(Date.now() + 180000).toISOString() });
    fixture.detectChanges();

    for (const [selector, value] of [
      ['#piece-observed-weight', '0,750'], ['#piece-counted-on-hand', '62,000'],
      ['#piece-evidence', 'Acta laboratorio 1'],
      ['#piece-reconcile-reason', 'Pieza identificada en el conteo'],
    ]) {
      const input = page.querySelector<HTMLInputElement | HTMLTextAreaElement>(selector)!;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    }
    fixture.detectChanges();
    Array.from(page.querySelectorAll<HTMLButtonElement>('button'))
      .find(item => item.textContent?.trim() === 'Confirmar conciliación')!.click();
    const first = http.expectOne('/api/inventory/pieces/piece-id/reconcile');
    expect(first.request.body).toMatchObject({ lockId: lock.request.body.lockId,
      outcome: 'available', observedWeightKg: 0.75, countedOnHand: 62,
      evidenceReference: 'Acta laboratorio 1' });
    first.error(new ProgressEvent('error'));
    fixture.detectChanges();
    expect(page.querySelector<HTMLInputElement>('#piece-evidence')!.disabled).toBe(true);
    Array.from(page.querySelectorAll<HTMLButtonElement>('button'))
      .find(item => item.textContent?.trim() === 'Reintentar confirmación')!.click();
    const second = http.expectOne('/api/inventory/pieces/piece-id/reconcile');
    expect(second.request.body).toEqual(first.request.body);
    second.flush({ pieceId: 'piece-id', status: 'Available', eventId: 'event-id',
      alreadyApplied: true });
    http.expectOne('/api/inventory/pieces?page=1&pageSize=20')
      .flush({ items: [], page: 1, pageSize: 20, total: 0 });
    http.expectOne('/api/admin/barcode-layouts').flush([]);
    http.expectOne('/api/inventory/stock').flush([]);
    http.expectOne('/api/inventory/pieces?page=1&pageSize=20&status=HistoricalUnverified')
      .flush({ items: [], page: 1, pageSize: 20, total: 0 });
    fixture.detectChanges();
    expect(page.textContent).toContain('Pieza verificada y habilitada');
    fixture.destroy();
  });
});
