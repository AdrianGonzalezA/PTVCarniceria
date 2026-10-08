import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { UsersPage } from './users-page';

describe('UsersPage', () => {
  const admin = { userId: 'admin-id', username: 'visual-admin', email: 'admin@example.test',
    isActive: true, createdAtUtc: '2026-10-01T00:00:00Z' };
  const cashier = { userId: 'cashier-id', username: 'visual-cashier', email: 'cashier@example.test',
    isActive: true, createdAtUtc: '2026-10-01T00:00:00Z' };

  beforeEach(() => TestBed.configureTestingModule({
    imports: [UsersPage],
    providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
  }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  function load() {
    const fixture = TestBed.createComponent(UsersPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/sessions/current').flush({
      userId: admin.userId, username: admin.username, expiresAtUtc: '2026-10-09T00:00:00Z',
      context: { userId: admin.userId, companyId: 'company-1', companyName: 'Empresa Visual',
        branchId: 'branch-1', branchName: 'Centro', sessionId: 'session-id',
        permissions: ['platform.users.manage'] },
    });
    http.expectOne('/api/operational-contexts').flush([{ companyId: 'company-1',
      companyName: 'Empresa Visual', branches: [{ branchId: 'branch-1', branchName: 'Centro' },
        { branchId: 'branch-2', branchName: 'Norte' }] }]);
    http.expectOne((request) => request.url === '/api/users').flush({
      items: [admin, cashier], page: 1, pageSize: 20, totalItems: 2, totalPages: 1,
    });
    fixture.detectChanges();
    return { fixture, http, page: fixture.nativeElement as HTMLElement };
  }

  it('creates a cashier scoped to selected branches, never another administrator', () => {
    const { fixture, http, page } = load();
    page.querySelector<HTMLButtonElement>('.create-user-button')!.click();
    fixture.detectChanges();
    for (const [id, value] of Object.entries({
      'create-username': 'nuevo-cajero', 'create-email': 'nuevo@example.test',
      'create-password': 'segura-de-prueba-2026',
    })) {
      const input = page.querySelector<HTMLInputElement>(`#${id}`)!;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    }
    const checkbox = page.querySelector<HTMLInputElement>('.user-editor input[type="checkbox"]')!;
    checkbox.checked = true;
    checkbox.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    page.querySelector<HTMLFormElement>('.user-editor')!.dispatchEvent(new Event('submit'));
    const request = http.expectOne('/api/users');
    expect(request.request.body).toEqual({ username: 'nuevo-cajero', email: 'nuevo@example.test',
      password: 'segura-de-prueba-2026', branchIds: ['branch-1'] });
    request.flush({ ...cashier, userId: 'new-id', username: 'nuevo-cajero' });
    http.expectOne((candidate) => candidate.url === '/api/users').flush({
      items: [admin, cashier], page: 1, pageSize: 20, totalItems: 2, totalPages: 1,
    });
    fixture.detectChanges();
    expect(page.querySelector('#create-password')).toBeNull();
  });

  it('loads and updates cashier branch assignments', () => {
    const { fixture, http, page } = load();
    const button = Array.from(page.querySelectorAll<HTMLButtonElement>('.row-actions button'))
      .find((item) => item.textContent?.includes('Sucursales'))!;
    button.click();
    http.expectOne('/api/users/admin-id/assignments').flush({ role: 'administrator', branchIds: [] });
    fixture.detectChanges();
    expect(page.textContent).toContain('administrador único');
    page.querySelector<HTMLButtonElement>('.user-editor .secondary-button')!.click();
    fixture.detectChanges();
    const cashierButton = Array.from(page.querySelectorAll<HTMLButtonElement>('.row-actions'))[1]
      .querySelectorAll('button')[3];
    cashierButton.click();
    http.expectOne('/api/users/cashier-id/assignments').flush({ role: 'cashier', branchIds: ['branch-1'] });
    fixture.detectChanges();
    const north = Array.from(page.querySelectorAll<HTMLInputElement>('.user-editor input[type="checkbox"]'))[1];
    north.checked = true;
    north.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    const save = Array.from(page.querySelectorAll<HTMLButtonElement>('.user-editor button'))
      .find((item) => item.textContent?.includes('Guardar asignaciones'))!;
    save.click();
    const request = http.expectOne('/api/users/cashier-id/assignments');
    expect(request.request.body).toEqual({ branchIds: ['branch-1', 'branch-2'] });
    request.flush({ role: 'cashier', branchIds: ['branch-1', 'branch-2'] });
    fixture.detectChanges();
    expect(page.textContent).toContain('Asignaciones actualizadas');
  });

  it('resets a cashier password without exposing it in the user list', () => {
    const { fixture, http, page } = load();
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true);
    try {
      const cashierRow = page.querySelectorAll('tbody tr')[1];
      const button = Array.from(cashierRow.querySelectorAll('button'))
        .find((item) => item.textContent?.includes('Contraseña'))!;
      button.click();
      fixture.detectChanges();
      const input = page.querySelector<HTMLInputElement>('#reset-password')!;
      input.value = 'clave-nueva-de-prueba';
      input.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      const form = input.closest('form')!;
      form.dispatchEvent(new Event('submit'));
      const request = http.expectOne('/api/users/cashier-id/password');
      expect(request.request.body).toEqual({ password: 'clave-nueva-de-prueba' });
      request.flush(null, { status: 204, statusText: 'No Content' });
      fixture.detectChanges();
      expect(page.querySelector('#reset-password')).toBeNull();
      expect(page.querySelector('tbody')?.textContent).not.toContain('clave-nueva-de-prueba');
    } finally {
      confirm.mockRestore();
    }
  });
});
