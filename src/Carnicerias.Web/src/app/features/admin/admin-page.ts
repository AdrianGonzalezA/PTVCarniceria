import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AdminAreaTabs } from './admin-area-tabs';
import { CurrentSession, SessionClient } from '../../core/session/session-client';
import { AdminBranch, AdminOrganizationClient } from '../../core/admin/admin-organization-client';
import { AdminHistoryClient, BusinessSummary } from '../../core/admin/admin-history-client';

@Component({
  selector: 'app-admin-page',
  imports: [RouterLink, AdminAreaTabs],
  templateUrl: './admin-page.html',
  styleUrl: './admin-page.scss',
})
export class AdminPage implements OnInit {
  private readonly sessions = inject(SessionClient);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly history = inject(AdminHistoryClient);
  private readonly organization = inject(AdminOrganizationClient);
  private readonly moneyFormatter = new Intl.NumberFormat('es-AR', {
    minimumFractionDigits: 2, maximumFractionDigits: 2,
  });
  private summaryRevision = 0;

  protected readonly isConfiguration = this.route.snapshot.data['area'] === 'configuration';

  protected readonly session = signal<CurrentSession | null>(null);
  protected readonly loadError = signal<string | null>(null);
  protected readonly signingOut = signal(false);
  protected readonly branches = signal<readonly AdminBranch[]>([]);
  protected readonly selectedBranchId = signal('');
  protected readonly summary = signal<BusinessSummary | null>(null);
  protected readonly summaryLoading = signal(false);
  protected readonly summaryError = signal<string | null>(null);
  protected readonly branchError = signal<string | null>(null);

  ngOnInit(): void {
    this.sessions.current().subscribe({
      next: (session) => {
        if (!session.context?.permissions.includes('platform.users.manage')) {
          void this.router.navigateByUrl('/');
          return;
        }
        this.session.set(session);
        if (!this.isConfiguration && session.context.permissions.includes('organization.manage')) {
          this.organization.branches(session.context.companyId).subscribe({
            next: (branches) => this.branches.set(branches),
            error: () => this.branchError.set('No se pudieron cargar las sucursales.'),
          });
          this.loadSummary();
        }
      },
      error: (error: HttpErrorResponse) => {
        if (error.status === 401 || error.status === 403) {
          void this.router.navigateByUrl('/');
          return;
        }
        this.loadError.set('No se pudo consultar la sesión. Intentá nuevamente.');
      },
    });
  }

  protected selectSummaryBranch(event: Event): void {
    this.selectedBranchId.set((event.target as HTMLSelectElement).value);
    this.loadSummary();
  }

  protected reloadSummary(): void { this.loadSummary(); }

  protected summaryDate(): string {
    const from = this.summary()?.fromUtc;
    if (!from) return '';
    return new Intl.DateTimeFormat('es-AR', { day: '2-digit', month: '2-digit', year: 'numeric',
      timeZone: 'America/Argentina/Buenos_Aires' }).format(new Date(from));
  }

  protected paymentLabel(method: string): string {
    const names: Record<string, string> = {
      cash: 'Efectivo', debit: 'Débito', credit: 'Crédito', transfer: 'Transferencia',
      mercadoPago: 'Mercado Pago', cheque: 'Cheque',
    };
    return names[method] ?? method;
  }

  protected formatMoney(amount: number): string { return this.moneyFormatter.format(amount); }

  private loadSummary(): void {
    const revision = ++this.summaryRevision;
    this.summaryLoading.set(true);
    this.summaryError.set(null);
    this.history.summary(this.selectedBranchId() || undefined).subscribe({
      next: (summary) => {
        if (revision !== this.summaryRevision) return;
        this.summary.set(summary);
        this.summaryLoading.set(false);
      },
      error: () => {
        if (revision !== this.summaryRevision) return;
        this.summary.set(null);
        this.summaryLoading.set(false);
        this.summaryError.set('No se pudo cargar el resumen. Reintentá la consulta.');
      },
    });
  }

  protected signOut(): void {
    if (this.signingOut()) return;
    this.signingOut.set(true);
    this.sessions.logout().subscribe({
      next: () => void this.router.navigateByUrl('/'),
      error: () => {
        this.signingOut.set(false);
        this.loadError.set('No se pudo cerrar la sesión. Intentá nuevamente.');
      },
    });
  }
}
