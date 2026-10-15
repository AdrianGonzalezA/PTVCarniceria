import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { App } from './app';

describe('central login', () => {
  function setup() {
    TestBed.configureTestingModule({
      imports: [App],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    return { fixture, http };
  }

  it('shows an accessible login after an absent central session', async () => {
    const { fixture, http } = setup();
    http.expectOne('/api/v1/sessions/current').flush(
      { error: { code: 'SESSION_REQUIRED', message: 'Sesión requerida.' } },
      { status: 401, statusText: 'Unauthorized' });
    await fixture.whenStable();
    fixture.detectChanges();

    const element: HTMLElement = fixture.nativeElement;
    expect(element.querySelector('h1')?.textContent).toContain('Administración central');
    expect(element.querySelector('label[for="central-code"]')).not.toBeNull();
    expect(element.querySelector('label[for="central-username"]')).not.toBeNull();
    expect(element.querySelector('label[for="central-password"]')).not.toBeNull();
    expect(element.querySelector('button[type="submit"]')?.textContent).toContain('Ingresar');
    http.verify();
  });

  it('uses the central account and clears password after a generic failed login', async () => {
    const { fixture, http } = setup();
    http.expectOne('/api/v1/sessions/current').flush(null, { status: 401, statusText: 'Unauthorized' });
    await fixture.whenStable();
    fixture.detectChanges();
    const element: HTMLElement = fixture.nativeElement;

    function type(id: string, value: string) {
      const input = element.querySelector<HTMLInputElement>(`#${id}`)!;
      input.value = value;
      input.dispatchEvent(new Event('input', { bubbles: true }));
    }
    type('central-code', 'VISUAL');
    type('central-username', 'central-admin');
    type('central-password', 'a unique laboratory password');
    element.querySelector<HTMLFormElement>('form')!.dispatchEvent(
      new Event('submit', { bubbles: true, cancelable: true }));

    const request = http.expectOne('/api/v1/sessions');
    expect(request.request.body).toEqual({
      companyCode: 'VISUAL', credential: 'central-admin', password: 'a unique laboratory password',
    });
    request.flush({ error: { code: 'INVALID_CREDENTIALS', message: 'Credenciales inválidas.' } },
      { status: 401, statusText: 'Unauthorized' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(element.querySelector('[role="alert"]')?.textContent).toContain('Credenciales inválidas');
    expect(element.querySelector<HTMLInputElement>('#central-password')?.value).toBe('');
    http.verify();
  });

  it('submits a six-character laboratory password to the central API', async () => {
    const { fixture, http } = setup();
    http.expectOne('/api/v1/sessions/current').flush(null, { status: 401, statusText: 'Unauthorized' });
    await fixture.whenStable();
    fixture.detectChanges();
    const element: HTMLElement = fixture.nativeElement;
    for (const [id, value] of [
      ['central-code', 'VISUAL-CENTRAL'],
      ['central-username', 'central-admin'],
      ['central-password', 'short1'],
    ]) {
      const input = element.querySelector<HTMLInputElement>(`#${id}`)!;
      input.value = value;
      input.dispatchEvent(new Event('input', { bubbles: true }));
    }
    element.querySelector<HTMLFormElement>('form')!.dispatchEvent(
      new Event('submit', { bubbles: true, cancelable: true }));

    const request = http.expectOne('/api/v1/sessions');
    expect(request.request.body.password).toBe('short1');
    request.flush(null, { status: 401, statusText: 'Unauthorized' });
    await fixture.whenStable();
    http.verify();
  });

  it('enters the central area and returns to login after logout', async () => {
    const { fixture, http } = setup();
    http.expectOne('/api/v1/sessions/current').flush(null, { status: 401, statusText: 'Unauthorized' });
    await fixture.whenStable();
    fixture.detectChanges();
    const element: HTMLElement = fixture.nativeElement;
    for (const [id, value] of [
      ['central-code', 'VISUAL'],
      ['central-username', 'central-admin'],
      ['central-password', 'a unique laboratory password'],
    ]) {
      const input = element.querySelector<HTMLInputElement>(`#${id}`)!;
      input.value = value;
      input.dispatchEvent(new Event('input', { bubbles: true }));
    }
    element.querySelector<HTMLFormElement>('form')!.dispatchEvent(
      new Event('submit', { bubbles: true, cancelable: true }));
    http.expectOne('/api/v1/sessions').flush({
      userId: '1', centralCompanyId: '2', username: 'central-admin', expiresAtUtc: '2026-10-14T23:00:00Z',
    });
    http.expectOne('/api/v1/installations?page=1&pageSize=20').flush({
      data: [{ installationId: 'i-1', remoteCompanyId: 'c-1', remoteBranchId: 'b-1', label: 'Sucursal Centro' }],
      pagination: { page: 1, pageSize: 20, totalCount: 1, totalPages: 1 },
    });
    await fixture.whenStable();
    fixture.detectChanges();
    http.expectOne('/api/v1/business/summary').flush({
      window: { fromUtc: '2026-10-14T03:00:00Z', toUtc: '2026-10-14T18:00:00Z' },
      retrievedAtUtc: '2026-10-14T18:00:00Z', completeness: 'empty',
      aggregate: null, branches: [],
    });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(element.querySelector('#branches-title')?.textContent).toContain('Sucursales registradas');
    expect(element.textContent).toContain('Sucursal Centro');
    expect(element.textContent).toContain('Conectividad sin comprobar');
    element.querySelector<HTMLButtonElement>('.header-button')!.click();
    http.expectOne('/api/v1/sessions/current').flush(null);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(element.querySelector('#central-code')).not.toBeNull();
    http.verify();
  });

  it('loads the live business panel for the signed-in central account', async () => {
    const { fixture, http } = setup();
    http.expectOne('/api/v1/sessions/current').flush({
      userId: '1', centralCompanyId: '2', username: 'central-admin',
      expiresAtUtc: '2026-10-14T23:00:00Z',
    });
    http.expectOne('/api/v1/installations?page=1&pageSize=20').flush({
      data: [], pagination: { page: 1, pageSize: 20, totalCount: 0, totalPages: 0 },
    });
    fixture.detectChanges();
    http.expectOne('/api/v1/business/summary').flush({
      window: { fromUtc: '2026-10-14T03:00:00Z', toUtc: '2026-10-14T18:00:00Z' },
      retrievedAtUtc: '2026-10-14T18:00:00Z', completeness: 'empty',
      aggregate: null, branches: [],
    });
    await fixture.whenStable();
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('#business-title')?.textContent)
      .toContain('Negocio en vivo');
    http.verify();
  });

  it('returns to login if the central session expires while loading branches', async () => {
    const { fixture, http } = setup();
    http.expectOne('/api/v1/sessions/current').flush({
      userId: '1', centralCompanyId: '2', username: 'central-admin', expiresAtUtc: '2026-10-14T23:00:00Z',
    });
    http.expectOne('/api/v1/installations?page=1&pageSize=20').flush(null,
      { status: 401, statusText: 'Unauthorized' });
    await fixture.whenStable();
    fixture.detectChanges();
    const element: HTMLElement = fixture.nativeElement;
    expect(element.querySelector('#central-code')).not.toBeNull();
    expect(element.textContent).not.toContain('Sucursales registradas');
    http.verify();
  });

  it('clears the tenant view if the business request expires before the branch list', async () => {
    const { fixture, http } = setup();
    http.expectOne('/api/v1/sessions/current').flush({
      userId: '1', centralCompanyId: '2', username: 'central-admin',
      expiresAtUtc: '2026-10-14T23:00:00Z',
    });
    fixture.detectChanges();
    const branches = http.expectOne('/api/v1/installations?page=1&pageSize=20');
    http.expectOne('/api/v1/business/summary').flush(null,
      { status: 401, statusText: 'Unauthorized' });
    await fixture.whenStable();
    fixture.detectChanges();
    branches.flush({
      data: [{ installationId: 'a', remoteCompanyId: 'a', remoteBranchId: 'a', label: 'Sucursal A' }],
      pagination: { page: 1, pageSize: 20, totalCount: 1, totalPages: 1 },
    });
    await fixture.whenStable();
    fixture.detectChanges();
    const element: HTMLElement = fixture.nativeElement;
    expect(element.querySelector('#central-code')).not.toBeNull();
    expect(element.textContent).not.toContain('Sucursal A');
    expect(element.textContent).toContain('La sesión central venció');
    http.verify();
  });

  it('does not show a prior company response after switching central accounts', async () => {
    const { fixture, http } = setup();
    http.expectOne('/api/v1/sessions/current').flush(null, { status: 401, statusText: 'Unauthorized' });
    await fixture.whenStable();
    fixture.detectChanges();
    const element: HTMLElement = fixture.nativeElement;
    const login = (companyCode: string) => {
      for (const [id, value] of [
        ['central-code', companyCode],
        ['central-username', 'central-admin'],
        ['central-password', 'a unique laboratory password'],
      ]) {
        const input = element.querySelector<HTMLInputElement>(`#${id}`)!;
        input.value = value;
        input.dispatchEvent(new Event('input', { bubbles: true }));
      }
      element.querySelector<HTMLFormElement>('form')!.dispatchEvent(
        new Event('submit', { bubbles: true, cancelable: true }));
      http.expectOne('/api/v1/sessions').flush({
        userId: companyCode, centralCompanyId: companyCode,
        username: 'central-admin', expiresAtUtc: '2026-10-14T23:00:00Z',
      });
      fixture.detectChanges();
    };

    login('A-COMPANY');
    const priorList = http.expectOne('/api/v1/installations?page=1&pageSize=20');
    const priorBusiness = http.expectOne('/api/v1/business/summary');
    element.querySelector<HTMLButtonElement>('.header-button')!.click();
    http.expectOne('/api/v1/sessions/current').flush(null);
    await fixture.whenStable();
    fixture.detectChanges();
    login('B-COMPANY');
    const newList = http.expectOne('/api/v1/installations?page=1&pageSize=20');
    const newBusiness = http.expectOne('/api/v1/business/summary');
    expect(priorBusiness.cancelled).toBe(true);

    priorList.flush({
      data: [{ installationId: 'a', remoteCompanyId: 'a', remoteBranchId: 'a', label: 'Sucursal A' }],
      pagination: { page: 1, pageSize: 20, totalCount: 1, totalPages: 1 },
    });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(element.textContent).not.toContain('Sucursal A');
    newList.flush({
      data: [{ installationId: 'b', remoteCompanyId: 'b', remoteBranchId: 'b', label: 'Sucursal B' }],
      pagination: { page: 1, pageSize: 20, totalCount: 1, totalPages: 1 },
    });
    newBusiness.flush({
      window: { fromUtc: '2026-10-14T03:00:00Z', toUtc: '2026-10-14T18:00:00Z' },
      retrievedAtUtc: '2026-10-14T18:00:00Z', completeness: 'empty',
      aggregate: null, branches: [],
    });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(element.textContent).toContain('Sucursal B');
    http.verify({ ignoreCancelled: true });
  });
});
