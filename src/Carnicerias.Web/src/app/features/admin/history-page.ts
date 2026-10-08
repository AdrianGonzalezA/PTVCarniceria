import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { AdminBranch, AdminOrganizationClient } from '../../core/admin/admin-organization-client';
import { AdminTerminal, AdminTerminalClient } from '../../core/admin/admin-terminal-client';
import { AdminHistoryClient, CashHistoryItem, HistoryFilters, HistoryPage as HistoryPageData, SaleDetail,
  SaleHistoryItem, ShiftHistoryItem, StockHistoryItem } from '../../core/admin/admin-history-client';
import { CurrentSession, SessionClient } from '../../core/session/session-client';

type HistoryTab = 'sales' | 'shifts' | 'cash' | 'stock';

@Component({
  selector: 'app-history-page',
  imports: [RouterLink, DatePipe, DecimalPipe],
  templateUrl: './history-page.html',
  styleUrls: ['./admin-page.scss', './categories-page.scss', './history-page.scss'],
})
export class HistoryPage implements OnInit {
  private readonly client = inject(AdminHistoryClient);
  private readonly organization = inject(AdminOrganizationClient);
  private readonly terminalsClient = inject(AdminTerminalClient);
  private readonly sessions = inject(SessionClient);
  private readonly router = inject(Router);
  private requestRevision = 0;

  protected readonly session = signal<CurrentSession | null>(null);
  protected readonly branches = signal<readonly AdminBranch[]>([]);
  protected readonly terminals = signal<readonly AdminTerminal[]>([]);
  protected readonly tab = signal<HistoryTab>('sales');
  protected readonly branchId = signal('');
  protected readonly terminalId = signal('');
  protected readonly fromDate = signal('');
  protected readonly toDate = signal('');
  protected readonly page = signal(1);
  protected readonly loading = signal(true);
  protected readonly loadError = signal<string | null>(null);
  protected readonly sales = signal<HistoryPageData<SaleHistoryItem> | null>(null);
  protected readonly shifts = signal<HistoryPageData<ShiftHistoryItem> | null>(null);
  protected readonly cash = signal<HistoryPageData<CashHistoryItem> | null>(null);
  protected readonly stock = signal<HistoryPageData<StockHistoryItem> | null>(null);
  protected readonly detail = signal<SaleDetail | null>(null);
  protected readonly detailLoading = signal(false);
  protected readonly detailError = signal<string | null>(null);
  protected readonly totalItems = computed(() => {
    switch (this.tab()) {
      case 'sales': return this.sales()?.totalItems ?? 0;
      case 'shifts': return this.shifts()?.totalItems ?? 0;
      case 'cash': return this.cash()?.totalItems ?? 0;
      case 'stock': return this.stock()?.totalItems ?? 0;
    }
  });
  protected readonly firstItem = computed(() => this.totalItems() === 0 ? 0 : (this.page() - 1) * 30 + 1);
  protected readonly lastItem = computed(() => Math.min(this.page() * 30, this.totalItems()));

  ngOnInit(): void {
    this.sessions.current().subscribe({
      next: (session) => {
        if (!session.context?.permissions.includes('organization.manage')) {
          void this.router.navigateByUrl('/');
          return;
        }
        this.session.set(session);
        this.organization.branches(session.context.companyId).subscribe({
          next: (branches) => this.branches.set(branches),
          error: () => this.loadError.set('No se pudieron cargar las sucursales.'),
        });
        this.load();
      },
      error: () => void this.router.navigateByUrl('/'),
    });
  }

  protected selectTab(tab: HistoryTab): void {
    if (this.tab() === tab) return;
    this.tab.set(tab);
    this.page.set(1);
    this.detail.set(null);
    if (tab === 'stock') this.terminalId.set('');
    this.load();
  }

  protected selectBranch(event: Event): void {
    const branchId = (event.target as HTMLSelectElement).value;
    this.branchId.set(branchId);
    this.terminalId.set('');
    this.terminals.set([]);
    if (branchId && this.session()?.context?.companyId) {
      this.terminalsClient.list(this.session()!.context!.companyId, branchId).subscribe({
        next: (terminals) => {
          if (this.branchId() === branchId) this.terminals.set(terminals);
        },
        error: () => this.loadError.set('No se pudieron cargar las cajas de la sucursal.'),
      });
    }
    this.page.set(1);
    this.load();
  }

  protected selectTerminal(event: Event): void {
    this.terminalId.set((event.target as HTMLSelectElement).value);
    this.page.set(1);
    this.load();
  }

  protected updateDate(field: 'from' | 'to', event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    if (field === 'from') this.fromDate.set(value);
    else this.toDate.set(value);
  }

  protected applyDates(): void {
    if (this.fromDate() && this.toDate() && this.fromDate() > this.toDate()) {
      this.loadError.set('La fecha final debe ser igual o posterior a la inicial.');
      return;
    }
    this.page.set(1);
    this.load();
  }

  protected changePage(delta: number): void {
    const next = this.page() + delta;
    if (next < 1 || (delta > 0 && this.lastItem() >= this.totalItems()) || this.loading()) return;
    this.page.set(next);
    this.load();
  }

  protected viewSale(id: string): void {
    this.detail.set(null);
    this.detailError.set(null);
    this.detailLoading.set(true);
    this.client.saleDetail(id).subscribe({
      next: (detail) => {
        this.detail.set(detail);
        this.detailLoading.set(false);
      },
      error: () => {
        this.detailLoading.set(false);
        this.detailError.set('No se pudo cargar el detalle del ticket.');
      },
    });
  }

  protected closeDetail(): void {
    this.detail.set(null);
    this.detailError.set(null);
  }

  protected paymentLabel(method: string): string {
    const names: Record<string, string> = {
      cash: 'Efectivo', debit: 'Débito', credit: 'Crédito', transfer: 'Transferencia',
      mercadoPago: 'Mercado Pago', cheque: 'Cheque',
    };
    return names[method] ?? method;
  }

  protected cashKindLabel(kind: CashHistoryItem['kind']): string {
    return { opening: 'Apertura', salePayment: 'Cobro', change: 'Vuelto' }[kind];
  }

  protected stockKindLabel(kind: StockHistoryItem['kind']): string {
    return { openingBalance: 'Saldo inicial', adjustment: 'Ajuste', sale: 'Venta' }[kind];
  }

  protected load(): void {
    const revision = ++this.requestRevision;
    const from = this.fromDate() ? new Date(`${this.fromDate()}T00:00:00`) : null;
    const to = this.toDate() ? new Date(`${this.toDate()}T00:00:00`) : null;
    if (to) to.setDate(to.getDate() + 1);
    const filters: HistoryFilters = {
      page: this.page(), pageSize: 30,
      branchId: this.branchId() || undefined,
      terminalId: this.tab() === 'stock' ? undefined : this.terminalId() || undefined,
      fromUtc: from?.toISOString(), toUtc: to?.toISOString(),
    };
    this.loading.set(true);
    this.loadError.set(null);
    this.detail.set(null);
    const onError = () => {
      if (revision !== this.requestRevision) return;
      this.loading.set(false);
      this.loadError.set('No se pudo cargar el historial. Intentá de nuevo.');
    };
    switch (this.tab()) {
      case 'sales':
        this.client.sales(filters).subscribe({
          next: (page) => { if (revision === this.requestRevision) { this.sales.set(page); this.loading.set(false); } },
          error: onError,
        });
        break;
      case 'shifts':
        this.client.shifts(filters).subscribe({
          next: (page) => { if (revision === this.requestRevision) { this.shifts.set(page); this.loading.set(false); } },
          error: onError,
        });
        break;
      case 'cash':
        this.client.cashMovements(filters).subscribe({
          next: (page) => { if (revision === this.requestRevision) { this.cash.set(page); this.loading.set(false); } },
          error: onError,
        });
        break;
      case 'stock':
        this.client.stockMovements(filters).subscribe({
          next: (page) => { if (revision === this.requestRevision) { this.stock.set(page); this.loading.set(false); } },
          error: onError,
        });
        break;
    }
  }
}
