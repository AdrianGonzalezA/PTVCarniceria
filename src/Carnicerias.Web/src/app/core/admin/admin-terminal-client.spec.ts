import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AdminTerminalClient } from './admin-terminal-client';

describe('AdminTerminalClient', () => {
  beforeEach(() => TestBed.configureTestingModule({
    providers: [provideHttpClient(), provideHttpClientTesting()],
  }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('creates a terminal and receives the one-time displayed credential', () => {
    const client = TestBed.inject(AdminTerminalClient);
    client.create('company-1', 'branch-1', 'Caja 2').subscribe();
    const request = TestBed.inject(HttpTestingController)
      .expectOne('/api/admin/companies/company-1/branches/branch-1/terminals');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ name: 'Caja 2' });
    request.flush({ id: 'terminal-2', name: 'Caja 2', credential: 'secret' });
  });

  it('rotates a terminal credential without exposing old credentials', () => {
    const client = TestBed.inject(AdminTerminalClient);
    client.rotate('company-1', 'branch-1', 'terminal-1').subscribe();
    const request = TestBed.inject(HttpTestingController)
      .expectOne('/api/admin/companies/company-1/branches/branch-1/terminals/terminal-1/rotate');
    expect(request.request.method).toBe('POST');
    request.flush({ id: 'terminal-1', name: 'Caja 1', credential: 'new-secret' });
  });
});
