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

  it('themes stock quantities and adjustment modal without writing an adjustment', () => {
    const root = document.documentElement;
    const previousTheme = root.getAttribute('data-visual-theme');
    try {
      root.dataset['visualTheme'] = 'original';
      const { fixture, http, page } = load();
      expect(getComputedStyle(page).getPropertyValue('--admin-stock-muted').trim()).toBe('#655d52');
      page.querySelector<HTMLButtonElement>('.stock-row button')!.click();
      fixture.detectChanges();
      expect(page.querySelector('#stock-adjustment-title')?.textContent).toContain('Asado');

      root.dataset['visualTheme'] = 'nocturno';
      expect(getComputedStyle(page).getPropertyValue('--admin-stock-muted').trim()).toBe('#bbb8b3');
      expect(page.querySelector('dialog[open] #stock-delta')).not.toBeNull();
      http.expectNone('/api/inventory/adjustments');
      fixture.destroy();
    } finally {
      if (previousTheme === null) root.removeAttribute('data-visual-theme');
      else root.setAttribute('data-visual-theme', previousTheme);
    }
  });

  it('shows branch stock and persists a positive adjustment with an operation id', () => {
    const { fixture, http, page } = load();
    expect(page.textContent).toContain('Centro');
    expect(page.querySelectorAll('.stock-row')).toHaveLength(2);
    page.querySelector<HTMLButtonElement>('.stock-row button')!.click();
    fixture.detectChanges();
    expect(page.querySelector('dialog.admin-detail-dialog[open]')).not.toBeNull();
    expect(page.querySelector('.category-panel .stock-form')).toBeNull();
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

  it('lets an adjustment omit its description and records a default audit reason', () => {
    const { fixture, http, page } = load();
    page.querySelector<HTMLButtonElement>('.stock-row button')!.click();
    fixture.detectChanges();
    expect(page.querySelector<HTMLInputElement>('#stock-reason')?.required).toBe(false);
    const quantity = page.querySelector<HTMLInputElement>('#stock-delta')!;
    quantity.value = '2';
    quantity.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    page.querySelector<HTMLFormElement>('.stock-form')!.dispatchEvent(new Event('submit'));
    const request = http.expectOne('/api/inventory/adjustments');
    expect(request.request.body).toMatchObject({ productId: 'product-1', quantityDelta: 2,
      reason: 'Ajuste manual' });
    request.flush({ operationId: request.request.body.operationId, onHand: 12,
      reserved: 2, available: 10 });
    fixture.detectChanges();
    expect(page.querySelector('dialog.admin-detail-dialog')).toBeNull();
  });

  it('previews a stock variation workbook before applying and refreshes balances', () => {
    const { fixture, http, page } = load();
    expect(page.querySelector<HTMLAnchorElement>('.import-template')?.getAttribute('href'))
      .toBe('/api/admin/imports/stock/template');
    page.querySelector<HTMLButtonElement>('.import-open')!.click();
    fixture.detectChanges();
    expect(page.textContent).toContain('Las variaciones positivas suman');
    const file = new File(['xlsx'], 'existencias.xlsx',
      { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });
    const input = page.querySelector<HTMLInputElement>('input[type=file]')!;
    Object.defineProperty(input, 'files', { configurable: true, value: [file] });
    input.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    page.querySelector<HTMLButtonElement>('.import-footer button:nth-child(2)')!.click();
    http.expectOne('/api/admin/imports/stock/preview').flush({ canApply: true,
      createCount: 0, updateCount: 1, skippedCount: 0, unchangedCount: 0,
      rows: [{ sheet: 'Existencias', row: 2, status: 'update', message: 'Asado: 10 → 12 kg.' }],
      issues: [] });
    fixture.detectChanges();
    expect(page.textContent).toContain('Asado: 10 → 12 kg.');
    page.querySelector<HTMLButtonElement>('.import-footer button:nth-child(3)')!.click();
    const apply = http.expectOne('/api/admin/imports/stock/apply');
    expect(apply.request.headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/i);
    apply.flush({ canApply: true, createCount: 0, updateCount: 1,
      skippedCount: 0, unchangedCount: 0, rows: [], issues: [] });
    http.expectOne('/api/inventory/stock').flush([]);
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
