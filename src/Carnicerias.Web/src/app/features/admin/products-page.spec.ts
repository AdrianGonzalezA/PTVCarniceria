import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ProductsPage } from './products-page';

describe('ProductsPage', () => {
  const adminSession = {
    userId: 'admin-id', username: 'visual-admin', expiresAtUtc: '2026-10-09T00:00:00Z',
    context: {
      userId: 'admin-id', companyId: 'company-id', companyName: 'Empresa Visual',
      branchId: 'branch-id', branchName: 'Sucursal Visual',
      permissions: ['platform.users.manage', 'catalog.manage'], sessionId: 'session-id',
    },
  };
  const product = { id: 'product-1', categoryId: 'cat-1', categoryName: 'Vacuno',
    code: 'VAC-001', name: 'Asado', unit: 'kg', saleMode: 'weight', cost: 100,
    isActive: true, alternateCodeCount: 0 };

  beforeEach(() => TestBed.configureTestingModule({
    imports: [ProductsPage],
    providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
  }));

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  function load(items = [product]) {
    const fixture = TestBed.createComponent(ProductsPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/sessions/current').flush(adminSession);
    http.expectOne('/api/admin/categories/options').flush([{ id: 'cat-1', name: 'Vacuno' }]);
    http.expectOne((request) => request.url === '/api/admin/products').flush({
      items, page: 1, pageSize: 20, totalItems: items.length,
      totalPages: items.length ? 1 : 0,
    });
    fixture.detectChanges();
    return { fixture, http, page: fixture.nativeElement as HTMLElement };
  }

  it('shows catalog properties including the unit and sale mode', () => {
    const { page } = load();
    expect(page.querySelector('h1')?.textContent).toContain('Artículos');
    expect(page.querySelector('tbody')?.textContent).toContain('VAC-001');
    expect(page.querySelector('tbody')?.textContent).toContain('Por peso');
    expect(page.querySelector('tbody')?.textContent).toContain('kg');
  });

  it('creates a new item without sending stock or sale price', () => {
    const { fixture, http, page } = load([]);
    page.querySelector<HTMLButtonElement>('.create-button')!.click();
    fixture.detectChanges();
    for (const [id, value] of Object.entries({
      'product-code': 'CER-001', 'product-name': 'Bondiola',
      'product-unit': 'kg', 'product-cost': '400,50',
    })) {
      const input = page.querySelector<HTMLInputElement>(`#${id}`)!;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    }
    fixture.detectChanges();
    page.querySelector<HTMLFormElement>('.editor-form')!.dispatchEvent(new Event('submit'));

    const request = http.expectOne('/api/admin/products');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ categoryId: 'cat-1', code: 'CER-001',
      name: 'Bondiola', unit: 'kg', saleMode: 'weight', cost: 400.5 });
    request.flush({ ...product, id: 'product-2', code: 'CER-001', name: 'Bondiola' });
    http.expectOne((candidate) => candidate.url === '/api/admin/products').flush({
      items: [{ ...product, id: 'product-2', code: 'CER-001', name: 'Bondiola' }],
      page: 1, pageSize: 20, totalItems: 1, totalPages: 1,
    });
    fixture.detectChanges();
    expect(page.querySelector('tbody')?.textContent).toContain('Bondiola');
  });

  it('keeps the principal code read-only while editing', () => {
    const { fixture, http, page } = load();
    page.querySelector<HTMLButtonElement>('tbody .edit-button')!.click();
    http.expectOne('/api/admin/products/product-1/codes').flush([]);
    fixture.detectChanges();
    expect(page.querySelector('dialog[aria-labelledby="product-editor-title"]')).not.toBeNull();
    expect(page.querySelector<HTMLInputElement>('#product-code')?.readOnly).toBe(true);
    const name = page.querySelector<HTMLInputElement>('#product-name')!;
    name.value = 'Asado especial';
    name.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    page.querySelector<HTMLFormElement>('.editor-form')!.dispatchEvent(new Event('submit'));
    const request = http.expectOne('/api/admin/products/product-1');
    expect(request.request.body.code).toBeUndefined();
    request.flush({ ...product, name: 'Asado especial' });
    fixture.detectChanges();
    expect(page.querySelector('tbody')?.textContent).toContain('Asado especial');
  });

  it('adds an alternate code and preserves it when inactivated', () => {
    const confirmation = vi.spyOn(window, 'confirm').mockReturnValue(true);
    const { fixture, http, page } = load();
    page.querySelector<HTMLButtonElement>('tbody .edit-button')!.click();
    http.expectOne('/api/admin/products/product-1/codes').flush([]);
    fixture.detectChanges();
    const alternate = page.querySelector<HTMLInputElement>('#alternate-code')!;
    alternate.value = '779123456';
    alternate.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    page.querySelector<HTMLButtonElement>('.add-code-button')!.click();
    const create = http.expectOne('/api/admin/products/product-1/codes');
    expect(create.request.method).toBe('POST');
    expect(create.request.body).toEqual({ code: '779123456' });
    create.flush({ code: '779123456', isActive: true });
    fixture.detectChanges();
    expect(page.querySelector('.alternate-codes')?.textContent).toContain('779123456');

    page.querySelector<HTMLButtonElement>('.alternate-codes .toggle-code-button')!.click();
    const change = http.expectOne('/api/admin/products/product-1/codes');
    expect(change.request.body).toEqual({ code: '779123456', isActive: false });
    change.flush({ code: '779123456', isActive: false });
    fixture.detectChanges();
    expect(page.querySelector('.alternate-codes')?.textContent).toContain('Inactivo');
    confirmation.mockRestore();
  });

  it('inactivates an article after confirmation', () => {
    const { fixture, http, page } = load();

    page.querySelector<HTMLButtonElement>('tbody .row-actions button:last-child')!.click();
    fixture.detectChanges();
    expect(page.querySelector('dialog[aria-labelledby="product-action-title"]')).not.toBeNull();
    http.expectNone('/api/admin/products/product-1');
    page.querySelector<HTMLButtonElement>('.confirm-product-action')!.click();
    const request = http.expectOne('/api/admin/products/product-1');
    expect(request.request.body).toEqual({ isActive: false });
    request.flush({ ...product, isActive: false });
    fixture.detectChanges();
    expect(page.querySelector('tbody')?.textContent).toContain('Inactivo');
    expect(page.querySelector('dialog[aria-labelledby="product-action-title"]')).toBeNull();
  });

  it('does not save a zero-cost article', () => {
    const { fixture, page } = load([]);
    page.querySelector<HTMLButtonElement>('.create-button')!.click();
    fixture.detectChanges();
    for (const [id, value] of Object.entries({
      'product-code': 'CER-001', 'product-name': 'Bondiola',
      'product-unit': 'kg', 'product-cost': '0',
    })) {
      const input = page.querySelector<HTMLInputElement>(`#${id}`)!;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    }
    fixture.detectChanges();
    page.querySelector<HTMLFormElement>('.editor-form')!.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
    expect(page.textContent).toContain('Ingresá un costo mayor que cero');
  });
});
