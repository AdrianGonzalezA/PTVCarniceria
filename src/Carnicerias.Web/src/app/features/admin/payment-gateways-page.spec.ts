import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { PaymentGatewaysPage } from './payment-gateways-page';

describe('PaymentGatewaysPage Clover sandbox', () => {
  beforeEach(() => TestBed.configureTestingModule({
    imports: [PaymentGatewaysPage],
    providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
  }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  function load() {
    const fixture = TestBed.createComponent(PaymentGatewaysPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/sessions/current').flush({
      userId: 'admin-id', username: 'visual-admin', expiresAtUtc: '2026-10-14T00:00:00Z',
      context: { userId: 'admin-id', companyId: 'company-1', companyName: 'Empresa Visual',
        branchId: 'branch-1', branchName: 'Sucursal Visual', sessionId: 'session-id',
        permissions: ['organization.manage'] },
    });
    http.expectOne('/api/admin/payment-gateways/mercado-pago').flush({
      provider: 'mercadoPago', environment: 'test', sellerUserId: '123',
      hasAccessToken: false, hasWebhookSecret: false, registers: [],
    });
    http.expectOne('/api/admin/payment-gateways/clover').flush({
      provider: 'clover', environment: 'sandbox', clientId: 'APP_123', merchantId: 'MERCHANT_123',
      hasClientSecret: true, isConnected: false,
      accessTokenExpiresAtUtc: null, refreshTokenExpiresAtUtc: null,
    });
    fixture.detectChanges();
    return { fixture, http, page: fixture.nativeElement as HTMLElement };
  }

  it('shows credential status but never echoes the App Secret', () => {
    const { page } = load();
    expect(page.textContent).toContain('Clover · Sandbox');
    expect(page.textContent).toContain('Pendiente');
    expect(page.querySelector<HTMLInputElement>('input[formcontrolname="clientSecret"]')?.value).toBe('');
  });

  it('starts a one-use authorization and offers the URL for an external browser', () => {
    const { fixture, http, page } = load();
    const connect = Array.from(page.querySelectorAll('button'))
      .find((button) => button.textContent?.includes('Conectar comercio sandbox'))!;
    connect.click();
    const request = http.expectOne('/api/admin/payment-gateways/clover/authorization');
    expect(request.request.method).toBe('POST');
    expect(request.request.withCredentials).toBe(true);
    request.flush({ authorizeUrl: 'https://sandbox.dev.clover.com/oauth/v2/authorize?state=test',
      expiresAtUtc: '2026-10-13T14:00:00Z' });
    fixture.detectChanges();
    expect(page.querySelector<HTMLInputElement>('.gateway-oauth-link input')?.value)
      .toContain('sandbox.dev.clover.com');
    expect(page.querySelector<HTMLAnchorElement>('.gateway-oauth-link a')?.target).toBe('_blank');
  });
});
