import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AdminAreaTabs } from './admin-area-tabs';
import { AdminBranch, AdminOrganizationClient } from '../../core/admin/admin-organization-client';
import { AdminTerminal, AdminTerminalClient } from '../../core/admin/admin-terminal-client';
import { AdminHistoryClient, CashHistoryItem, FiscalDocumentDetail, HistoryFilters, HistoryPage as HistoryPageData, SaleDetail,
  SaleHistoryItem, ShiftHistoryItem, StockHistoryItem, StockHistoryPage, HistorySaleDocumentType } from '../../core/admin/admin-history-client';
import { CurrentSession, SessionClient } from '../../core/session/session-client';
import { AdminDetailDialog } from './admin-detail-dialog';
import { ShiftClosingReport } from '../../core/sales/cashier-shift-client';

type HistoryTab = 'sales' | 'shifts' | 'cash' | 'stock';

@Component({
  selector: 'app-history-page',
  imports: [RouterLink, AdminAreaTabs, AdminDetailDialog, DatePipe, DecimalPipe],
  templateUrl: './history-page.html',
  styleUrls: ['./admin-page.scss', './categories-page.scss', './history-page.scss'],
})
export class HistoryPage implements OnInit {
  private readonly client = inject(AdminHistoryClient);
  private readonly organization = inject(AdminOrganizationClient);
  private readonly terminalsClient = inject(AdminTerminalClient);
  private readonly sessions = inject(SessionClient);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private requestRevision = 0;
  private detailRevision = 0;

  protected readonly session = signal<CurrentSession | null>(null);
  protected readonly branches = signal<readonly AdminBranch[]>([]);
  protected readonly terminals = signal<readonly AdminTerminal[]>([]);
  protected readonly tab = signal<HistoryTab>('sales');
  protected readonly branchId = signal('');
  protected readonly selectedBranchName = computed(() => this.branches()
    .find((branch) => branch.id === this.branchId())?.name ?? 'Sucursal');
  protected readonly terminalId = signal('');
  protected readonly documentType = signal<HistorySaleDocumentType | ''>('');
  protected readonly productSearch = signal('');
  protected readonly productId = signal('');
  protected readonly categorySearch = signal('');
  protected readonly stockKind = signal<StockHistoryItem['kind'] | ''>('');
  protected readonly fromDate = signal('');
  protected readonly toDate = signal('');
  protected readonly page = signal(1);
  protected readonly loading = signal(true);
  protected readonly loadError = signal<string | null>(null);
  protected readonly sales = signal<HistoryPageData<SaleHistoryItem> | null>(null);
  protected readonly shifts = signal<HistoryPageData<ShiftHistoryItem> | null>(null);
  protected readonly cash = signal<HistoryPageData<CashHistoryItem> | null>(null);
  protected readonly stock = signal<StockHistoryPage | null>(null);
  protected readonly selectedStockMovement = signal<StockHistoryItem | null>(null);
  protected readonly detail = signal<SaleDetail | null>(null);
  protected readonly detailSaleId = signal<string | null>(null);
  protected readonly detailLoading = signal(false);
  protected readonly detailError = signal<string | null>(null);
  protected readonly shiftReportId = signal<string | null>(null);
  protected readonly shiftReport = signal<ShiftClosingReport | null>(null);
  protected readonly shiftReportLoading = signal(false);
  protected readonly shiftReportError = signal<string | null>(null);
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
        const query = this.route.snapshot.queryParamMap;
        if (query.get('tab') === 'stock' && query.has('branchId') && query.has('productId')) {
          this.tab.set('stock');
          this.branchId.set(query.get('branchId')!);
          this.productId.set(query.get('productId')!);
          this.productSearch.set(query.get('productCode') ?? '');
        }
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
    this.closeDetail();
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

  protected selectDocumentType(event: Event): void {
    this.documentType.set((event.target as HTMLSelectElement).value as HistorySaleDocumentType | '');
    this.page.set(1);
    this.load();
  }

  protected updateStockFilter(field: 'product' | 'category' | 'kind', event: Event): void {
    const value = (event.target as HTMLInputElement | HTMLSelectElement).value;
    if (field === 'product') {
      this.productSearch.set(value);
      this.productId.set('');
    }
    else if (field === 'category') this.categorySearch.set(value);
    else this.stockKind.set(value as StockHistoryItem['kind'] | '');
  }

  protected applyStockFilters(): void {
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
    this.closeDetail();
    const revision = ++this.detailRevision;
    this.detailSaleId.set(id);
    this.detail.set(null);
    this.detailError.set(null);
    this.detailLoading.set(true);
    this.client.saleDetail(id).subscribe({
      next: (detail) => {
        if (revision !== this.detailRevision) return;
        this.detail.set(detail);
        this.detailLoading.set(false);
      },
      error: () => {
        if (revision !== this.detailRevision) return;
        this.detailLoading.set(false);
        this.detailError.set('No se pudo cargar el detalle del ticket.');
      },
    });
  }

  protected closeDetail(): void {
    this.detailRevision++;
    this.selectedStockMovement.set(null);
    this.detailSaleId.set(null);
    this.detail.set(null);
    this.detailError.set(null);
    this.detailLoading.set(false);
    this.shiftReportId.set(null);
    this.shiftReport.set(null);
    this.shiftReportLoading.set(false);
    this.shiftReportError.set(null);
  }

  protected viewShiftReport(id: string): void {
    const revision = ++this.detailRevision;
    this.shiftReportId.set(id);
    this.shiftReport.set(null);
    this.shiftReportError.set(null);
    this.shiftReportLoading.set(true);
    this.client.shiftReport(id).subscribe({
      next: (report) => {
        if (revision !== this.detailRevision) return;
        this.shiftReport.set(report);
        this.shiftReportLoading.set(false);
      },
      error: () => {
        if (revision !== this.detailRevision) return;
        this.shiftReportError.set('No se pudo cargar la rendición.');
        this.shiftReportLoading.set(false);
      },
    });
  }

  protected downloadShiftReport(id: string): void {
    this.shiftReportError.set(null);
    this.client.shiftReportPdf(id).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const anchor = document.createElement('a');
        anchor.href = url;
        anchor.download = `rendicion-turno-${id}.pdf`;
        anchor.click();
        setTimeout(() => URL.revokeObjectURL(url), 30_000);
      },
      error: () => this.shiftReportError.set('No se pudo descargar el PDF.'),
    });
  }

  protected paymentLabel(method: string): string {
    const names: Record<string, string> = {
      cash: 'Efectivo', debit: 'Débito', credit: 'Crédito', transfer: 'Transferencia',
      mercadoPago: 'Mercado Pago', cheque: 'Cheque',
    };
    return names[method] ?? method;
  }

  protected taxTreatmentLabel(treatment: string): string {
    return { Taxed: 'Gravado', Exempt: 'Exento', NotTaxed: 'No alcanzado' }[treatment] ?? treatment;
  }

  protected fiscalStatusLabel(status: FiscalDocumentDetail['status']): string {
    return { Prepared: 'preparado', NeedsReconciliation: 'pendiente de conciliación',
      Rejected: 'rechazado', Authorized: 'autorizado' }[status];
  }

  protected cashKindLabel(kind: CashHistoryItem['kind']): string {
    return { opening: 'Apertura', salePayment: 'Cobro de venta', change: 'Vuelto de venta',
      accountCollection: 'Cobro de cuenta', accountCollectionRefund: 'Devolución de cuenta',
      accountCollectionChange: 'Vuelto de cuenta',
      extraordinaryWithdrawal: 'Retiro extraordinario' }[kind];
  }

  protected stockKindLabel(kind: StockHistoryItem['kind']): string {
    return { openingBalance: 'Saldo inicial', adjustment: 'Ajuste', sale: 'Venta',
      pieceReceipt: 'Recepción de pieza', receipt: 'Recepción',
      transferOut: 'Transferencia enviada' }[kind];
  }

  protected viewStockMovement(movement: StockHistoryItem): void {
    this.closeDetail();
    this.selectedStockMovement.set(movement);
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
      documentType: this.tab() === 'sales' ? this.documentType() || undefined : undefined,
      productSearch: this.tab() === 'stock' ? this.productSearch().trim() || undefined : undefined,
      productId: this.tab() === 'stock' ? this.productId() || undefined : undefined,
      categorySearch: this.tab() === 'stock' ? this.categorySearch().trim() || undefined : undefined,
      kind: this.tab() === 'stock' ? this.stockKind() || undefined : undefined,
    };
    this.loading.set(true);
    this.loadError.set(null);
    this.closeDetail();
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
