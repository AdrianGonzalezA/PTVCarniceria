import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { CentralManufacturerPage } from './central-manufacturer-page';

describe('CentralManufacturerPage', () => {
  function setup() {
    TestBed.configureTestingModule({
      imports: [CentralManufacturerPage],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    const fixture = TestBed.createComponent(CentralManufacturerPage);
    fixture.detectChanges();
    return { fixture, http: TestBed.inject(HttpTestingController),
      element: fixture.nativeElement as HTMLElement };
  }

  function type(element: HTMLElement, id: string, value: string) {
    const input = element.querySelector<HTMLInputElement>(`#${id}`)!;
    input.value = value;
    input.dispatchEvent(new Event('input', { bubbles: true }));
  }

  it('uses an independent login and creates a central company with its first administrator', async () => {
    const { fixture, http, element } = setup();
    http.expectOne('/api/v1/superadmin/sessions/current').flush(null,
      { status: 401, statusText: 'Unauthorized' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(element.querySelector('h1')?.textContent).toContain('SuperAdmin central');
    expect(element.querySelector('label[for="manufacturer-username"]')).not.toBeNull();
    http.expectNone('/api/v1/sessions/current');
    type(element, 'manufacturer-username', 'superadmin-central');
    type(element, 'manufacturer-password', 'a distinct central password');
    element.querySelector<HTMLFormElement>('#manufacturer-login-form')!.dispatchEvent(
      new Event('submit', { bubbles: true, cancelable: true }));
    http.expectOne('/api/v1/superadmin/sessions').flush({
      userId: 'm-1', username: 'superadmin-central', expiresAtUtc: '2026-10-15T20:00:00Z',
    });
    http.expectOne('/api/v1/superadmin/companies?page=1&pageSize=20').flush({
      data: [], pagination: { page: 1, pageSize: 20, totalCount: 0, totalPages: 0 },
    });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(element.textContent).toContain('Todavía no hay empresas centrales');

    element.querySelector<HTMLButtonElement>('#new-central-company')!.click();
    fixture.detectChanges();
    type(element, 'central-company-code', 'RUSTICA-CENTRAL');
    type(element, 'central-company-name', 'LA Rústica');
    type(element, 'central-company-admin', 'admin-rustica');
    type(element, 'central-company-password', 'a distinct tenant password');
    element.querySelector<HTMLFormElement>('#create-central-company-form')!.dispatchEvent(
      new Event('submit', { bubbles: true, cancelable: true }));
    const create = http.expectOne('/api/v1/superadmin/companies');
    expect(create.request.body).toEqual({ code: 'RUSTICA-CENTRAL', name: 'LA Rústica',
      initialAdministratorUsername: 'admin-rustica',
      initialAdministratorPassword: 'a distinct tenant password' });
    create.flush({ id: 'c-1', code: 'RUSTICA-CENTRAL', name: 'LA Rústica',
      initialAdministratorId: 'a-1' });
    http.expectOne('/api/v1/superadmin/companies?page=1&pageSize=20').flush({
      data: [{ id: 'c-1', code: 'RUSTICA-CENTRAL', name: 'LA Rústica', isActive: true,
        administratorCount: 1, installationCount: 0 }],
      pagination: { page: 1, pageSize: 20, totalCount: 1, totalPages: 1 },
    });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(element.textContent).toContain('Empresa central RUSTICA-CENTRAL creada');
    expect(element.textContent).not.toContain('a distinct tenant password');
    http.verify();
  });

  it('shows administrators on a separate view and adds one only to the selected company', async () => {
    const { fixture, http, element } = setup();
    http.expectOne('/api/v1/superadmin/sessions/current').flush({
      userId: 'm-1', username: 'superadmin-central', expiresAtUtc: '2026-10-15T20:00:00Z',
    });
    http.expectOne('/api/v1/superadmin/companies?page=1&pageSize=20').flush({
      data: [{ id: 'c-1', code: 'RUSTICA-CENTRAL', name: 'LA Rústica', isActive: true,
        administratorCount: 1, installationCount: 0 }],
      pagination: { page: 1, pageSize: 20, totalCount: 1, totalPages: 1 },
    });
    await fixture.whenStable();
    fixture.detectChanges();

    element.querySelector<HTMLButtonElement>('[aria-label="Administrar LA Rústica"]')!.click();
    http.expectOne('/api/v1/superadmin/companies/c-1/administrators?page=1&pageSize=20').flush({
      data: [{ id: 'a-1', username: 'admin-rustica', isActive: true }],
      pagination: { page: 1, pageSize: 20, totalCount: 1, totalPages: 1 },
    });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(element.querySelector('#central-admins-title')?.textContent).toContain('LA Rústica');
    expect(element.querySelector('#central-companies-title')).toBeNull();

    type(element, 'additional-admin-username', 'another-admin');
    type(element, 'additional-admin-password', 'another tenant password');
    element.querySelector<HTMLFormElement>('#additional-admin-form')!.dispatchEvent(
      new Event('submit', { bubbles: true, cancelable: true }));
    const add = http.expectOne('/api/v1/superadmin/companies/c-1/administrators');
    expect(add.request.body).toEqual({ username: 'another-admin', password: 'another tenant password' });
    add.flush({ id: 'a-2', username: 'another-admin', isActive: true });
    http.expectOne('/api/v1/superadmin/companies/c-1/administrators?page=1&pageSize=20').flush({
      data: [{ id: 'a-1', username: 'admin-rustica', isActive: true },
        { id: 'a-2', username: 'another-admin', isActive: true }],
      pagination: { page: 1, pageSize: 20, totalCount: 2, totalPages: 1 },
    });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(element.textContent).toContain('another-admin');
    expect(element.textContent).not.toContain('another tenant password');
    http.verify();
  });

  it('does not show a previous company response after changing the selected company', async () => {
    const { fixture, http, element } = setup();
    http.expectOne('/api/v1/superadmin/sessions/current').flush({
      userId: 'm-1', username: 'superadmin-central', expiresAtUtc: '2026-10-15T20:00:00Z',
    });
    const companies = [
      { id: 'c-1', code: 'ONE', name: 'Empresa Uno', isActive: true,
        administratorCount: 1, installationCount: 0 },
      { id: 'c-2', code: 'TWO', name: 'Empresa Dos', isActive: true,
        administratorCount: 1, installationCount: 0 },
    ];
    const page = { data: companies,
      pagination: { page: 1, pageSize: 20, totalCount: 2, totalPages: 1 } };
    http.expectOne('/api/v1/superadmin/companies?page=1&pageSize=20').flush(page);
    await fixture.whenStable();
    fixture.detectChanges();

    element.querySelector<HTMLButtonElement>('[aria-label="Administrar Empresa Uno"]')!.click();
    const previous = http.expectOne('/api/v1/superadmin/companies/c-1/administrators?page=1&pageSize=20');
    fixture.detectChanges();
    element.querySelector<HTMLButtonElement>('.back-button')!.click();
    http.expectOne('/api/v1/superadmin/companies?page=1&pageSize=20').flush(page);
    fixture.detectChanges();
    element.querySelector<HTMLButtonElement>('[aria-label="Administrar Empresa Dos"]')!.click();
    const current = http.expectOne('/api/v1/superadmin/companies/c-2/administrators?page=1&pageSize=20');
    previous.flush({ data: [{ id: 'a-1', username: 'admin-uno', isActive: true }],
      pagination: { page: 1, pageSize: 20, totalCount: 1, totalPages: 1 } });
    current.flush({ data: [{ id: 'a-2', username: 'admin-dos', isActive: true }],
      pagination: { page: 1, pageSize: 20, totalCount: 1, totalPages: 1 } });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(element.textContent).toContain('admin-dos');
    expect(element.textContent).not.toContain('admin-uno');
    http.verify();
  });
});
