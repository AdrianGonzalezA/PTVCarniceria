import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { WelcomePage } from './welcome-page';

describe('WelcomePage', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [WelcomePage],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
  });

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('renders the public page and reports API availability', () => {
    const fixture = TestBed.createComponent(WelcomePage);

    fixture.detectChanges();

    const http = TestBed.inject(HttpTestingController);
    const request = http.expectOne('/api/health');
    expect(request.request.method).toBe('GET');
    request.flush({ status: 'healthy' });
    http.expectOne('/api/sessions/current').flush(null, { status: 401, statusText: 'Unauthorized' });
    fixture.detectChanges();

    const page = fixture.nativeElement as HTMLElement;
    expect(page.querySelector('h1')?.textContent).toContain('Carnicerías');
    expect(page.querySelector('[role="status"]')?.textContent).toContain('Sistema disponible');
    expect(page.querySelector('form h2')?.textContent).toContain('Iniciar sesión');
    expect(page.querySelector('label[for="credential"]')?.textContent).toContain('Usuario o correo');
  });

  it('reports when the API is unavailable', () => {
    const fixture = TestBed.createComponent(WelcomePage);

    fixture.detectChanges();

    const http = TestBed.inject(HttpTestingController);
    const request = http.expectOne('/api/health');
    request.flush(null, { status: 503, statusText: 'Service Unavailable' });
    http.expectOne('/api/sessions/current').flush(null, { status: 401, statusText: 'Unauthorized' });
    fixture.detectChanges();

    const page = fixture.nativeElement as HTMLElement;
    expect(page.querySelector('[role="status"]')?.textContent).toContain(
      'Sistema temporalmente no disponible',
    );
  });

  it('takes an administrator with a confirmed context to the admin area', () => {
    const navigation = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    const fixture = TestBed.createComponent(WelcomePage);
    fixture.detectChanges();

    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/health').flush({ status: 'healthy' });
    http.expectOne('/api/sessions/current').flush({
      userId: 'admin-id',
      username: 'visual-admin',
      expiresAtUtc: '2026-10-08T00:00:00Z',
      context: {
        userId: 'admin-id',
        companyId: 'company-id',
        companyName: 'Empresa Visual',
        branchId: 'branch-id',
        branchName: 'Sucursal Visual',
        permissions: ['platform.users.manage'],
        sessionId: 'session-id',
      },
    });

    expect(navigation).toHaveBeenCalledWith('/admin');
  });

  it('shows a generic message after a rejected sign-in', () => {
    const fixture = TestBed.createComponent(WelcomePage);
    fixture.detectChanges();

    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/health').flush({ status: 'healthy' });
    http.expectOne('/api/sessions/current').flush(null, { status: 401, statusText: 'Unauthorized' });
    fixture.detectChanges();

    const page = fixture.nativeElement as HTMLElement;
    const credential = page.querySelector<HTMLInputElement>('#credential')!;
    const password = page.querySelector<HTMLInputElement>('#password')!;
    credential.value = 'unknown';
    credential.dispatchEvent(new Event('input'));
    password.value = 'incorrect password';
    password.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    page.querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit'));
    const loginRequest = http.expectOne('/api/sessions');
    expect(loginRequest.request.body).toEqual({ credential: 'unknown', password: 'incorrect password' });
    loginRequest.flush(
      { error: { code: 'AUTHENTICATION_FAILED' } },
      { status: 401, statusText: 'Unauthorized' },
    );
    fixture.detectChanges();

    expect(page.querySelector('[role="alert"]')?.textContent).toContain('Usuario o contraseña incorrectos.');
  });
});
