import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { PriceListsPage } from './price-lists-page';

describe('PriceListsPage', () => {
  const session = {
    userId: 'admin-id', username: 'visual-admin', expiresAtUtc: '2026-10-09T00:00:00Z',
    context: { userId: 'admin-id', companyId: 'company-id', companyName: 'Empresa Visual',
      branchId: 'branch-id', branchName: 'Centro', sessionId: 'session-id',
      permissions: ['catalog.manage', 'platform.users.manage'] },
  };
  const list = { id: 'list-1', name: 'Mostrador', isActive: true,
    activeBranchCount: 1, currentPriceCount: 1 };

  beforeEach(() => TestBed.configureTestingModule({
    imports: [PriceListsPage],
    providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
  }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  function load(items = [list]) {
    const fixture = TestBed.createComponent(PriceListsPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/sessions/current').flush(session);
    http.expectOne((request) => request.url === '/api/admin/price-lists').flush({
      items, page: 1, pageSize: 20, totalItems: items.length,
      totalPages: items.length ? 1 : 0,
    });
    fixture.detectChanges();
    return { fixture, http, page: fixture.nativeElement as HTMLElement };
  }

  it('shows list status, branch count and price count', () => {
    const { page } = load();
    expect(page.querySelector('h1')?.textContent).toContain('Listas de precios');
    expect(page.querySelector('tbody')?.textContent).toContain('Mostrador');
    expect(page.querySelector('tbody')?.textContent).toContain('1 sucursal');
    expect(page.querySelector('tbody')?.textContent).toContain('1 precio');
  });

  it('creates a price list and refreshes the listing', () => {
    const { fixture, http, page } = load([]);
    page.querySelector<HTMLButtonElement>('.create-button')!.click();
    fixture.detectChanges();
    const name = page.querySelector<HTMLInputElement>('#price-list-name')!;
    name.value = 'Mayorista';
    name.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    page.querySelector<HTMLFormElement>('.editor-form')!.dispatchEvent(new Event('submit'));
    const create = http.expectOne('/api/admin/price-lists');
    expect(create.request.body).toEqual({ name: 'Mayorista' });
    create.flush({ ...list, id: 'list-2', name: 'Mayorista', activeBranchCount: 0, currentPriceCount: 0 });
    http.expectOne((request) => request.url === '/api/admin/price-lists').flush({
      items: [{ ...list, id: 'list-2', name: 'Mayorista', activeBranchCount: 0, currentPriceCount: 0 }],
      page: 1, pageSize: 20, totalItems: 1, totalPages: 1,
    });
    fixture.detectChanges();
    expect(page.querySelector('tbody')?.textContent).toContain('Mayorista');
  });

  it('assigns the selected list to a branch', () => {
    const { fixture, http, page } = load();
    page.querySelector<HTMLButtonElement>('tbody .branches-button')!.click();
    http.expectOne('/api/admin/price-lists/list-1/branches').flush([
      { branchId: 'branch-1', branchName: 'Centro', branchActive: true, isAssigned: false },
    ]);
    fixture.detectChanges();
    expect(page.querySelector('dialog[open] .branch-row')).not.toBeNull();
    page.querySelector<HTMLButtonElement>('.branch-row button')!.click();
    const assign = http.expectOne('/api/admin/price-lists/list-1/branches/branch-1');
    expect(assign.request.body).toEqual({ isActive: true });
    assign.flush({ branchId: 'branch-1', branchName: 'Centro', branchActive: true, isAssigned: true });
    fixture.detectChanges();
    expect(page.querySelector('.branch-row')?.textContent).toContain('Habilitada');
  });

  it('changes a product price and shows saved history', () => {
    const { fixture, http, page } = load();
    page.querySelector<HTMLButtonElement>('tbody .prices-button')!.click();
    http.expectOne((request) => request.url === '/api/admin/price-lists/list-1/prices').flush({
      items: [{ productId: 'product-1', code: 'VAC-001', name: 'Asado', unit: 'kg',
        cost: 500, isActive: true, currentPrice: 600, effectiveFromUtc: '2026-10-08T00:00:00Z' }],
      page: 1, pageSize: 20, totalItems: 1, totalPages: 1,
    });
    fixture.detectChanges();
    page.querySelector<HTMLButtonElement>('.price-row .set-price-button')!.click();
    fixture.detectChanges();
    const amount = page.querySelector<HTMLInputElement>('#new-price')!;
    amount.value = '700,50';
    amount.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    page.querySelector<HTMLFormElement>('.price-form')!.dispatchEvent(new Event('submit'));
    const change = http.expectOne('/api/admin/price-lists/list-1/products/product-1/price');
    expect(change.request.body).toEqual({ amount: 700.5 });
    change.flush({ id: 'price-2', amount: 700.5, effectiveFromUtc: '2026-10-08T01:00:00Z' });
    fixture.detectChanges();
    expect(page.querySelector('.price-row')?.textContent).toContain('700.50');
    page.querySelector<HTMLButtonElement>('.price-row .history-button')!.click();
    http.expectOne('/api/admin/price-lists/list-1/products/product-1/history').flush([
      { id: 'price-2', amount: 700.5, effectiveFromUtc: '2026-10-08T01:00:00Z',
        effectiveToUtc: null, changedByUsername: 'visual-admin' },
    ]);
    fixture.detectChanges();
    expect(page.querySelector('.price-history')?.textContent).toContain('visual-admin');
    expect(page.querySelectorAll('dialog[open]').length).toBe(1);
    expect(page.querySelector('.detail-panel')).toBeNull();
    page.querySelector<HTMLButtonElement>('.price-history button')!.click();
    fixture.detectChanges();
    expect(page.querySelector('dialog[open] .detail-panel')).not.toBeNull();
  });
});
