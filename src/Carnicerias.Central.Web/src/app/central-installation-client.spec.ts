import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { CentralInstallationClient } from './central-installation-client';

describe('CentralInstallationClient', () => {
  it('requests a paginated same-origin list without a browser-supplied tenant', () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const client = TestBed.inject(CentralInstallationClient);
    const http = TestBed.inject(HttpTestingController);
    client.list(2).subscribe();
    const request = http.expectOne('/api/v1/installations?page=2&pageSize=20');
    expect(request.request.method).toBe('GET');
    expect(request.request.credentials).toBe('same-origin');
    request.flush({ data: [], pagination: { page: 2, pageSize: 20, totalCount: 0, totalPages: 0 } });
    http.verify();
  });
});
