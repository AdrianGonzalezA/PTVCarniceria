import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { CentralBranchConnectionClient } from './central-branch-connection-client';

describe('CentralBranchConnectionClient', () => {
  it('uses tenant routes and sends CSRF only on mutations', () => {
    TestBed.configureTestingModule({
      providers: [CentralBranchConnectionClient, provideHttpClient(), provideHttpClientTesting()],
    });
    const client = TestBed.inject(CentralBranchConnectionClient);
    const http = TestBed.inject(HttpTestingController);
    client.list(2).subscribe();
    const list = http.expectOne('/api/v1/branch-connections?page=2&pageSize=20');
    expect(list.request.method).toBe('GET');
    list.flush({ data: [], pagination: { page: 2, pageSize: 20, totalCount: 0, totalPages: 0 } });

    client.create({
      installationId: 'i', remoteCompanyId: 'c', remoteBranchId: 'b', label: 'Sucursal',
      destinationUrl: 'https://branch.example/', token: 'A'.repeat(64),
    }).subscribe();
    const create = http.expectOne('/api/v1/branch-connections');
    expect(create.request.method).toBe('POST');
    expect(create.request.headers.get('X-Carnicerias-CSRF')).toBe('1');
    create.flush({ installationId: 'i', remoteCompanyId: 'c', remoteBranchId: 'b',
      label: 'Sucursal', destinationUrl: 'https://branch.example/', isActive: true,
      hasCredential: true });
    http.verify();
  });
});
