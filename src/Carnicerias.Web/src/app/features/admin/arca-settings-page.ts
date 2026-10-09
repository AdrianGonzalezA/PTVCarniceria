import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AdminArcaClient, ArcaSettings } from '../../core/admin/admin-arca-client';
import { CurrentSession, SessionClient } from '../../core/session/session-client';
import { AdminAreaTabs } from './admin-area-tabs';

@Component({
  selector: 'app-arca-settings-page',
  imports: [RouterLink, ReactiveFormsModule, AdminAreaTabs],
  templateUrl: './arca-settings-page.html',
  styleUrls: ['./admin-page.scss', './categories-page.scss', './arca-settings-page.scss'],
})
export class ArcaSettingsPage implements OnInit {
  private readonly sessions = inject(SessionClient);
  private readonly client = inject(AdminArcaClient);
  private readonly router = inject(Router);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly session = signal<CurrentSession | null>(null);
  protected readonly settings = signal<ArcaSettings | null>(null);
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly uploading = signal(false);
  protected readonly file = signal<File | null>(null);
  protected readonly message = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly form = this.formBuilder.nonNullable.group({
    issuerCuit: ['', [Validators.required, Validators.pattern(/^\d{11}$/)]],
    pointOfSale: [99, [Validators.required, Validators.min(1), Validators.max(99998)]],
    issuerName: ['', [Validators.required, Validators.maxLength(200)]],
    issuerAddress: ['', [Validators.required, Validators.maxLength(300)]],
    issuerIibb: ['', Validators.maxLength(40)],
    issuerActivityStartDate: [''],
  });

  ngOnInit(): void {
    this.sessions.current().subscribe({
      next: (session) => {
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
      next: (settings) => {
        this.applySettings(settings);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('No se pudo cargar la configuración de ARCA.');
        this.loading.set(false);
      },
    });
  }

  protected save(): void {
    if (this.form.invalid || this.saving()) {
      this.form.markAllAsTouched();
      return;
    }
    this.saving.set(true);
    this.error.set(null);
    this.message.set(null);
    const value = this.form.getRawValue();
    this.client.save({
      issuerCuit: value.issuerCuit.trim(), pointOfSale: Number(value.pointOfSale),
      issuerName: value.issuerName.trim(), issuerAddress: value.issuerAddress.trim(),
      issuerIibb: value.issuerIibb.trim() || null,
      issuerActivityStartDate: value.issuerActivityStartDate || null,
    }).subscribe({
      next: (settings) => {
        this.applySettings(settings);
        this.saving.set(false);
        this.message.set('Datos del emisor guardados.');
      },
      error: () => {
        this.saving.set(false);
        this.error.set('No se pudieron guardar los datos. Revisá el CUIT, el punto de venta y la fecha.');
      },
    });
  }

  protected selectFile(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.item(0) ?? null;
    this.file.set(file);
    this.error.set(file && file.size > 1024 * 1024 ? 'El PFX no puede superar 1 MB.' : null);
  }

  protected async upload(): Promise<void> {
    const file = this.file();
    if (!file || file.size > 1024 * 1024 || this.uploading()) return;
    this.uploading.set(true);
    this.error.set(null);
    this.message.set(null);
    try {
      const contentBase64 = await this.toBase64(file);
      const password = (document.getElementById('arca-pfx-password') as HTMLInputElement).value;
      this.client.upload(contentBase64, password || null).subscribe({
        next: (settings) => {
          this.applySettings(settings);
          this.file.set(null);
          (document.getElementById('arca-pfx-password') as HTMLInputElement).value = '';
          (document.getElementById('arca-pfx-file') as HTMLInputElement).value = '';
          this.uploading.set(false);
          this.message.set('Certificado cargado. La próxima factura usará esta configuración.');
        },
        error: (response: HttpErrorResponse) => {
          this.uploading.set(false);
          this.error.set(response.error?.error?.code === 'PFX_INVALID'
            ? 'El PFX o su contraseña son incorrectos, no tiene clave privada o está vencido.'
            : 'No se pudo cargar el certificado. El anterior sigue vigente.');
        },
      });
    } catch {
      this.uploading.set(false);
      this.error.set('No se pudo leer el archivo PFX.');
    }
  }

  protected expiry(value: string): string {
    return new Intl.DateTimeFormat('es-AR', { dateStyle: 'long', timeZone: 'America/Argentina/Buenos_Aires' })
      .format(new Date(value));
  }

  private applySettings(settings: ArcaSettings): void {
    this.settings.set(settings);
    this.form.setValue({
      issuerCuit: settings.issuerCuit, pointOfSale: settings.pointOfSale || 99,
      issuerName: settings.issuerName, issuerAddress: settings.issuerAddress,
      issuerIibb: settings.issuerIibb ?? '',
      issuerActivityStartDate: settings.issuerActivityStartDate ?? '',
    });
  }

  private toBase64(file: File): Promise<string> {
    return new Promise((resolve, reject) => {
      const reader = new FileReader();
      reader.onload = () => resolve(String(reader.result).split(',')[1] ?? '');
      reader.onerror = () => reject(reader.error);
      reader.readAsDataURL(file);
    });
  }
}
