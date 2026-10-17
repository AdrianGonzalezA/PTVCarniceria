import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { CustomersPage } from './customers-page';

describe('CustomersPage dialogs', () => {
  beforeEach(() => TestBed.configureTestingModule({
    imports: [CustomersPage],
    providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
  }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('inherits both themes for the customer table and editor without sending a change', () => {
    const root = document.documentElement;
    const previousTheme = root.getAttribute('data-visual-theme');
    try {
      root.dataset['visualTheme'] = 'original';
      const fixture = TestBed.createComponent(CustomersPage);
      fixture.detectChanges();
      const http = TestBed.inject(HttpTestingController);
      http.expectOne('/api/sessions/current').flush({ userId: 'admin', username: 'admin', expiresAtUtc: '',
        context: { userId: 'admin', companyId: 'c', companyName: 'Empresa', branchId: 'b', branchName: 'Sucursal',
          permissions: ['organization.manage'], sessionId: 's' } });
      http.expectOne((request) => request.url === '/api/admin/customers').flush({
        items: [{ id: 'customer-1', code: 'C1', name: 'Cliente 1', isActive: true, creditEnabled: false }],
        page: 1, pageSize: 20, totalItems: 1, totalPages: 1,
      });
      fixture.detectChanges();
      const page = fixture.nativeElement as HTMLElement;
      expect(getComputedStyle(page).getPropertyValue('--admin-abm-surface').trim()).toBe('#ededed');
      page.querySelector<HTMLButtonElement>('tbody .row-actions button')!.click();
      fixture.detectChanges();
      expect(page.querySelector<HTMLInputElement>('#customer-name')?.value).toBe('Cliente 1');

      root.dataset['visualTheme'] = 'nocturno';
      expect(getComputedStyle(page).getPropertyValue('--admin-abm-surface').trim()).toBe('#24282e');
      expect(getComputedStyle(page).getPropertyValue('--admin-abm-control').trim()).toBe('#181b20');
      expect(page.querySelector<HTMLInputElement>('#customer-name')?.value).toBe('Cliente 1');
      http.expectNone((request) => request.method !== 'GET');
      fixture.destroy();
    } finally {
      if (previousTheme === null) root.removeAttribute('data-visual-theme');
      else root.setAttribute('data-visual-theme', previousTheme);
    }
  });

  it('opens editing and credit actions in dialogs without mutating before confirmation', () => {
    const fixture = TestBed.createComponent(CustomersPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/sessions/current').flush({ userId: 'admin', username: 'admin', expiresAtUtc: '',
      context: { userId: 'admin', companyId: 'c', companyName: 'Empresa', branchId: 'b', branchName: 'Sucursal',
        permissions: ['organization.manage'], sessionId: 's' } });
    http.expectOne((request) => request.url === '/api/admin/customers').flush({
      items: [{ id: 'customer-1', code: 'C1', name: 'Cliente 1', isActive: true, creditEnabled: false }],
      page: 1, pageSize: 20, totalItems: 1, totalPages: 1,
    });
    fixture.detectChanges();
    const page = fixture.nativeElement as HTMLElement;
    page.querySelector<HTMLButtonElement>('tbody .row-actions button')!.click();
    fixture.detectChanges();
    expect(page.querySelector('dialog[aria-labelledby="customer-editor-title"]')).not.toBeNull();
    page.querySelector<HTMLButtonElement>('.editor-actions .secondary-button')!.click();
    fixture.detectChanges();
    page.querySelectorAll<HTMLButtonElement>('tbody .row-actions button')[1].click();
    fixture.detectChanges();
    expect(page.querySelector('dialog[aria-labelledby="customer-action-title"]')).not.toBeNull();
    http.expectNone('/api/admin/customers/customer-1');
    page.querySelector<HTMLButtonElement>('.confirm-customer-action')!.click();
    const update = http.expectOne('/api/admin/customers/customer-1');
    expect(update.request.body).toEqual({ creditEnabled: true });
    update.flush({ id: 'customer-1', code: 'C1', name: 'Cliente 1', isActive: true, creditEnabled: true });
    fixture.detectChanges();
    expect(page.querySelector('dialog[aria-labelledby="customer-action-title"]')).toBeNull();
  });
});
