import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ProductionClassifiersPage } from './production-classifiers-page';

describe('ProductionClassifiersPage', () => {
  beforeEach(() => TestBed.configureTestingModule({
    imports: [ProductionClassifiersPage],
    providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
  }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('creates a classifier and can deactivate it without removing it', () => {
    const fixture = TestBed.createComponent(ProductionClassifiersPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(request => request.url === '/api/production/classifiers' &&
      request.params.get('includeInactive') === 'true').flush([]);
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    const name = host.querySelector<HTMLInputElement>('#classifier-name')!;
    name.value = 'Desarme'; name.dispatchEvent(new Event('input'));
    host.querySelector<HTMLFormElement>('.editor-form')!
      .dispatchEvent(new Event('submit'));
    const create = http.expectOne('/api/production/classifiers');
    expect(create.request.body).toEqual({ name: 'Desarme' });
    create.flush({ id: 'classifier', name: 'Desarme', isActive: true });
    http.expectOne(request => request.url === '/api/production/classifiers')
      .flush([{ id: 'classifier', name: 'Desarme', isActive: true }]);
    fixture.detectChanges();
    host.querySelector<HTMLButtonElement>('tbody button')!.click();
    fixture.detectChanges();
    const checkbox = host.querySelector<HTMLInputElement>('.classifier-dialog input[type=checkbox]')!;
    checkbox.checked = false; checkbox.dispatchEvent(new Event('change'));
    host.querySelector<HTMLButtonElement>('.classifier-dialog button')!.click();
    const update = http.expectOne('/api/production/classifiers/classifier');
    expect(update.request.method).toBe('PATCH');
    expect(update.request.body).toEqual({ name: 'Desarme', isActive: false });
    update.flush({ id: 'classifier', name: 'Desarme', isActive: false });
    http.expectOne(request => request.url === '/api/production/classifiers')
      .flush([{ id: 'classifier', name: 'Desarme', isActive: false }]);
    fixture.detectChanges();
    expect(host.textContent).toContain('Inactivo');
  });
});
