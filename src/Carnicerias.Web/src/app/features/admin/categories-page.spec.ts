import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { CategoriesPage } from './categories-page';

describe('CategoriesPage', () => {
  const adminSession = {
    userId: 'admin-id', username: 'visual-admin', expiresAtUtc: '2026-10-08T00:00:00Z',
    context: {
      userId: 'admin-id', companyId: 'company-id', companyName: 'Empresa Visual',
      branchId: 'branch-id', branchName: 'Sucursal Visual',
      permissions: ['platform.users.manage', 'catalog.manage'], sessionId: 'session-id',
    },
  };

  beforeEach(() => TestBed.configureTestingModule({
    imports: [CategoriesPage],
    providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
  }));

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('lists active and inactive categories for the current company', () => {
    const fixture = TestBed.createComponent(CategoriesPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/sessions/current').flush(adminSession);
    http.expectOne((request) => request.url === '/api/admin/categories').flush({
      items: [
        { id: 'cat-1', name: 'Vacuno', isActive: true, productCount: 4 },
        { id: 'cat-2', name: 'Cerdo', isActive: false, productCount: 1 },
      ],
      page: 1, pageSize: 20, totalItems: 2, totalPages: 1,
    });
    fixture.detectChanges();

    const page = fixture.nativeElement as HTMLElement;
    expect(page.querySelector('h1')?.textContent).toContain('Categorías');
    expect(page.querySelectorAll('tbody tr')).toHaveLength(2);
    expect(page.textContent).toContain('Vacuno');
    expect(page.textContent).toContain('Inactiva');
    expect(page.textContent).toContain('4 productos');
  });

  it('creates a category and refreshes the list', () => {
    const fixture = TestBed.createComponent(CategoriesPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/sessions/current').flush(adminSession);
    http.expectOne((request) => request.url === '/api/admin/categories').flush({
      items: [], page: 1, pageSize: 20, totalItems: 0, totalPages: 0,
    });
    fixture.detectChanges();

    const page = fixture.nativeElement as HTMLElement;
    page.querySelector<HTMLButtonElement>('.create-button')!.click();
    fixture.detectChanges();
    const name = page.querySelector<HTMLInputElement>('#category-name')!;
    name.value = 'Fiambres';
    name.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    page.querySelector<HTMLFormElement>('.editor-form')!.dispatchEvent(new Event('submit'));

    const create = http.expectOne('/api/admin/categories');
    expect(create.request.method).toBe('POST');
    expect(create.request.body).toEqual({ name: 'Fiambres' });
    create.flush({ id: 'cat-3', name: 'Fiambres', isActive: true, productCount: 0 });
    http.expectOne((request) => request.url === '/api/admin/categories').flush({
      items: [{ id: 'cat-3', name: 'Fiambres', isActive: true, productCount: 0 }],
      page: 1, pageSize: 20, totalItems: 1, totalPages: 1,
    });
    fixture.detectChanges();
    expect(page.textContent).toContain('Categoría creada');
    expect(page.querySelector('tbody')?.textContent).toContain('Fiambres');
  });

  it('warns before inactivating a category and reflects the saved state', () => {
    const fixture = TestBed.createComponent(CategoriesPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/sessions/current').flush(adminSession);
    http.expectOne((request) => request.url === '/api/admin/categories').flush({
      items: [{ id: 'cat-1', name: 'Vacuno', isActive: true, productCount: 4 }],
      page: 1, pageSize: 20, totalItems: 1, totalPages: 1,
    });
    fixture.detectChanges();

    const page = fixture.nativeElement as HTMLElement;
    page.querySelector<HTMLButtonElement>('tbody .toggle-button')!.click();
    fixture.detectChanges();
    expect(page.querySelector('dialog[aria-labelledby="category-action-title"]')).not.toBeNull();
    http.expectNone('/api/admin/categories/cat-1');
    page.querySelector<HTMLButtonElement>('.confirm-category-action')!.click();
    const update = http.expectOne('/api/admin/categories/cat-1');
    expect(update.request.method).toBe('PATCH');
    expect(update.request.body).toEqual({ isActive: false });
    update.flush({ id: 'cat-1', name: 'Vacuno', isActive: false, productCount: 4 });
    fixture.detectChanges();

    expect(page.querySelector('tbody')?.textContent).toContain('Inactiva');
    expect(page.querySelector('dialog[aria-labelledby="category-action-title"]')).toBeNull();
  });

  it('renames a category without changing its state', () => {
    const fixture = TestBed.createComponent(CategoriesPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/sessions/current').flush(adminSession);
    http.expectOne((request) => request.url === '/api/admin/categories').flush({
      items: [{ id: 'cat-1', name: 'Vacuno', isActive: true, productCount: 4 }],
      page: 1, pageSize: 20, totalItems: 1, totalPages: 1,
    });
    fixture.detectChanges();

    const page = fixture.nativeElement as HTMLElement;
    page.querySelector<HTMLButtonElement>('tbody .row-actions button')!.click();
    fixture.detectChanges();
    expect(page.querySelector('dialog[aria-labelledby="category-editor-title"]')).not.toBeNull();
    const name = page.querySelector<HTMLInputElement>('#category-name')!;
    name.value = 'Carnes vacunas';
    name.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    page.querySelector<HTMLFormElement>('.editor-form')!.dispatchEvent(new Event('submit'));

    const update = http.expectOne('/api/admin/categories/cat-1');
    expect(update.request.body).toEqual({ name: 'Carnes vacunas' });
    update.flush({ id: 'cat-1', name: 'Carnes vacunas', isActive: true, productCount: 4 });
    fixture.detectChanges();
    expect(page.querySelector('tbody')?.textContent).toContain('Carnes vacunas');
    expect(page.querySelector('tbody')?.textContent).toContain('Activa');
  });
});
