import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { PaymentGatewaysPage } from './payment-gateways-page';
import { MercadoPagoRegister } from '../../core/admin/admin-payment-gateway-client';

describe('PaymentGatewaysPage Clover sandbox', () => {
  beforeEach(() => TestBed.configureTestingModule({
    imports: [PaymentGatewaysPage],
    providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
  }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  function load(cloverConnected = false, accessExpiry = '2026-10-14T00:00:00Z',
    registers: readonly MercadoPagoRegister[] = []) {
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
      hasAccessToken: false, hasWebhookSecret: false, registers,
    });
    http.expectOne('/api/admin/payment-gateways/clover').flush({
      provider: 'clover', environment: 'sandbox', clientId: 'APP_123', merchantId: 'MERCHANT_123',
      hasClientSecret: true, isConnected: cloverConnected, requiresReauthorization: false,
      accessTokenExpiresAtUtc: cloverConnected ? accessExpiry : null,
      refreshTokenExpiresAtUtc: cloverConnected ? '2026-11-14T00:00:00Z' : null,
    });
    fixture.detectChanges();
    return { fixture, http, page: fixture.nativeElement as HTMLElement };
  }

  it('themes gateway credentials and register editor without saving or connecting', () => {
    const root = document.documentElement;
    const previousTheme = root.getAttribute('data-visual-theme');
    try {
      root.dataset['visualTheme'] = 'original';
      const { fixture, http, page } = load(false, '2026-10-14T00:00:00Z', [
        { id: 'register-1', name: 'Caja A', branchId: 'branch-1', branchName: 'Centro',
          isActive: true, qrExternalPosId: 'CAJA_A', pointTerminalId: null },
      ]);
      expect(getComputedStyle(page).getPropertyValue('--admin-gateway-card').trim()).toBe('#fff');
      page.querySelector<HTMLButtonElement>('.gateway-register button')!.click();
      fixture.detectChanges();
      expect(page.querySelector<HTMLInputElement>('.gateway-dialog input')?.value).toBe('CAJA_A');

      root.dataset['visualTheme'] = 'nocturno';
      expect(getComputedStyle(page).getPropertyValue('--admin-gateway-card').trim()).toBe('#2d3239');
      expect(getComputedStyle(page).getPropertyValue('--admin-gateway-dialog').trim()).toBe('#24282e');
      expect(page.querySelector<HTMLInputElement>('.gateway-dialog input')?.value).toBe('CAJA_A');
      expect(page.querySelector<HTMLInputElement>('input[formcontrolname="clientSecret"]')?.value).toBe('');
      http.expectNone((request) => request.method !== 'GET');
      fixture.destroy();
    } finally {
      if (previousTheme === null) root.removeAttribute('data-visual-theme');
      else root.setAttribute('data-visual-theme', previousTheme);
    }
  });

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

  it('checks the connected sandbox merchant without sending a token to the browser', () => {
    const { fixture, http, page } = load(true);
    const check = Array.from(page.querySelectorAll('button'))
      .find((button) => button.textContent?.includes('Probar conexión Clover'))!;
    check.click();
    const request = http.expectOne('/api/admin/payment-gateways/clover/connection');
    expect(request.request.method).toBe('POST');
    expect(request.request.withCredentials).toBe(true);
    expect(request.request.body).toBeNull();
    request.flush({ status: 'verified', environment: 'sandbox' });
    http.expectOne('/api/admin/payment-gateways/clover').flush({
      provider: 'clover', environment: 'sandbox', clientId: 'APP_123', merchantId: 'MERCHANT_123',
      hasClientSecret: true, isConnected: true, requiresReauthorization: false,
      accessTokenExpiresAtUtc: '2026-10-14T01:00:00Z',
      refreshTokenExpiresAtUtc: '2026-11-14T00:00:00Z',
    });
    fixture.detectChanges();
    expect(page.textContent).toContain('Comercio verificado contra Clover sandbox');
  });

  it('allows a deliberate probe after access expiry so the server can refresh', () => {
    const { page } = load(true, '2026-10-12T00:00:00Z');
    const check = Array.from(page.querySelectorAll('button'))
      .find((button) => button.textContent?.includes('Probar') && button.textContent?.includes('Clover'))!;
    expect(check.disabled).toBe(false);
  });

  it('shows only sanitized device inventory and does not imply a payment terminal is ready', () => {
    const { fixture, http, page } = load(true);
    const check = Array.from(page.querySelectorAll('button'))
      .find((button) => button.textContent?.includes('Consultar dispositivos Clover'))!;
    check.click();
    const request = http.expectOne('/api/admin/payment-gateways/clover/devices');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toBeNull();
    expect(request.request.withCredentials).toBe(true);
    request.flush({ environment: 'sandbox', devices: [{ id: 'DEV_1', name: 'Mini prueba', model: 'Mini' }] });
    http.expectOne('/api/admin/payment-gateways/clover').flush({
      provider: 'clover', environment: 'sandbox', clientId: 'APP_123', merchantId: 'MERCHANT_123',
      hasClientSecret: true, isConnected: true, requiresReauthorization: false,
      accessTokenExpiresAtUtc: '2026-10-14T01:00:00Z',
      refreshTokenExpiresAtUtc: '2026-11-14T00:00:00Z',
    });
    fixture.detectChanges();
    expect(page.textContent).toContain('Mini prueba');
    expect(page.textContent).toContain('no confirma Cloud Pay Display');
  });
});
