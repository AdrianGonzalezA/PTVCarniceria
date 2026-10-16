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

    client.check('i').subscribe();
    const check = http.expectOne('/api/v1/branch-connections/i/checks');
    expect(check.request.method).toBe('POST');
    expect(check.request.headers.get('X-Carnicerias-CSRF')).toBe('1');
    check.flush({});

    client.edit('i', { label: 'Nueva etiqueta' }).subscribe();
    const edit = http.expectOne('/api/v1/branch-connections/i');
    expect(edit.request.method).toBe('PATCH');
    expect(edit.request.headers.get('X-Carnicerias-CSRF')).toBe('1');
    expect(edit.request.body).toEqual({ label: 'Nueva etiqueta' });
    edit.flush({});

    client.changeState('i', false).subscribe();
    const state = http.expectOne('/api/v1/branch-connections/i/state');
    expect(state.request.method).toBe('PATCH');
    expect(state.request.headers.get('X-Carnicerias-CSRF')).toBe('1');
    expect(state.request.body).toEqual({ isActive: false });
    state.flush({});
    http.verify();
  });
});
