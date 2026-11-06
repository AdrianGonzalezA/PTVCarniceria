import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Observable } from 'rxjs';
import { CountPolicyOptions, PhysicalCount, PhysicalCountPage,
  PhysicalStockCountClient, StockCountPolicy,
} from '../../core/inventory/physical-stock-count-client';
import { CurrentSession, SessionClient } from '../../core/session/session-client';
import { formatLocalizedDecimal, parseLocalizedDecimal } from '../../shared/localized-decimal';
import { AdminDetailDialog } from './admin-detail-dialog';

@Component({
  selector: 'app-physical-stock-count-page',
  imports: [RouterLink, AdminDetailDialog],
  templateUrl: './physical-stock-count-page.html',
  styleUrls: ['./admin-page.scss', './categories-page.scss',
    './inventory-receipts-page.scss'],
})
export class PhysicalStockCountPage implements OnInit {
  private readonly client = inject(PhysicalStockCountClient);
  private readonly sessions = inject(SessionClient);
  private readonly router = inject(Router);

  protected readonly session = signal<CurrentSession | null>(null);
  protected readonly home = computed(() => this.session()?.context?.permissions
    .includes('platform.users.manage') ? '/admin' : '/');
  protected readonly administrator = computed(() => this.session()?.context?.permissions
    .includes('platform.roles.manage') ?? false);
  protected readonly policyOptions = signal<CountPolicyOptions | null>(null);
  protected readonly page = signal<PhysicalCountPage | null>(null);
  protected readonly selected = signal<PhysicalCount | null>(null);
  protected readonly selectedProductId = signal('');
  protected readonly countedQuantity = signal('');
  protected readonly decisionReason = signal('');
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  private pendingCreate: string | null = null;

  ngOnInit(): void {
    this.sessions.current().subscribe({
      next: session => {
        if (!session.context?.permissions.includes('inventory.stock.manage')) {
          void this.router.navigateByUrl('/'); return;
        }
        this.session.set(session);
        this.reloadPolicies();
        this.loadPage(1);
      }, error: () => void this.router.navigateByUrl('/'),
    });
  }

  protected loadPage(page: number): void {
    this.loading.set(true);
    this.client.list(page).subscribe({
      next: result => { this.page.set(result); this.loading.set(false); },
      error: () => { this.loading.set(false); this.error.set('No se pudieron cargar los conteos.'); },
    });
  }

  protected open(id: string): void {
    this.client.get(id).subscribe({ next: count => this.selected.set(count),
      error: () => this.error.set('No se pudo abrir el conteo.') });
  }

  protected close(): void { this.selected.set(null); }

  protected create(): void {
    this.pendingCreate ??= crypto.randomUUID();
    this.mutate(this.client.create(this.pendingCreate), 'Conteo creado.', true);
  }

  protected addLine(): void {
    const count = this.selected();
    const product = this.policyOptions()?.products.find(item =>
      item.id === this.selectedProductId());
    if (!count || !product) { this.error.set('Elegí un artículo.'); return; }
    const quantity = parseLocalizedDecimal(this.countedQuantity(),
      product.saleMode === 'unit' ? 0 : 3);
    if (quantity === null || quantity < 0) {
      this.error.set('Ingresá una cantidad física válida; coma o punto son decimales.');
      return;
    }
    this.mutate(this.client.addLine(count.id, count.version, product.id, quantity),
      'Medición registrada.');
  }

  protected submit(): void {
    const count = this.selected();
    if (count) this.mutate(this.client.submit(count.id, count.version),
      'Conteo enviado a revisión.');
  }

  protected approve(): void {
    const count = this.selected();
    const reason = this.decisionReason().trim();
    if (!count || !reason) { this.error.set('Indicá el motivo de aprobación.'); return; }
    this.mutate(this.client.approve(count.id, count.version, reason),
      'Conteo aprobado; las diferencias de stock quedaron registradas.');
  }

  protected reject(): void {
    const count = this.selected();
    const reason = this.decisionReason().trim();
    if (!count || !reason) { this.error.set('Indicá el motivo del rechazo.'); return; }
    this.mutate(this.client.reject(count.id, count.version, reason),
      'Conteo rechazado. No se modificó el stock.');
  }

  protected cancel(): void {
    const count = this.selected();
    if (count) this.mutate(this.client.cancel(count.id, count.version),
      'Borrador cancelado.');
  }

  protected setCategoryPolicy(id: string, raw: string): void {
    if (raw !== 'TheoreticalStock' && raw !== 'MassObservation') return;
    this.saving.set(true);
    this.client.setCategoryPolicy(id, raw).subscribe({
      next: () => { this.saving.set(false); this.reloadPolicies(); },
      error: () => { this.saving.set(false); this.error.set('No se pudo guardar la política de categoría.'); },
    });
  }

  protected setProductPolicy(id: string, raw: string): void {
    if (raw !== '' && raw !== 'TheoreticalStock' && raw !== 'MassObservation') return;
    this.saving.set(true);
    this.client.setProductPolicy(id, raw === '' ? null : raw).subscribe({
      next: () => { this.saving.set(false); this.reloadPolicies(); },
      error: () => { this.saving.set(false); this.error.set('No se pudo guardar la política de artículo.'); },
    });
  }

  protected categoryName(id: string): string {
    return this.policyOptions()?.categories.find(item => item.id === id)?.name ?? '';
  }

  protected format(value: number, mode: 'unit' | 'weight'): string {
    return formatLocalizedDecimal(value, mode === 'unit' ? 0 : 3);
  }

  protected policyLabel(value: StockCountPolicy): string {
    return value === 'TheoreticalStock' ? 'Saldo por artículo' : 'Solo medición de masa';
  }

  private reloadPolicies(): void {
    this.client.policies().subscribe({ next: options => this.policyOptions.set(options),
      error: () => this.error.set('No se pudieron cargar las políticas de conteo.') });
  }

  private mutate(request: Observable<PhysicalCount>, message: string,
    created = false): void {
    if (this.saving()) return;
    this.saving.set(true);
    this.error.set(null);
    this.notice.set(null);
    request.subscribe({
      next: count => {
        this.saving.set(false);
        this.selected.set(count);
        this.notice.set(message);
        this.loadPage(this.page()?.page ?? 1);
        if (created) this.pendingCreate = null;
      },
      error: error => {
        this.saving.set(false);
        const code = error instanceof HttpErrorResponse ? error.error?.error?.code : null;
        this.error.set(code === 'COUNT_STALE_RECOUNT_REQUIRED'
          ? 'El saldo cambió mientras se contaba. Rechazá este conteo y registrá uno nuevo.'
          : code ? `No se pudo completar: ${code}.` : 'No se pudo completar. Reintentá.');
      },
    });
  }
}
