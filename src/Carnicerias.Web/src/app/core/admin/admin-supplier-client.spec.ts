import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AdminSupplierClient } from './admin-supplier-client';

describe('AdminSupplierClient', () => {
  beforeEach(() => TestBed.configureTestingModule({
    providers: [provideHttpClient(), provideHttpClientTesting()],
  }));

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('keeps company scope on the server and carries pagination and search', () => {
    const client = TestBed.inject(AdminSupplierClient);
    client.list(2, 'frigo').subscribe();
    const request = TestBed.inject(HttpTestingController).expectOne(
      (item) => item.url === '/api/admin/suppliers',
    );
    expect(request.request.method).toBe('GET');
    expect(request.request.withCredentials).toBe(true);
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('search')).toBe('frigo');
    request.flush({ items: [], page: 2, pageSize: 20, totalItems: 0, totalPages: 0 });
  });

  it('creates, edits and deactivates a supplier without a client-supplied company', () => {
    const client = TestBed.inject(AdminSupplierClient);
    client.create({ code: 'FR1', name: 'Frigorífico', identification: '30710106513' }).subscribe();
    const create = TestBed.inject(HttpTestingController).expectOne('/api/admin/suppliers');
    expect(create.request.body).toEqual({ code: 'FR1', name: 'Frigorífico', identification: '30710106513' });
    create.flush({ id: 's1', code: 'FR1', name: 'Frigorífico', identification: '30710106513', isActive: true });

    client.update('s1', { code: 'FR1', name: 'Nuevo', identification: null }).subscribe();
    const edit = TestBed.inject(HttpTestingController).expectOne('/api/admin/suppliers/s1');
    expect(edit.request.method).toBe('PATCH');
    expect(edit.request.body).toEqual({ code: 'FR1', name: 'Nuevo', identification: null });
    edit.flush({ id: 's1', code: 'FR1', name: 'Nuevo', identification: null, isActive: true });

    client.update('s1', { isActive: false }).subscribe();
    const deactivate = TestBed.inject(HttpTestingController).expectOne('/api/admin/suppliers/s1');
    expect(deactivate.request.body).toEqual({ isActive: false });
    deactivate.flush({ id: 's1', code: 'FR1', name: 'Nuevo', identification: null, isActive: false });
  });
});
