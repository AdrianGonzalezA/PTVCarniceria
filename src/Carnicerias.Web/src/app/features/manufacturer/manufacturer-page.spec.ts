import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ManufacturerPage } from './manufacturer-page';

describe('ManufacturerPage', () => {
  beforeEach(() => TestBed.configureTestingModule({
    imports: [ManufacturerPage],
    providers: [provideHttpClient(), provideHttpClientTesting()],
  }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('uses a separate manufacturer login and then offers company creation', () => {
    const fixture = TestBed.createComponent(ManufacturerPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/manufacturer/sessions/current').flush(null,
      { status: 401, statusText: 'Unauthorized' });
    fixture.detectChanges();

    const page = fixture.nativeElement as HTMLElement;
    const username = page.querySelector<HTMLInputElement>('#manufacturer-credential')!;
    const password = page.querySelector<HTMLInputElement>('#manufacturer-password')!;
    username.value = 'superadmin';
    username.dispatchEvent(new Event('input'));
    password.value = 'clave-de-prueba';
    password.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    username.closest('form')!.dispatchEvent(new Event('submit'));
    const login = http.expectOne('/api/manufacturer/sessions');
    expect(login.request.body).toEqual({ credential: 'superadmin', password: 'clave-de-prueba' });
    login.flush({ userId: 'manufacturer-id', username: 'superadmin', expiresAtUtc: '2026-10-14T00:00:00Z' });
    http.expectOne('/api/manufacturer/companies').flush([]);
    fixture.detectChanges();

    expect(page.querySelector<HTMLButtonElement>('.create-company-button')).not.toBeNull();
    expect(page.textContent).toContain('Esta cuenta no abre cajas ni consulta ventas.');
    expect(page.querySelector('.brand')?.textContent).toContain('SuperAdmin');
    expect(page.querySelector('.eyebrow')?.textContent).toContain('SuperAdmin');
  });

  it('identifies each company state and action in the list', () => {
    const fixture = TestBed.createComponent(ManufacturerPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/manufacturer/sessions/current').flush({
      userId: 'manufacturer-id', username: 'superadmin', expiresAtUtc: '2026-10-14T00:00:00Z',
    });
    http.expectOne('/api/manufacturer/companies').flush([{
      id: 'company-id', code: 'VISUAL', name: 'Empresa Visual', activeBranchCount: 1, isActive: true,
    }]);
    fixture.detectChanges();

    const page = fixture.nativeElement as HTMLElement;
    expect(page.querySelector('.manufacturer-status--active')?.textContent).toContain('Activa');
    expect(page.querySelector('button[aria-label="Editar Empresa Visual"]')).not.toBeNull();
    expect(page.querySelector('button[aria-label="Inactivar Empresa Visual"]')).not.toBeNull();
  });

  it('creates another administrator from Editar empresa without changing the company', () => {
    const fixture = TestBed.createComponent(ManufacturerPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/manufacturer/sessions/current').flush({
      userId: 'manufacturer-id', username: 'superadmin', expiresAtUtc: '2026-10-14T00:00:00Z',
    });
    http.expectOne('/api/manufacturer/companies').flush([{
      id: 'company-id', code: 'VISUAL', name: 'Empresa Visual', activeBranchCount: 1, isActive: true,
    }]);
    fixture.detectChanges();

    const page = fixture.nativeElement as HTMLElement;
    page.querySelector<HTMLButtonElement>('button[aria-label="Editar Empresa Visual"]')!.click();
    fixture.detectChanges();
    const form = page.querySelector<HTMLFormElement>('form[data-testid="create-company-admin"]')!;
    expect(form).not.toBeNull();
    const values = { username: 'admin-nuevo', email: 'nuevo@visual.test', password: 'clave-de-prueba' };
    for (const [key, value] of Object.entries(values)) {
      const input = form.querySelector<HTMLInputElement>(`[formControlName="${key}"]`)!;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    }
    fixture.detectChanges();
    form.dispatchEvent(new Event('submit'));
    const request = http.expectOne('/api/manufacturer/companies/company-id/administrators');
    expect(request.request.body).toEqual(values);
    request.flush({ id: 'admin-id', username: values.username, email: values.email });
    fixture.detectChanges();
    expect(page.textContent).toContain('Administrador admin-nuevo creado');
    expect(form.querySelector<HTMLInputElement>('[formControlName="password"]')?.value).toBe('');
    http.expectNone('/api/manufacturer/companies/company-id');
  });

  it('keeps the administrator form open and explains a duplicate account', () => {
    const fixture = TestBed.createComponent(ManufacturerPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/manufacturer/sessions/current').flush({
      userId: 'manufacturer-id', username: 'superadmin', expiresAtUtc: '2026-10-14T00:00:00Z',
    });
    http.expectOne('/api/manufacturer/companies').flush([{
      id: 'company-id', code: 'VISUAL', name: 'Empresa Visual', activeBranchCount: 1, isActive: true,
    }]);
    fixture.detectChanges();
    const page = fixture.nativeElement as HTMLElement;
    page.querySelector<HTMLButtonElement>('button[aria-label="Editar Empresa Visual"]')!.click();
    fixture.detectChanges();
    const form = page.querySelector<HTMLFormElement>('form[data-testid="create-company-admin"]')!;
    form.dispatchEvent(new Event('submit'));
    http.expectNone('/api/manufacturer/companies/company-id/administrators');
    fixture.detectChanges();
    expect(page.textContent).toContain('Revisá usuario, correo y contraseña.');

    for (const [key, value] of Object.entries({ username: 'admin', email: 'admin@visual.test', password: 'clave-de-prueba' })) {
      const input = form.querySelector<HTMLInputElement>(`[formControlName="${key}"]`)!;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    }
    fixture.detectChanges();
    form.dispatchEvent(new Event('submit'));
    http.expectOne('/api/manufacturer/companies/company-id/administrators').flush(
      { error: { code: 'USER_ALREADY_EXISTS' } }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();
    expect(page.textContent).toContain('El usuario o correo ya existe en esta empresa.');
    expect(page.querySelector('form[data-testid="create-company-admin"]')).not.toBeNull();
  });
});
