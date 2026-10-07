import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AdminPage } from './admin-page';

describe('AdminPage', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AdminPage],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
  });

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('shows the active organization and existing management links', () => {
    const fixture = TestBed.createComponent(AdminPage);
    fixture.detectChanges();

    TestBed.inject(HttpTestingController).expectOne('/api/sessions/current').flush({
      userId: 'admin-id',
      username: 'visual-admin',
      expiresAtUtc: '2026-10-08T00:00:00Z',
      context: {
        userId: 'admin-id',
        companyId: 'company-id',
        companyName: 'Empresa Visual',
        branchId: 'branch-id',
        branchName: 'Sucursal Visual',
        permissions: ['platform.users.manage'],
        sessionId: 'session-id',
      },
    });
    fixture.detectChanges();

    const page = fixture.nativeElement as HTMLElement;
    expect(page.querySelector('h1')?.textContent).toContain('Administración');
    expect(page.textContent).toContain('Empresa Visual');
    expect(page.textContent).toContain('Sucursal Visual');
    expect(page.querySelector('a[href="/users"]')?.textContent).toContain('Usuarios');
    expect(page.querySelector('a[href="/pos"]')?.textContent).toContain('Punto de venta');
  });
});
