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
    http.expectOne('/api/inventory/stock').flush([{ productId: 'product-1', code: 'P1', alternateCodes: ['ALT-P1'],
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

  it('finds stock by an active alternate code without changing the item', () => {
    const { fixture, page } = load();
    const search = page.querySelector<HTMLInputElement>('#stock-search')!;
    search.value = 'alt-p1';
    search.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(page.querySelectorAll('.stock-row')).toHaveLength(1);
    expect(page.querySelector('.stock-row')?.textContent).toContain('Asado');
    expect(page.querySelector('.stock-row')?.textContent).not.toContain('Chorizo');
  });

  it('shows kilograms with three decimals and Argentine separators', () => {
    const { page } = load();
    expect(page.querySelector('.stock-row')?.textContent).toContain('10,000 kg');
  });

  it('shows generic and traceable availability separately when generic stock is in debt', () => {
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
      name: 'Asado', unit: 'kg', saleMode: 'weight', onHand: -3, reserved: 0,
      quarantined: 0, available: -3, genericAvailable: -5, protectedAvailable: 2,
      allowSaleWithoutStock: true, hasProtectedPieces: true }]);
    fixture.detectChanges();
    const row = (fixture.nativeElement as HTMLElement).querySelector('.stock-row')!;
    expect(row.textContent).toContain('-3,000 kg');
    expect(row.textContent).toContain('-5,000 kg');
    expect(row.textContent).toContain('2,000 kg');
  });

  it('focuses the quantity field on adjustment and selects it when revisited', () => {
    const { fixture, page } = load();
    page.querySelector<HTMLButtonElement>('.stock-row button')!.click();
    fixture.detectChanges();
    const quantity = page.querySelector<HTMLInputElement>('#stock-delta')!;
    expect(document.activeElement).toBe(quantity);
    quantity.value = '1.250';
    page.querySelector<HTMLInputElement>('#stock-reason')!.focus();
    quantity.focus();
    expect(quantity.selectionStart).toBe(0);
    expect(quantity.selectionEnd).toBe(quantity.value.length);
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
    expect(page.querySelector('.stock-row')?.textContent).toContain('11,500 kg');
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

  it('protects quarantined weight and disables adjustment during a physical count', () => {
    const fixture = TestBed.createComponent(StockPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/sessions/current').flush({ userId: 'admin-id', username: 'admin',
      expiresAtUtc: '', context: { userId: 'admin-id', companyId: 'company-1',
        companyName: 'Empresa', branchId: 'branch-1', branchName: 'Centro',
        sessionId: 'session-id', permissions: ['inventory.stock.manage'] } });
    http.expectOne('/api/inventory/stock').flush([
      { productId: 'p1', code: '1', name: 'Asado', unit: 'kg', saleMode: 'weight',
        onHand: 10, reserved: 2, quarantined: 5, available: 3, reconciliationLocked: false },
      { productId: 'p2', code: '2', name: 'Aguja', unit: 'kg', saleMode: 'weight',
        onHand: 10, reserved: 0, quarantined: 1, available: 0, reconciliationLocked: true },
    ]);
    fixture.detectChanges();
    const page = fixture.nativeElement as HTMLElement;
    expect(page.querySelectorAll<HTMLButtonElement>('.stock-row button')[1].disabled).toBe(true);
    expect(page.textContent).toContain('Inmovilizado');
    page.querySelectorAll<HTMLButtonElement>('.stock-row button')[0].click();
    fixture.detectChanges();
    const quantity = page.querySelector<HTMLInputElement>('#stock-delta')!;
    quantity.value = '-4';
    quantity.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    page.querySelector<HTMLFormElement>('.stock-form')!.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
    expect(page.querySelector('.error-message')?.textContent).toContain('inmovilizada');
    http.expectNone('/api/inventory/adjustments');
  });
});
