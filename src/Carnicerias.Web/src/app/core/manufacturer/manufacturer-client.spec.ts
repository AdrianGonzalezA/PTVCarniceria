import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ManufacturerClient } from './manufacturer-client';

describe('ManufacturerClient', () => {
  beforeEach(() => TestBed.configureTestingModule({
    providers: [provideHttpClient(), provideHttpClientTesting()],
  }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('uses a separate cookie session and sends no client company context', () => {
    const client = TestBed.inject(ManufacturerClient);
    client.login('superadmin', 'password').subscribe();
    const request = TestBed.inject(HttpTestingController).expectOne('/api/manufacturer/sessions');
    expect(request.request.withCredentials).toBe(true);
    expect(request.request.body).toEqual({ credential: 'superadmin', password: 'password' });
    request.flush({ userId: 'id', username: 'superadmin', expiresAtUtc: '2026-10-13T22:00:00Z' });
    expect(localStorage.length).toBe(0);
  });

  it('creates a company with its first branch and administrator', () => {
    const client = TestBed.inject(ManufacturerClient);
    const input = { name: 'Empresa B', code: 'EMPRESA-B', initialBranchName: 'Centro',
      adminUsername: 'admin', adminEmail: 'admin@example.test', adminPassword: 'password' };
    client.createCompany(input).subscribe();
    const request = TestBed.inject(HttpTestingController).expectOne('/api/manufacturer/companies');
    expect(request.request.body).toEqual(input);
    request.flush({ id: 'company-id', code: 'EMPRESA-B', name: 'Empresa B',
      initialBranchId: 'branch-id', initialAdministratorId: 'admin-id' });
  });

  it('creates an administrator under the selected company using the manufacturer session', () => {
    const client = TestBed.inject(ManufacturerClient);
    const input = { username: 'nuevo-admin', email: 'nuevo@empresa.test', password: 'password' };
    client.createAdministrator('company-id', input).subscribe();
    const request = TestBed.inject(HttpTestingController)
      .expectOne('/api/manufacturer/companies/company-id/administrators');
    expect(request.request.method).toBe('POST');
    expect(request.request.withCredentials).toBe(true);
    expect(request.request.body).toEqual(input);
    request.flush({ id: 'admin-id', username: input.username, email: input.email });
  });
});
