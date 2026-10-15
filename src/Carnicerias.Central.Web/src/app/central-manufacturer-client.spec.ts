import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { CentralManufacturerClient } from './central-manufacturer-client';

describe('CentralManufacturerClient', () => {
  it('uses only same-origin manufacturer routes and never returns credentials', () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const client = TestBed.inject(CentralManufacturerClient);
    const http = TestBed.inject(HttpTestingController);

    client.login('superadmin-central', 'a distinct central password').subscribe();
    const login = http.expectOne('/api/v1/superadmin/sessions');
    expect(login.request.method).toBe('POST');
    expect(login.request.credentials).toBe('same-origin');
    expect(login.request.headers.get('X-Carnicerias-CSRF')).toBe('1');
    login.flush({ userId: 'm-1', username: 'superadmin-central', expiresAtUtc: '2026-10-15T20:00:00Z' });

    client.companies(1).subscribe();
    http.expectOne('/api/v1/superadmin/companies?page=1&pageSize=20').flush({
      data: [], pagination: { page: 1, pageSize: 20, totalCount: 0, totalPages: 0 },
    });

    client.createCompany({ code: 'RUSTICA-CENTRAL', name: 'LA Rústica',
      initialAdministratorUsername: 'admin-rustica',
      initialAdministratorPassword: 'a distinct tenant password' }).subscribe();
    const create = http.expectOne('/api/v1/superadmin/companies');
    expect(create.request.headers.get('X-Carnicerias-CSRF')).toBe('1');
    expect(create.request.body.initialAdministratorPassword).toBe('a distinct tenant password');
    create.flush({ id: 'c-1', code: 'RUSTICA-CENTRAL', name: 'LA Rústica', initialAdministratorId: 'a-1' });

    client.administrators('c-1', 1).subscribe();
    http.expectOne('/api/v1/superadmin/companies/c-1/administrators?page=1&pageSize=20').flush({
      data: [], pagination: { page: 1, pageSize: 20, totalCount: 0, totalPages: 0 },
    });

    client.addAdministrator('c-1', { username: 'another-admin', password: 'another password' }).subscribe();
    const add = http.expectOne('/api/v1/superadmin/companies/c-1/administrators');
    expect(add.request.headers.get('X-Carnicerias-CSRF')).toBe('1');
    add.flush({ id: 'a-2', username: 'another-admin', isActive: true });
    http.verify();
  });
});
