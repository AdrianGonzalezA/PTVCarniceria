import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { CentralBusinessClient } from './central-business-client';

describe('CentralBusinessClient', () => {
  it('requests today without browser timezone or tenant parameters', () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const client = TestBed.inject(CentralBusinessClient);
    const http = TestBed.inject(HttpTestingController);

    client.today().subscribe();

    const request = http.expectOne('/api/v1/business/summary');
    expect(request.request.method).toBe('GET');
    expect(request.request.credentials).toBe('same-origin');
    request.flush({ window: {}, retrievedAtUtc: '', completeness: 'empty', aggregate: null, branches: [] });
    http.verify();
  });

  it('sends inclusive Argentina date selections without installation IDs', () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const client = TestBed.inject(CentralBusinessClient);
    const http = TestBed.inject(HttpTestingController);

    client.dates('2026-10-14', '2026-10-15').subscribe();

    const request = http.expectOne('/api/v1/business/summary?fromDate=2026-10-14&toDate=2026-10-15');
    expect(request.request.method).toBe('GET');
    expect(request.request.credentials).toBe('same-origin');
    request.flush({ window: {}, retrievedAtUtc: '', completeness: 'empty', aggregate: null, branches: [] });
    http.verify();
  });
});
