import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { HealthClient } from '../../core/health/health-client';
import { PosTerminal, PosTerminalClient } from '../../core/pos/pos-terminal-client';
import { POS_ELECTRON_MODE } from '../../core/pos/pos-electron-mode';
import { CurrentSession, OperationalCompanyOption, SessionClient } from '../../core/session/session-client';

@Component({
  selector: 'app-welcome-page',
  imports: [FormsModule, ReactiveFormsModule, RouterLink],
  templateUrl: './welcome-page.html',
  styleUrl: './welcome-page.scss',
})
export class WelcomePage implements OnInit {
  private readonly healthClient = inject(HealthClient);
  private readonly posTerminalClient = inject(PosTerminalClient);
  private readonly sessionClient = inject(SessionClient);
  private readonly router = inject(Router);
  private readonly formBuilder = inject(FormBuilder);
  protected readonly isPosElectron = inject(POS_ELECTRON_MODE);

  protected readonly apiStatus = signal<'checking' | 'available' | 'unavailable'>('checking');
  protected readonly terminalStatus = signal<'checking' | 'ready' | 'unavailable'>('checking');
  protected readonly terminal = signal<PosTerminal | null>(null);
  protected readonly currentSession = signal<CurrentSession | null>(null);
  protected readonly sessionChecked = signal(false);
  protected readonly isSubmitting = signal(false);
  protected readonly sessionError = signal<string | null>(null);
  protected readonly contextError = signal<string | null>(null);
  protected readonly isLoadingContexts = signal(false);
  protected readonly isChangingContext = signal(false);
  protected readonly contextOptions = signal<readonly OperationalCompanyOption[]>([]);
  protected readonly selectedBranchId = signal('');
  protected readonly selectedCompany = computed(() => {
    const options = this.contextOptions();
    return options.length === 1 ? options[0] : null;
  });
  protected readonly availableBranches = computed(
    () => this.selectedCompany()?.branches ?? [],
  );
  protected readonly canConfirmContext = computed(
    () =>
      this.selectedCompany() !== null &&
      this.availableBranches().some((branch) => branch.branchId === this.selectedBranchId()) &&
      !this.isLoadingContexts(),
  );
  protected readonly credentialsForm = this.formBuilder.nonNullable.group({
    companyCode: ['', this.isPosElectron ? [Validators.maxLength(40)] : [Validators.required, Validators.maxLength(40)]],
    credential: ['', [Validators.required, Validators.maxLength(320)]],
    password: ['', [Validators.required, Validators.maxLength(1024)]],
  });

  ngOnInit(): void {
    if (this.isPosElectron) {
      this.posTerminalClient.current().subscribe({
        next: (terminal) => {
          this.terminal.set(terminal);
          this.terminalStatus.set('ready');
          this.activateTerminalContext();
        },
        error: () => this.terminalStatus.set('unavailable'),
      });
    }

    this.healthClient.check().subscribe({
      next: () => this.apiStatus.set('available'),
      error: () => this.apiStatus.set('unavailable'),
    });

    this.sessionClient.current().subscribe({
      next: (session) => {
        this.currentSession.set(session);
        this.sessionChecked.set(true);
        if (session.context) void this.router.navigateByUrl(this.homeFor(session));
        else if (this.isPosElectron) this.activateTerminalContext();
        else this.loadOperationalContexts();
      },
      error: (error: HttpErrorResponse) => {
        if (error.status !== 401) this.sessionError.set('No se pudo consultar la sesión.');
        this.sessionChecked.set(true);
      },
    });
  }

  protected onBranchChange(event: Event): void {
    this.selectedBranchId.set((event.target as HTMLSelectElement).value);
    this.contextError.set(null);
  }

  protected confirmContext(): void {
    const company = this.selectedCompany();
    if (!company || !this.canConfirmContext() || this.isSubmitting()) return;

    this.selectContext(company.companyId, this.selectedBranchId());
  }

  protected retryTerminalContext(): void {
    this.activateTerminalContext();
  }

  private activateTerminalContext(): void {
    const terminal = this.terminal();
    if (!this.isPosElectron || !this.sessionChecked() || !this.currentSession() ||
        this.currentSession()?.context || !terminal || this.isSubmitting()) return;

    this.selectContext(terminal.companyId, terminal.branchId);
  }

  private selectContext(companyId: string, branchId: string): void {
    this.contextError.set(null);
    this.isSubmitting.set(true);
    this.sessionClient.selectOperationalContext(companyId, branchId).subscribe({
      next: (session) => {
        this.currentSession.set(session);
        this.isChangingContext.set(false);
        this.isSubmitting.set(false);
        void this.router.navigateByUrl(this.homeFor(session));
      },
      error: (error: HttpErrorResponse) => {
        this.contextError.set(this.contextSelectionError(error));
        this.isSubmitting.set(false);
      },
    });
  }

  protected startContextChange(): void {
    if (this.isPosElectron || this.currentSession()?.branchBound) return;
    this.isChangingContext.set(true);
    this.loadOperationalContexts();
  }

  protected cancelContextChange(): void {
    this.isChangingContext.set(false);
    this.contextError.set(null);
    this.selectedBranchId.set('');
  }

  protected retryBoundBranch(): void {
    if (this.currentSession()?.branchBound && !this.isLoadingContexts() && !this.isSubmitting()) {
      this.loadOperationalContexts();
    }
  }

  protected signIn(): void {
    if (this.credentialsForm.invalid || this.isSubmitting() ||
        (this.isPosElectron && this.terminalStatus() !== 'ready')) return;

    this.sessionError.set(null);
    this.isSubmitting.set(true);
    const { companyCode, credential, password } = this.credentialsForm.getRawValue();
    this.sessionClient.login(this.isPosElectron ? null : companyCode, credential, password).subscribe({
      next: (session) => {
        this.currentSession.set(session);
        this.sessionChecked.set(true);
        this.credentialsForm.reset();
        this.isSubmitting.set(false);
        if (this.isPosElectron) this.activateTerminalContext();
        else this.loadOperationalContexts();
      },
      error: (error: HttpErrorResponse) => {
        this.sessionError.set(
          error.status === 401
            ? 'Empresa, usuario o contraseña incorrectos.'
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
    this.selectedBranchId.set('');
    this.contextOptions.set([]);
    this.sessionClient.operationalContexts().subscribe({
      next: (options) => {
        this.contextOptions.set(options);
        this.isLoadingContexts.set(false);
        if (this.currentSession()?.branchBound && options.length === 1 &&
            options[0].branches.length === 1) {
          const company = options[0];
          const branch = company.branches[0];
          this.selectedBranchId.set(branch.branchId);
          this.selectContext(company.companyId, branch.branchId);
        } else if (this.currentSession()?.branchBound) {
          this.contextError.set('Tu usuario no tiene acceso a la sucursal de esta instalación. Pedí a administración que revise tus permisos.');
        }
      },
      error: () => {
        this.contextOptions.set([]);
        this.contextError.set('No se pudieron cargar las empresas y sucursales. Intentá de nuevo.');
        this.isLoadingContexts.set(false);
      },
    });
  }

  private homeFor(session: CurrentSession): string {
    return session.context?.permissions.includes('platform.users.manage') ? '/admin' : '/pos';
  }

  private contextSelectionError(error: HttpErrorResponse): string {
    if (error.status === 403) return this.isPosElectron
      ? 'Tu usuario no tiene acceso a la sucursal de esta caja. Pedí a administración que revise tus permisos.'
      : this.currentSession()?.branchBound
        ? 'Tu usuario no tiene acceso a la sucursal de esta instalación.'
        : 'Esa combinación ya no está habilitada para tu usuario. Volvé a elegir una opción.';
    if (error.status === 409) return this.contextChangeBlockedMessage(error);
    return this.isPosElectron
      ? 'No se pudo abrir esta caja. Revisá la conexión e intentá de nuevo.'
      : 'No se pudo confirmar la sucursal. Revisá la conexión e intentá de nuevo.';
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
