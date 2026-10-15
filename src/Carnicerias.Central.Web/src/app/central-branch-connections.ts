import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, ElementRef, EventEmitter, inject, OnInit, Output, signal, ViewChild } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { CentralBranchConnection, CentralBranchConnectionClient, EditCentralBranchConnection } from './central-branch-connection-client';
import { parseBranchConnectionBundle } from './central-branch-connection-bundle';

@Component({
  selector: 'app-central-branch-connections',
  standalone: true,
  imports: [ReactiveFormsModule, DatePipe],
  templateUrl: './central-branch-connections.html',
  styleUrl: './central-branch-connections.scss',
})
export class CentralBranchConnections implements OnInit {
  @Output() readonly sessionExpired = new EventEmitter<void>();
  @ViewChild('connectionDialog') private dialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('stateDialog') private stateDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('addButton') private addButton!: ElementRef<HTMLButtonElement>;

  private readonly client = inject(CentralBranchConnectionClient);
  private readonly formBuilder = inject(FormBuilder);
  private returnFocus: HTMLElement | null = null;
  protected readonly connections = signal<readonly CentralBranchConnection[]>([]);
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly listError = signal<string | null>(null);
  protected readonly unsupported = signal(false);
  protected readonly formError = signal<string | null>(null);
  protected readonly actionError = signal<string | null>(null);
  protected readonly editing = signal<CentralBranchConnection | null>(null);
  protected readonly stateTarget = signal<CentralBranchConnection | null>(null);
  protected readonly busyId = signal<string | null>(null);
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
        this.unsupported.set(false);
        this.connections.set(result.data);
        this.page.set(result.pagination.page);
        this.totalPages.set(result.pagination.totalPages);
        this.totalCount.set(result.pagination.totalCount);
        this.loading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.loading.set(false);
        if (error.status === 401) this.sessionExpired.emit();
        else if (error.status === 404) {
          this.unsupported.set(true);
          this.listError.set('Esta función requiere actualizar el servicio central. Contactá al equipo de implementación.');
        }
        else this.listError.set('No se pudieron consultar las conexiones. Reintentá.');
      },
    });
  }

  protected openCreate(): void {
    if (this.unsupported()) return;
    this.returnFocus = this.addButton.nativeElement;
    this.editing.set(null);
    this.form.reset();
    this.form.controls.token.setValidators([Validators.required, Validators.pattern(/^[0-9a-fA-F]{64}$/)]);
    this.form.controls.token.updateValueAndValidity();
    this.formError.set(null);
    const dialog = this.dialog.nativeElement;
    if (typeof dialog.showModal === 'function') dialog.showModal();
    else dialog.setAttribute('open', '');
    dialog.querySelector<HTMLInputElement>('#connection-label')?.focus();
  }

  protected openEdit(connection: CentralBranchConnection): void {
    this.returnFocus = document.activeElement instanceof HTMLElement
      ? document.activeElement : this.addButton.nativeElement;
    this.editing.set(connection);
    this.form.reset({
      installationId: connection.installationId,
      remoteCompanyId: connection.remoteCompanyId,
      remoteBranchId: connection.remoteBranchId,
      label: connection.label,
      destinationUrl: connection.destinationUrl,
      token: '',
    });
    this.form.controls.token.setValidators([Validators.pattern(/^[0-9a-fA-F]{64}$/)]);
    this.form.controls.token.updateValueAndValidity();
    this.formError.set(null);
    this.actionError.set(null);
    const dialog = this.dialog.nativeElement;
    if (typeof dialog.showModal === 'function') dialog.showModal();
    else dialog.setAttribute('open', '');
    dialog.querySelector<HTMLInputElement>('#connection-label')?.focus();
  }

  protected applyBundle(): void {
    const input = this.dialog.nativeElement.querySelector<HTMLTextAreaElement>('#connection-bundle');
    if (!input) return;
    const raw = input.value;
    input.value = '';
    try {
      const parsed = parseBranchConnectionBundle(raw);
      const editing = this.editing();
      if (editing && (parsed.installationId.toLowerCase() !== editing.installationId.toLowerCase() ||
        parsed.remoteCompanyId.toLowerCase() !== editing.remoteCompanyId.toLowerCase() ||
        parsed.remoteBranchId.toLowerCase() !== editing.remoteBranchId.toLowerCase())) {
        this.formError.set('El paquete corresponde a otra sucursal. Copiá el de esta conexión.');
        return;
      }
      const { version: _version, ...bundle } = parsed;
      this.form.setValue(bundle);
      this.formError.set(null);
      this.dialog.nativeElement.querySelector<HTMLInputElement>('#connection-label')?.focus();
    } catch {
      this.formError.set('El paquete no es válido. Copialo nuevamente desde Configuración de la sucursal.');
    }
  }

  protected closeCreate(): void {
    if (this.saving()) return;
    const dialog = this.dialog.nativeElement;
    if (typeof dialog.close === 'function') dialog.close();
    else dialog.removeAttribute('open');
    this.form.reset();
    this.editing.set(null);
    this.formError.set(null);
    this.returnFocus?.focus();
    this.returnFocus = null;
  }

  protected submit(): void {
    if (this.saving() || this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.saving.set(true);
    this.formError.set(null);
    const input = this.form.getRawValue();
    const editing = this.editing();
    const edit: EditCentralBranchConnection = {
      ...(input.label !== editing?.label ? { label: input.label } : {}),
      ...(input.destinationUrl !== editing?.destinationUrl ? { destinationUrl: input.destinationUrl } : {}),
      ...(input.token ? { token: input.token } : {}),
    };
    if (editing && Object.keys(edit).length === 0) {
      this.saving.set(false);
      this.closeCreate();
      return;
    }
    const operation = editing
      ? this.client.edit(editing.installationId, edit)
      : this.client.create(input);
    operation.subscribe({
      next: () => {
        this.saving.set(false);
        this.closeCreate();
        this.load(editing ? this.page() : 1);
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
        } else if (error.status === 0) {
          this.formError.set('Se interrumpió la respuesta. Revisá el listado antes de volver a enviar.');
          this.load(this.page());
        } else {
          this.formError.set('No se pudo registrar la sucursal. Revisá los datos e intentá de nuevo.');
        }
      },
    });
    this.form.controls.token.setValue('');
  }

  protected check(connection: CentralBranchConnection): void {
    if (this.busyId()) return;
    this.busyId.set(connection.installationId);
    this.actionError.set(null);
    this.client.check(connection.installationId).subscribe({
      next: () => { this.busyId.set(null); this.load(this.page()); },
      error: (error: HttpErrorResponse) => this.handleActionError(error),
    });
  }

  protected openStateChange(connection: CentralBranchConnection): void {
    if (this.busyId()) return;
    this.stateTarget.set(connection);
    this.actionError.set(null);
    const dialog = this.stateDialog.nativeElement;
    if (typeof dialog.showModal === 'function') dialog.showModal();
    else dialog.setAttribute('open', '');
  }

  protected closeStateChange(): void {
    if (this.busyId()) return;
    const dialog = this.stateDialog.nativeElement;
    if (typeof dialog.close === 'function') dialog.close();
    else dialog.removeAttribute('open');
    this.stateTarget.set(null);
  }

  protected confirmStateChange(): void {
    const connection = this.stateTarget();
    if (!connection || this.busyId()) return;
    this.busyId.set(connection.installationId);
    this.actionError.set(null);
    this.client.changeState(connection.installationId, !connection.isActive).subscribe({
      next: () => {
        this.busyId.set(null);
        this.closeStateChange();
        this.load(this.page());
      },
      error: (error: HttpErrorResponse) => {
        this.busyId.set(null);
        this.closeStateChange();
        this.handleActionError(error);
      },
    });
  }

  private handleActionError(error: HttpErrorResponse): void {
    this.busyId.set(null);
    if (error.status === 401) this.sessionExpired.emit();
    else if (error.status === 422) this.actionError.set('No se pudo verificar la sucursal; la configuración anterior sigue vigente.');
    else if (error.status === 409) this.actionError.set('La conexión cambió. Recargá el listado y reintentá.');
    else if (error.status === 0) {
      this.actionError.set('Se interrumpió la respuesta. Revisá el estado antes de reintentar.');
      this.load(this.page());
    } else this.actionError.set('No se pudo completar la acción. Reintentá.');
  }
}
