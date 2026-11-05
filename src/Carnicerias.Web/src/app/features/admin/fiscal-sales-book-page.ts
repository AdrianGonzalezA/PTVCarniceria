import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { AdminBranch, AdminOrganizationClient } from '../../core/admin/admin-organization-client';
import {
  FiscalBookItem, FiscalBookPage, FiscalBookVatLine, FiscalSalesBookClient,
} from '../../core/admin/fiscal-sales-book-client';
import { CurrentSession, SessionClient } from '../../core/session/session-client';
import { AdminAreaTabs } from './admin-area-tabs';

function todayInArgentina(): string {
  const parts = new Intl.DateTimeFormat('en-US', {
    timeZone: 'America/Argentina/Buenos_Aires', year: 'numeric', month: '2-digit', day: '2-digit',
  }).formatToParts(new Date());
  const part = (type: string) => parts.find(value => value.type === type)!.value;
  return `${part('year')}-${part('month')}-${part('day')}`;
}

@Component({
  selector: 'app-fiscal-sales-book-page',
  imports: [RouterLink, AdminAreaTabs],
  templateUrl: './fiscal-sales-book-page.html',
  styleUrls: ['./admin-page.scss', './categories-page.scss', './fiscal-sales-book-page.scss'],
})
export class FiscalSalesBookPage implements OnInit {
  private readonly client = inject(FiscalSalesBookClient);
  private readonly organization = inject(AdminOrganizationClient);
  private readonly sessions = inject(SessionClient);
  private readonly router = inject(Router);
  private requestRevision = 0;

  protected readonly session = signal<CurrentSession | null>(null);
  protected readonly branches = signal<readonly AdminBranch[]>([]);
  protected readonly from = signal(todayInArgentina().slice(0, 8) + '01');
  protected readonly to = signal(todayInArgentina());
  protected readonly branchId = signal('');
  protected readonly page = signal(1);
  protected readonly result = signal<FiscalBookPage | null>(null);
  protected readonly loading = signal(true);
  protected readonly exporting = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly exportError = signal<string | null>(null);
  protected readonly lastItem = computed(() => Math.min(this.page() * 30, this.result()?.totalItems ?? 0));
  protected readonly firstItem = computed(() => this.result()?.totalItems ? (this.page() - 1) * 30 + 1 : 0);

  ngOnInit(): void {
    this.sessions.current().subscribe({
      next: session => {
        if (!session.context?.permissions.includes('organization.manage') ||
            !session.context.permissions.includes('platform.users.manage')) {
          void this.router.navigateByUrl('/');
          return;
        }
        this.session.set(session);
        this.organization.branches(session.context.companyId).subscribe({
          next: branches => this.branches.set(branches),
          error: () => this.error.set('No se pudieron cargar las sucursales.'),
        });
        this.load();
      },
      error: () => void this.router.navigateByUrl('/'),
    });
  }

  protected changeDate(field: 'from' | 'to', event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    if (field === 'from') this.from.set(value);
    else this.to.set(value);
  }

  protected applyPeriod(): void {
    this.page.set(1);
    this.load();
  }

  protected changeBranch(event: Event): void {
    this.branchId.set((event.target as HTMLSelectElement).value);
    this.page.set(1);
    this.load();
  }

  protected changePage(delta: number): void {
    if (this.loading() || this.page() + delta < 1 ||
        (delta > 0 && this.lastItem() >= (this.result()?.totalItems ?? 0))) return;
    this.page.update(value => value + delta);
    this.load();
  }

  protected load(): void {
    const from = this.from();
    const to = this.to();
    if (!from || !to || from > to ||
        (Date.parse(`${to}T00:00:00Z`) - Date.parse(`${from}T00:00:00Z`)) / 86400000 > 366) {
      this.error.set('Elegí un período válido de hasta 366 días.');
      return;
    }
    const revision = ++this.requestRevision;
    this.loading.set(true);
    this.error.set(null);
    this.client.list(from, to, this.branchId(), this.page()).subscribe({
      next: result => {
        if (revision !== this.requestRevision) return;
        this.result.set(result);
        this.loading.set(false);
      },
      error: () => {
        if (revision !== this.requestRevision) return;
        this.error.set('No se pudo consultar el Libro IVA Ventas. Reintentá la consulta.');
        this.loading.set(false);
      },
    });
  }

  protected exportExcel(): void {
    const result = this.result();
    if (!result || this.loading() || this.exporting()) return;
    this.exporting.set(true);
    this.exportError.set(null);
    this.client.export(result.from, result.to, result.branchId ?? '').subscribe({
      next: blob => {
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = `libro-iva-ventas-${result.from.replaceAll('-', '')}-${result.to.replaceAll('-', '')}.xlsx`;
        link.click();
        setTimeout(() => URL.revokeObjectURL(url), 0);
        this.exporting.set(false);
      },
      error: () => {
        this.exportError.set('No se pudo descargar el Excel. Reintentá la exportación.');
        this.exporting.set(false);
      },
    });
  }

  protected money(value: number | null | undefined): string {
    return value == null ? '—' : new Intl.NumberFormat('es-AR', {
      minimumFractionDigits: 2, maximumFractionDigits: 2,
    }).format(value);
  }

  protected date(value: string): string {
    const [year, month, day] = value.slice(0, 10).split('-');
    return `${day}/${month}/${year}`;
  }

  protected voucher(item: FiscalBookItem): string {
    const kind = item.documentType === 'fiscalTicket' ? 'Ticket fiscal' : 'Factura electrónica';
    const letter = { 1: 'A', 6: 'B', 11: 'C' }[item.voucherType];
    return `${kind} ${letter ?? `tipo ${item.voucherType}`}`;
  }

  protected status(item: FiscalBookItem): string {
    return item.snapshotStatus === 'complete' ? 'Completo' :
      item.snapshotStatus === 'inconsistent' ? 'Revisar importes' : 'Histórico incompleto';
  }

  protected vatCondition(code: number | null): string {
    if (code == null) return '—';
    return { 1: 'Responsable inscripto', 4: 'Exento',
      5: 'Consumidor final', 6: 'Monotributista' }[code] ?? `Código ${code}`;
  }

  protected rateValue(item: FiscalBookItem, rate: FiscalBookVatLine, field: 'taxableBase' | 'taxAmount'): number | null {
    if (item.snapshotStatus !== 'complete') return null;
    return item.vatBreakdown.find(line => line.arcaRateCode === rate.arcaRateCode &&
      line.ratePercent === rate.ratePercent)?.[field] ?? 0;
  }
}
