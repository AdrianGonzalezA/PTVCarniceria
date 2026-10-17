import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { initializeVisualTheme, visualThemeStorageKey } from '../../core/theme/visual-theme';
import { AdminPage } from './admin-page';

describe('administrator areas', () => {
  beforeEach(() => TestBed.configureTestingModule({
    imports: [AdminPage],
    providers: [
      provideRouter([
        { path: 'admin', component: AdminPage, data: { area: 'business' } },
        { path: 'admin/configuracion', component: AdminPage, data: { area: 'configuration' } },
      ]),
      provideHttpClient(), provideHttpClientTesting(),
    ],
  }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  async function open(url: string): Promise<HTMLElement> {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url, AdminPage);
    TestBed.inject(HttpTestingController).expectOne('/api/sessions/current').flush({
      userId: 'admin-id', username: 'visual-admin', expiresAtUtc: '2026-10-08T00:00:00Z',
      context: { userId: 'admin-id', companyId: 'company-id', companyName: 'Empresa Visual',
        branchId: 'branch-id', branchName: 'Sucursal Visual',
        permissions: ['platform.users.manage', 'organization.manage', 'inventory.stock.manage', 'catalog.manage'],
        sessionId: 'session-id' },
    });
    if (url === '/admin') {
      TestBed.inject(HttpTestingController).expectOne('/api/admin/companies/company-id/branches').flush([]);
      TestBed.inject(HttpTestingController).expectOne('/api/admin/history/summary').flush({
        fromUtc: '2026-10-08T03:00:00Z', toUtc: '2026-10-09T03:00:00Z', branchId: null,
        saleCount: 0, salesTotal: 0, immediateSalePayments: 0, paymentsByMethod: [],
        newAccountCharges: 0, registeredAccountCharges: 0, creditApplied: 0,
        outstandingDebt: 0, companyCreditAvailable: 0, collectionsReceived: 0,
        collectionsRefunded: 0, cashCollectionsNet: 0, nonCashCollectionsNet: 0,
        openShiftCount: 0, openShiftCashBalance: 0,
      });
    }
    harness.detectChanges();
    return harness.routeNativeElement as HTMLElement;
  }

  it('puts history, stock and piece reception under Negocio', async () => {
    const page = await open('/admin');
    expect(page.querySelector('h1')?.textContent).toContain('Negocio');
    expect(page.querySelector('a[href="/admin/history"]')).not.toBeNull();
    expect(page.querySelector('a[href="/admin/stock"]')).not.toBeNull();
    expect(page.querySelector('a[href="/admin/pieces"]')).not.toBeNull();
    expect(page.querySelector('a[href="/users"]')).toBeNull();
    expect(page.querySelector('[aria-labelledby="visual-theme-title"]')).toBeNull();
    expect(page.querySelector('a[href="/admin"]')?.getAttribute('aria-current')).toBe('page');
  });

  it('puts management and barcode rules under Configuración', async () => {
    const page = await open('/admin/configuracion');
    expect(page.querySelector('h1')?.textContent).toContain('Configuración');
    expect(page.querySelector('a[href="/admin/customers"]')).not.toBeNull();
    expect(page.querySelector('a[href="/admin/barcode-layouts"]')).not.toBeNull();
    expect(page.querySelector('a[href="/admin/taxes"]')).not.toBeNull();
    expect(page.querySelector('a[href="/admin/arca"]')).not.toBeNull();
    expect(page.querySelector('a[href="/users"]')).not.toBeNull();
    expect(page.querySelector('a[href="/admin/history"]')).toBeNull();
    expect(page.querySelector('a[href="/admin/configuracion"]')?.getAttribute('aria-current')).toBe('page');
    expect(page.querySelector('[aria-labelledby="visual-theme-title"]')).not.toBeNull();
  });

  it('selects the local theme immediately without navigation or business requests', async () => {
    const root = document.documentElement;
    const originalAttribute = root.getAttribute('data-visual-theme');
    const originalStorage = localStorage.getItem(visualThemeStorageKey);
    try {
      localStorage.setItem(visualThemeStorageKey, 'original');
      initializeVisualTheme();
      const page = await open('/admin/configuracion');
      const original = page.querySelector('input[value="original"]') as HTMLInputElement;
      const nocturno = page.querySelector('input[value="nocturno"]') as HTMLInputElement;
      expect(original.checked).toBe(true);
      expect(nocturno.checked).toBe(false);
      expect(page.textContent).toContain('este perfil');

      nocturno.click();
      expect(root.dataset['visualTheme']).toBe('nocturno');
      expect(localStorage.getItem(visualThemeStorageKey)).toBe('nocturno');
      expect(TestBed.inject(Router).url).toBe('/admin/configuracion');
      TestBed.inject(HttpTestingController).expectNone(() => true);

      original.click();
      expect(root.dataset['visualTheme']).toBe('original');
      expect(localStorage.getItem(visualThemeStorageKey)).toBe('original');
      expect(initializeVisualTheme()).toBe('original');
    } finally {
      if (originalStorage === null) localStorage.removeItem(visualThemeStorageKey);
      else localStorage.setItem(visualThemeStorageKey, originalStorage);
      if (originalAttribute === null) root.removeAttribute('data-visual-theme');
      else root.setAttribute('data-visual-theme', originalAttribute);
    }
  });

  it('changes area-tab contrast with the local theme while retaining navigation and focus', async () => {
    const root = document.documentElement;
    const previousTheme = root.getAttribute('data-visual-theme');
    try {
      root.dataset['visualTheme'] = 'original';
      const page = await open('/admin/configuracion');
      const tabs = page.querySelector('app-admin-area-tabs') as HTMLElement;
      const active = tabs.querySelector('a[aria-current="page"]') as HTMLAnchorElement;
      expect(getComputedStyle(tabs).getPropertyValue('--admin-tab-accent').trim()).toBe('#54c2bd');
      active.focus();
      expect(document.activeElement).toBe(active);

      root.dataset['visualTheme'] = 'nocturno';
      expect(getComputedStyle(tabs).getPropertyValue('--admin-tab-accent').trim()).toBe('#e5b27f');
      expect(active.getAttribute('href')).toBe('/admin/configuracion');
      expect(active.getAttribute('aria-current')).toBe('page');
      expect(document.activeElement).toBe(active);
    } finally {
      if (previousTheme === null) root.removeAttribute('data-visual-theme');
      else root.setAttribute('data-visual-theme', previousTheme);
    }
  });
});
