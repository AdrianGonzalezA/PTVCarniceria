import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { SuppliersPage } from './suppliers-page';

describe('SuppliersPage', () => {
  const session = {
    userId: 'admin', username: 'admin', expiresAtUtc: '2026-11-06T00:00:00Z',
    context: {
      userId: 'admin', companyId: 'company-a', companyName: 'Empresa A',
      branchId: 'branch-a', branchName: 'Sucursal A',
      permissions: ['organization.manage'], sessionId: 'session',
    },
  };

  beforeEach(() => TestBed.configureTestingModule({
    imports: [SuppliersPage],
    providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
  }));

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('creates a company supplier and refreshes the list', () => {
    const fixture = TestBed.createComponent(SuppliersPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/sessions/current').flush(session);
    http.expectOne((request) => request.url === '/api/admin/suppliers').flush({
      items: [], page: 1, pageSize: 20, totalItems: 0, totalPages: 0,
    });
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    host.querySelector<HTMLButtonElement>('.create-button')!.click();
    fixture.detectChanges();
    const set = (selector: string, value: string) => {
      const input = host.querySelector<HTMLInputElement>(selector)!;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    };
    set('#supplier-code', 'FR-1');
    set('#supplier-name', 'Frigorífico Norte');
    set('#supplier-identification', '30710106513');
    fixture.detectChanges();
    host.querySelector<HTMLFormElement>('.editor-form')!.dispatchEvent(new Event('submit'));

    const create = http.expectOne('/api/admin/suppliers');
    expect(create.request.method).toBe('POST');
    expect(create.request.body).toEqual({
      code: 'FR-1', name: 'Frigorífico Norte', identification: '30710106513',
    });
    create.flush({ id: 'supplier-a', code: 'FR-1', name: 'Frigorífico Norte',
      identification: '30710106513', isActive: true });
    http.expectOne((request) => request.url === '/api/admin/suppliers').flush({
      items: [{ id: 'supplier-a', code: 'FR-1', name: 'Frigorífico Norte',
        identification: '30710106513', isActive: true }],
      page: 1, pageSize: 20, totalItems: 1, totalPages: 1,
    });
    fixture.detectChanges();
    expect(host.querySelector('tbody')?.textContent).toContain('Frigorífico Norte');
  });

  it('requires confirmation to inactivate a supplier and keeps it visible', () => {
    const fixture = TestBed.createComponent(SuppliersPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/sessions/current').flush(session);
    http.expectOne((request) => request.url === '/api/admin/suppliers').flush({
      items: [{ id: 'supplier-a', code: 'FR-1', name: 'Frigorífico Norte',
        identification: null, isActive: true }],
      page: 1, pageSize: 20, totalItems: 1, totalPages: 1,
    });
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    host.querySelector<HTMLButtonElement>('.toggle-button')!.click();
    fixture.detectChanges();
    http.expectNone('/api/admin/suppliers/supplier-a');
    host.querySelector<HTMLButtonElement>('.confirm-supplier-action')!.click();
    const update = http.expectOne('/api/admin/suppliers/supplier-a');
    expect(update.request.body).toEqual({ isActive: false });
    update.flush({ id: 'supplier-a', code: 'FR-1', name: 'Frigorífico Norte',
      identification: null, isActive: false });
    fixture.detectChanges();
    expect(host.querySelector('tbody')?.textContent).toContain('Inactivo');
  });
});
