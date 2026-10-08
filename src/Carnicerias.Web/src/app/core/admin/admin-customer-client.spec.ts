import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AdminCustomerClient } from './admin-customer-client';

describe('AdminCustomerClient', () => {
  beforeEach(() => TestBed.configureTestingModule({
    providers: [provideHttpClient(), provideHttpClientTesting()],
  }));

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('creates customers without enabling credit by default', () => {
    TestBed.inject(AdminCustomerClient).create('CLI-1', 'Cliente').subscribe();

    const request = TestBed.inject(HttpTestingController).expectOne('/api/admin/customers');
    expect(request.request.method).toBe('POST');
    expect(request.request.withCredentials).toBe(true);
    expect(request.request.body).toEqual({ code: 'CLI-1', name: 'Cliente' });
    request.flush({ id: 'id', code: 'CLI-1', name: 'Cliente', isActive: true, creditEnabled: false });
  });

  it('sends an explicit credit-enablement change', () => {
    TestBed.inject(AdminCustomerClient).update('customer-id', { creditEnabled: true }).subscribe();

    const request = TestBed.inject(HttpTestingController)
      .expectOne('/api/admin/customers/customer-id');
    expect(request.request.method).toBe('PATCH');
    expect(request.request.body).toEqual({ creditEnabled: true });
    request.flush({ id: 'customer-id', code: 'CLI-1', name: 'Cliente', isActive: true, creditEnabled: true });
  });
});
