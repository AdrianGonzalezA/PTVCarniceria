import { HttpErrorResponse } from '@angular/common/http';
import { Component, ElementRef, EventEmitter, inject, OnInit, Output, signal, ViewChild } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { CentralBranchConnection, CentralBranchConnectionClient } from './central-branch-connection-client';

@Component({
  selector: 'app-central-branch-connections',
  standalone: true,
  imports: [ReactiveFormsModule],
  templateUrl: './central-branch-connections.html',
  styleUrl: './central-branch-connections.scss',
})
export class CentralBranchConnections implements OnInit {
  @Output() readonly sessionExpired = new EventEmitter<void>();
  @ViewChild('connectionDialog') private dialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('addButton') private addButton!: ElementRef<HTMLButtonElement>;

  private readonly client = inject(CentralBranchConnectionClient);
  private readonly formBuilder = inject(FormBuilder);
  protected readonly connections = signal<readonly CentralBranchConnection[]>([]);
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly listError = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);
  protected readonly page = signal(1);
  protected readonly totalPages = signal(0);
  protected readonly totalCount = signal(0);
  protected readonly form = this.formBuilder.nonNullable.group({
    installationId: ['', [Validators.required, Validators.pattern(/^[0-9a-fA-F-]{36}$/)]],
    remoteCompanyId: ['', [Validators.required, Validators.pattern(/^[0-9a-fA-F-]{36}$/)]],
    remoteBranchId: ['', [Validators.required, Validators.pattern(/^[0-9a-fA-F-]{36}$/)]],
    label: ['', [Validators.required, Validators.maxLength(200)]],
    destinationUrl: ['', [Validators.required, Validators.maxLength(2048)]],
    token: ['', [Validators.required, Validators.pattern(/^[0-9a-fA-F]{64}$/)]],
  });

  ngOnInit(): void { this.load(1); }

  protected load(page: number): void {
    if (this.loading()) return;
    this.loading.set(true);
    this.listError.set(null);
    this.client.list(page).subscribe({
      next: result => {
        this.connections.set(result.data);
        this.page.set(result.pagination.page);
        this.totalPages.set(result.pagination.totalPages);
        this.totalCount.set(result.pagination.totalCount);
        this.loading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.loading.set(false);
        if (error.status === 401) this.sessionExpired.emit();
        else this.listError.set('No se pudieron consultar las conexiones. Reintentá.');
      },
    });
  }

  protected openCreate(): void {
    this.formError.set(null);
    const dialog = this.dialog.nativeElement;
    if (typeof dialog.showModal === 'function') dialog.showModal();
    else dialog.setAttribute('open', '');
    dialog.querySelector<HTMLInputElement>('#connection-label')?.focus();
  }

  protected closeCreate(): void {
    if (this.saving()) return;
    const dialog = this.dialog.nativeElement;
    if (typeof dialog.close === 'function') dialog.close();
    else dialog.removeAttribute('open');
    this.form.reset();
    this.formError.set(null);
    this.addButton.nativeElement.focus();
  }

  protected submit(): void {
    if (this.saving() || this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.saving.set(true);
    this.formError.set(null);
    const input = this.form.getRawValue();
    this.client.create(input).subscribe({
      next: () => {
        this.saving.set(false);
        this.closeCreate();
        this.load(1);
      },
      error: (error: HttpErrorResponse) => {
        this.saving.set(false);
        if (error.status === 401) {
          this.closeCreate();
          this.sessionExpired.emit();
        } else if (error.status === 422) {
          this.formError.set('No se pudo verificar la sucursal. Revisá destino, IDs y credencial.');
        } else if (error.status === 409) {
          this.formError.set('La instalación o sucursal ya está registrada.');
        } else if (error.status === 503) {
          this.formError.set('La central aún no tiene disponible el almacén de credenciales.');
        } else {
          this.formError.set('No se pudo registrar la sucursal. Revisá los datos e intentá de nuevo.');
        }
      },
    });
    this.form.controls.token.setValue('');
  }
}
