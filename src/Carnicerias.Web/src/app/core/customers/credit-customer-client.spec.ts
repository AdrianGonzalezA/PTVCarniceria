import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { CreditCustomerClient } from './credit-customer-client';

describe('CreditCustomerClient', () => {
  beforeEach(() => TestBed.configureTestingModule({
    providers: [provideHttpClient(), provideHttpClientTesting()],
  }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('searches only the credit-options endpoint with the current session', () => {
    TestBed.inject(CreditCustomerClient).search('CLI-CC').subscribe();

    const request = TestBed.inject(HttpTestingController).expectOne((item) =>
      item.url === '/api/customers/credit-options' && item.params.get('search') === 'CLI-CC');
    expect(request.request.method).toBe('GET');
    expect(request.request.withCredentials).toBe(true);
    request.flush([{ id: 'customer-id', code: 'CLI-CC', name: 'Cliente' }]);
  });
});
