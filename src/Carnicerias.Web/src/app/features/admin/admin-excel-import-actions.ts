import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, input, output, signal } from '@angular/core';
import {
  AdminExcelImportClient, AdminImportKind, AdminImportPreview,
} from '../../core/admin/admin-excel-import-client';
import { AdminDetailDialog } from './admin-detail-dialog';

@Component({
  selector: 'app-admin-excel-import-actions',
  imports: [AdminDetailDialog],
  templateUrl: './admin-excel-import-actions.html',
  styleUrl: './admin-excel-import-actions.scss',
})
export class AdminExcelImportActions {
  private readonly client = inject(AdminExcelImportClient);
  readonly kind = input.required<AdminImportKind>();
  readonly disabled = input(false);
  readonly completed = output<void>();
  protected readonly open = signal(false);
  protected readonly file = signal<File | null>(null);
  protected readonly busy = signal<'preview' | 'apply' | null>(null);
  protected readonly previewResult = signal<AdminImportPreview | null>(null);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly applied = signal(false);
  protected readonly templateUrl = () => this.client.templateUrl(this.kind());
  private operationId = '';

  protected start(): void {
    this.file.set(null);
    this.previewResult.set(null);
    this.errorMessage.set(null);
    this.applied.set(false);
    this.open.set(true);
  }

  protected close(): void {
    if (this.busy()) return;
    this.open.set(false);
  }

  protected chooseFile(event: Event): void {
    const selected = (event.target as HTMLInputElement).files?.[0] ?? null;
    this.file.set(selected);
    this.previewResult.set(null);
    this.errorMessage.set(null);
    this.applied.set(false);
    this.operationId = selected ? crypto.randomUUID() : '';
    if (selected && (!selected.name.toLowerCase().endsWith('.xlsx') || selected.size > 5 * 1024 * 1024))
      this.errorMessage.set('Elegí un archivo .xlsx de hasta 5 MiB.');
  }

  protected preview(): void {
    const file = this.file();
    if (!file || this.busy() || this.errorMessage()) return;
    this.busy.set('preview');
    this.previewResult.set(null);
    this.client.preview(this.kind(), file).subscribe({
      next: (result) => {
        this.previewResult.set(result);
        this.busy.set(null);
      },
      error: (error: HttpErrorResponse) => {
        this.busy.set(null);
        this.acceptError(error, 'No se pudo leer el Excel. Revisá la plantilla y reintentá.');
      },
    });
  }

  protected apply(): void {
    const file = this.file();
    if (!file || !this.previewResult()?.canApply || this.busy() || this.applied()) return;
    this.busy.set('apply');
    this.errorMessage.set(null);
    this.client.apply(this.kind(), file, this.operationId).subscribe({
      next: (result) => {
        this.previewResult.set(result);
        this.applied.set(true);
        this.busy.set(null);
        this.completed.emit();
      },
      error: (error: HttpErrorResponse) => {
        this.busy.set(null);
        this.acceptError(error, 'No se pudo aplicar la carga. Volvé a previsualizar antes de reintentar.');
      },
    });
  }

  private acceptError(error: HttpErrorResponse, fallback: string): void {
    if (error.error?.issues && Array.isArray(error.error.issues)) {
      this.previewResult.set(error.error as AdminImportPreview);
      this.errorMessage.set('La carga tiene errores. Corregí las filas indicadas y volvé a elegir el archivo.');
    } else {
      this.previewResult.set(null);
      this.errorMessage.set(error.status === 413 ? 'El archivo supera el límite de 5 MiB.' : fallback);
    }
  }
}
