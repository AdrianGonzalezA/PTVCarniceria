import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { POS_ELECTRON_MODE } from '../../core/pos/pos-electron-mode';
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
    expect(page.querySelector('label[for="company-code"]')?.textContent).toContain('Código de empresa');
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

  it('identifies the provisioned company and register before Electron sign-in', () => {
    TestBed.overrideProvider(POS_ELECTRON_MODE, { useValue: true });
    const fixture = TestBed.createComponent(WelcomePage);
    fixture.detectChanges();

    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/health').flush({ status: 'healthy' });
    http.expectOne('/api/sessions/current').flush(null, { status: 401, statusText: 'Unauthorized' });
    const terminalRequest = http.expectOne('/api/pos-terminals/current');
    expect(terminalRequest.request.method).toBe('GET');
    terminalRequest.flush({
      id: 'terminal-id', name: 'Caja 1', companyId: 'company-id', companyName: 'Empresa Visual',
      branchId: 'branch-id', branchName: 'Sucursal Centro',
    });
    fixture.detectChanges();

    const page = fixture.nativeElement as HTMLElement;
    expect(page.querySelector('.terminal-company')?.textContent).toContain('Empresa Visual');
    expect(page.querySelector('.terminal-location')?.textContent).toContain('Sucursal Centro');
    expect(page.querySelector('.terminal-location')?.textContent).toContain('Caja 1');
    expect(page.querySelector('#company-code')).toBeNull();
    for (const [id, value] of [['credential', 'cajero1'], ['password', 'password']]) {
      const input = page.querySelector<HTMLInputElement>(`#${id}`)!;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    }
    fixture.detectChanges();
    expect(page.querySelector<HTMLButtonElement>('form button[type="submit"]')?.disabled).toBe(false);
  });

  it('blocks Electron sign-in when its terminal credential is unavailable', () => {
    TestBed.overrideProvider(POS_ELECTRON_MODE, { useValue: true });
    const fixture = TestBed.createComponent(WelcomePage);
    fixture.detectChanges();

    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/health').flush({ status: 'healthy' });
    http.expectOne('/api/sessions/current').flush(null, { status: 401, statusText: 'Unauthorized' });
    http.expectOne('/api/pos-terminals/current').flush(
      { error: { code: 'POS_TERMINAL_REQUIRED' } },
      { status: 401, statusText: 'Unauthorized' },
    );
    fixture.detectChanges();

    const page = fixture.nativeElement as HTMLElement;
    expect(page.querySelector('.terminal-identity[role="alert"]')?.textContent).toContain('Caja no vinculada');
    for (const [id, value] of [['credential', 'cajero1'], ['password', 'password']]) {
      const input = page.querySelector<HTMLInputElement>(`#${id}`)!;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    }
    fixture.detectChanges();
    expect(page.querySelector<HTMLButtonElement>('form button[type="submit"]')?.disabled).toBe(true);
    expect(page.querySelector('.terminal-company')).toBeNull();
    page.querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit'));
    http.expectNone('/api/sessions');
  });

  it('opens the fixed branch immediately after an Electron cashier signs in', () => {
    TestBed.overrideProvider(POS_ELECTRON_MODE, { useValue: true });
    const navigation = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    const fixture = TestBed.createComponent(WelcomePage);
    fixture.detectChanges();

    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/health').flush({ status: 'healthy' });
    http.expectOne('/api/sessions/current').flush(null, { status: 401, statusText: 'Unauthorized' });
    http.expectOne('/api/pos-terminals/current').flush({
      id: 'terminal-id', name: 'Caja 1', companyId: 'company-id', companyName: 'Empresa Visual',
      branchId: 'branch-id', branchName: 'Sucursal Visual',
    });
    fixture.detectChanges();

    const page = fixture.nativeElement as HTMLElement;
    for (const [id, value] of [['credential', 'cajero1'], ['password', 'password']]) {
      const input = page.querySelector<HTMLInputElement>(`#${id}`)!;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    }
    fixture.detectChanges();
    page.querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit'));
    const login = http.expectOne('/api/sessions');
    expect(login.request.body).toEqual({ credential: 'cajero1', password: 'password' });
    login.flush({
      userId: 'cashier-id', username: 'cajero1', expiresAtUtc: '2026-10-14T20:00:00Z', context: null,
    });

    const selection = http.expectOne('/api/sessions/current/context');
    expect(selection.request.body).toEqual({ companyId: 'company-id', branchId: 'branch-id' });
    http.expectNone('/api/operational-contexts');
    selection.flush({
      userId: 'cashier-id', username: 'cajero1', expiresAtUtc: '2026-10-14T20:00:00Z',
      context: {
        userId: 'cashier-id', companyId: 'company-id', companyName: 'Empresa Visual',
        branchId: 'branch-id', branchName: 'Sucursal Visual', permissions: [], sessionId: 'session-id',
      },
    });
    fixture.detectChanges();
    expect(navigation).toHaveBeenCalledWith('/pos');
    expect(page.querySelector('#branch')).toBeNull();
  });

  it('enters the branch fixed to Electron without asking the cashier to select it', () => {
    TestBed.overrideProvider(POS_ELECTRON_MODE, { useValue: true });
    const navigation = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    const fixture = TestBed.createComponent(WelcomePage);
    fixture.detectChanges();

    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/health').flush({ status: 'healthy' });
    http.expectOne('/api/pos-terminals/current').flush({
      id: 'terminal-id', name: 'Caja 1', companyId: 'company-id', companyName: 'Empresa Visual',
      branchId: 'branch-id', branchName: 'Sucursal Visual',
    });
    http.expectOne('/api/sessions/current').flush({
      userId: 'cashier-id', username: 'cajero1', expiresAtUtc: '2026-10-14T20:00:00Z', context: null,
    });
    const selection = http.expectOne('/api/sessions/current/context');
    expect(selection.request.body).toEqual({ companyId: 'company-id', branchId: 'branch-id' });
    http.expectNone('/api/operational-contexts');
    fixture.detectChanges();

    const page = fixture.nativeElement as HTMLElement;
    expect(page.querySelector('#branch')).toBeNull();
    expect(page.textContent).not.toContain('Cambiar sucursal');
    selection.flush({
      userId: 'cashier-id', username: 'cajero1', expiresAtUtc: '2026-10-14T20:00:00Z',
      context: {
        userId: 'cashier-id', companyId: 'company-id', companyName: 'Empresa Visual',
        branchId: 'branch-id', branchName: 'Sucursal Visual', permissions: [], sessionId: 'session-id',
      },
    });
    fixture.detectChanges();
    expect(navigation).toHaveBeenCalledWith('/pos');
    expect(page.querySelector('#branch')).toBeNull();
    expect(page.textContent).not.toContain('Cambiar sucursal');
  });

  it('waits for the terminal identity if the cashier session arrives first', () => {
    TestBed.overrideProvider(POS_ELECTRON_MODE, { useValue: true });
    vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    const fixture = TestBed.createComponent(WelcomePage);
    fixture.detectChanges();

    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/health').flush({ status: 'healthy' });
    http.expectOne('/api/sessions/current').flush({
      userId: 'cashier-id', username: 'cajero1', expiresAtUtc: '2026-10-14T20:00:00Z', context: null,
    });
    http.expectNone('/api/sessions/current/context');
    http.expectOne('/api/pos-terminals/current').flush({
      id: 'terminal-id', name: 'Caja 1', companyId: 'company-id', companyName: 'Empresa Visual',
      branchId: 'branch-id', branchName: 'Sucursal Visual',
    });

    const selection = http.expectOne('/api/sessions/current/context');
    expect(selection.request.body).toEqual({ companyId: 'company-id', branchId: 'branch-id' });
    selection.flush({
      userId: 'cashier-id', username: 'cajero1', expiresAtUtc: '2026-10-14T20:00:00Z',
      context: {
        userId: 'cashier-id', companyId: 'company-id', companyName: 'Empresa Visual',
        branchId: 'branch-id', branchName: 'Sucursal Visual', permissions: [], sessionId: 'session-id',
      },
    });
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('#branch')).toBeNull();
  });

  it('does not offer another branch if the cashier lacks access to this terminal branch', () => {
    TestBed.overrideProvider(POS_ELECTRON_MODE, { useValue: true });
    const fixture = TestBed.createComponent(WelcomePage);
    fixture.detectChanges();

    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/health').flush({ status: 'healthy' });
    http.expectOne('/api/pos-terminals/current').flush({
      id: 'terminal-id', name: 'Caja 1', companyId: 'company-id', companyName: 'Empresa Visual',
      branchId: 'branch-id', branchName: 'Sucursal Visual',
    });
    http.expectOne('/api/sessions/current').flush({
      userId: 'cashier-id', username: 'cajero1', expiresAtUtc: '2026-10-14T20:00:00Z', context: null,
    });
    http.expectOne('/api/sessions/current/context').flush(
      { error: { code: 'CONTEXT_NOT_ASSIGNED' } },
      { status: 403, statusText: 'Forbidden' },
    );
    fixture.detectChanges();

    const page = fixture.nativeElement as HTMLElement;
    expect(page.querySelector('#branch')).toBeNull();
    expect(page.querySelector('[role="alert"]')?.textContent).toContain('no tiene acceso');
  });

  it('does not ask for a company again after browser login', () => {
    const fixture = TestBed.createComponent(WelcomePage);
    fixture.detectChanges();

    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/health').flush({ status: 'healthy' });
    http.expectOne('/api/sessions/current').flush({
      userId: 'admin-id', username: 'visual-admin', expiresAtUtc: '2026-10-14T20:00:00Z', context: null,
    });
    http.expectOne('/api/operational-contexts').flush([{
      companyId: 'company-id', companyName: 'Empresa Visual',
      branches: [{ branchId: 'branch-id', branchName: 'Sucursal Visual' }],
    }]);
    fixture.detectChanges();

    const page = fixture.nativeElement as HTMLElement;
    expect(page.querySelector('#company')).toBeNull();
    expect(page.querySelector('.context-picker .context-company')?.textContent).toContain('Empresa Visual');
    expect(page.querySelector('#branch')).not.toBeNull();
  });

  it('enters the branch-bound administrator without asking for a branch', () => {
    const navigation = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    const fixture = TestBed.createComponent(WelcomePage);
    fixture.detectChanges();

    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/health').flush({ status: 'healthy' });
    http.expectOne('/api/sessions/current').flush({
      userId: 'admin-id', username: 'rustica-admin', expiresAtUtc: '2026-10-15T20:00:00Z',
      branchBound: true, context: null,
    });
    http.expectOne('/api/operational-contexts').flush([{
      companyId: 'rustica-company', companyName: 'La Rústica',
      branches: [{ branchId: 'alta-branch', branchName: 'Alta Córdoba' }],
    }]);

    const selection = http.expectOne('/api/sessions/current/context');
    expect(selection.request.body).toEqual({ companyId: 'rustica-company', branchId: 'alta-branch' });
    fixture.detectChanges();
    const page = fixture.nativeElement as HTMLElement;
    expect(page.querySelector('#branch')).toBeNull();
    selection.flush({
      userId: 'admin-id', username: 'rustica-admin', expiresAtUtc: '2026-10-15T20:00:00Z',
      branchBound: true,
      context: {
        userId: 'admin-id', companyId: 'rustica-company', companyName: 'La Rústica',
        branchId: 'alta-branch', branchName: 'Alta Córdoba',
        permissions: ['platform.users.manage'], sessionId: 'session-id',
      },
    });
    fixture.detectChanges();
    expect(navigation).toHaveBeenCalledWith('/admin');
    expect(page.textContent).not.toContain('Cambiar sucursal');
  });

  it('does not offer another branch when a bound administrator has no branch access', () => {
    const fixture = TestBed.createComponent(WelcomePage);
    fixture.detectChanges();

    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/health').flush({ status: 'healthy' });
    http.expectOne('/api/sessions/current').flush({
      userId: 'admin-id', username: 'rustica-admin', expiresAtUtc: '2026-10-15T20:00:00Z',
      branchBound: true, context: null,
    });
    http.expectOne('/api/operational-contexts').flush([]);
    fixture.detectChanges();

    const page = fixture.nativeElement as HTMLElement;
    expect(page.querySelector('#branch')).toBeNull();
    expect(page.textContent).toContain('no tiene acceso a la sucursal');
    expect(page.textContent).toContain('Reintentar acceso');

    (page.querySelector('.context-picker button') as HTMLButtonElement).click();
    http.expectOne('/api/operational-contexts').flush([]);
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
    const companyCode = page.querySelector<HTMLInputElement>('#company-code')!;
    const password = page.querySelector<HTMLInputElement>('#password')!;
    companyCode.value = 'VISUAL';
    companyCode.dispatchEvent(new Event('input'));
    credential.value = 'unknown';
    credential.dispatchEvent(new Event('input'));
    password.value = 'incorrect password';
    password.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    page.querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit'));
    const loginRequest = http.expectOne('/api/sessions');
    expect(loginRequest.request.body).toEqual({ companyCode: 'VISUAL', credential: 'unknown', password: 'incorrect password' });
    loginRequest.flush(
      { error: { code: 'AUTHENTICATION_FAILED' } },
      { status: 401, statusText: 'Unauthorized' },
    );
    fixture.detectChanges();

    expect(page.querySelector('[role="alert"]')?.textContent).toContain('Empresa, usuario o contraseña incorrectos.');
  });
});
