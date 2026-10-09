import { DatePipe } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { AdminProductTaxClient, TaxProductPage, TaxProductRow } from '../../core/admin/admin-product-tax-client';
import { AdminTaxAssignmentClient, AssignmentResult, OtherTaxAssignment } from '../../core/admin/admin-tax-assignment-client';
import { AdminTaxCatalogClient, TaxCatalogEntry } from '../../core/admin/admin-tax-catalog-client';
import { AdminDetailDialog } from './admin-detail-dialog';

@Component({
  selector: 'app-tax-assignment-panel',
  imports: [AdminDetailDialog, DatePipe],
  templateUrl: './tax-assignment-panel.html',
  styleUrls: ['./categories-page.scss', './tax-assignment-panel.scss'],
})
export class TaxAssignmentPanel implements OnInit {
  private readonly catalog = inject(AdminTaxCatalogClient);
  private readonly productsClient = inject(AdminProductTaxClient);
  private readonly assignments = inject(AdminTaxAssignmentClient);
  private productsRevision = 0;
  private historyRevision = 0;
  protected readonly taxes = signal<readonly TaxCatalogEntry[]>([]);
  protected readonly taxesError = signal<string | null>(null);
  protected readonly selectedTaxId = signal('');
  protected readonly scope = signal<'selected' | 'all'>('selected');
  protected readonly isAssigned = signal(true);
  protected readonly selectedIds = signal<readonly string[]>([]);
  protected readonly productPage = signal(1);
  protected readonly productSearch = signal('');
  protected readonly products = signal<TaxProductPage | null>(null);
  protected readonly productsLoading = signal(true);
  protected readonly productsError = signal<string | null>(null);
  protected readonly allCount = signal<number | null>(null);
  protected readonly countError = signal<string | null>(null);
  protected readonly saving = signal(false);
  protected readonly saveError = signal<string | null>(null);
  protected readonly saveSuccess = signal<string | null>(null);
  protected readonly historyProduct = signal<TaxProductRow | null>(null);
  protected readonly history = signal<readonly OtherTaxAssignment[]>([]);
  protected readonly historyLoading = signal(false);
  protected readonly historyError = signal<string | null>(null);

  ngOnInit(): void {
    this.loadTaxes();
    this.loadCount();
    this.loadProducts();
  }

  protected loadTaxes(): void {
    this.taxesError.set(null);
    this.catalog.active().subscribe({
      next: (taxes) => this.taxes.set(taxes),
      error: () => this.taxesError.set('No se pudieron consultar los impuestos activos.'),
    });
  }

  protected selectedTax(): TaxCatalogEntry | undefined {
    return this.taxes().find((item) => item.id === this.selectedTaxId());
  }
  protected taxChanged(event: Event): void {
    this.selectedTaxId.set((event.target as HTMLSelectElement).value);
    if (this.selectedTax()?.kind === 'iva') this.isAssigned.set(true);
    this.saveError.set(null);
  }
  protected scopeChanged(scope: 'selected' | 'all'): void { this.scope.set(scope); }
  protected actionChanged(event: Event): void {
    this.isAssigned.set((event.target as HTMLSelectElement).value === 'assign');
  }
  protected searchChanged(event: Event): void {
    this.productSearch.set((event.target as HTMLInputElement).value);
  }
  protected runSearch(): void { this.productPage.set(1); this.loadProducts(); }
  protected changePage(delta: number): void {
    this.productPage.update((value) => value + delta);
    this.loadProducts();
  }
  protected pageCount(total: number): number { return Math.ceil(total / 25); }
  protected isSelected(id: string): boolean { return this.selectedIds().includes(id); }
  protected toggleProduct(id: string, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    this.selectedIds.update((ids) => checked ? [...ids, id] : ids.filter((item) => item !== id));
  }

  protected loadProducts(): void {
    const revision = ++this.productsRevision;
    this.productsLoading.set(true);
    this.productsError.set(null);
    this.productsClient.list(this.productPage(), this.productSearch().trim()).subscribe({
      next: (page) => {
        if (revision !== this.productsRevision) return;
        this.products.set(page);
        this.productsLoading.set(false);
      },
      error: () => {
        if (revision !== this.productsRevision) return;
        this.productsLoading.set(false);
        this.productsError.set('No se pudieron consultar los artículos.');
      },
    });
  }

  protected viewHistory(product: TaxProductRow): void {
    const revision = ++this.historyRevision;
    this.historyProduct.set(product);
    this.history.set([]);
    this.historyLoading.set(true);
    this.historyError.set(null);
    this.assignments.history(product.id).subscribe({
      next: (rows) => {
        if (revision !== this.historyRevision) return;
        this.history.set(rows);
        this.historyLoading.set(false);
      },
      error: () => {
        if (revision !== this.historyRevision) return;
        this.historyLoading.set(false);
        this.historyError.set('No se pudo consultar el historial de gravámenes.');
      },
    });
  }

  protected closeHistory(): void {
    this.historyRevision++;
    this.historyProduct.set(null);
    this.history.set([]);
    this.historyLoading.set(false);
    this.historyError.set(null);
  }

  protected apply(): void {
    const tax = this.selectedTax();
    if (!tax || this.saving()) {
      this.saveError.set('Elegí una entrada activa del catálogo.');
      return;
    }
    if (this.scope() === 'selected') {
      const ids = this.selectedIds();
      if (ids.length === 0) {
        this.saveError.set('Seleccioná al menos un artículo.');
        return;
      }
      if (!window.confirm(this.confirmation(tax, ids.length))) return;
      this.submit(this.assignments.setSelected(tax.id, this.isAssigned(), ids));
      return;
    }
    this.countError.set(null);
    this.assignments.count().subscribe({
      next: ({ total }) => {
        this.allCount.set(total);
        if (!window.confirm(this.confirmation(tax, total))) return;
        this.submit(this.assignments.setAll(tax.id, this.isAssigned(), total));
      },
      error: () => this.countError.set('No se pudo confirmar el número de artículos.'),
    });
  }

  private confirmation(tax: TaxCatalogEntry, count: number): string {
    const action = this.isAssigned() ? 'Aplicar' : 'Quitar';
    return `¿${action} ${tax.name} a ${count} artículos? No cambia ventas anteriores.`;
  }

  private submit(request: ReturnType<AdminTaxAssignmentClient['setAll']>): void {
    this.saving.set(true);
    this.saveError.set(null);
    this.saveSuccess.set(null);
    request.subscribe({
      next: (result: AssignmentResult) => {
        this.saving.set(false);
        this.selectedIds.set([]);
        this.saveSuccess.set(`${result.changedCount} de ${result.affectedCount} artículos actualizados.`);
        if (this.historyProduct()) this.viewHistory(this.historyProduct()!);
      },
      error: () => {
        this.saving.set(false);
        this.saveError.set('No se guardaron las asignaciones. Actualizá y reintentá.');
      },
    });
  }

  private loadCount(): void {
    this.assignments.count().subscribe({
      next: ({ total }) => this.allCount.set(total),
      error: () => this.countError.set('No se pudo consultar el total de artículos.'),
    });
  }
}
