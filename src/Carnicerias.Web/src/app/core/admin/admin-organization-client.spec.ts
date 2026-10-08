import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AdminOrganizationClient } from './admin-organization-client';

describe('AdminOrganizationClient', () => {
  beforeEach(() => TestBed.configureTestingModule({
    providers: [provideHttpClient(), provideHttpClientTesting()],
  }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('creates a company with an initial branch for the existing administrator', () => {
    const client = TestBed.inject(AdminOrganizationClient);
    client.createCompany('Empresa nueva', 'Principal').subscribe();
    const request = TestBed.inject(HttpTestingController).expectOne('/api/admin/companies');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ name: 'Empresa nueva', initialBranchName: 'Principal' });
    request.flush({ id: 'company-2', name: 'Empresa nueva',
      initialBranchId: 'branch-2', initialBranchName: 'Principal' });
  });

  it('inactivates a branch without deleting its record', () => {
    const client = TestBed.inject(AdminOrganizationClient);
    client.updateBranch('company-1', 'branch-1', { isActive: false }).subscribe();
    const request = TestBed.inject(HttpTestingController)
      .expectOne('/api/admin/companies/company-1/branches/branch-1');
    expect(request.request.method).toBe('PATCH');
    expect(request.request.body).toEqual({ isActive: false });
    request.flush({ id: 'branch-1', name: 'Centro', isActive: false, activeTerminalCount: 0 });
  });
});
