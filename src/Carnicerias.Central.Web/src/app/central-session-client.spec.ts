import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { CentralSessionClient } from './central-session-client';

describe('CentralSessionClient', () => {
  it('uses only relative central routes and does not persist tokens', () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const client = TestBed.inject(CentralSessionClient);
    const http = TestBed.inject(HttpTestingController);

    client.login('VISUAL', 'central-admin', 'a unique laboratory password').subscribe();
    const login = http.expectOne('/api/v1/sessions');
    expect(login.request.method).toBe('POST');
    expect(login.request.credentials).toBe('same-origin');
    expect(login.request.headers.get('X-Carnicerias-CSRF')).toBe('1');
    expect(login.request.body).toEqual({
      companyCode: 'VISUAL', credential: 'central-admin', password: 'a unique laboratory password',
    });
    login.flush({ userId: '1', centralCompanyId: '2', username: 'central-admin', expiresAtUtc: '2026-10-14T23:00:00Z' });

    client.current().subscribe();
    const current = http.expectOne('/api/v1/sessions/current');
    expect(current.request.method).toBe('GET');
    current.flush({ userId: '1', centralCompanyId: '2', username: 'central-admin', expiresAtUtc: '2026-10-14T23:00:00Z' });

    client.logout().subscribe();
    const logout = http.expectOne('/api/v1/sessions/current');
    expect(logout.request.method).toBe('DELETE');
    expect(logout.request.headers.get('X-Carnicerias-CSRF')).toBe('1');
    logout.flush(null);
    http.verify();
  });
});
