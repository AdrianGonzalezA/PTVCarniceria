import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ArcaSettingsPage } from './arca-settings-page';

describe('ArcaSettingsPage', () => {
  const settings = {
    companyId: 'company-1', source: 'database', issuerCuit: '30710106513', pointOfSale: 99,
    issuerName: 'Empresa Visual', issuerAddress: 'Domicilio de prueba',
    issuerIibb: null, issuerActivityStartDate: null,
    certificate: { subject: 'CN=RIHomo1', thumbprint: 'A'.repeat(40),
      validFromUtc: '2026-09-30T12:00:00Z', expiresAtUtc: '2028-09-29T12:00:00Z',
      isExpired: false, isAccessible: true },
  };

  beforeEach(() => TestBed.configureTestingModule({
    imports: [ArcaSettingsPage],
    providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
  }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  function load() {
    const fixture = TestBed.createComponent(ArcaSettingsPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/sessions/current').flush({
      userId: 'admin-id', username: 'visual-admin', expiresAtUtc: '2026-10-09T00:00:00Z',
      context: { userId: 'admin-id', companyId: 'company-1', companyName: 'Empresa Visual',
        branchId: 'branch-1', branchName: 'Sucursal Visual', sessionId: 'session-id',
        permissions: ['organization.manage'] },
    });
    http.expectOne('/api/admin/arca-settings').flush(settings);
    fixture.detectChanges();
    return { fixture, http, page: fixture.nativeElement as HTMLElement };
  }

  it('shows certificate expiry without showing private material', () => {
    const { page } = load();
    expect(page.textContent).toContain('29 de septiembre de 2028');
    expect(page.textContent).toContain('CN=RIHomo1');
    expect(page.textContent).not.toContain('BEGIN PRIVATE KEY');
    expect(page.querySelector<HTMLInputElement>('#arca-pfx-password')?.value).toBe('');
  });

  it('sends issuer metadata without certificate bytes or password', () => {
    const { fixture, http, page } = load();
    page.querySelector<HTMLButtonElement>('.arca-actions button')!.click();
    const request = http.expectOne('/api/admin/arca-settings');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({
      issuerCuit: '30710106513', pointOfSale: 99, issuerName: 'Empresa Visual',
      issuerAddress: 'Domicilio de prueba', issuerIibb: null, issuerActivityStartDate: null,
    });
    request.flush(settings);
    fixture.detectChanges();
    expect(page.textContent).toContain('Datos del emisor guardados');
  });
});
