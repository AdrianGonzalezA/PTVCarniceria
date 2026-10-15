import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { CentralBusinessDashboard } from './central-business-dashboard';
import { CentralInstallation, CentralInstallationClient } from './central-installation-client';
import { CentralSession, CentralSessionClient } from './central-session-client';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [ReactiveFormsModule, CentralBusinessDashboard],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App implements OnInit {
  private sessionEpoch = 0;
  private readonly client = inject(CentralSessionClient);
  private readonly installationClient = inject(CentralInstallationClient);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly checked = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly session = signal<CentralSession | null>(null);
  protected readonly installations = signal<CentralInstallation[]>([]);
  protected readonly installationsBusy = signal(false);
  protected readonly installationsError = signal<string | null>(null);
  protected readonly currentPage = signal(1);
  protected readonly totalPages = signal(0);
  protected readonly totalCount = signal(0);
  protected readonly loginForm = this.formBuilder.nonNullable.group({
    companyCode: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(40)]],
    credential: ['', [Validators.required, Validators.maxLength(100)]],
    password: ['', [Validators.required]],
  });

  ngOnInit(): void {
    this.client.current().subscribe({
      next: (session) => {
        this.sessionEpoch++;
        this.session.set(session);
        this.checked.set(true);
        this.loadInstallations(1);
      },
      error: (error: HttpErrorResponse) => {
        this.checked.set(true);
        if (error.status !== 401) {
          this.error.set('No se pudo verificar la sesión central. Reintentá en unos momentos.');
        }
      },
    });
  }

  protected login(): void {
    if (this.loginForm.invalid || this.busy()) {
      this.loginForm.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    const { companyCode, credential, password } = this.loginForm.getRawValue();
    this.client.login(companyCode.trim(), credential.trim(), password).subscribe({
      next: (session) => {
        this.sessionEpoch++;
        this.session.set(session);
        this.loginForm.reset();
        this.busy.set(false);
        this.loadInstallations(1);
      },
      error: (error: HttpErrorResponse) => {
        this.loginForm.controls.password.setValue('');
        this.error.set(error.status === 429
          ? 'Demasiados intentos. Esperá unos minutos antes de volver a ingresar.'
          : error.status === 401
            ? 'Credenciales inválidas. Revisá los datos e intentá nuevamente.'
            : 'No se pudo ingresar a la administración central. Reintentá.');
        this.busy.set(false);
      },
    });
  }

  protected logout(): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set(null);
    this.client.logout().subscribe({
      next: () => {
        this.sessionEpoch++;
        this.session.set(null);
        this.installations.set([]);
        this.installationsError.set(null);
        this.installationsBusy.set(false);
        this.totalCount.set(0);
        this.totalPages.set(0);
        this.busy.set(false);
      },
      error: () => {
        this.error.set('No se pudo cerrar la sesión. Reintentá.');
        this.busy.set(false);
      },
    });
  }

  protected loadInstallations(page: number): void {
    if (!this.session() || this.installationsBusy()) return;
    this.installationsBusy.set(true);
    this.installationsError.set(null);
    const requestedSessionEpoch = this.sessionEpoch;
    this.installationClient.list(page).subscribe({
      next: (result) => {
        if (requestedSessionEpoch !== this.sessionEpoch) return;
        this.installations.set(result.data);
        this.currentPage.set(result.pagination.page);
        this.totalPages.set(result.pagination.totalPages);
        this.totalCount.set(result.pagination.totalCount);
        this.installationsBusy.set(false);
      },
      error: (error: HttpErrorResponse) => {
        if (requestedSessionEpoch !== this.sessionEpoch) return;
        this.installationsBusy.set(false);
        if (error.status === 401) {
          this.expireSession();
        } else {
          this.installationsError.set('No se pudo consultar el registro. Reintentá.');
        }
      },
    });
  }

  protected expireSession(): void {
    this.sessionEpoch++;
    this.session.set(null);
    this.installations.set([]);
    this.installationsBusy.set(false);
    this.totalCount.set(0);
    this.totalPages.set(0);
    this.error.set('La sesión central venció. Ingresá nuevamente.');
  }
}
