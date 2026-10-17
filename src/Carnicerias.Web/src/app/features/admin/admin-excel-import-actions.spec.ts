import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AdminExcelImportActions } from './admin-excel-import-actions';

describe('AdminExcelImportActions', () => {
  beforeEach(() => TestBed.configureTestingModule({
    imports: [AdminExcelImportActions],
    providers: [provideHttpClient(), provideHttpClientTesting()],
  }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  function selectFile(page: HTMLElement): File {
    const file = new File(['xlsx'], 'categorias.xlsx',
      { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });
    const input = page.querySelector<HTMLInputElement>('input[type=file]')!;
    Object.defineProperty(input, 'files', { configurable: true, value: [file] });
    input.dispatchEvent(new Event('change'));
    return file;
  }

  it('changes template, file picker and import dialog colors without starting an import', () => {
    const root = document.documentElement;
    const previousTheme = root.getAttribute('data-visual-theme');
    try {
      root.dataset['visualTheme'] = 'original';
      const fixture = TestBed.createComponent(AdminExcelImportActions);
      fixture.componentRef.setInput('kind', 'categories');
      fixture.detectChanges();
      const host = fixture.nativeElement as HTMLElement;
      expect(getComputedStyle(host).getPropertyValue('--admin-import-link').trim()).toBe('#254b46');
      host.querySelector<HTMLButtonElement>('.import-open')!.click();
      fixture.detectChanges();
      const dialog = host.querySelector('dialog') as HTMLDialogElement;
      expect(dialog.open).toBe(true);
      expect(host.querySelector<HTMLInputElement>('.import-file input')?.files).toHaveLength(0);

      root.dataset['visualTheme'] = 'nocturno';
      expect(getComputedStyle(host).getPropertyValue('--admin-import-link').trim()).toBe('#e5b27f');
      expect(getComputedStyle(host).getPropertyValue('--admin-import-dialog-ink').trim()).toBe('#f3efe9');
      expect(dialog.open).toBe(true);
      expect(host.querySelector<HTMLButtonElement>('.import-footer button:nth-child(3)')?.disabled).toBe(true);
      fixture.destroy();
    } finally {
      if (previousTheme === null) root.removeAttribute('data-visual-theme');
      else root.setAttribute('data-visual-theme', previousTheme);
    }
  });

  it('previews before applying and reuses one idempotency key for the selected file', () => {
    const fixture = TestBed.createComponent(AdminExcelImportActions);
    fixture.componentRef.setInput('kind', 'categories');
    let completed = 0;
    fixture.componentInstance.completed.subscribe(() => completed++);
    fixture.detectChanges();
    const page = fixture.nativeElement as HTMLElement;
    expect(page.querySelector<HTMLAnchorElement>('.import-template')?.getAttribute('href'))
      .toBe('/api/admin/imports/categories/template');
    page.querySelector<HTMLButtonElement>('.import-open')!.click();
    fixture.detectChanges();
    const file = selectFile(page);
    fixture.detectChanges();
    page.querySelector<HTMLButtonElement>('.import-footer button:nth-child(2)')!.click();
    const http = TestBed.inject(HttpTestingController);
    const preview = http.expectOne('/api/admin/imports/categories/preview');
    expect(preview.request.method).toBe('POST');
    expect(preview.request.body).toBe(file);
    preview.flush({ canApply: true, createCount: 1, updateCount: 0, skippedCount: 0,
      unchangedCount: 0, rows: [{ sheet: 'Categorias', row: 2, status: 'create', message: 'Crear categoría.' }],
      issues: [] });
    fixture.detectChanges();
    expect(page.textContent).toContain('Crear categoría.');
    page.querySelector<HTMLButtonElement>('.import-footer button:nth-child(3)')!.click();
    const apply = http.expectOne('/api/admin/imports/categories/apply');
    expect(apply.request.body).toBe(file);
    expect(apply.request.headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/i);
    apply.flush({ canApply: true, createCount: 1, updateCount: 0, skippedCount: 0,
      unchangedCount: 0, rows: [], issues: [] });
    fixture.detectChanges();
    expect(page.textContent).toContain('quedó guardada');
    expect(completed).toBe(1);
  });

  it('shows row errors and never enables confirmation for invalid workbook', () => {
    const fixture = TestBed.createComponent(AdminExcelImportActions);
    fixture.componentRef.setInput('kind', 'categories');
    fixture.detectChanges();
    const page = fixture.nativeElement as HTMLElement;
    page.querySelector<HTMLButtonElement>('.import-open')!.click();
    fixture.detectChanges();
    selectFile(page);
    fixture.detectChanges();
    page.querySelector<HTMLButtonElement>('.import-footer button:nth-child(2)')!.click();
    TestBed.inject(HttpTestingController).expectOne('/api/admin/imports/categories/preview').flush({
      canApply: false, createCount: 0, updateCount: 0, skippedCount: 0, unchangedCount: 0,
      rows: [], issues: [{ sheet: 'Categorias', row: 2, column: 'Nombre',
        code: 'CATEGORY_ALREADY_EXISTS', message: 'La categoría ya existe.' }],
    }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();
    expect(page.textContent).toContain('La categoría ya existe.');
    expect(page.querySelector<HTMLButtonElement>('.import-footer button:nth-child(3)')?.disabled).toBe(true);
  });
});
