import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SaleDraftClient } from './sale-draft-client';

describe('SaleDraftClient', () => {
  beforeEach(() => TestBed.configureTestingModule({
    providers: [provideHttpClient(), provideHttpClientTesting()],
  }));

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('sends the requested document and recipient with the sale confirmation', () => {
    TestBed.inject(SaleDraftClient).confirm('draft-id', [{ method: 'cash', amount: 2500 }], undefined, {
      documentType: 'electronicInvoice', recipientTaxStatus: 'registered',
      recipientName: 'Cliente', recipientDocumentNumber: '20000000001',
      recipientAddress: 'Calle 123',
    }).subscribe();

    const request = TestBed.inject(HttpTestingController)
      .expectOne('/api/sales/drafts/draft-id/confirmation');
    expect(request.request.method).toBe('POST');
    expect(request.request.withCredentials).toBe(true);
    expect(request.request.body).toEqual({
      payments: [{ method: 'cash', amount: 2500 }], documentType: 'electronicInvoice',
      recipientTaxStatus: 'registered', recipientName: 'Cliente',
      recipientDocumentNumber: '20000000001', recipientAddress: 'Calle 123',
    });
    request.flush({ id: 'sale-id' });
  });
});
