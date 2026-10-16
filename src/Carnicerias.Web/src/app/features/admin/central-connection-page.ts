import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AdminCentralConnectionClient, BranchCentralConnectionBundle,
  BranchCentralConnectionStatus } from '../../core/admin/admin-central-connection-client';
import { CurrentSession, SessionClient } from '../../core/session/session-client';
import { AdminAreaTabs } from './admin-area-tabs';

@Component({
  selector: 'app-central-connection-page',
  imports: [RouterLink, ReactiveFormsModule, AdminAreaTabs],
  templateUrl: './central-connection-page.html',
  styleUrls: ['./admin-page.scss', './categories-page.scss', './central-connection-page.scss'],
})
export class CentralConnectionPage implements OnInit {
  private readonly sessions = inject(SessionClient);
  private readonly client = inject(AdminCentralConnectionClient);
  private readonly router = inject(Router);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly session = signal<CurrentSession | null>(null);
  protected readonly status = signal<BranchCentralConnectionStatus | null>(null);
  protected readonly bundle = signal<BranchCentralConnectionBundle | null>(null);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);
  protected readonly confirmingRotation = signal(false);
  protected readonly confirmingDeactivation = signal(false);
  protected readonly urlForm = this.formBuilder.nonNullable.group({
    destinationUrl: ['', [Validators.required, Validators.maxLength(2048)]],
  });

  ngOnInit(): void {
    this.sessions.current().subscribe({
      next: session => {
        if (!session.context?.permissions.includes('organization.manage')) {
          void this.router.navigateByUrl('/');
          return;
        }
        this.session.set(session);
        this.reload();
      },
      error: () => void this.router.navigateByUrl('/'),
    });
  }

  protected reload(): void {
    this.loading.set(true);
    this.error.set(null);
    this.client.get().subscribe({
      next: status => {
        this.status.set(status);
        this.urlForm.reset({ destinationUrl: status.destinationUrl ?? '' });
        this.loading.set(false);
      },
      error: (failure: HttpErrorResponse) => {
        this.loading.set(false);
        this.error.set(failure.status === 409
          ? 'Esta API no está vinculada a esta sucursal. Abrí el administrador de la instalación correcta o revisá el despliegue.'
          : 'No se pudo consultar la conexión con central. Reintentá.');
      },
    });
  }

  protected saveUrl(): void {
    const status = this.status();
    if (!status || this.busy() || this.urlForm.invalid) {
      this.urlForm.markAllAsTouched();
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.message.set(null);
    this.client.saveUrl(this.urlForm.controls.destinationUrl.value.trim(), status.revision)
      .subscribe({
        next: updated => {
          this.status.set({ ...status, ...updated });
          this.urlForm.reset({ destinationUrl: updated.destinationUrl ?? '' });
          this.busy.set(false);
          this.message.set('URL guardada en esta sucursal. La central deberá comprobar que puede alcanzarla.');
        },
        error: (failure: HttpErrorResponse) => {
          this.busy.set(false);
          this.error.set(failure.status === 409
            ? 'La conexión cambió. Recargá antes de guardar la URL.'
            : 'No se pudo guardar la URL. Usá HTTPS o, en el laboratorio habilitado, HTTP de loopback.');
        },
      });
  }

  protected issue(): void {
    if (this.status()?.isActive) {
      this.confirmingRotation.set(true);
      return;
    }
    this.confirmIssue();
  }

  protected confirmIssue(): void {
    if (!this.status()?.destinationUrl || this.busy()) return;
    this.busy.set(true);
    this.error.set(null);
    this.message.set(null);
    this.confirmingRotation.set(false);
    this.bundle.set(null);
    this.client.issueCredential().subscribe({
      next: bundle => {
        const status = this.status()!;
        this.bundle.set(bundle);
        this.status.set({ ...status, isActive: true, hasCredential: true,
          hasPreviousCredential: status.isActive, revision: status.revision + 1 });
        this.busy.set(false);
        this.message.set('Credencial emitida. Copiá el paquete ahora: el token no volverá a mostrarse.');
      },
      error: (failure: HttpErrorResponse) => {
        this.busy.set(false);
        this.error.set(failure.status === 409
          ? 'Hay una rotación pendiente o la conexión cambió. Actualizá el estado y finalizá la rotación antes de repetir.'
          : 'No se pudo emitir la credencial. Reintentá.');
      },
    });
  }

  protected bundleText(): string {
    return this.bundle() ? JSON.stringify(this.bundle(), null, 2) : '';
  }

  protected async copyBundle(): Promise<void> {
    if (!this.bundle()) return;
    try {
      await navigator.clipboard.writeText(this.bundleText());
      this.message.set('Paquete copiado. Pegalo en Conexiones a sucursales de la central.');
    } catch {
      this.error.set('No se pudo usar el portapapeles. Seleccioná el paquete y copialo manualmente.');
    }
  }

  protected clearBundle(): void {
    this.bundle.set(null);
    this.message.set('El token se ocultó. Si lo perdés antes de conectarlo, rotá la credencial.');
  }

  protected completeRotation(): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.client.completeRotation().subscribe({
      next: updated => {
        this.busy.set(false);
        this.status.update(status => status ? { ...status,
          hasPreviousCredential: false, previousCredentialExpiresAtUtc: null,
          revision: updated.revision } : status);
        this.message.set('Rotación finalizada. La credencial anterior ya no funciona.');
      },
      error: () => {
        this.busy.set(false);
        this.error.set('No se pudo finalizar la rotación. Reintentá.');
      },
    });
  }

  protected deactivate(): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.confirmingDeactivation.set(false);
    this.client.deactivate().subscribe({
      next: updated => {
        this.busy.set(false);
        this.bundle.set(null);
        this.status.update(status => status ? { ...status, isActive: false,
          hasCredential: false, hasPreviousCredential: false,
          previousCredentialExpiresAtUtc: null, revision: updated.revision } : status);
        this.message.set('Conexión desactivada. Las ventas y cajas locales siguen funcionando.');
      },
      error: () => {
        this.busy.set(false);
        this.error.set('No se pudo desactivar la conexión. Reintentá.');
      },
    });
  }
}
