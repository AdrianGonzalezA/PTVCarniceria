import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { AdminAccountClient, AccountCustomer, AccountDetail, AccountPage,
  AccountReceipt, CollectionCorrectionResult } from '../../core/admin/admin-account-client';
import { CurrentSession, SessionClient } from '../../core/session/session-client';
import { PosTerminalClient } from '../../core/pos/pos-terminal-client';
import { CashierShiftClient } from '../../core/sales/cashier-shift-client';
import { AdminAreaTabs } from './admin-area-tabs';

@Component({
  selector: 'app-accounts-page',
  imports: [RouterLink, AdminAreaTabs, DatePipe],
  templateUrl: './accounts-page.html',
  styleUrls: ['./admin-page.scss', './categories-page.scss', './history-page.scss', './accounts-page.scss'],
})
export class AccountsPage implements OnInit {
  private readonly client = inject(AdminAccountClient);
  private readonly sessions = inject(SessionClient);
  private readonly router = inject(Router);
  private readonly terminals = inject(PosTerminalClient);
  private readonly shifts = inject(CashierShiftClient);
  private listRevision = 0;
  private detailRevision = 0;
  private targetRevision = 0;
  private correctionOperationId = crypto.randomUUID();

  protected readonly session = signal<CurrentSession | null>(null);
  protected readonly search = signal('');
  protected readonly page = signal(1);
  protected readonly list = signal<AccountPage<AccountCustomer> | null>(null);
  protected readonly listLoading = signal(true);
  protected readonly listError = signal<string | null>(null);
  protected readonly selectedCustomerId = signal<string | null>(null);
  protected readonly detail = signal<AccountDetail | null>(null);
  protected readonly detailLoading = signal(false);
  protected readonly detailError = signal<string | null>(null);
  protected readonly salePage = signal(1);
  protected readonly receiptPage = signal(1);
  protected readonly applicationPage = signal(1);
  protected readonly correctionReceipt = signal<AccountReceipt | null>(null);
  protected readonly correctionKind = signal<'reallocate' | 'refund'>('reallocate');
  protected readonly correctionReason = signal('');
  protected readonly targetSearch = signal('');
  protected readonly targetOptions = signal<readonly AccountCustomer[]>([]);
  protected readonly targetCustomerId = signal('');
  protected readonly correctionBusy = signal(false);
  protected readonly correctionError = signal<string | null>(null);
  protected readonly correctionResult = signal<CollectionCorrectionResult | null>(null);
  protected readonly correctionReady = signal(false);
  protected readonly canCorrect = computed(() =>
    !!this.session()?.context?.permissions.includes('pos.account.correct') && this.correctionReady());
  protected readonly correctionProblem = computed(() => {
    if (this.correctionReason().trim().length < 10) return 'Escribí un motivo de al menos 10 caracteres.';
    if (this.correctionKind() === 'reallocate' && !this.targetCustomerId())
      return 'Elegí la cuenta que recibirá el dinero.';
    return null;
  });

  ngOnInit(): void {
    this.sessions.current().subscribe({
      next: (session) => {
        if (!session.context?.permissions.includes('organization.manage')) {
          void this.router.navigateByUrl('/');
          return;
        }
        this.session.set(session);
        this.loadList();
        if (session.context.permissions.includes('pos.account.correct')) this.checkCorrectionReady();
      },
      error: () => void this.router.navigateByUrl('/'),
    });
  }

  private checkCorrectionReady(): void {
    this.correctionReady.set(false);
    this.terminals.current().subscribe({
      next: () => this.shifts.current().subscribe({
        next: (shift) => this.correctionReady.set(shift !== null),
        error: () => this.correctionReady.set(false),
      }),
      error: () => this.correctionReady.set(false),
    });
  }

  protected updateSearch(event: Event): void { this.search.set((event.target as HTMLInputElement).value); }
  protected searchAccounts(): void { this.page.set(1); this.loadList(); }
  protected changePage(delta: number): void { this.page.update((value) => value + delta); this.loadList(); }

  protected loadList(): void {
    const revision = ++this.listRevision;
    this.listLoading.set(true);
    this.listError.set(null);
    this.client.list(this.page(), this.search().trim()).subscribe({
      next: (page) => {
        if (revision !== this.listRevision) return;
        this.list.set(page);
        this.listLoading.set(false);
      },
      error: () => {
        if (revision !== this.listRevision) return;
        this.listLoading.set(false);
        this.listError.set('No se pudieron consultar los estados de cuenta. Reintentá.');
      },
    });
  }

  protected selectCustomer(customerId: string): void {
    this.selectedCustomerId.set(customerId);
    this.salePage.set(1);
    this.receiptPage.set(1);
    this.applicationPage.set(1);
    this.loadDetail();
  }

  protected closeDetail(): void { this.selectedCustomerId.set(null); this.detail.set(null); }

  protected changeDetailPage(section: 'sales' | 'receipts' | 'creditApplications', delta: number): void {
    switch (section) {
      case 'sales': this.salePage.update((value) => value + delta); break;
      case 'receipts': this.receiptPage.update((value) => value + delta); break;
      case 'creditApplications': this.applicationPage.update((value) => value + delta); break;
    }
    this.loadDetail();
  }

  protected loadDetail(): void {
    const customerId = this.selectedCustomerId();
    if (!customerId) return;
    const revision = ++this.detailRevision;
    this.detailLoading.set(true);
    this.detailError.set(null);
    this.client.detail(customerId, this.salePage(), this.receiptPage(), this.applicationPage()).subscribe({
      next: (detail) => {
        if (revision !== this.detailRevision) return;
        this.detail.set(detail);
        this.detailLoading.set(false);
      },
      error: () => {
        if (revision !== this.detailRevision) return;
        this.detailLoading.set(false);
        this.detailError.set('No se pudo consultar el estado de cuenta. Reintentá.');
      },
    });
  }

  protected openCorrection(receipt: AccountReceipt): void {
    this.correctionReceipt.set(receipt);
    this.correctionKind.set('reallocate');
    this.correctionReason.set('');
    this.targetSearch.set('');
    this.targetOptions.set([]);
    this.targetCustomerId.set('');
    this.correctionError.set(null);
    this.correctionResult.set(null);
    this.correctionOperationId = crypto.randomUUID();
    this.findTargets();
  }

  protected closeCorrection(): void {
    if (this.correctionBusy()) return;
    this.correctionReceipt.set(null);
  }

  protected updateCorrectionKind(event: Event): void {
    this.correctionKind.set((event.target as HTMLSelectElement).value as 'reallocate' | 'refund');
  }
  protected updateReason(event: Event): void {
    this.correctionReason.set((event.target as HTMLTextAreaElement).value);
  }
  protected updateTargetSearch(event: Event): void {
    this.targetSearch.set((event.target as HTMLInputElement).value);
  }
  protected selectTarget(event: Event): void {
    this.targetCustomerId.set((event.target as HTMLSelectElement).value);
  }

  protected findTargets(): void {
    const revision = ++this.targetRevision;
    this.client.list(1, this.targetSearch().trim()).subscribe({
      next: (page) => {
        if (revision !== this.targetRevision) return;
        this.targetOptions.set(page.items);
      },
      error: () => this.correctionError.set('No se pudo buscar la cuenta de destino.'),
    });
  }

  protected correct(): void {
    const receipt = this.correctionReceipt();
    if (!receipt || this.correctionProblem() || this.correctionBusy()) return;
    const kind = this.correctionKind();
    if (!window.confirm(kind === 'refund'
      ? `¿Confirmás que se devolverán ${this.money(receipt.amount)} y se revertirá la cuenta del recibo ${receipt.receiptNumber}?`
      : `¿Confirmás reasignar el recibo ${receipt.receiptNumber} sin salida de caja?`)) return;
    this.correctionBusy.set(true);
    this.correctionError.set(null);
    this.client.correct(receipt.id, {
      operationId: this.correctionOperationId, kind, reason: this.correctionReason().trim(),
      ...(kind === 'reallocate' ? { targetCustomerId: this.targetCustomerId() } : {}),
    }).subscribe({
      next: (result) => {
        this.correctionBusy.set(false);
        this.correctionResult.set(result);
        this.loadList();
        this.loadDetail();
      },
      error: (response: HttpErrorResponse) => {
        this.correctionBusy.set(false);
        const code = response.error?.error?.code;
        this.correctionError.set(code === 'COLLECTION_CREDIT_ALREADY_USED'
          ? 'Parte del anticipo ya se aplicó a una venta. No se puede revertir este recibo sin compensarla.'
          : code === 'CASHIER_SHIFT_REQUIRED' || code === 'POS_TERMINAL_REQUIRED'
            ? 'Esta corrección requiere ingresar como administrador en una caja Electron con turno abierto.'
            : code === 'COLLECTION_ALREADY_CORRECTED'
              ? 'El recibo ya fue corregido. Actualizá el estado de cuenta.'
              : code === 'ACCOUNT_ALLOCATION_CONFLICT'
                ? 'La deuda cambió. Actualizá el estado de cuenta y volvé a intentar.'
                : 'No se pudo registrar la corrección. Conservá los datos y reintentá la misma operación.');
      },
    });
  }

  protected money(value: number): string { return `$ ${value.toLocaleString('es-AR',
    { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`; }
  protected pageCount(total: number): number { return Math.ceil(total / 25); }
}
