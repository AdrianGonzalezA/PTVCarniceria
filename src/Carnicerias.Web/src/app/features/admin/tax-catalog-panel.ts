import { Component, inject, OnInit, signal } from '@angular/core';
import { AdminTaxCatalogClient, TaxCatalogEntry, TaxCatalogPage,
  TaxKind } from '../../core/admin/admin-tax-catalog-client';
import { AdminDetailDialog } from './admin-detail-dialog';

@Component({
  selector: 'app-tax-catalog-panel',
  imports: [AdminDetailDialog],
  templateUrl: './tax-catalog-panel.html',
  styleUrls: ['./categories-page.scss', './tax-catalog-panel.scss'],
})
export class TaxCatalogPanel implements OnInit {
  private readonly client = inject(AdminTaxCatalogClient);
  private listRevision = 0;
  protected readonly page = signal(1);
  protected readonly search = signal('');
  protected readonly list = signal<TaxCatalogPage | null>(null);
  protected readonly loading = signal(true);
  protected readonly listError = signal<string | null>(null);
  protected readonly code = signal('');
  protected readonly name = signal('');
  protected readonly kind = signal<TaxKind>('iva');
  protected readonly rate = signal('');
  protected readonly saving = signal(false);
  protected readonly saveError = signal<string | null>(null);
  protected readonly saveSuccess = signal<string | null>(null);
  protected readonly pendingEntry = signal<TaxCatalogEntry | null>(null);

  ngOnInit(): void { this.load(); }

  protected load(): void {
    const revision = ++this.listRevision;
    this.loading.set(true);
    this.listError.set(null);
    this.client.list(this.page(), this.search().trim()).subscribe({
      next: (result) => {
        if (revision !== this.listRevision) return;
        this.list.set(result);
        this.loading.set(false);
      },
      error: () => {
        if (revision !== this.listRevision) return;
        this.loading.set(false);
        this.listError.set('No se pudo consultar el catálogo. Reintentá.');
      },
    });
  }

  protected searchChanged(event: Event): void {
    this.search.set((event.target as HTMLInputElement).value);
  }
  protected runSearch(): void { this.page.set(1); this.load(); }
  protected changePage(delta: number): void { this.page.update((value) => value + delta); this.load(); }
  protected pageCount(total: number): number { return Math.ceil(total / 25); }
  protected codeChanged(event: Event): void { this.code.set((event.target as HTMLInputElement).value); }
  protected nameChanged(event: Event): void { this.name.set((event.target as HTMLInputElement).value); }
  protected kindChanged(event: Event): void {
    this.kind.set((event.target as HTMLSelectElement).value as TaxKind);
  }
  protected rateChanged(event: Event): void { this.rate.set((event.target as HTMLInputElement).value); }

  protected create(): void {
    if (this.saving()) return;
    const code = this.code().trim().toUpperCase();
    const name = this.name().trim();
    const rate = Number(this.rate().replace(',', '.'));
    if (!/^[A-Z0-9_-]{1,40}$/.test(code) || name.length < 1 || name.length > 120 ||
      !this.rate().trim() || !Number.isFinite(rate) || rate < 0 || rate > 100 ||
      Math.abs(Math.round(rate * 100) - rate * 100) > 0.000001) {
      this.saveError.set('Ingresá código, nombre y porcentaje válido (0 a 100, hasta dos decimales).');
      return;
    }
    this.saving.set(true);
    this.saveError.set(null);
    this.saveSuccess.set(null);
    this.client.create(code, name, this.kind(), rate).subscribe({
      next: () => {
        this.saving.set(false);
        this.code.set('');
        this.name.set('');
        this.rate.set('');
        this.saveSuccess.set('Impuesto o gravamen agregado al catálogo.');
        this.page.set(1);
        this.load();
      },
      error: () => {
        this.saving.set(false);
        this.saveError.set('No se creó la entrada. Verificá si el código ya existe.');
      },
    });
  }

  protected deactivate(entry: TaxCatalogEntry): void {
    if (this.saving() || !entry.isActive) return;
    this.pendingEntry.set(entry);
  }

  protected confirmDeactivate(): void {
    const entry = this.pendingEntry();
    if (!entry || this.saving()) return;
    this.saving.set(true);
    this.saveError.set(null);
    this.saveSuccess.set(null);
    this.client.deactivate(entry.id).subscribe({
      next: () => {
        this.saving.set(false);
        this.pendingEntry.set(null);
        this.saveSuccess.set('Entrada inactivada; su historial permanece disponible.');
        this.load();
      },
      error: () => {
        this.saving.set(false);
        this.saveError.set('No se pudo inactivar. Puede tener asignaciones vigentes.');
      },
    });
  }

  protected closeAction(): void {
    if (!this.saving()) this.pendingEntry.set(null);
  }
}
