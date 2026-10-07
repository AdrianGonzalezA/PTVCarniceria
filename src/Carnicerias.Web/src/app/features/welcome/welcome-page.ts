import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { HealthClient } from '../../core/health/health-client';
import { CurrentSession, OperationalCompanyOption, SessionClient } from '../../core/session/session-client';

@Component({
  selector: 'app-welcome-page',
  imports: [FormsModule, ReactiveFormsModule, RouterLink],
  templateUrl: './welcome-page.html',
  styleUrl: './welcome-page.scss',
})
export class WelcomePage implements OnInit {
  private readonly healthClient = inject(HealthClient);
  private readonly sessionClient = inject(SessionClient);
  private readonly router = inject(Router);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly apiStatus = signal<'checking' | 'available' | 'unavailable'>('checking');
  protected readonly currentSession = signal<CurrentSession | null>(null);
  protected readonly sessionChecked = signal(false);
  protected readonly isSubmitting = signal(false);
  protected readonly sessionError = signal<string | null>(null);
  protected readonly contextError = signal<string | null>(null);
  protected readonly isLoadingContexts = signal(false);
  protected readonly isChangingContext = signal(false);
  protected readonly contextOptions = signal<readonly OperationalCompanyOption[]>([]);
  protected readonly selectedCompanyId = signal('');
  protected readonly selectedBranchId = signal('');
  protected readonly availableBranches = computed(
    () => this.contextOptions().find((company) => company.companyId === this.selectedCompanyId())?.branches ?? [],
  );
  protected readonly canConfirmContext = computed(
    () =>
      this.availableBranches().some((branch) => branch.branchId === this.selectedBranchId()) &&
      !this.isLoadingContexts(),
  );
  protected readonly credentialsForm = this.formBuilder.nonNullable.group({
    credential: ['', [Validators.required, Validators.maxLength(320)]],
    password: ['', [Validators.required, Validators.maxLength(1024)]],
  });

  ngOnInit(): void {
    this.healthClient.check().subscribe({
      next: () => this.apiStatus.set('available'),
      error: () => this.apiStatus.set('unavailable'),
    });

    this.sessionClient.current().subscribe({
      next: (session) => {
        this.currentSession.set(session);
        this.sessionChecked.set(true);
        if (session.context) void this.router.navigateByUrl('/pos');
        else this.loadOperationalContexts();
      },
      error: (error: HttpErrorResponse) => {
        if (error.status !== 401) this.sessionError.set('No se pudo consultar la sesión.');
        this.sessionChecked.set(true);
      },
    });
  }

  protected onCompanyChange(event: Event): void {
    this.selectedCompanyId.set((event.target as HTMLSelectElement).value);
    this.selectedBranchId.set('');
    this.contextError.set(null);
  }

  protected onBranchChange(event: Event): void {
    this.selectedBranchId.set((event.target as HTMLSelectElement).value);
    this.contextError.set(null);
  }

  protected confirmContext(): void {
    if (!this.canConfirmContext() || this.isSubmitting()) return;

    this.contextError.set(null);
    this.isSubmitting.set(true);
    this.sessionClient
      .selectOperationalContext(this.selectedCompanyId(), this.selectedBranchId())
      .subscribe({
        next: (session) => {
          this.currentSession.set(session);
          this.isChangingContext.set(false);
          this.isSubmitting.set(false);
          void this.router.navigateByUrl('/pos');
        },
        error: (error: HttpErrorResponse) => {
          this.contextError.set(
            error.status === 403
              ? 'Esa combinación ya no está habilitada para tu usuario. Volvé a elegir una opción.'
              : error.status === 409
                ? this.contextChangeBlockedMessage(error)
              : 'No se pudo confirmar la sucursal. Revisá la conexión e intentá de nuevo.',
          );
          this.isSubmitting.set(false);
        },
      });
  }

  protected startContextChange(): void {
    this.isChangingContext.set(true);
    this.loadOperationalContexts();
  }

  protected cancelContextChange(): void {
    this.isChangingContext.set(false);
    this.contextError.set(null);
    this.selectedCompanyId.set('');
    this.selectedBranchId.set('');
  }

  protected signIn(): void {
    if (this.credentialsForm.invalid || this.isSubmitting()) return;

    this.sessionError.set(null);
    this.isSubmitting.set(true);
    const { credential, password } = this.credentialsForm.getRawValue();
    this.sessionClient.login(credential, password).subscribe({
      next: (session) => {
        this.currentSession.set(session);
        this.sessionChecked.set(true);
        this.credentialsForm.reset();
        this.isSubmitting.set(false);
        this.loadOperationalContexts();
      },
      error: (error: HttpErrorResponse) => {
        this.sessionError.set(
          error.status === 401
            ? 'Usuario o contraseña incorrectos.'
            : 'No se pudo iniciar sesión. Revisá la conexión e intentá de nuevo.',
        );
        this.isSubmitting.set(false);
      },
    });
  }

  protected signOut(): void {
    if (this.isSubmitting()) return;

    this.sessionError.set(null);
    this.isSubmitting.set(true);
    this.sessionClient.logout().subscribe({
      next: () => {
        this.currentSession.set(null);
        this.contextOptions.set([]);
        this.isChangingContext.set(false);
        this.isSubmitting.set(false);
      },
      error: () => {
        this.sessionError.set('No se pudo cerrar la sesión. Intentá de nuevo.');
        this.isSubmitting.set(false);
      },
    });
  }

  private loadOperationalContexts(): void {
    this.isLoadingContexts.set(true);
    this.contextError.set(null);
    this.selectedCompanyId.set('');
    this.selectedBranchId.set('');
    this.sessionClient.operationalContexts().subscribe({
      next: (options) => {
        this.contextOptions.set(options);
        this.isLoadingContexts.set(false);
      },
      error: () => {
        this.contextOptions.set([]);
        this.contextError.set('No se pudieron cargar las empresas y sucursales. Intentá de nuevo.');
        this.isLoadingContexts.set(false);
      },
    });
  }

  private contextChangeBlockedMessage(error: HttpErrorResponse): string {
    const genericMessage = 'No se puede cambiar de contexto mientras haya operaciones pendientes.';
    if (typeof error.error !== 'object' || error.error === null) return genericMessage;

    const envelope = (error.error as Record<string, unknown>)['error'];
    if (typeof envelope !== 'object' || envelope === null) return genericMessage;

    const apiError = envelope as Record<string, unknown>;
    if (apiError['code'] !== 'CONTEXT_CHANGE_BLOCKED' || !Array.isArray(apiError['details'])) {
      return genericMessage;
    }

    const messages = apiError['details']
      .slice(0, 3)
      .flatMap((detail: unknown) => {
        if (typeof detail !== 'object' || detail === null) return [];
        const message = (detail as Record<string, unknown>)['message'];
        return typeof message === 'string' && message.trim().length > 0 && message.length <= 240
          ? [message.trim()]
          : [];
      });

    return messages.length > 0 ? `${genericMessage} ${messages.join(' ')}` : genericMessage;
  }
}
