import { DatePipe } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { AdminProductTaxClient, ProductTaxRule, TaxProductPage, TaxProductRow,
  TaxTreatment } from '../../core/admin/admin-product-tax-client';
import { CurrentSession, SessionClient } from '../../core/session/session-client';
import { AdminAreaTabs } from './admin-area-tabs';
import { AdminTaxCatalogClient, VatTaxOption } from '../../core/admin/admin-tax-catalog-client';
import { TaxCatalogPanel } from './tax-catalog-panel';
import { TaxAssignmentPanel } from './tax-assignment-panel';

@Component({
  selector: 'app-product-tax-page',
  imports: [RouterLink, AdminAreaTabs, DatePipe, TaxCatalogPanel, TaxAssignmentPanel],
  templateUrl: './product-tax-page.html',
  styleUrls: ['./admin-page.scss', './categories-page.scss', './accounts-page.scss', './product-tax-page.scss'],
})
export class ProductTaxPage implements OnInit {
  private readonly client = inject(AdminProductTaxClient);
  private readonly taxCatalog = inject(AdminTaxCatalogClient);
  private readonly sessions = inject(SessionClient);
  private readonly router = inject(Router);
  private listRevision = 0;
  protected readonly session = signal<CurrentSession | null>(null);
  protected readonly page = signal(1);
  protected readonly search = signal('');
  protected readonly list = signal<TaxProductPage | null>(null);
  protected readonly loading = signal(true);
  protected readonly listError = signal<string | null>(null);
  protected readonly selected = signal<TaxProductRow | null>(null);
  protected readonly history = signal<readonly ProductTaxRule[]>([]);
  protected readonly historyError = signal<string | null>(null);
  protected readonly treatment = signal<TaxTreatment>('taxed');
  protected readonly rate = signal('');
  protected readonly selectedTaxId = signal('');
  protected readonly vatOptions = signal<readonly VatTaxOption[]>([]);
  protected readonly vatOptionsError = signal<string | null>(null);
  protected readonly saving = signal(false);
  protected readonly saveError = signal<string | null>(null);
  protected readonly saveSuccess = signal(false);

  ngOnInit(): void {
    this.sessions.current().subscribe({
      next: (session) => {
        if (!session.context?.permissions.includes('catalog.manage')) {
          void this.router.navigateByUrl('/');
          return;
        }
        this.session.set(session);
        this.load();
      },
      error: () => void this.router.navigateByUrl('/'),
    });
  }

  protected searchChanged(event: Event): void {
    this.search.set((event.target as HTMLInputElement).value);
  }
  protected runSearch(): void { this.page.set(1); this.load(); }
  protected changePage(delta: number): void { this.page.update((value) => value + delta); this.load(); }
  protected pageCount(total: number): number { return Math.ceil(total / 25); }

  protected load(): void {
    const revision = ++this.listRevision;
    this.loading.set(true);
    this.listError.set(null);
    this.client.list(this.page(), this.search().trim()).subscribe({
      next: (result) => {
        if (revision !== this.listRevision) return;
        this.list.set(result);
        this.loading.set(false);
        const selected = this.selected();
        if (selected) this.selected.set(result.items.find((item) => item.id === selected.id) ?? selected);
      },
      error: () => {
        if (revision !== this.listRevision) return;
        this.loading.set(false);
        this.listError.set('No se pudieron consultar los artículos. Reintentá.');
      },
    });
  }

  protected select(product: TaxProductRow): void {
    this.selected.set(product);
    this.treatment.set(product.currentRule?.treatment ?? 'taxed');
    this.rate.set(product.currentRule?.ratePercent.toString() ?? '');
    this.selectedTaxId.set(product.currentRule?.taxCatalogEntryId ?? '');
    this.vatOptionsError.set(null);
    this.taxCatalog.vatOptions().subscribe({
      next: (options) => this.vatOptions.set(options),
      error: () => this.vatOptionsError.set('No se pudieron consultar las alícuotas de IVA.'),
    });
    this.history.set([]);
    this.historyError.set(null);
    this.saveError.set(null);
    this.saveSuccess.set(false);
    this.loadHistory(product.id);
  }

  protected close(): void { if (!this.saving()) this.selected.set(null); }
  protected treatmentChanged(event: Event): void {
    const value = (event.target as HTMLSelectElement).value as TaxTreatment;
    this.treatment.set(value);
    if (value !== 'taxed') { this.rate.set('0'); this.selectedTaxId.set(''); }
  }
  protected taxChanged(event: Event): void {
    const taxId = (event.target as HTMLSelectElement).value;
    this.selectedTaxId.set(taxId);
    this.rate.set(this.vatOptions().find((option) => option.id === taxId)?.ratePercent.toString() ?? '');
  }

  protected save(): void {
    const product = this.selected();
    if (!product || this.saving()) return;
    if (this.treatment() === 'taxed' && !this.selectedTaxId()) {
      this.saveError.set('Elegí una alícuota de IVA del catálogo antes de guardar.');
      return;
    }
    const rate = Number(this.rate().replace(',', '.'));
    if (!this.rate().trim() || !Number.isFinite(rate) || rate < 0 || rate > 100 ||
      Math.abs(Math.round(rate * 100) - rate * 100) > 0.000001 ||
      (this.treatment() !== 'taxed' && rate !== 0)) {
      this.saveError.set('Ingresá una alícuota válida de 0 a 100 con hasta dos decimales.');
      return;
    }
    if (!window.confirm(`¿Guardar el tratamiento fiscal de ${product.name}? No altera ventas anteriores.`)) return;
    this.saving.set(true);
    this.saveError.set(null);
    this.saveSuccess.set(false);
    this.client.set(product.id, this.treatment(), rate,
      this.treatment() === 'taxed' ? this.selectedTaxId() : undefined).subscribe({
      next: (rule) => {
        this.saving.set(false);
        this.saveSuccess.set(true);
        this.selected.set({ ...product, currentRule: rule });
        this.loadHistory(product.id);
        this.load();
      },
      error: () => {
        this.saving.set(false);
        this.saveError.set('No se guardó la regla fiscal. Verificá los datos y reintentá.');
      },
    });
  }

  private loadHistory(productId: string): void {
    this.client.history(productId).subscribe({
      next: (rules) => {
        if (this.selected()?.id === productId) this.history.set(rules);
      },
      error: () => this.historyError.set('No se pudo cargar el historial fiscal.'),
    });
  }

  protected label(treatment: TaxTreatment): string {
    return treatment === 'taxed' ? 'Gravado' : treatment === 'exempt' ? 'Exento' : 'No alcanzado';
  }
}
