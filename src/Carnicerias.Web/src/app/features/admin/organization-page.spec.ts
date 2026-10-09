import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { OrganizationPage } from './organization-page';

describe('OrganizationPage', () => {
  const session = {
    userId: 'admin-id', username: 'visual-admin', expiresAtUtc: '2026-10-09T00:00:00Z',
    context: { userId: 'admin-id', companyId: 'company-1', companyName: 'Empresa Visual',
      branchId: 'branch-1', branchName: 'Centro', sessionId: 'session-id',
      permissions: ['platform.users.manage', 'organization.manage'] },
  };
  const company = { id: 'company-1', name: 'Empresa Visual', isActive: true, activeBranchCount: 1 };
  const branch = { id: 'branch-1', name: 'Centro', isActive: true, activeTerminalCount: 1 };

  beforeEach(() => TestBed.configureTestingModule({
    imports: [OrganizationPage],
    providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
  }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  function load(companies = [company]) {
    const fixture = TestBed.createComponent(OrganizationPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/sessions/current').flush(session);
    http.expectOne('/api/admin/companies').flush(companies);
    fixture.detectChanges();
    return { fixture, http, page: fixture.nativeElement as HTMLElement };
  }

  it('shows the current company and prevents inactivating its active branch', () => {
    const { fixture, http, page } = load();
    expect(page.querySelector('h1')?.textContent).toContain('Organización');
    expect(page.querySelector('.company-row')?.textContent).toContain('Empresa Visual');
    expect(page.querySelector('dialog')).toBeNull();
    page.querySelector<HTMLButtonElement>('.company-row button')!.click();
    http.expectOne('/api/admin/companies/company-1/branches').flush([branch]);
    fixture.detectChanges();
    expect(page.querySelector('dialog[open] .branch-row')).not.toBeNull();
    expect(page.querySelector('.branch-row')?.textContent).toContain('Centro');
    expect(page.querySelector<HTMLButtonElement>('.branch-row .toggle-button')?.disabled).toBe(true);
  });

  it('creates a company with its first branch', () => {
    const { fixture, http, page } = load();
    page.querySelector<HTMLButtonElement>('.create-company-button')!.click();
    fixture.detectChanges();
    expect(page.querySelector('dialog[aria-labelledby="company-editor-title"]')).not.toBeNull();
    for (const [id, value] of Object.entries({
      'company-name': 'Nueva empresa', 'initial-branch-name': 'Principal',
    })) {
      const input = page.querySelector<HTMLInputElement>(`#${id}`)!;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    }
    fixture.detectChanges();
    page.querySelector<HTMLFormElement>('.company-form')!.dispatchEvent(new Event('submit'));
    const create = http.expectOne('/api/admin/companies');
    expect(create.request.body).toEqual({ name: 'Nueva empresa', initialBranchName: 'Principal' });
    create.flush({ id: 'company-2', name: 'Nueva empresa',
      initialBranchId: 'branch-2', initialBranchName: 'Principal' });
    http.expectOne('/api/admin/companies').flush([company,
      { id: 'company-2', name: 'Nueva empresa', isActive: true, activeBranchCount: 1 }]);
    fixture.detectChanges();
    expect(page.textContent).toContain('Nueva empresa');
  });

  it('adds a branch under the selected company', () => {
    const { fixture, http, page } = load();
    page.querySelector<HTMLButtonElement>('.company-row button')!.click();
    http.expectOne('/api/admin/companies/company-1/branches').flush([branch]);
    fixture.detectChanges();
    page.querySelector<HTMLButtonElement>('.create-branch-button')!.click();
    fixture.detectChanges();
    expect(page.querySelector('dialog[aria-labelledby="branch-editor-title"]')).not.toBeNull();
    expect(page.querySelector('.branches-panel')).toBeNull();
    const name = page.querySelector<HTMLInputElement>('#branch-name')!;
    name.value = 'Norte';
    name.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    page.querySelector<HTMLFormElement>('.branch-form')!.dispatchEvent(new Event('submit'));
    const create = http.expectOne('/api/admin/companies/company-1/branches');
    expect(create.request.body).toEqual({ name: 'Norte' });
    create.flush({ id: 'branch-2', name: 'Norte', isActive: true, activeTerminalCount: 0 });
    fixture.detectChanges();
    expect(page.textContent).toContain('Norte');
  });

  it('creates a terminal and shows its credential only until dismissed', () => {
    const { fixture, http, page } = load();
    page.querySelector<HTMLButtonElement>('.company-row button')!.click();
    http.expectOne('/api/admin/companies/company-1/branches').flush([branch]);
    fixture.detectChanges();
    page.querySelector<HTMLButtonElement>('.branch-row .terminals-button')!.click();
    http.expectOne('/api/admin/companies/company-1/branches/branch-1/terminals').flush([]);
    fixture.detectChanges();
    expect(page.querySelectorAll('dialog[open]').length).toBe(1);
    expect(page.querySelector('.branches-panel')).toBeNull();
    page.querySelector<HTMLButtonElement>('.create-terminal-button')!.click();
    fixture.detectChanges();
    expect(page.querySelector('dialog[aria-labelledby="terminal-editor-title"]')).not.toBeNull();
    expect(page.querySelector('.terminals-panel')).toBeNull();
    const name = page.querySelector<HTMLInputElement>('#terminal-name')!;
    name.value = 'Caja 2';
    name.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    page.querySelector<HTMLFormElement>('.terminal-form')!.dispatchEvent(new Event('submit'));
    const create = http.expectOne('/api/admin/companies/company-1/branches/branch-1/terminals');
    expect(create.request.body).toEqual({ name: 'Caja 2' });
    create.flush({ id: 'terminal-2', name: 'Caja 2', credential: 'one-time-secret' });
    fixture.detectChanges();
    expect(page.querySelector('.credential-notice')?.textContent).toContain('one-time-secret');
    page.querySelector<HTMLButtonElement>('.credential-notice button')!.click();
    fixture.detectChanges();
    expect(page.querySelector('.credential-notice')).toBeNull();
    page.querySelector<HTMLButtonElement>('.terminals-panel .header-actions .secondary-button')!.click();
    fixture.detectChanges();
    expect(page.querySelector('dialog[open] .branches-panel')).not.toBeNull();
  });

  it('confirms company inactivation in a dialog before updating', () => {
    const other = { id: 'company-2', name: 'Otra empresa', isActive: true, activeBranchCount: 0 };
    const { fixture, http, page } = load([company, other]);
    page.querySelector<HTMLButtonElement>('.company-row:last-child .toggle-button')!.click();
    fixture.detectChanges();
    expect(page.querySelector('dialog[aria-labelledby="organization-action-title"]')).not.toBeNull();
    http.expectNone('/api/admin/companies/company-2');
    page.querySelector<HTMLButtonElement>('.confirm-organization-action')!.click();
    const update = http.expectOne('/api/admin/companies/company-2');
    expect(update.request.body).toEqual({ isActive: false });
    update.flush({ ...other, isActive: false });
    fixture.detectChanges();
    expect(page.querySelector('dialog')).toBeNull();
  });

  it('confirms a branch status change without leaving two dialogs open', () => {
    const { fixture, http, page } = load();
    page.querySelector<HTMLButtonElement>('.company-row button')!.click();
    http.expectOne('/api/admin/companies/company-1/branches').flush([branch,
      { id: 'branch-2', name: 'Norte', isActive: true, activeTerminalCount: 0 }]);
    fixture.detectChanges();
    page.querySelector<HTMLButtonElement>('.branch-row:last-child .toggle-button')!.click();
    fixture.detectChanges();
    expect(page.querySelectorAll('dialog[open]')).toHaveLength(1);
    expect(page.querySelector('dialog[aria-labelledby="organization-action-title"]')).not.toBeNull();
    page.querySelector<HTMLButtonElement>('.confirm-organization-action')!.click();
    const update = http.expectOne('/api/admin/companies/company-1/branches/branch-2');
    expect(update.request.body).toEqual({ isActive: false });
    update.flush({ id: 'branch-2', name: 'Norte', isActive: false, activeTerminalCount: 0 });
    fixture.detectChanges();
    expect(page.querySelector('.branches-panel')?.textContent).toContain('Norte');
  });

  it('confirms credential rotation and then shows the one-time value', () => {
    const { fixture, http, page } = load();
    page.querySelector<HTMLButtonElement>('.company-row button')!.click();
    http.expectOne('/api/admin/companies/company-1/branches').flush([branch]);
    fixture.detectChanges();
    page.querySelector<HTMLButtonElement>('.branch-row .terminals-button')!.click();
    http.expectOne('/api/admin/companies/company-1/branches/branch-1/terminals').flush([
      { id: 'terminal-1', name: 'Caja 1', isActive: true, isHistorical: false, hasCredential: true },
    ]);
    fixture.detectChanges();
    page.querySelectorAll<HTMLButtonElement>('.terminal-row button')[1].click();
    fixture.detectChanges();
    expect(page.querySelectorAll('dialog[open]')).toHaveLength(1);
    expect(page.querySelector('dialog[aria-labelledby="organization-action-title"]')).not.toBeNull();
    page.querySelector<HTMLButtonElement>('.confirm-organization-action')!.click();
    const rotate = http.expectOne('/api/admin/companies/company-1/branches/branch-1/terminals/terminal-1/rotate');
    expect(rotate.request.method).toBe('POST');
    rotate.flush({ credential: 'new-secret' });
    fixture.detectChanges();
    expect(page.querySelector('.credential-notice')?.textContent).toContain('new-secret');
  });
});
