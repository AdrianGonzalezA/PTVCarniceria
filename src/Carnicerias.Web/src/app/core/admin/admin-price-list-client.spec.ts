import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AdminPriceListClient } from './admin-price-list-client';

describe('AdminPriceListClient', () => {
  beforeEach(() => TestBed.configureTestingModule({
    providers: [provideHttpClient(), provideHttpClientTesting()],
  }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('assigns a list to a branch explicitly', () => {
    const client = TestBed.inject(AdminPriceListClient);
    client.assignBranch('list-1', 'branch-1', true).subscribe();
    const request = TestBed.inject(HttpTestingController)
      .expectOne('/api/admin/price-lists/list-1/branches/branch-1');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ isActive: true });
    expect(request.request.withCredentials).toBe(true);
    request.flush({ branchId: 'branch-1', branchName: 'Centro', branchActive: true, isAssigned: true });
  });

  it('sends price changes separately from list and product updates', () => {
    const client = TestBed.inject(AdminPriceListClient);
    client.setPrice('list-1', 'product-1', 1200.5).subscribe();
    const request = TestBed.inject(HttpTestingController)
      .expectOne('/api/admin/price-lists/list-1/products/product-1/price');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ amount: 1200.5 });
    request.flush({ id: 'price-1', amount: 1200.5, effectiveFromUtc: '2026-10-08T00:00:00Z' });
  });
});
