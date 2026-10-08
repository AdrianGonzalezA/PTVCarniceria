import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { StockPage } from './stock-page';

describe('StockPage', () => {
  beforeEach(() => TestBed.configureTestingModule({
    imports: [StockPage],
    providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
  }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  function load() {
    const fixture = TestBed.createComponent(StockPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/sessions/current').flush({
      userId: 'admin-id', username: 'admin', expiresAtUtc: '2026-10-09T00:00:00Z',
      context: { userId: 'admin-id', companyId: 'company-1', companyName: 'Carnicería',
        branchId: 'branch-1', branchName: 'Centro', sessionId: 'session-id',
        permissions: ['inventory.stock.manage'] },
    });
    http.expectOne('/api/inventory/stock').flush([{ productId: 'product-1', code: 'P1',
      name: 'Asado', unit: 'kg', saleMode: 'weight', onHand: 10, reserved: 2, available: 8 },
    { productId: 'product-2', code: 'P2', name: 'Chorizo', unit: 'u.', saleMode: 'unit',
      onHand: 5, reserved: 0, available: 5 }]);
    fixture.detectChanges();
    return { fixture, http, page: fixture.nativeElement as HTMLElement };
  }

  it('shows branch stock and persists a positive adjustment with an operation id', () => {
    const { fixture, http, page } = load();
    expect(page.textContent).toContain('Centro');
    expect(page.querySelectorAll('.stock-row')).toHaveLength(2);
    page.querySelector<HTMLButtonElement>('.stock-row button')!.click();
    fixture.detectChanges();
    const quantity = page.querySelector<HTMLInputElement>('#stock-delta')!;
    quantity.value = '1.5';
    quantity.dispatchEvent(new Event('input'));
    const reason = page.querySelector<HTMLInputElement>('#stock-reason')!;
    reason.value = 'Reposición';
    reason.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    page.querySelector<HTMLFormElement>('.stock-form')!.dispatchEvent(new Event('submit'));
    const request = http.expectOne('/api/inventory/adjustments');
    expect(request.request.body).toMatchObject({
      productId: 'product-1', quantityDelta: 1.5, reason: 'Reposición',
    });
    expect(request.request.body.operationId).toBeTruthy();
    request.flush({ operationId: request.request.body.operationId, onHand: 11.5,
      reserved: 2, available: 9.5 });
    fixture.detectChanges();
    expect(page.querySelector('.stock-row')?.textContent).toContain('11.5 kg');
  });

  it('rejects fractional quantities for unit products before sending a request', () => {
    const { fixture, page } = load();
    page.querySelectorAll<HTMLButtonElement>('.stock-row button')[1].click();
    fixture.detectChanges();
    const quantity = page.querySelector<HTMLInputElement>('#stock-delta')!;
    quantity.value = '1.5';
    quantity.dispatchEvent(new Event('input'));
    const reason = page.querySelector<HTMLInputElement>('#stock-reason')!;
    reason.value = 'Corrección';
    reason.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    page.querySelector<HTMLFormElement>('.stock-form')!.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
    expect(page.querySelector('.error-message')?.textContent).toContain('entera');
  });
});
