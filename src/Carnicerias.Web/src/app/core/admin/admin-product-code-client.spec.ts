import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AdminProductCodeClient } from './admin-product-code-client';

describe('AdminProductCodeClient', () => {
  beforeEach(() => TestBed.configureTestingModule({
    providers: [provideHttpClient(), provideHttpClientTesting()],
  }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('lists and changes alternate codes without deleting them', () => {
    const client = TestBed.inject(AdminProductCodeClient);
    const http = TestBed.inject(HttpTestingController);

    client.list('product-1').subscribe();
    const list = http.expectOne('/api/admin/products/product-1/codes');
    expect(list.request.method).toBe('GET');
    list.flush([{ code: '779123456', isActive: true }]);

    client.changeState('product-1', '779123456', false).subscribe();
    const change = http.expectOne('/api/admin/products/product-1/codes');
    expect(change.request.method).toBe('PATCH');
    expect(change.request.body).toEqual({ code: '779123456', isActive: false });
    change.flush({ code: '779123456', isActive: false });
  });
});
