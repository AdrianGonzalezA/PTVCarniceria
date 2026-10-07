import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SessionClient } from './session-client';

describe('SessionClient', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
  });

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('posts credentials without exposing or persisting a session token', () => {
    TestBed.inject(SessionClient).login('admin', 'correct password').subscribe();

    const request = TestBed.inject(HttpTestingController).expectOne('/api/sessions');
    expect(request.request.method).toBe('POST');
    expect(request.request.withCredentials).toBe(true);
    expect(request.request.body).toEqual({ credential: 'admin', password: 'correct password' });
    request.flush({ userId: 'user-id', username: 'admin', expiresAtUtc: '2026-10-01T20:00:00Z' });
    expect(localStorage.length).toBe(0);
  });

  it('reads and revokes the current server-managed session', () => {
    const client = TestBed.inject(SessionClient);
    client.current().subscribe();
    const currentRequest = TestBed.inject(HttpTestingController).expectOne('/api/sessions/current');
    expect(currentRequest.request.method).toBe('GET');
    expect(currentRequest.request.withCredentials).toBe(true);
    currentRequest.flush({ userId: 'user-id', username: 'admin', expiresAtUtc: '2026-10-01T20:00:00Z' });

    client.logout().subscribe();
    const logoutRequest = TestBed.inject(HttpTestingController).expectOne('/api/sessions/current');
    expect(logoutRequest.request.method).toBe('DELETE');
    expect(logoutRequest.request.withCredentials).toBe(true);
    logoutRequest.flush(null, { status: 204, statusText: 'No Content' });
  });
});
