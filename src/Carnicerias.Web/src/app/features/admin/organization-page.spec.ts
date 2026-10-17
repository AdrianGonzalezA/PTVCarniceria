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

  it('themes organization selection and branch details without changing entities', () => {
    const root = document.documentElement;
    const previousTheme = root.getAttribute('data-visual-theme');
    try {
      root.dataset['visualTheme'] = 'original';
      const { fixture, http, page } = load();
      expect(getComputedStyle(page).getPropertyValue('--admin-org-selected').trim()).toBe('#dceeed');
      page.querySelector<HTMLButtonElement>('.company-row button')!.click();
      http.expectOne('/api/admin/companies/company-1/branches').flush([branch]);
      fixture.detectChanges();
      expect(page.querySelector('dialog[open] .branch-row')?.textContent).toContain('Centro');

      root.dataset['visualTheme'] = 'nocturno';
      expect(getComputedStyle(page).getPropertyValue('--admin-org-selected').trim()).toBe('#48392b');
      expect(getComputedStyle(page).getPropertyValue('--admin-org-notice').trim()).toBe('#2d3239');
      expect(page.querySelector('dialog[open] .branch-row')?.textContent).toContain('Centro');
      http.expectNone((request) => request.method !== 'GET');
      fixture.destroy();
    } finally {
      if (previousTheme === null) root.removeAttribute('data-visual-theme');
      else root.setAttribute('data-visual-theme', previousTheme);
    }
  });

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

  it('does not offer company creation or editing to a client administrator', () => {
    const { page } = load();
    expect(page.querySelector('.create-company-button')).toBeNull();
    expect(page.querySelector('.company-row .toggle-button')).toBeNull();
    expect(page.textContent).toContain('El alta y el estado de las empresas los administra el fabricante');
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

  it('assigns or removes serial devices for a specific box in a modal', () => {
    const { fixture, http, page } = load();
    page.querySelector<HTMLButtonElement>('.company-row button')!.click();
    http.expectOne('/api/admin/companies/company-1/branches').flush([branch]);
    fixture.detectChanges();
    page.querySelector<HTMLButtonElement>('.branch-row .terminals-button')!.click();
    const terminal = { id: 'terminal-1', name: 'Caja 1', isActive: true,
      isHistorical: false, hasCredential: true, devices: { scale: null, printer: null } };
    http.expectOne('/api/admin/companies/company-1/branches/branch-1/terminals').flush([terminal]);
    fixture.detectChanges();
    expect(page.querySelector('.terminal-row')?.textContent).toContain('Sin impresora');
    page.querySelector<HTMLButtonElement>('.terminal-row .devices-button')!.click();
    fixture.detectChanges();
    expect(page.querySelector('dialog[aria-labelledby="terminal-devices-title"]')).not.toBeNull();
    expect(page.querySelectorAll('dialog[open]')).toHaveLength(1);

    const scaleMode = page.querySelector<HTMLSelectElement>('#device-scale-mode')!;
    scaleMode.value = 'serial';
    scaleMode.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    const scalePort = page.querySelector<HTMLInputElement>('#device-scale-port')!;
    scalePort.value = 'COM6';
    scalePort.dispatchEvent(new Event('input'));
    const printerKind = page.querySelector<HTMLSelectElement>('#device-printer-kind')!;
    printerKind.value = 'hasar';
    printerKind.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    const printerPort = page.querySelector<HTMLInputElement>('#device-printer-port')!;
    printerPort.value = 'COM8';
    printerPort.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    page.querySelector<HTMLFormElement>('.terminal-devices-form')!
      .dispatchEvent(new Event('submit'));
    const update = http.expectOne('/api/admin/companies/company-1/branches/branch-1/terminals/terminal-1/devices');
    expect(update.request.method).toBe('PUT');
    expect(update.request.body).toEqual({
      scale: { port: 'COM6', baudRate: 9600 },
      printer: { kind: 'hasar', port: 'COM8', baudRate: 9600 },
    });
    update.flush({ ...terminal, devices: update.request.body });
    fixture.detectChanges();
    expect(page.querySelector('.terminal-row')?.textContent).toContain('Balanza COM6');
    expect(page.querySelector('.terminal-row')?.textContent).toContain('Hasar COM8');

    page.querySelector<HTMLButtonElement>('.terminal-row .devices-button')!.click();
    fixture.detectChanges();
    const removeScale = page.querySelector<HTMLSelectElement>('#device-scale-mode')!;
    removeScale.value = 'none';
    removeScale.dispatchEvent(new Event('change'));
    const removePrinter = page.querySelector<HTMLSelectElement>('#device-printer-kind')!;
    removePrinter.value = 'none';
    removePrinter.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    page.querySelector<HTMLFormElement>('.terminal-devices-form')!
      .dispatchEvent(new Event('submit'));
    const remove = http.expectOne('/api/admin/companies/company-1/branches/branch-1/terminals/terminal-1/devices');
    expect(remove.request.body).toEqual({ scale: null, printer: null });
    remove.flush(terminal);
    fixture.detectChanges();
    expect(page.querySelector('.terminal-row')?.textContent).toContain('Sin balanza · Sin impresora');
  });
});
