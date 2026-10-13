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
    username.value = 'fabricante';
    username.dispatchEvent(new Event('input'));
    password.value = 'clave-de-prueba';
    password.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    username.closest('form')!.dispatchEvent(new Event('submit'));
    const login = http.expectOne('/api/manufacturer/sessions');
    expect(login.request.body).toEqual({ credential: 'fabricante', password: 'clave-de-prueba' });
    login.flush({ userId: 'manufacturer-id', username: 'fabricante', expiresAtUtc: '2026-10-14T00:00:00Z' });
    http.expectOne('/api/manufacturer/companies').flush([]);
    fixture.detectChanges();

    expect(page.querySelector<HTMLButtonElement>('.create-company-button')).not.toBeNull();
    expect(page.textContent).toContain('Esta cuenta no abre cajas ni consulta ventas.');
  });
});
