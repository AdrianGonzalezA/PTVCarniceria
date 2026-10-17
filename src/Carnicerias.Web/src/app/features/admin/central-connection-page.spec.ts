import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { CentralConnectionPage } from './central-connection-page';

describe('CentralConnectionPage', () => {
  const current = {
    companyId: 'company-1', companyName: 'La Rústica', branchId: 'branch-1',
    branchName: 'Alta Córdoba', installationId: '11111111-1111-4111-8111-111111111111',
    destinationUrl: 'http://127.0.0.1:5249/', isActive: false,
    hasCredential: false, hasPreviousCredential: false,
    previousCredentialExpiresAtUtc: null, revision: 1,
  };

  beforeEach(() => TestBed.configureTestingModule({
    imports: [CentralConnectionPage],
    providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
  }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  function load(bindingAvailable = true, status = current) {
    const fixture = TestBed.createComponent(CentralConnectionPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/sessions/current').flush({
      userId: 'admin-1', username: 'rustica-admin', expiresAtUtc: '2026-10-15T23:00:00Z',
      context: { userId: 'admin-1', companyId: 'company-1', companyName: 'La Rústica',
        branchId: 'branch-1', branchName: 'Alta Córdoba', sessionId: 'session-1',
        permissions: ['organization.manage'] },
    });
    const connection = http.expectOne('/api/admin/central-connection');
    if (bindingAvailable) connection.flush(status);
    else connection.flush({}, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();
    return { fixture, http, page: fixture.nativeElement as HTMLElement };
  }

  it('themes branch identity and connection controls without issuing a credential', () => {
    const root = document.documentElement;
    const previousTheme = root.getAttribute('data-visual-theme');
    try {
      root.dataset['visualTheme'] = 'original';
      const { fixture, http, page } = load();
      expect(getComputedStyle(page).getPropertyValue('--admin-central-control').trim()).toBe('#fff');
      expect(page.querySelector<HTMLInputElement>('#central-destination-url')?.value)
        .toBe('http://127.0.0.1:5249/');

      root.dataset['visualTheme'] = 'nocturno';
      expect(getComputedStyle(page).getPropertyValue('--admin-central-control').trim()).toBe('#181b20');
      expect(getComputedStyle(page).getPropertyValue('--admin-central-panel').trim()).toBe('#2d3239');
      expect(page.querySelector<HTMLInputElement>('#central-destination-url')?.value)
        .toBe('http://127.0.0.1:5249/');
      expect(page.querySelector('#central-connection-bundle')).toBeNull();
      http.expectNone((request) => request.method !== 'GET');
      fixture.destroy();
    } finally {
      if (previousTheme === null) root.removeAttribute('data-visual-theme');
      else root.setAttribute('data-visual-theme', previousTheme);
    }
  });

  it('shows the branch identity and saves its central URL with CSRF protection', () => {
    const { fixture, http, page } = load();
    expect(page.textContent).toContain('Alta Córdoba');
    expect(page.textContent).toContain('11111111-1111-4111-8111-111111111111');
    const url = page.querySelector<HTMLInputElement>('#central-destination-url')!;
    url.value = 'https://alta.example.test/';
    url.dispatchEvent(new Event('input', { bubbles: true }));
    page.querySelector<HTMLButtonElement>('#save-central-url')!.click();

    const save = http.expectOne('/api/admin/central-connection');
    expect(save.request.method).toBe('PUT');
    expect(save.request.headers.get('X-Carnicerias-CSRF')).toBe('1');
    expect(save.request.body).toEqual({ destinationUrl: 'https://alta.example.test/', revision: 1 });
    save.flush({ installationId: current.installationId,
      destinationUrl: 'https://alta.example.test/', isActive: false, revision: 2 });
    fixture.detectChanges();
    expect(page.textContent).toContain('URL guardada');
  });

  it('displays the complete package only after issuing a credential and can clear it', () => {
    const { fixture, http, page } = load();
    expect(page.textContent).not.toContain('A'.repeat(64));
    page.querySelector<HTMLButtonElement>('#issue-central-credential')!.click();
    const issue = http.expectOne('/api/admin/central-connection/credentials');
    expect(issue.request.method).toBe('POST');
    issue.flush({ version: 1, label: 'Alta Córdoba',
      destinationUrl: current.destinationUrl, installationId: current.installationId,
      remoteCompanyId: current.companyId, remoteBranchId: current.branchId,
      token: 'A'.repeat(64) });
    fixture.detectChanges();

    const packageInput = page.querySelector<HTMLTextAreaElement>('#central-connection-bundle')!;
    expect(JSON.parse(packageInput.value).token).toBe('A'.repeat(64));
    page.querySelector<HTMLButtonElement>('#clear-central-credential')!.click();
    fixture.detectChanges();
    expect(page.querySelector('#central-connection-bundle')).toBeNull();
    expect(page.textContent).not.toContain('A'.repeat(64));
  });

  it('explains when this API is not bound to the selected branch', () => {
    const { page } = load(false);
    expect(page.textContent).toContain('no está vinculada a esta sucursal');
  });
  it('uses the new revision after completing a rotation', () => {
    const { fixture, http, page } = load(true, { ...current,
      isActive: true, hasCredential: true, hasPreviousCredential: true, revision: 4 });
    page.querySelector<HTMLButtonElement>('.warning-panel button')!.click();
    http.expectOne('/api/admin/central-connection/credentials/complete-rotation')
      .flush({ isActive: true, revision: 5 });
    fixture.detectChanges();
    page.querySelector<HTMLButtonElement>('#save-central-url')!.click();
    const save = http.expectOne('/api/admin/central-connection');
    expect(save.request.body.revision).toBe(5);
    save.flush({ installationId: current.installationId,
      destinationUrl: current.destinationUrl, isActive: true, revision: 6 });
  });

  it('uses the new revision after deactivating a connection', () => {
    const { fixture, http, page } = load(true, { ...current,
      isActive: true, hasCredential: true, revision: 4 });
    page.querySelectorAll<HTMLButtonElement>('.credential-content > button')[1].click();
    fixture.detectChanges();
    page.querySelector<HTMLButtonElement>('.warning-panel button:not(.secondary)')!.click();
    http.expectOne('/api/admin/central-connection/deactivation')
      .flush({ isActive: false, revision: 5 });
    fixture.detectChanges();
    page.querySelector<HTMLButtonElement>('#save-central-url')!.click();
    const save = http.expectOne('/api/admin/central-connection');
    expect(save.request.body.revision).toBe(5);
    save.flush({ installationId: current.installationId,
      destinationUrl: current.destinationUrl, isActive: false, revision: 6 });
  });
});
