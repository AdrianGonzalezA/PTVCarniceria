import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { BarcodeLayoutPage } from './barcode-layout-page';

describe('BarcodeLayoutPage', () => {
  beforeEach(() => TestBed.configureTestingModule({
    imports: [BarcodeLayoutPage],
    providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
  }));

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('previews configured fields and weight without adding stock', () => {
    const fixture = TestBed.createComponent(BarcodeLayoutPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/sessions/current').flush({
      userId: 'admin-id', username: 'visual-admin', expiresAtUtc: '2026-10-08T00:00:00Z',
      context: {
        userId: 'admin-id', companyId: 'company-id', companyName: 'Empresa Visual',
        branchId: 'branch-id', branchName: 'Sucursal Visual',
        permissions: ['inventory.stock.manage'], sessionId: 'session-id',
      },
    });
    fixture.detectChanges();

    const page = fixture.nativeElement as HTMLElement;
    const formula = page.querySelector<HTMLInputElement>('#barcode-formula')!;
    formula.value = 'pro_numero(5) pro_item(3) peso(4)';
    formula.dispatchEvent(new Event('input'));
    const code = page.querySelector<HTMLInputElement>('#barcode-code')!;
    code.value = '123450070245';
    code.dispatchEvent(new Event('input'));
    const decimals = page.querySelector<HTMLInputElement>('#barcode-decimals')!;
    decimals.value = '2';
    decimals.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    page.querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit'));

    const preview = http.expectOne('/api/admin/barcode-layouts/preview');
    expect(preview.request.method).toBe('POST');
    expect(preview.request.body).toEqual({
      formula: 'pro_numero(5) pro_item(3) peso(4)', code: '123450070245',
      weightField: 'peso', weightDecimals: 2,
    });
    preview.flush({
      length: 12, fields: { pro_numero: '12345', pro_item: '007', peso: '0245' }, weightKg: 2.45,
    });
    fixture.detectChanges();

    expect(page.textContent).toContain('2,45 kg');
    expect(page.textContent).toContain('12345');
    expect(page.textContent).toContain('007');
    expect(http.match('/api/inventory/adjustments')).toHaveLength(0);
  });
});
