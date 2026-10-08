import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AdminProductClient } from './admin-product-client';

describe('AdminProductClient', () => {
  beforeEach(() => TestBed.configureTestingModule({
    providers: [provideHttpClient(), provideHttpClientTesting()],
  }));

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('lists products with company-scoped server filters', () => {
    const client = TestBed.inject(AdminProductClient);
    client.list(2, 'Asado', 'cat-1', 'inactive').subscribe();

    const request = TestBed.inject(HttpTestingController).expectOne((candidate) =>
      candidate.url === '/api/admin/products');
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('search')).toBe('Asado');
    expect(request.request.params.get('categoryId')).toBe('cat-1');
    expect(request.request.params.get('isActive')).toBe('false');
    expect(request.request.withCredentials).toBe(true);
    request.flush({ items: [], page: 2, pageSize: 20, totalItems: 0, totalPages: 0 });
  });

  it('creates a product without a price or stock payload', () => {
    const client = TestBed.inject(AdminProductClient);
    client.create({ categoryId: 'cat-1', code: 'VAC-001', name: 'Asado', unit: 'kg',
      saleMode: 'weight', cost: 100 }).subscribe();

    const request = TestBed.inject(HttpTestingController).expectOne('/api/admin/products');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ categoryId: 'cat-1', code: 'VAC-001',
      name: 'Asado', unit: 'kg', saleMode: 'weight', cost: 100 });
    request.flush({});
  });

  it('cannot send a new primary code in a product update', () => {
    const client = TestBed.inject(AdminProductClient);
    client.update('product-1', { name: 'Asado especial', isActive: false }).subscribe();

    const request = TestBed.inject(HttpTestingController).expectOne('/api/admin/products/product-1');
    expect(request.request.method).toBe('PATCH');
    expect(request.request.body).toEqual({ name: 'Asado especial', isActive: false });
    request.flush({});
  });
});
