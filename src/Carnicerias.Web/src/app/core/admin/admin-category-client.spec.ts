import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AdminCategoryClient } from './admin-category-client';

describe('AdminCategoryClient', () => {
  beforeEach(() => TestBed.configureTestingModule({
    providers: [provideHttpClient(), provideHttpClientTesting()],
  }));

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('requests a filtered page of categories with the session cookie', () => {
    const client = TestBed.inject(AdminCategoryClient);
    client.list(2, 'Vacuno').subscribe();

    const request = TestBed.inject(HttpTestingController).expectOne(
      (item) => item.url === '/api/admin/categories',
    );
    expect(request.request.method).toBe('GET');
    expect(request.request.withCredentials).toBe(true);
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('search')).toBe('Vacuno');
    request.flush({ items: [], page: 2, pageSize: 20, totalItems: 0, totalPages: 0 });
  });

  it('creates and updates categories without sending a company id', () => {
    const client = TestBed.inject(AdminCategoryClient);
    client.create('Cerdo').subscribe();
    const create = TestBed.inject(HttpTestingController).expectOne('/api/admin/categories');
    expect(create.request.method).toBe('POST');
    expect(create.request.body).toEqual({ name: 'Cerdo' });
    create.flush({ id: 'id-1', name: 'Cerdo', isActive: true, productCount: 0 });

    client.update('id-1', { isActive: false }).subscribe();
    const update = TestBed.inject(HttpTestingController).expectOne('/api/admin/categories/id-1');
    expect(update.request.method).toBe('PATCH');
    expect(update.request.body).toEqual({ isActive: false });
    update.flush({ id: 'id-1', name: 'Cerdo', isActive: false, productCount: 0 });
  });
});
