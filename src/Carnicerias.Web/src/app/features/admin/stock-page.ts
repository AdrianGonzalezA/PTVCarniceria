import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AdminAreaTabs } from './admin-area-tabs';
import { InventoryClient, InventoryStockItem } from '../../core/inventory/inventory-client';
import { CurrentSession, SessionClient } from '../../core/session/session-client';

@Component({
  selector: 'app-stock-page',
  imports: [RouterLink, AdminAreaTabs, ReactiveFormsModule],
  templateUrl: './stock-page.html',
  styleUrls: ['./admin-page.scss', './categories-page.scss', './stock-page.scss'],
})
export class StockPage implements OnInit {
  private readonly inventory = inject(InventoryClient);
  private readonly sessions = inject(SessionClient);
  private readonly router = inject(Router);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly session = signal<CurrentSession | null>(null);
  protected readonly items = signal<readonly InventoryStockItem[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadError = signal<string | null>(null);
  protected readonly actionError = signal<string | null>(null);
  protected readonly actionMessage = signal<string | null>(null);
  protected readonly search = signal('');
  protected readonly selected = signal<InventoryStockItem | null>(null);
  protected readonly saving = signal(false);
  protected readonly visibleItems = computed(() => {
    const term = this.search().trim().toLocaleLowerCase();
    return term ? this.items().filter((item) =>
      item.name.toLocaleLowerCase().includes(term) || item.code.toLocaleLowerCase().includes(term))
      : this.items();
  });
  protected readonly adjustmentForm = this.formBuilder.nonNullable.group({
    quantityDelta: ['', [Validators.required]],
    reason: ['', [Validators.required, Validators.pattern(/\S/), Validators.maxLength(240)]],
  });
  private pendingOperation: { signature: string; id: string } | null = null;

  ngOnInit(): void {
    this.sessions.current().subscribe({
      next: (session) => {
        if (!session.context?.permissions.includes('inventory.stock.manage')) {
          void this.router.navigateByUrl('/');
          return;
        }
        this.session.set(session);
        this.load();
      },
      error: () => void this.router.navigateByUrl('/'),
    });
  }

  protected load(): void {
    this.loading.set(true);
    this.loadError.set(null);
    this.inventory.stock().subscribe({
      next: (items) => {
        this.items.set(items);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.loadError.set('No se pudieron cargar las existencias.');
      },
    });
  }

  protected updateSearch(event: Event): void {
    this.search.set((event.target as HTMLInputElement).value);
  }

  protected startAdjustment(item: InventoryStockItem): void {
    this.selected.set(item);
    this.adjustmentForm.reset({ quantityDelta: '', reason: '' });
    this.pendingOperation = null;
    this.actionError.set(null);
    this.actionMessage.set(null);
  }

  protected cancelAdjustment(): void {
    if (!this.saving()) {
      this.selected.set(null);
      this.pendingOperation = null;
    }
  }

  protected saveAdjustment(): void {
    const item = this.selected();
    const raw = this.adjustmentForm.getRawValue();
    const quantityDelta = Number(raw.quantityDelta);
    const reason = raw.reason.trim();
    const precision = item?.saleMode === 'unit' ? 0 : 3;
    if (!item || this.saving() || this.adjustmentForm.invalid || !reason ||
        !Number.isFinite(quantityDelta) || quantityDelta === 0 ||
        Math.abs(quantityDelta) > 100000 ||
        Number(quantityDelta.toFixed(precision)) !== quantityDelta) {
      this.adjustmentForm.markAllAsTouched();
      this.actionError.set(item?.saleMode === 'unit'
        ? 'Ingresá una cantidad entera distinta de cero y un motivo.'
        : 'Ingresá una cantidad distinta de cero (hasta 3 decimales) y un motivo.');
      return;
    }
    if (quantityDelta < 0 && item.onHand + quantityDelta < item.reserved) {
      this.actionError.set('El ajuste dejaría menos existencia que la reservada en tickets abiertos.');
      return;
    }
    const signature = `${item.productId}|${quantityDelta}|${reason}`;
    if (quantityDelta < 0 && this.pendingOperation?.signature !== signature &&
        !window.confirm(`¿Descontar ${Math.abs(quantityDelta)} ${item.unit ?? 'unidades'} de ${item.name}?`)) return;
    if (this.pendingOperation?.signature !== signature)
      this.pendingOperation = { signature, id: crypto.randomUUID() };
    this.saving.set(true);
    this.actionError.set(null);
    this.inventory.adjust(item.productId, this.pendingOperation.id, quantityDelta, reason).subscribe({
      next: (result) => {
        this.items.set(this.items().map((existing) => existing.productId === item.productId
          ? { ...existing, onHand: result.onHand, reserved: result.reserved, available: result.available }
          : existing));
        this.saving.set(false);
        this.selected.set(null);
        this.pendingOperation = null;
        this.actionMessage.set(`Existencia de ${item.name} actualizada. Movimiento ${result.operationId}.`);
      },
      error: (error: HttpErrorResponse) => {
        this.saving.set(false);
        this.actionError.set(error.error?.error?.code === 'STOCK_BELOW_RESERVED'
          ? 'El stock cambió y no se puede descontar lo reservado. Actualizá y revisá la cantidad.'
          : 'No se pudo guardar el ajuste. Podés reintentar sin duplicar el movimiento.');
      },
    });
  }
}
