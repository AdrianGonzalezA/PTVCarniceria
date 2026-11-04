import { HttpErrorResponse } from '@angular/common/http';
import { DatePipe } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { AdminAreaTabs } from './admin-area-tabs';
import { AdminDetailDialog } from './admin-detail-dialog';
import { CashWithdrawal, CashWithdrawalClient, CashWithdrawalPage, CashWithdrawalStatus } from '../../core/sales/cash-withdrawal-client';
import { CurrentSession, SessionClient } from '../../core/session/session-client';

@Component({
  selector: 'app-cash-withdrawals-page',
  imports: [RouterLink, AdminAreaTabs, AdminDetailDialog, DatePipe],
  templateUrl: './cash-withdrawals-page.html',
  styleUrls: ['./admin-page.scss', './categories-page.scss', './cash-withdrawals-page.scss'],
})
export class CashWithdrawalsPage implements OnInit {
  private readonly client = inject(CashWithdrawalClient);
  private readonly sessions = inject(SessionClient);
  private readonly router = inject(Router);
  private readonly moneyFormatter = new Intl.NumberFormat('es-AR', {
    minimumFractionDigits: 2, maximumFractionDigits: 2,
  });
  private revision = 0;

  protected readonly session = signal<CurrentSession | null>(null);
  protected readonly status = signal<CashWithdrawalStatus | ''>('pending');
  protected readonly page = signal(1);
  protected readonly data = signal<CashWithdrawalPage | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadError = signal<string | null>(null);
  protected readonly selected = signal<CashWithdrawal | null>(null);
  protected readonly action = signal<'approve' | 'reject'>('approve');
  protected readonly decisionReason = signal('');
  protected readonly deciding = signal(false);
  protected readonly decisionError = signal<string | null>(null);

  ngOnInit(): void {
    this.sessions.current().subscribe({
      next: session => {
        if (!session.context?.permissions.includes('cash.withdrawal.approve')) {
          void this.router.navigateByUrl('/');
          return;
        }
        this.session.set(session);
        this.load();
      },
      error: () => void this.router.navigateByUrl('/'),
    });
  }

  protected filter(event: Event): void {
    this.status.set((event.target as HTMLSelectElement).value as CashWithdrawalStatus | '');
    this.page.set(1);
    this.load();
  }

  protected goToPage(page: number): void {
    if (page < 1 || page > this.totalPages() || this.loading()) return;
    this.page.set(page);
    this.load();
  }

  protected totalPages(): number {
    return Math.max(1, Math.ceil((this.data()?.totalItems ?? 0) / 30));
  }

  protected load(): void {
    const revision = ++this.revision;
    this.loading.set(true);
    this.loadError.set(null);
    this.client.adminList(this.status(), this.page()).subscribe({
      next: data => {
        if (revision !== this.revision) return;
        this.data.set(data);
        this.loading.set(false);
      },
      error: () => {
        if (revision !== this.revision) return;
        this.loading.set(false);
        this.loadError.set('No se pudieron consultar los retiros. Reintentá.');
      },
    });
  }

  protected openDecision(item: CashWithdrawal, action: 'approve' | 'reject'): void {
    if (item.status !== 'pending') return;
    this.selected.set(item);
    this.action.set(action);
    this.decisionReason.set('');
    this.decisionError.set(null);
  }

  protected closeDecision(): void {
    if (!this.deciding()) this.selected.set(null);
  }

  protected updateReason(event: Event): void {
    this.decisionReason.set((event.target as HTMLTextAreaElement).value);
  }

  protected decide(): void {
    const item = this.selected();
    if (!item || this.deciding()) return;
    const reason = this.decisionReason().trim();
    if (this.action() === 'reject' && (!reason || reason.length > 500)) {
      this.decisionError.set('Indicá un motivo de rechazo de hasta 500 caracteres.');
      return;
    }
    this.deciding.set(true);
    this.decisionError.set(null);
    this.client.decide(item.id, this.action(), this.action() === 'reject' ? reason : undefined)
      .subscribe({
        next: () => {
          this.deciding.set(false);
          this.selected.set(null);
          this.load();
        },
        error: (error: HttpErrorResponse) => {
          this.deciding.set(false);
          this.decisionError.set(this.decisionMessage(error));
        },
      });
  }

  protected formatMoney(amount: number): string { return this.moneyFormatter.format(amount); }

  private decisionMessage(error: HttpErrorResponse): string {
    switch (error.error?.error?.code) {
      case 'WITHDRAWAL_SELF_DECISION_FORBIDDEN':
        return 'Un administrador diferente del solicitante debe decidir este retiro.';
      case 'WITHDRAWAL_INSUFFICIENT_CASH':
        return 'El efectivo registrado en el turno es insuficiente para aprobar el retiro.';
      case 'CASHIER_SHIFT_REQUIRED':
        return 'El turno ya no está abierto. Actualizá la lista.';
      case 'WITHDRAWAL_ALREADY_DECIDED':
      case 'WITHDRAWAL_CONFLICT':
        return 'La solicitud cambió. Actualizá la lista antes de decidir.';
      default:
        return 'No se pudo registrar la decisión. Reintentá.';
    }
  }
}
