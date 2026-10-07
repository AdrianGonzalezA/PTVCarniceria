import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { Observable, of, Subject } from 'rxjs';
import { catchError, concatMap, map } from 'rxjs/operators';
import { CatalogCategory, CatalogClient, PriceListOption } from '../../core/catalog/catalog-client';
import { InventoryClient, InventoryStockItem } from '../../core/inventory/inventory-client';
import { ConfirmedSale, SaleDraft, SaleDraftClient, SaleDraftLine, SalePaymentMethod } from '../../core/sales/sale-draft-client';
import { CashierShift, CashierShiftClient } from '../../core/sales/cashier-shift-client';
import { CurrentSession, SessionClient } from '../../core/session/session-client';

type SaleMode = 'weight' | 'unit';

interface PosProduct {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly category: string;
  readonly price: number;
  readonly mode: SaleMode;
  readonly unit?: string;
  readonly availableStock?: number;
}

interface SaleLine {
  readonly product: PosProduct;
  readonly quantity: number;
}

const categories = [
  { id: 'res', label: 'Carne de res', icon: '🐄' },
  { id: 'cerdo', label: 'Carne de cerdo', icon: '🐖' },
  { id: 'pollo', label: 'Carne de pollo', icon: '🐓' },
  { id: 'pescado', label: 'Pescado', icon: '🐟' },
  { id: 'vegetales', label: 'Vegetales', icon: '🥬' },
  { id: 'frutas', label: 'Frutas', icon: '🍎' },
  { id: 'congelados', label: 'Congelados', icon: '❄️' },
  { id: 'panificados', label: 'Panificados', icon: '🍞' },
  { id: 'bebidas', label: 'Bebidas', icon: '🧃' },
  { id: 'otros', label: 'Otros', icon: '•••' },
] as const;

const demoProducts: readonly PosProduct[] = [
  { id: 'p1', code: '1001', name: 'Bife de chorizo', category: 'res', price: 12900, mode: 'weight' },
  { id: 'p2', code: '1002', name: 'Asado', category: 'res', price: 11500, mode: 'weight' },
  { id: 'p3', code: '1003', name: 'Milanesa de nalga', category: 'res', price: 14800, mode: 'weight' },
  { id: 'p4', code: '1004', name: 'Carne picada especial', category: 'res', price: 9500, mode: 'weight' },
  { id: 'p5', code: '2001', name: 'Bondiola', category: 'cerdo', price: 8900, mode: 'weight' },
  { id: 'p6', code: '2002', name: 'Costilla de cerdo', category: 'cerdo', price: 8200, mode: 'weight' },
  { id: 'p7', code: '3001', name: 'Suprema de pollo', category: 'pollo', price: 8700, mode: 'weight' },
  { id: 'p8', code: '3002', name: 'Pata muslo', category: 'pollo', price: 5100, mode: 'weight' },
  { id: 'p9', code: '4001', name: 'Merluza', category: 'pescado', price: 10500, mode: 'weight' },
  { id: 'p10', code: '5001', name: 'Papa blanca', category: 'vegetales', price: 2200, mode: 'weight' },
  { id: 'p11', code: '5002', name: 'Tomate perita', category: 'vegetales', price: 2800, mode: 'weight' },
  { id: 'p12', code: '6001', name: 'Manzana roja', category: 'frutas', price: 3200, mode: 'weight' },
  { id: 'p13', code: '7001', name: 'Hamburguesas x 4', category: 'congelados', price: 8000, mode: 'unit' },
  { id: 'p14', code: '8001', name: 'Pan rallado', category: 'panificados', price: 2400, mode: 'unit' },
  { id: 'p15', code: '9001', name: 'Gaseosa 1,5 L', category: 'bebidas', price: 2500, mode: 'unit' },
];

const demoPriceLists = [
  { id: 'demo:mostrador', name: 'Mostrador (prueba)', priceFactor: 1 },
  { id: 'demo:convenio', name: 'Convenio (prueba)', priceFactor: 0.95 },
] as const;

@Component({
  selector: 'app-pos-page',
  imports: [RouterLink],
  templateUrl: './pos-page.html',
})
export class PosPage implements OnInit {
  private readonly sessionClient = inject(SessionClient);
  private readonly catalogClient = inject(CatalogClient);
  private readonly saleDraftClient = inject(SaleDraftClient);
  private readonly cashierShiftClient = inject(CashierShiftClient);
  private readonly inventoryClient = inject(InventoryClient);
  private readonly router = inject(Router);
  private readonly draftOperations = new Subject<{
    readonly revision: number;
    readonly operation: 'cancel' | { readonly priceListId: string; readonly lines: readonly SaleLine[] };
  }>();
  private draftRevision = 0;
  private stockRequestRevision = 0;

  protected readonly session = signal<CurrentSession | null>(null);
  protected readonly demoPriceLists = demoPriceLists;
  protected readonly realPriceLists = signal<readonly PriceListOption[]>([]);
  protected readonly catalogCategories = signal<readonly CatalogCategory[]>([]);
  protected readonly realProducts = signal<readonly PosProduct[]>([]);
  private readonly stockByProduct = signal<ReadonlyMap<string, number>>(new Map());
  protected readonly priceListLoadState = signal<'loading' | 'ready' | 'error'>('loading');
  protected readonly catalogLoadState = signal<'idle' | 'loading' | 'ready' | 'error'>('idle');
  protected readonly selectedPriceListId = signal('');
  protected readonly isDemoPriceList = computed(() => this.selectedPriceListId().startsWith('demo:'));
  protected readonly selectedPriceListName = computed(
    () => this.realPriceLists().find((list) => list.id === this.selectedPriceListId())?.name ??
      this.demoPriceLists.find((list) => list.id === this.selectedPriceListId())?.name ?? '',
  );
  protected readonly products = computed<readonly PosProduct[]>(() => {
    const list = this.demoPriceLists.find((item) => item.id === this.selectedPriceListId());
    return list
      ? demoProducts.map((product) => ({
          ...product,
          price: Math.round(product.price * list.priceFactor * 100) / 100,
        }))
      : [];
  });
  protected readonly categories = computed(() => this.isDemoPriceList()
    ? categories
    : this.catalogCategories().map((category) => ({
        id: category.id,
        label: category.name,
        icon: this.categoryIcon(category.name),
      })));
  protected readonly canSearchCatalog = computed(() =>
    this.isDemoPriceList() || (this.selectedPriceListId().length > 0 && this.catalogLoadState() === 'ready'),
  );
  protected readonly activeCategory = signal<string>('res');
  protected readonly searchText = signal('');
  protected readonly lines = signal<readonly SaleLine[]>([]);
  private readonly persistedLines = signal<readonly SaleLine[]>([]);
  protected readonly selectedProduct = signal<PosProduct | null>(null);
  protected readonly quantityDraft = signal('1');
  protected readonly quantityProblem = computed(() => {
    const product = this.selectedProduct();
    if (!product) return null;
    const quantity = Number(this.quantityDraft());
    if (!Number.isFinite(quantity) || quantity <= 0 || quantity > 10000 ||
        Math.abs(Math.round(quantity * 1000) - quantity * 1000) > 0.000001)
      return product.mode === 'weight' ? 'Ingresá un peso válido mayor a cero (hasta 3 decimales).' : 'Ingresá una cantidad válida mayor a cero.';
    if (product.mode === 'unit' && !Number.isInteger(quantity)) return 'Ingresá una cantidad entera de unidades de venta.';
    const available = this.availableStock(product);
    if (!this.isDemoPriceList() && available !== undefined && quantity > available)
      return `Stock insuficiente. Disponible: ${available.toLocaleString('es-AR', { maximumFractionDigits: 3 })} ${this.unitLabel(product)}.`;
    return null;
  });
  protected readonly isMenuOpen = signal(false);
  protected readonly checkoutNotice = signal(false);
  protected readonly cashierShift = signal<CashierShift | null>(null);
  protected readonly lastClosedShift = signal<CashierShift | null>(null);
  protected readonly shiftDialog = signal(false);
  protected readonly shiftOpeningCash = signal('0');
  protected readonly shiftBusy = signal(false);
  protected readonly shiftError = signal<string | null>(null);
  protected readonly draftId = signal('');
  protected readonly checkoutBusy = signal(false);
  protected readonly checkoutError = signal<string | null>(null);
  protected readonly confirmedSale = signal<ConfirmedSale | null>(null);
  protected readonly selectedPayments = signal<readonly { method: SalePaymentMethod; amount: number }[]>([]);
  protected readonly paymentMethods: readonly { readonly id: SalePaymentMethod; readonly label: string }[] = [
    { id: 'cash', label: 'Efectivo' }, { id: 'debit', label: 'Débito' },
    { id: 'credit', label: 'Crédito' }, { id: 'transfer', label: 'Transferencia' },
    { id: 'mercadoPago', label: 'Mercado Pago' }, { id: 'cheque', label: 'Cheque' },
  ];
  protected readonly changePreview = computed(() => {
    const selected = this.selectedPayments();
    const cash = selected.find((payment) => payment.method === 'cash')?.amount ?? 0;
    const nonCash = selected.filter((payment) => payment.method !== 'cash').reduce((sum, payment) => sum + payment.amount, 0);
    return nonCash > this.subtotal() ? 0 : Math.max(0, Math.round((cash + nonCash - this.subtotal()) * 100) / 100);
  });
  protected readonly paymentBalance = computed(() => Math.max(0, Math.round((this.subtotal() -
    this.selectedPayments().reduce((sum, payment) => sum + payment.amount, 0)) * 100) / 100));
  protected readonly paymentProblem = computed(() => {
    const payments = this.selectedPayments();
    if (payments.length === 0) return 'Seleccioná al menos un medio de pago.';
    if (payments.some((payment) => !Number.isFinite(payment.amount) || payment.amount <= 0 ||
        Math.round(payment.amount * 100) !== payment.amount * 100))
      return 'Completá cada medio de pago con un importe válido.';
    const nonCash = payments.filter((payment) => payment.method !== 'cash')
      .reduce((sum, payment) => sum + payment.amount, 0);
    if (nonCash > this.subtotal()) return 'Los medios sin efectivo no pueden superar el total del ticket.';
    if (this.paymentBalance() > 0) return 'Los medios de pago todavía no cubren el total.';
    return null;
  });
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly draftStatus = signal<'demo' | 'loading' | 'saved' | 'saving' | 'error'>('loading');
  protected readonly inventoryOpen = signal(false);
  protected readonly inventoryItems = signal<readonly InventoryStockItem[]>([]);
  protected readonly inventoryStatus = signal<'idle' | 'loading' | 'ready' | 'error'>('idle');
  protected readonly adjustmentProductId = signal('');
  protected readonly adjustmentQuantity = signal('');
  protected readonly adjustmentReason = signal('Carga o ajuste de stock');
  protected readonly inventoryMessage = signal<string | null>(null);
  protected readonly realSearchSubmitted = signal(false);
  protected readonly subtotal = computed(() => this.lines().reduce((total, line) => total + this.lineTotal(line), 0));
  protected readonly visibleProducts = computed(() => {
    if (!this.isDemoPriceList()) return this.realSearchSubmitted() || !this.searchText().trim()
      ? this.realProducts()
      : [];
    const query = this.searchText().trim().toLocaleLowerCase('es-AR');
    if (query.length >= 3) {
      return this.products().filter((product) =>
        `${product.name} ${product.code}`.toLocaleLowerCase('es-AR').includes(query),
      );
    }
    return this.products().filter((product) => product.category === this.activeCategory());
  });
  protected readonly searchReady = computed(() => this.searchText().trim().length >= 3);
  protected readonly activeCategoryName = computed(() =>
    this.categories().find((category) => category.id === this.activeCategory())?.label ?? 'Productos',
  );

  constructor() {
    this.draftOperations.pipe(
      concatMap(({ revision, operation }) => {
        const request: Observable<SaleDraft | void> = operation === 'cancel'
          ? this.saleDraftClient.cancel()
          : this.saleDraftClient.save(operation.priceListId, operation.lines.map((line) => ({
              productId: line.product.id,
              quantity: line.quantity,
            })));
        return request.pipe(
          map((result) => ({ revision, operation, result, error: null as HttpErrorResponse | null })),
          catchError((error: HttpErrorResponse) => of({ revision, operation, result: null, error })),
        );
      }),
      takeUntilDestroyed(),
    ).subscribe(({ revision, operation, result, error }) => {
      if (error) {
        if (revision === this.draftRevision) {
          if (operation === 'cancel') this.lines.set(this.persistedLines());
          if (error.error?.error?.code === 'INSUFFICIENT_STOCK') {
            this.lines.set(this.persistedLines());
            this.draftStatus.set('saved');
            this.errorMessage.set('No hay stock suficiente. El producto o la cantidad rechazada no se agregó al ticket. Revisá las existencias.');
            this.refreshStock();
          } else {
            this.draftStatus.set('error');
            this.errorMessage.set(operation === 'cancel'
              ? 'No se pudo cancelar el borrador guardado. Intentá de nuevo.'
              : this.draftSaveErrorMessage(error));
          }
        }
        return;
      }
      if (result === null || result === undefined) {
        this.persistedLines.set([]);
        this.draftId.set('');
      } else {
        const savedDraft = result as SaleDraft;
        this.draftId.set(savedDraft.id);
        this.lines.update((currentLines) => currentLines.map((line) => {
          const savedLine = savedDraft.lines.find((item) => item.productId === line.product.id);
          return savedLine ? { ...line, product: { ...line.product, price: savedLine.unitPrice } } : line;
        }));
        this.persistedLines.set(savedDraft.lines.map((item) => ({
          product: {
            id: item.productId,
            code: item.productCode,
            name: item.productName,
            category: '',
            price: item.unitPrice,
            mode: item.saleMode,
            unit: item.unit,
          },
          quantity: item.quantity,
        })));
      }
      if (revision === this.draftRevision) {
        this.draftStatus.set('saved');
        this.errorMessage.set(null);
        this.refreshStock();
      }
    });
  }

  ngOnInit(): void {
    this.sessionClient.current().subscribe({
      next: (session) => {
        if (!session.context) {
          void this.router.navigateByUrl('/');
          return;
        }
        this.session.set(session);
        this.loadCashierShift();
        this.loadPriceLists();
        this.refreshStock();
      },
      error: () => void this.router.navigateByUrl('/'),
    });
  }

  protected loadPriceLists(): void {
    this.priceListLoadState.set('loading');
    this.catalogClient.priceLists().subscribe({
      next: (lists) => {
        this.realPriceLists.set(lists);
        if (!lists.some((list) => list.id === this.selectedPriceListId())) {
          this.selectedPriceListId.set(lists[0]?.id ?? '');
        }
        this.priceListLoadState.set('ready');
        if (this.selectedPriceListId()) this.loadCategories(this.selectedPriceListId());
        this.loadSavedDraft();
      },
      error: () => this.priceListLoadState.set('error'),
    });
  }

  protected updateSearch(event: Event): void {
    this.searchText.set((event.target as HTMLInputElement).value.slice(0, 80));
    if (!this.isDemoPriceList()) this.realSearchSubmitted.set(false);
  }

  protected selectPriceList(event: Event): void {
    if (this.lines().length > 0) return;
    const priceListId = (event.target as HTMLSelectElement).value;
    this.selectedPriceListId.set(priceListId);
    this.searchText.set('');
    this.selectedProduct.set(null);
    this.errorMessage.set(null);
    this.realSearchSubmitted.set(false);
    this.realProducts.set([]);
    if (priceListId.startsWith('demo:')) {
      this.catalogCategories.set([]);
      this.catalogLoadState.set('ready');
    } else if (priceListId) {
      this.loadCategories(priceListId);
    } else {
      this.catalogCategories.set([]);
      this.catalogLoadState.set('idle');
    }
  }

  protected submitSearch(event: Event): void {
    event.preventDefault();
    const code = this.searchText().trim();
    if (!this.isDemoPriceList()) {
      const priceListId = this.selectedPriceListId();
      if (!priceListId || code.length === 0) return;
      this.realSearchSubmitted.set(true);
      this.searchRealProducts(priceListId, code);
      return;
    }
    const product = this.products().find((item) => item.code === code);
    if (!product) return;

    this.searchText.set('');
    this.openProduct(product);
  }

  protected selectCategory(id: string): void {
    this.activeCategory.set(id);
    this.searchText.set('');
    this.realSearchSubmitted.set(false);
    if (!this.isDemoPriceList()) this.loadProducts(this.selectedPriceListId(), id);
  }

  protected retryCatalog(): void {
    const priceListId = this.selectedPriceListId();
    if (!priceListId || this.isDemoPriceList()) return;
    const query = this.searchText().trim();
    if (this.realSearchSubmitted() && query) this.searchRealProducts(priceListId, query);
    else if (this.activeCategory()) this.loadProducts(priceListId, this.activeCategory());
    else this.loadCategories(priceListId);
  }

  private searchRealProducts(priceListId: string, query: string): void {
    this.catalogLoadState.set('loading');
    this.catalogClient.products({ priceListId, code: query }).subscribe({
      next: (page) => {
        if (page.totalItems === 1) {
          const product = this.mapCatalogProduct(page.items[0]);
          this.realProducts.set([product]);
          this.catalogLoadState.set('ready');
          this.searchText.set('');
          this.openProduct(product);
          return;
        }
        if (page.totalItems > 0 || query.length < 3) {
          this.setRealProducts(page.items);
          return;
        }
        this.catalogClient.products({ priceListId, q: query }).subscribe({
          next: (searchPage) => this.setRealProducts(searchPage.items),
          error: () => this.catalogLoadState.set('error'),
        });
      },
      error: () => this.catalogLoadState.set('error'),
    });
  }

  private loadCategories(priceListId: string): void {
    this.catalogLoadState.set('loading');
    this.catalogCategories.set([]);
    this.catalogClient.categories(priceListId).subscribe({
      next: (items) => {
        this.catalogCategories.set(items);
        this.catalogLoadState.set('ready');
        const firstCategory = items[0]?.id;
        this.activeCategory.set(firstCategory ?? '');
        this.realProducts.set([]);
        if (firstCategory) this.loadProducts(priceListId, firstCategory);
      },
      error: () => this.catalogLoadState.set('error'),
    });
  }

  private loadProducts(priceListId: string, categoryId?: string): void {
    this.catalogLoadState.set('loading');
    this.catalogClient.products({ priceListId, ...(categoryId ? { categoryId } : {}) }).subscribe({
      next: (page) => this.setRealProducts(page.items),
      error: () => this.catalogLoadState.set('error'),
    });
  }

  private setRealProducts(items: readonly {
    readonly id: string;
    readonly code: string;
    readonly name: string;
    readonly categoryId: string;
    readonly saleMode: 'weight' | 'unit';
    readonly unit?: string;
    readonly price: number;
    readonly availableStock: number;
  }[]): void {
    this.realProducts.set(items.map((item) => this.mapCatalogProduct(item)));
    this.catalogLoadState.set('ready');
  }

  private mapCatalogProduct(item: {
    readonly id: string;
    readonly code: string;
    readonly name: string;
    readonly categoryId: string;
    readonly saleMode: 'weight' | 'unit';
    readonly unit?: string;
    readonly price: number;
    readonly availableStock: number;
  }): PosProduct {
    return {
      id: item.id,
      code: item.code,
      name: item.name,
      category: item.categoryId,
      price: item.price,
      mode: item.saleMode,
      unit: item.unit,
      availableStock: item.availableStock,
    };
  }

  protected openProduct(product: PosProduct): void {
    this.errorMessage.set(null);
    if (!this.isDemoPriceList() && (this.availableStock(product) ?? 0) <= 0) {
      this.errorMessage.set('No hay stock disponible para este producto en la sucursal.');
      return;
    }
    this.selectedProduct.set(product);
    this.quantityDraft.set('1');
  }

  protected updateQuantity(event: Event): void {
    this.quantityDraft.set((event.target as HTMLInputElement).value.replace(',', '.'));
  }

  protected addSelectedProduct(): void {
    const product = this.selectedProduct();
    const quantity = Number(this.quantityDraft());
    if (!product || this.quantityProblem()) {
      this.errorMessage.set(this.quantityProblem());
      return;
    }

    this.errorMessage.set(null);
    this.addProductLine(product, quantity);
    this.selectedProduct.set(null);
  }

  protected updateLineQuantity(productId: string, event: Event): void {
    const input = event.target as HTMLInputElement;
    const quantity = Number(input.value.replace(',', '.'));
    const line = this.lines().find((item) => item.product.id === productId);
    if (!line || !Number.isFinite(quantity) || quantity <= 0 ||
        quantity > 10000 || Math.abs(Math.round(quantity * 1000) - quantity * 1000) > 0.000001 ||
        (line.product.mode === 'unit' && !Number.isInteger(quantity))) {
      this.errorMessage.set('La cantidad o el peso debe ser mayor a cero y válido para el producto.');
      if (line) input.value = String(line.quantity);
      return;
    }
    const available = this.availableStock(line.product);
    const reservedByThisTicket = this.persistedLines().find((item) => item.product.id === productId)?.quantity ?? 0;
    if (!this.isDemoPriceList() && available !== undefined && quantity > available + reservedByThisTicket) {
      this.errorMessage.set(`Stock insuficiente. Disponible para agregar: ${available.toLocaleString('es-AR', { maximumFractionDigits: 3 })} ${this.unitLabel(line.product)}.`);
      input.value = String(line.quantity);
      return;
    }
    this.errorMessage.set(null);
    this.lines.update((lines) => lines.map((item) => item.product.id === productId
      ? { ...item, quantity: this.roundQuantity(quantity) }
      : item));
    this.persistCurrentDraft();
  }

  protected removeLine(productId: string): void {
    const line = this.lines().find((item) => item.product.id === productId);
    if (!line || !window.confirm(`¿Querés quitar ${line.product.name} del detalle?`)) return;
    this.lines.update((lines) => lines.filter((item) => item.product.id !== productId));
    this.persistCurrentDraft();
  }

  protected cancelSale(): void {
    if (!window.confirm('¿Querés cancelar la venta y quitar todos sus productos?')) return;
    if (!this.isDemoPriceList() && this.selectedPriceListId()) {
      this.queueDraftOperation('cancel');
    }
    this.lines.set([]);
    this.checkoutNotice.set(false);
    this.errorMessage.set(null);
  }

  protected openInventory(): void {
    this.inventoryOpen.set(true);
    this.inventoryStatus.set('loading');
    this.inventoryMessage.set(null);
    this.inventoryClient.stock().subscribe({
      next: (items) => {
        this.inventoryItems.set(items);
        this.adjustmentProductId.set(items[0]?.productId ?? '');
        this.inventoryStatus.set('ready');
      },
      error: () => this.inventoryStatus.set('error'),
    });
  }

  protected updateAdjustmentQuantity(event: Event): void {
    this.adjustmentQuantity.set((event.target as HTMLInputElement).value.replace(',', '.'));
  }

  protected updateAdjustmentProduct(event: Event): void {
    this.adjustmentProductId.set((event.target as HTMLSelectElement).value);
  }

  protected updateAdjustmentReason(event: Event): void {
    this.adjustmentReason.set((event.target as HTMLInputElement).value.slice(0, 240));
  }

  protected submitStockAdjustment(): void {
    const quantityDelta = Number(this.adjustmentQuantity());
    const reason = this.adjustmentReason().trim();
    if (!this.adjustmentProductId() || !Number.isFinite(quantityDelta) || quantityDelta === 0 ||
        Math.abs(quantityDelta) > 100000 || !reason) {
      this.inventoryMessage.set('Ingresá un producto, una cantidad distinta de cero y un motivo.');
      return;
    }

    this.inventoryStatus.set('loading');
    this.inventoryClient.adjust(this.adjustmentProductId(), crypto.randomUUID(), quantityDelta, reason).subscribe({
      next: (result) => {
        this.inventoryItems.update((items) => items.map((item) => item.productId === this.adjustmentProductId()
          ? { ...item, onHand: result.onHand, reserved: result.reserved, available: result.available }
          : item));
        this.inventoryStatus.set('ready');
        this.inventoryMessage.set('Ajuste guardado. El historial conserva el motivo y el usuario.');
        this.adjustmentQuantity.set('');
        if (this.selectedPriceListId() && !this.isDemoPriceList())
          this.loadProducts(this.selectedPriceListId(), this.activeCategory());
      },
      error: (error: HttpErrorResponse) => {
        this.inventoryStatus.set('ready');
        this.inventoryMessage.set(error.status === 409
          ? 'El ajuste dejaría menos stock que el reservado por tickets abiertos.'
          : 'No se pudo guardar el ajuste. Revisá los datos e intentá de nuevo.');
      },
    });
  }

  protected closeInventory(): void {
    this.inventoryOpen.set(false);
    this.inventoryMessage.set(null);
  }

  protected closeProductDialog(): void {
    this.selectedProduct.set(null);
    this.errorMessage.set(null);
  }

  protected continueToCheckout(): void {
    if (this.isDemoPriceList()) {
      this.errorMessage.set(null);
      this.checkoutNotice.set(true);
      return;
    }
    if (this.lines().length === 0) {
      this.errorMessage.set(null);
      this.queueDraftOperation('cancel');
      return;
    }
    if (this.draftStatus() === 'error') {
      this.errorMessage.set(this.errorMessage() ?? 'No se pudo guardar el ticket. Revisá el detalle y reintentá el guardado.');
      return;
    }
    if (this.draftStatus() !== 'saved' || !this.draftId()) {
      this.errorMessage.set('Esperá a que el ticket termine de guardarse antes de cobrar.');
      return;
    }
    this.errorMessage.set(null);
    if (!this.cashierShift()) {
      this.shiftDialog.set(true);
      this.shiftError.set('Abrí tu turno de caja para poder cobrar esta venta.');
      return;
    }
    this.selectedPayments.set([{ method: 'cash', amount: this.subtotal() }]);
    this.checkoutError.set(null);
    this.checkoutNotice.set(true);
  }

  protected retryDraftSave(): void {
    if (this.lines().length > 0) this.persistCurrentDraft();
  }

  private categoryIcon(name: string): string {
    const normalizedName = name.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLocaleLowerCase('es-AR');
    if (normalizedName.includes('vacuno') || normalizedName.includes('res')) return '🐄';
    if (normalizedName.includes('cerdo') || normalizedName.includes('porcino')) return '🐖';
    if (normalizedName.includes('pollo') || normalizedName.includes('ave')) return '🐓';
    if (normalizedName.includes('almacen')) return '🧺';
    return '▪';
  }

  private draftSaveErrorMessage(error: HttpErrorResponse): string {
    const code = error.error?.error?.code;
    if (code === 'STOCK_RESERVATION_MISSING') return 'La reserva de stock cambió. Actualizá el ticket e intentá nuevamente.';
    if (code === 'PRICE_LIST_NOT_AVAILABLE') return 'La lista de precios ya no está habilitada para esta sucursal.';
    if (code === 'PRODUCT_PRICE_NOT_AVAILABLE') return 'Hay un producto sin precio vigente en esta lista.';
    if (code === 'PRODUCT_NOT_AVAILABLE') return 'Uno de los productos ya no está disponible para la venta.';
    return error.status === 0
      ? 'No hay conexión con el servidor. El detalle se conserva; podés reintentar.'
      : `No se pudo guardar el ticket (HTTP ${error.status}). El detalle se conserva; podés reintentar.`;
  }

  protected updateOpeningCash(event: Event): void {
    this.shiftOpeningCash.set((event.target as HTMLInputElement).value.replace(',', '.'));
  }

  protected openShiftDialog(): void {
    this.shiftDialog.set(true);
    this.shiftError.set(null);
    this.loadCashierShift();
    if (!this.cashierShift()) this.loadLastClosedShift();
  }

  protected openCashierShift(): void {
    const amount = Number(this.shiftOpeningCash());
    if (!Number.isFinite(amount) || amount < 0 || amount > 9_999_999_999.99 || Math.round(amount * 100) !== amount * 100) {
      this.shiftError.set('Ingresá un fondo inicial válido (puede ser $0).');
      return;
    }
    this.shiftBusy.set(true);
    this.shiftError.set(null);
    this.cashierShiftClient.open(amount).subscribe({
      next: (shift) => { this.cashierShift.set(shift); this.shiftBusy.set(false); this.shiftDialog.set(false); },
      error: (error: HttpErrorResponse) => {
        this.shiftBusy.set(false);
        this.shiftError.set(error.status === 409 ? 'Ya hay un turno abierto para tu usuario y esta sucursal.' : 'No se pudo abrir el turno. Revisá la conexión e intentá de nuevo.');
        if (error.status === 409) this.loadCashierShift();
      },
    });
  }

  protected closeCashierShift(): void {
    if (this.lines().length > 0) {
      this.shiftError.set('Finalizá o cancelá el ticket antes de cerrar el turno.');
      return;
    }
    this.shiftBusy.set(true);
    this.shiftError.set(null);
    this.cashierShiftClient.close().subscribe({
      next: (closedShift) => {
        this.cashierShift.set(null);
        this.lastClosedShift.set(closedShift);
        this.shiftBusy.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.shiftBusy.set(false);
        this.shiftError.set(error.status === 409 ? 'No hay un turno abierto para cerrar.' : 'No se pudo cerrar el turno. Intentá de nuevo.');
      },
    });
  }

  protected togglePayment(method: SalePaymentMethod, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    this.selectedPayments.update((payments) => checked
      ? [...payments, { method, amount: method === 'cash' ? this.subtotal() : Math.max(0, this.subtotal() - payments.reduce((sum, item) => sum + item.amount, 0)) }]
      : payments.filter((payment) => payment.method !== method));
    this.checkoutError.set(null);
  }

  protected updatePaymentAmount(method: SalePaymentMethod, event: Event): void {
    const amount = Number((event.target as HTMLInputElement).value.replace(',', '.'));
    this.selectedPayments.update((payments) => payments.map((payment) => payment.method === method ? { ...payment, amount } : payment));
    this.checkoutError.set(null);
  }

  protected paymentAmount(method: SalePaymentMethod): number {
    return this.selectedPayments().find((payment) => payment.method === method)?.amount ?? 0;
  }

  protected hasPayment(method: SalePaymentMethod): boolean {
    return this.selectedPayments().some((payment) => payment.method === method);
  }

  protected confirmSale(): void {
    if (!this.draftId() || !this.cashierShift() || this.checkoutBusy()) return;
    const problem = this.paymentProblem();
    if (problem) {
      this.checkoutError.set(problem);
      return;
    }
    const payments = this.selectedPayments();
    this.checkoutBusy.set(true);
    this.checkoutError.set(null);
    this.saleDraftClient.confirm(this.draftId(), payments).subscribe({
      next: (sale) => this.finishConfirmedSale(sale),
      error: (error: HttpErrorResponse) => {
        this.checkoutBusy.set(false);
        this.checkoutError.set(error.status === 409 || error.status === 400
          ? this.checkoutConflictMessage(error)
          : 'No se pudo confirmar la venta. El ticket sigue guardado; revisá e intentá de nuevo.');
        if (error.status === 409) { this.loadCashierShift(); this.loadPriceLists(); }
      },
    });
  }

  protected closeCheckout(): void {
    if (this.checkoutBusy()) return;
    this.checkoutNotice.set(false);
    this.checkoutError.set(null);
  }

  private finishConfirmedSale(sale: ConfirmedSale): void {
    this.checkoutBusy.set(false);
    this.checkoutNotice.set(false);
    this.confirmedSale.set(sale);
    this.lines.set([]);
    this.persistedLines.set([]);
    this.draftId.set('');
    this.draftStatus.set('saved');
    this.selectedPayments.set([]);
    this.errorMessage.set(null);
    this.loadCashierShift();
    this.loadPriceLists();
  }

  protected paymentMethodName(method: SalePaymentMethod): string {
    return this.paymentMethods.find((item) => item.id === method)?.label ?? method;
  }

  private checkoutConflictMessage(error: HttpErrorResponse): string {
    const code = error.error?.error?.code;
    if (code === 'CASHIER_SHIFT_REQUIRED') return 'El turno ya no está abierto. Abrí un nuevo turno para continuar.';
    if (code === 'STOCK_RESERVATION_MISSING') return 'El stock reservado cambió. Actualizá el ticket antes de volver a cobrar.';
    if (code === 'PAYMENT_TOTAL_MISMATCH') return 'Los medios de pago no alcanzan a cubrir el total.';
    if (code === 'INVALID_CHANGE') return 'El vuelto solo puede entregarse cuando se paga en efectivo.';
    if (code === 'IDEMPOTENCY_CONFLICT') return 'La venta ya fue confirmada con otro pago. Actualizá el estado antes de continuar.';
    return 'El ticket cambió o ya se procesó. Revisá el estado de la venta e intentá de nuevo.';
  }

  private loadCashierShift(): void {
    this.cashierShiftClient.current().subscribe({
      next: (shift) => this.cashierShift.set(shift),
      error: () => this.shiftError.set('No se pudo consultar tu turno de caja.'),
    });
  }

  private loadLastClosedShift(): void {
    this.cashierShiftClient.lastClosed().subscribe({
      next: (shift) => this.lastClosedShift.set(shift),
      error: () => this.shiftError.set('No se pudo consultar el último turno cerrado.'),
    });
  }

  canDeactivate(): boolean {
    return this.lines().length === 0 || (!this.isDemoPriceList() && this.draftStatus() === 'saved') ||
      window.confirm(this.isDemoPriceList()
        ? 'Hay productos en la venta de prueba. Si salís ahora se perderán. ¿Querés continuar?'
        : 'El ticket todavía no terminó de guardarse. ¿Querés salir de todos modos?');
  }

  protected signOut(): void {
    if (!this.canDeactivate()) return;
    this.sessionClient.logout().subscribe({
      next: () => void this.router.navigateByUrl('/'),
      error: (error: HttpErrorResponse) => {
        if (error.status === 401) void this.router.navigateByUrl('/');
        else this.errorMessage.set('No se pudo cerrar la sesión. Intentá de nuevo.');
      },
    });
  }

  protected formatMoney(value: number): string {
    return value.toLocaleString('es-AR', { style: 'currency', currency: 'ARS' });
  }

  protected formatQuantity(line: SaleLine): string {
    return line.product.mode === 'weight'
      ? `${line.quantity.toLocaleString('es-AR', { minimumFractionDigits: 3, maximumFractionDigits: 3 })} kg`
      : `${line.quantity.toLocaleString('es-AR')} ${this.unitLabel(line.product)}`;
  }

  protected unitLabel(product: PosProduct): string {
    return product.mode === 'weight' ? 'kg' : product.unit || 'unidad';
  }

  protected availableStock(product: PosProduct): number | undefined {
    return this.stockByProduct().get(product.id) ?? product.availableStock;
  }

  private refreshStock(): void {
    const revision = ++this.stockRequestRevision;
    this.inventoryClient.stock().subscribe({
      next: (items) => {
        if (revision === this.stockRequestRevision)
          this.stockByProduct.set(new Map(items.map((item) => [item.productId, item.available])));
      },
      error: () => {
        if (revision === this.stockRequestRevision) this.stockByProduct.set(new Map());
      },
    });
  }

  protected lineTotal(line: SaleLine): number {
    return Math.round(line.product.price * line.quantity * 100) / 100;
  }

  private roundQuantity(value: number): number {
    return Math.round(value * 1000) / 1000;
  }

  private addProductLine(product: PosProduct, quantity: number): void {
    this.lines.update((lines) => {
      const existing = lines.find((line) => line.product.id === product.id);
      if (!existing) return [...lines, { product, quantity }];
      return lines.map((line) => line.product.id === product.id
        ? { ...line, quantity: this.roundQuantity(line.quantity + quantity) }
        : line);
    });
    this.persistCurrentDraft();
  }

  private persistCurrentDraft(): void {
    if (!this.selectedPriceListId() || this.isDemoPriceList()) {
      this.draftStatus.set('demo');
      return;
    }
    if (this.lines().length === 0) return;
    this.queueDraftOperation({ priceListId: this.selectedPriceListId(), lines: this.lines() });
  }

  private queueDraftOperation(operation: 'cancel' | { readonly priceListId: string; readonly lines: readonly SaleLine[] }): void {
    this.draftStatus.set('saving');
    this.draftOperations.next({ revision: ++this.draftRevision, operation });
  }

  private loadSavedDraft(): void {
    this.draftStatus.set('loading');
    this.saleDraftClient.current().subscribe({
      next: (draft) => {
        if (!draft) {
          this.draftId.set('');
          this.draftStatus.set('saved');
          return;
        }
        this.draftId.set(draft.id);
        this.selectedPriceListId.set(draft.priceListId);
        this.lines.set(draft.lines.map((item: SaleDraftLine) => ({
          product: {
            id: item.productId,
            code: item.productCode,
            name: item.productName,
            category: '',
            price: item.unitPrice,
            mode: item.saleMode,
            unit: item.unit,
          },
          quantity: item.quantity,
        })));
        this.persistedLines.set(this.lines());
        this.draftStatus.set('saved');
        if (this.realPriceLists().some((item) => item.id === draft.priceListId))
          this.loadCategories(draft.priceListId);
      },
      error: () => this.draftStatus.set('error'),
    });
  }
}
