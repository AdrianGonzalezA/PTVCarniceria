import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, ElementRef, inject, HostListener, OnInit, signal, ViewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { Observable, of, Subject } from 'rxjs';
import { catchError, concatMap, map } from 'rxjs/operators';
import { CatalogCategory, CatalogClient, PriceListOption } from '../../core/catalog/catalog-client';
import { InventoryClient, InventoryStockItem } from '../../core/inventory/inventory-client';
import { InventoryPieceClient, PosPieceLookup } from '../../core/inventory/inventory-piece-client';
import { PosTerminal, PosTerminalClient } from '../../core/pos/pos-terminal-client';
import { PosDeviceClient, SerialPrintResult } from '../../core/pos/pos-device-client';
import { ConfirmedSale, SaleDocumentType, SaleDraft, SaleDraftClient, SaleDraftLine, SalePaymentMethod, SaleRecipientTaxStatus, SaleTicketSlot } from '../../core/sales/sale-draft-client';
import { ReceiptPdfClient } from '../../core/sales/receipt-pdf-client';
import { AuthorizedFiscalDocument, FiscalDocumentClient } from '../../core/sales/fiscal-document-client';
import { CashierShift, CashierShiftClient } from '../../core/sales/cashier-shift-client';
import { CurrentSession, SessionClient } from '../../core/session/session-client';
import { CreditCustomerAccount, CreditCustomerClient, CreditCustomerOption } from '../../core/customers/credit-customer-client';
import { AccountCollectionDialog } from './account-collection-dialog';

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
  readonly id: string;
  readonly product: PosProduct;
  readonly quantity: number;
  readonly inventoryPieceId?: string;
  readonly pieceIdentifier?: string;
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
  imports: [RouterLink, AccountCollectionDialog],
  templateUrl: './pos-page.html',
})
export class PosPage implements OnInit {
  private readonly sessionClient = inject(SessionClient);
  private readonly catalogClient = inject(CatalogClient);
  private readonly saleDraftClient = inject(SaleDraftClient);
  private readonly receiptPdfClient = inject(ReceiptPdfClient);
  private readonly fiscalDocumentClient = inject(FiscalDocumentClient);
  private readonly posDeviceClient = inject(PosDeviceClient);
  private readonly cashierShiftClient = inject(CashierShiftClient);
  private readonly inventoryClient = inject(InventoryClient);
  private readonly pieceClient = inject(InventoryPieceClient);
  private readonly terminalClient = inject(PosTerminalClient);
  private readonly creditCustomerClient = inject(CreditCustomerClient);
  private readonly router = inject(Router);
  @ViewChild('scanInput') private scanInput?: ElementRef<HTMLInputElement>;
  private readonly draftOperations = new Subject<{
    readonly revision: number;
    readonly slot: SaleTicketSlot;
    readonly operation: 'cancel' | { readonly priceListId: string; readonly lines: readonly SaleLine[];
      readonly discountAmount: number; readonly discountReason: string | null };
  }>();
  private draftRevision = 0;
  private ticketSlotsRequestRevision = 0;
  private ticketViewRevision = 0;
  private stockRequestRevision = 0;
  private scaleRequestRevision = 0;
  private scannedCode = '';
  private lastScanKeyAt = 0;

  protected readonly session = signal<CurrentSession | null>(null);
  protected readonly terminal = signal<PosTerminal | null>(null);
  protected readonly terminalError = signal<string | null>(null);
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
  protected readonly scannedPieceIdentifier = signal<string | null>(null);
  private readonly scannedPieceId = signal<string | null>(null);
  protected readonly quantityDraft = signal('1');
  protected readonly scaleBusy = signal(false);
  protected readonly scaleNotice = signal<string | null>(null);
  protected readonly scaleError = signal<string | null>(null);
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
  protected readonly shiftLoadState = signal<'loading' | 'ready' | 'error'>('loading');
  protected readonly canOperate = computed(() => !!this.terminal() && !!this.cashierShift());
  protected readonly lastClosedShift = signal<CashierShift | null>(null);
  protected readonly shiftDialog = signal(false);
  protected readonly shiftOpeningCash = signal('0');
  protected readonly shiftBusy = signal(false);
  protected readonly shiftError = signal<string | null>(null);
  protected readonly draftId = signal('');
  protected readonly ticketSlots: readonly SaleTicketSlot[] = ['A', 'B', 'C', 'D'];
  protected readonly activeTicketSlot = signal<SaleTicketSlot>('A');
  protected readonly savedTicketSlots = signal<ReadonlySet<SaleTicketSlot>>(new Set());
  protected readonly ticketSlotsLoadState = signal<'idle' | 'loading' | 'ready' | 'error'>('idle');
  protected readonly checkoutBusy = signal(false);
  protected readonly checkoutError = signal<string | null>(null);
  protected readonly documentType = signal<SaleDocumentType>('nonFiscalTicket');
  protected readonly recipientTaxStatus = signal<SaleRecipientTaxStatus>('finalConsumer');
  protected readonly recipientName = signal('');
  protected readonly recipientDocumentNumber = signal('');
  protected readonly recipientAddress = signal('');
  protected readonly documentProblem = computed(() => {
    if (this.documentType() === 'nonFiscalTicket') return null;
    const name = this.recipientName().trim();
    const address = this.recipientAddress().trim();
    const document = this.recipientDocumentNumber().replace(/[- ]/g, '');
    if (this.recipientTaxStatus() !== 'finalConsumer') {
      if (!name || !address || !document)
        return 'Para este cliente necesitás nombre o razón social, CUIT y domicilio.';
      if (!this.validCuit(document)) return 'Ingresá un CUIT válido para el comprobante fiscal.';
    } else if (this.subtotal() >= 10_000_000 && !document) {
      return 'Por el importe de esta venta necesitás identificar al consumidor final con DNI o CUIT.';
    } else if (document && !(/^\d{7,8}$/.test(document) || this.validCuit(document))) {
      return 'Ingresá un DNI o CUIT válido para el consumidor final.';
    }
    if (name.length > 200 || address.length > 200) return 'Los datos del cliente son demasiado extensos.';
    return null;
  });
  protected readonly confirmedSale = signal<ConfirmedSale | null>(null);
  protected readonly fiscalDocument = signal<AuthorizedFiscalDocument | null>(null);
  protected readonly fiscalBusy = signal(false);
  protected readonly fiscalError = signal<string | null>(null);
  protected readonly receiptPdfBusy = signal(false);
  protected readonly receiptPdfPath = signal<string | null>(null);
  protected readonly receiptPdfError = signal<string | null>(null);
  protected readonly serialPrintBusy = signal(false);
  protected readonly serialPrintResult = signal<SerialPrintResult | null>(null);
  protected readonly serialPrintError = signal<string | null>(null);
  protected readonly selectedPayments = signal<readonly { method: SalePaymentMethod; amount: number }[]>([]);
  protected readonly creditCustomerOptions = signal<readonly CreditCustomerOption[]>([]);
  protected readonly creditCustomerSearch = signal('');
  protected readonly creditCustomerId = signal('');
  protected readonly creditCustomerAccount = signal<CreditCustomerAccount | null>(null);
  protected readonly creditCustomerAccountStatus = signal<'idle' | 'loading' | 'error'>('idle');
  protected readonly creditCustomerLoading = signal(false);
  protected readonly creditCustomerLoadError = signal<string | null>(null);
  protected readonly accountChargeDraft = signal('0');
  protected readonly discountDraft = signal('0');
  protected readonly discountReason = signal('');
  private readonly persistedDiscountAmount = signal(0);
  private readonly persistedDiscountReason = signal('');
  protected readonly discountAmount = computed(() => Number(this.discountDraft().replace(',', '.')));
  protected readonly canDiscount = computed(() =>
    !!this.session()?.context?.permissions.includes('pos.discount.apply'));
  protected readonly accountChargeAmount = computed(() => Number(this.accountChargeDraft().replace(',', '.')));
  protected readonly creditAppliedDraft = signal('0');
  protected readonly creditAppliedAmount = computed(() => Number(this.creditAppliedDraft().replace(',', '.')));
  protected readonly dueNow = computed(() => this.subtotal() - this.accountChargeAmount() - this.creditAppliedAmount());
  protected readonly canChargeToAccount = computed(() =>
    !!this.session()?.context?.permissions.includes('pos.account.charge'));
  protected readonly collectionDialog = signal(false);
  protected readonly collectionScopeKey = computed(() => [this.session()?.userId,
    this.session()?.context?.companyId, this.session()?.context?.branchId, this.terminal()?.id].join(':'));
  protected readonly paymentMethods: readonly { readonly id: SalePaymentMethod; readonly label: string }[] = [
    { id: 'cash', label: 'Efectivo' }, { id: 'debit', label: 'Débito' },
    { id: 'credit', label: 'Crédito' }, { id: 'transfer', label: 'Transferencia' },
    { id: 'mercadoPago', label: 'Mercado Pago' }, { id: 'cheque', label: 'Cheque' },
  ];
  protected readonly changePreview = computed(() => {
    const selected = this.selectedPayments();
    const cash = selected.find((payment) => payment.method === 'cash')?.amount ?? 0;
    const nonCash = selected.filter((payment) => payment.method !== 'cash').reduce((sum, payment) => sum + payment.amount, 0);
    const dueNow = this.dueNow();
    return nonCash > dueNow ? 0 : Math.max(0, Math.round((cash + nonCash - dueNow) * 100) / 100);
  });
  protected readonly paymentBalance = computed(() => Math.max(0, Math.round((this.dueNow() -
    this.selectedPayments().reduce((sum, payment) => sum + payment.amount, 0)) * 100) / 100));
  protected readonly paymentProblem = computed(() => {
    const accountCharge = this.accountChargeAmount();
    if (!Number.isFinite(accountCharge) || accountCharge < 0 || accountCharge > this.subtotal() ||
        Math.abs(Math.round(accountCharge * 100) - accountCharge * 100) > 0.000001)
      return 'Ingresá un importe válido a cuenta corriente, sin superar el total.';
    const creditApplied = this.creditAppliedAmount();
    if (!Number.isFinite(creditApplied) || creditApplied < 0 ||
        Math.abs(Math.round(creditApplied * 100) - creditApplied * 100) > 0.000001 ||
        accountCharge + creditApplied > this.subtotal())
      return 'El saldo a favor aplicado no es válido o supera el total.';
    if (creditApplied > (this.creditCustomerAccount()?.creditAvailable ?? 0))
      return 'El saldo a favor aplicado supera el disponible del cliente.';
    if ((accountCharge > 0 || creditApplied > 0) &&
        (!this.canChargeToAccount() || !this.creditCustomerId() || !this.creditCustomerAccount()))
      return 'Elegí un cliente habilitado para cuenta corriente.';
    const payments = this.selectedPayments();
    if (payments.length === 0 && this.dueNow() !== 0) return 'Seleccioná al menos un medio de pago.';
    if (payments.some((payment) => !Number.isFinite(payment.amount) || payment.amount <= 0 ||
        Math.round(payment.amount * 100) !== payment.amount * 100))
      return 'Completá cada medio de pago con un importe válido.';
    const nonCash = payments.filter((payment) => payment.method !== 'cash')
      .reduce((sum, payment) => sum + payment.amount, 0);
    if (nonCash > this.dueNow()) return 'Los medios sin efectivo no pueden superar el importe a cobrar ahora.';
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
  protected readonly grossSubtotal = computed(() => this.lines().reduce((total, line) => total + this.lineTotal(line), 0));
  protected readonly subtotal = computed(() => Math.round((this.grossSubtotal() - this.discountAmount()) * 100) / 100);
  protected readonly discountProblem = computed(() => {
    const amount = this.discountAmount();
    if (!Number.isFinite(amount) || amount < 0 ||
        Math.abs(Math.round(amount * 100) - amount * 100) > 0.000001 ||
        (amount > 0 && amount >= this.grossSubtotal()))
      return 'El descuento debe ser menor al subtotal y tener hasta dos decimales.';
    if (amount > 0 && !this.canDiscount()) return 'Tu rol no permite aplicar descuentos.';
    if (amount > 0 && this.discountReason().trim().length < 10)
      return 'Indicá un motivo de al menos 10 caracteres para el descuento.';
    if (this.discountReason().trim().length > 200)
      return 'El motivo del descuento no puede superar 200 caracteres.';
    return null;
  });
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
      concatMap(({ revision, slot, operation }) => {
        const request: Observable<SaleDraft | void> = operation === 'cancel'
          ? this.saleDraftClient.cancel(slot)
          : this.saleDraftClient.save(operation.priceListId, operation.lines.map((line) => ({
              productId: line.product.id,
              quantity: line.quantity,
              ...(line.inventoryPieceId ? { inventoryPieceId: line.inventoryPieceId } : {}),
            })), slot, operation.discountAmount, operation.discountReason);
        return request.pipe(
          map((result) => ({ revision, slot, operation, result, error: null as HttpErrorResponse | null })),
          catchError((error: HttpErrorResponse) => of({ revision, slot, operation, result: null, error })),
        );
      }),
      takeUntilDestroyed(),
    ).subscribe(({ revision, slot, operation, result, error }) => {
      if (slot !== this.activeTicketSlot()) return;
      if (error) {
        if (revision === this.draftRevision) {
          if (operation === 'cancel') this.lines.set(this.persistedLines());
          if (['INSUFFICIENT_STOCK', 'PIECE_ALREADY_USED', 'PIECE_NOT_AVAILABLE'].includes(error.error?.error?.code)) {
            this.lines.set(this.persistedLines());
            this.discountDraft.set(String(this.persistedDiscountAmount()));
            this.discountReason.set(this.persistedDiscountReason());
            this.draftStatus.set('saved');
            this.errorMessage.set(error.error?.error?.code === 'INSUFFICIENT_STOCK'
              ? 'No hay stock suficiente. El producto o la cantidad rechazada no se agregó al ticket. Revisá las existencias.'
              : 'Esta pieza ya no está disponible. No se agregó al ticket.');
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
        this.persistedDiscountAmount.set(0);
        this.persistedDiscountReason.set('');
      } else {
        const savedDraft = result as SaleDraft;
        this.draftId.set(savedDraft.id);
        this.persistedDiscountAmount.set(savedDraft.discountAmount ?? 0);
        this.persistedDiscountReason.set(savedDraft.discountReason ?? '');
        this.lines.update((currentLines) => currentLines.map((line) => {
          const savedLine = savedDraft.lines.find((item) => item.productId === line.product.id &&
            (item.inventoryPieceId ?? null) === (line.inventoryPieceId ?? null));
          return savedLine ? { ...line, product: { ...line.product, price: savedLine.unitPrice } } : line;
        }));
        this.persistedLines.set(savedDraft.lines.map((item) => ({
          id: item.id,
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
          ...(item.inventoryPieceId ? { inventoryPieceId: item.inventoryPieceId } : {}),
          ...(item.pieceIdentifier ? { pieceIdentifier: item.pieceIdentifier } : {}),
        })));
      }
      if (revision === this.draftRevision) {
        this.draftStatus.set('saved');
        this.errorMessage.set(null);
        this.refreshStock();
      }
      this.loadTicketSlots();
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
        this.terminalClient.current().subscribe({
          next: (terminal) => {
            if (terminal.companyId !== session.context?.companyId ||
                terminal.branchId !== session.context?.branchId) {
              this.terminalError.set('La caja no corresponde a la sucursal seleccionada. Volvé a identificarte.');
              return;
            }
            this.terminal.set(terminal);
            this.loadCashierShift();
          },
          error: (error: HttpErrorResponse) => this.terminalError.set(error.status === 401
            ? 'Este Electron no tiene una credencial vigente para la caja. Abrí el perfil de la caja o renová su credencial desde administración.'
            : 'No se pudo verificar la caja. Revisá la conexión con el servidor.'),
        });
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

  @HostListener('window:keydown', ['$event'])
  protected captureScannerKeys(event: KeyboardEvent): void {
    if (!this.canOperate() || !this.canSearchCatalog() || this.selectedProduct() ||
        this.checkoutNotice() || this.shiftDialog() || this.inventoryOpen() || this.confirmedSale()) {
      this.scannedCode = '';
      return;
    }
    if (event.target instanceof HTMLElement &&
        event.target.closest('input, textarea, select, [contenteditable="true"]')) return;
    if (Date.now() - this.lastScanKeyAt > 1500) this.scannedCode = '';
    if (/^[0-9]$/.test(event.key)) {
      this.scannedCode = (this.scannedCode + event.key).slice(0, 80);
      this.lastScanKeyAt = Date.now();
    } else if (event.key === 'Enter' && this.scannedCode) {
      event.preventDefault();
      this.searchText.set(this.scannedCode);
      this.scannedCode = '';
      this.submitSearch(event);
    } else if (event.key !== 'Shift') {
      this.scannedCode = '';
    }
  }

  protected selectPriceList(event: Event): void {
    if (this.lines().length > 0) return;
    const priceListId = (event.target as HTMLSelectElement).value;
    this.selectedPriceListId.set(priceListId);
    this.searchText.set('');
    this.selectedProduct.set(null);
    this.scannedPieceIdentifier.set(null);
    this.scannedPieceId.set(null);
    this.errorMessage.set(null);
    this.realSearchSubmitted.set(false);
    this.realProducts.set([]);
    if (priceListId.startsWith('demo:')) {
      this.catalogCategories.set([]);
      this.catalogLoadState.set('ready');
      this.focusScanInput();
    } else if (priceListId) {
      this.loadCategories(priceListId);
    } else {
      this.catalogCategories.set([]);
      this.catalogLoadState.set('idle');
    }
  }

  protected submitSearch(event: Event): void {
    event.preventDefault();
    if (!this.canOperate()) return;
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
    if (!this.canOperate()) return;
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
    const viewRevision = this.ticketViewRevision;
    this.catalogLoadState.set('loading');
    this.catalogClient.products({ priceListId, code: query }).subscribe({
      next: (page) => {
        if (this.selectedPriceListId() !== priceListId || viewRevision !== this.ticketViewRevision) return;
        if (page.totalItems === 1) {
          const product = this.mapCatalogProduct(page.items[0]);
          this.realProducts.set([product]);
          this.catalogLoadState.set('ready');
          this.searchText.set('');
          this.openProduct(product);
          return;
        }
        if (page.totalItems === 0 && /^\d+$/.test(query)) {
          this.searchReceivedPiece(priceListId, query, viewRevision);
          return;
        }
        if (page.totalItems > 0 || query.length < 3) {
          this.setRealProducts(page.items);
          return;
        }
        this.catalogClient.products({ priceListId, q: query }).subscribe({
          next: (searchPage) => {
            if (this.selectedPriceListId() === priceListId && viewRevision === this.ticketViewRevision)
              this.setRealProducts(searchPage.items);
          },
          error: () => {
            if (this.selectedPriceListId() === priceListId && viewRevision === this.ticketViewRevision)
              this.catalogLoadState.set('error');
          },
        });
      },
      error: () => {
        if (this.selectedPriceListId() === priceListId && viewRevision === this.ticketViewRevision)
          this.catalogLoadState.set('error');
      },
    });
  }

  private searchReceivedPiece(priceListId: string, barcode: string, viewRevision: number): void {
    this.pieceClient.lookup(barcode).subscribe({
      next: (piece) => {
        if (this.selectedPriceListId() !== priceListId || viewRevision !== this.ticketViewRevision) return;
        if (!this.validPieceLookup(piece, barcode)) {
          this.pieceLookupError('La etiqueta recibida contiene un peso inválido.');
          return;
        }
        this.catalogClient.products({ priceListId, code: piece.productCode }).subscribe({
          next: (page) => {
            if (this.selectedPriceListId() !== priceListId || viewRevision !== this.ticketViewRevision) return;
            const item = page.items[0];
            if (page.totalItems !== 1 || !item || item.id !== piece.productId || item.saleMode !== 'weight') {
              this.pieceLookupError('El artículo de esta pieza no está disponible en la lista de precios.');
              return;
            }
            const product = this.mapCatalogProduct(item);
            this.realProducts.set([product]);
            this.catalogLoadState.set('ready');
            this.searchText.set('');
            this.openProduct(product, piece.receivedWeightKg, piece.externalIdentifier, piece.id);
          },
          error: () => {
            if (viewRevision === this.ticketViewRevision)
              this.pieceLookupError('No se pudo consultar el artículo de la pieza.');
          },
        });
      },
      error: (error: HttpErrorResponse) => {
        if (viewRevision !== this.ticketViewRevision) return;
        this.pieceLookupError(error.status === 409
          ? error.error?.error?.code === 'PIECE_ALREADY_SOLD'
            ? 'Esta pieza ya fue vendida y no se puede agregar a otro ticket.'
            : 'Hay más de una pieza con esa etiqueta en la sucursal. Revisá el origen antes de vender.'
          : error.status === 404
            ? 'El código no corresponde a un artículo ni a una pieza recibida en esta sucursal.'
            : 'No se pudo consultar la pieza. Intentá nuevamente.');
      },
    });
  }

  private validPieceLookup(piece: PosPieceLookup, barcode: string): boolean {
    const weight = piece.receivedWeightKg;
    return piece.rawBarcode === barcode && !!piece.productCode &&
      Number.isFinite(weight) && weight > 0 && weight <= 10000 &&
      Math.abs(Math.round(weight * 1000) - weight * 1000) < 0.000001;
  }

  private pieceLookupError(message: string): void {
    this.catalogLoadState.set('ready');
    this.realProducts.set([]);
    this.errorMessage.set(message);
  }

  private loadCategories(priceListId: string): void {
    const viewRevision = this.ticketViewRevision;
    this.catalogLoadState.set('loading');
    this.catalogCategories.set([]);
    this.catalogClient.categories(priceListId).subscribe({
      next: (items) => {
        if (this.selectedPriceListId() !== priceListId || viewRevision !== this.ticketViewRevision) return;
        this.catalogCategories.set(items);
        this.catalogLoadState.set('ready');
        const firstCategory = items[0]?.id;
        this.activeCategory.set(firstCategory ?? '');
        this.realProducts.set([]);
        if (firstCategory) this.loadProducts(priceListId, firstCategory);
      },
      error: () => {
        if (this.selectedPriceListId() === priceListId && viewRevision === this.ticketViewRevision)
          this.catalogLoadState.set('error');
      },
    });
  }

  private loadProducts(priceListId: string, categoryId?: string): void {
    const viewRevision = this.ticketViewRevision;
    this.catalogLoadState.set('loading');
    this.catalogClient.products({ priceListId, ...(categoryId ? { categoryId } : {}) }).subscribe({
      next: (page) => {
        if (this.selectedPriceListId() === priceListId && viewRevision === this.ticketViewRevision &&
            this.activeCategory() === (categoryId ?? ''))
          this.setRealProducts(page.items);
      },
      error: () => {
        if (this.selectedPriceListId() === priceListId && viewRevision === this.ticketViewRevision)
          this.catalogLoadState.set('error');
      },
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
    this.focusScanInput();
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

  protected openProduct(product: PosProduct, quantity = 1, pieceIdentifier: string | null = null,
    pieceId: string | null = null): void {
    if (!this.canOperate()) return;
    this.errorMessage.set(null);
    if (!this.isDemoPriceList() && (this.availableStock(product) ?? 0) <= 0) {
      this.errorMessage.set('No hay stock disponible para este producto en la sucursal.');
      return;
    }
    this.selectedProduct.set(product);
    this.scaleRequestRevision++;
    this.scannedPieceIdentifier.set(pieceIdentifier);
    this.scannedPieceId.set(pieceId);
    this.quantityDraft.set(String(quantity));
    this.scaleNotice.set(null);
    this.scaleError.set(null);
  }

  protected updateQuantity(event: Event): void {
    if (this.scannedPieceIdentifier()) {
      (event.target as HTMLInputElement).value = this.quantityDraft();
      return;
    }
    this.quantityDraft.set((event.target as HTMLInputElement).value.replace(',', '.'));
    this.scaleNotice.set(null);
  }

  protected readSerialScale(): void {
    if (this.scaleBusy() || this.selectedProduct()?.mode !== 'weight' || this.scannedPieceIdentifier()) return;
    const productId = this.selectedProduct()?.id;
    const revision = ++this.scaleRequestRevision;
    this.scaleBusy.set(true);
    this.scaleError.set(null);
    this.scaleNotice.set(null);
    void this.posDeviceClient.readScale().then((weightKg) => {
      if (this.selectedProduct()?.id !== productId || this.scaleRequestRevision !== revision) return;
      this.quantityDraft.set(String(weightKg));
      this.scaleNotice.set(`Balanza COM6: ${weightKg.toLocaleString('es-AR', { minimumFractionDigits: 3, maximumFractionDigits: 3 })} kg`);
    }).catch(() => {
      if (this.selectedProduct()?.id === productId && this.scaleRequestRevision === revision)
        this.scaleError.set('No hay una lectura estable y reciente en COM6. Enviá ST,peso,kg desde COM5 o ingresá el peso manualmente.');
    }).finally(() => this.scaleBusy.set(false));
  }

  protected addSelectedProduct(): void {
    if (!this.canOperate()) return;
    const product = this.selectedProduct();
    const quantity = Number(this.quantityDraft());
    if (!product || this.quantityProblem()) {
      this.errorMessage.set(this.quantityProblem());
      return;
    }

    this.errorMessage.set(null);
    const pieceId = this.scannedPieceId();
    if (pieceId && this.lines().some(line => line.inventoryPieceId === pieceId)) {
      this.errorMessage.set('Esta pieza ya está en el ticket.');
      return;
    }
    const existingQuantity = this.lines().filter(line => line.product.id === product.id)
      .reduce((sum, line) => sum + line.quantity, 0);
    const reservedQuantity = this.persistedLines().filter(line => line.product.id === product.id)
      .reduce((sum, line) => sum + line.quantity, 0);
    const available = this.availableStock(product);
    if (!this.isDemoPriceList() && available !== undefined &&
        existingQuantity + quantity > available + reservedQuantity) {
      this.errorMessage.set('Stock insuficiente para agregar esta pieza al ticket.');
      return;
    }
    this.addProductLine(product, quantity, pieceId, this.scannedPieceIdentifier());
    this.selectedProduct.set(null);
    this.scannedPieceIdentifier.set(null);
    this.scannedPieceId.set(null);
    this.focusScanInput();
  }

  protected updateLineQuantity(lineId: string, event: Event): void {
    if (!this.canOperate()) return;
    const input = event.target as HTMLInputElement;
    const quantity = Number(input.value.replace(',', '.'));
    const line = this.lines().find((item) => item.id === lineId);
    if (line?.inventoryPieceId) {
      input.value = String(line.quantity);
      this.errorMessage.set('El peso de una pieza leída no se puede modificar. Quitá la pieza si no corresponde.');
      return;
    }
    if (!line || !Number.isFinite(quantity) || quantity <= 0 ||
        quantity > 10000 || Math.abs(Math.round(quantity * 1000) - quantity * 1000) > 0.000001 ||
        (line.product.mode === 'unit' && !Number.isInteger(quantity))) {
      this.errorMessage.set('La cantidad o el peso debe ser mayor a cero y válido para el producto.');
      if (line) input.value = String(line.quantity);
      return;
    }
    const available = this.availableStock(line.product);
    const reservedByThisTicket = this.persistedLines().filter(item => item.product.id === line.product.id)
      .reduce((sum, item) => sum + item.quantity, 0);
    const otherQuantity = this.lines().filter(item => item.product.id === line.product.id && item.id !== lineId)
      .reduce((sum, item) => sum + item.quantity, 0);
    if (!this.isDemoPriceList() && available !== undefined && quantity + otherQuantity > available + reservedByThisTicket) {
      this.errorMessage.set(`Stock insuficiente. Disponible para agregar: ${available.toLocaleString('es-AR', { maximumFractionDigits: 3 })} ${this.unitLabel(line.product)}.`);
      input.value = String(line.quantity);
      return;
    }
    this.errorMessage.set(null);
    this.lines.update((lines) => lines.map((item) => item.id === lineId
      ? { ...item, quantity: this.roundQuantity(quantity) }
      : item));
    this.persistCurrentDraft();
  }

  protected removeLine(lineId: string): void {
    if (!this.canOperate()) return;
    const line = this.lines().find((item) => item.id === lineId);
    if (!line || !window.confirm(`¿Querés quitar ${line.product.name} del detalle?`)) return;
    this.lines.update((lines) => lines.filter((item) => item.id !== lineId));
    if (this.lines().length === 0 && !this.isDemoPriceList() && this.selectedPriceListId()) {
      this.discountDraft.set('0');
      this.discountReason.set('');
      this.queueDraftOperation('cancel');
    } else this.persistCurrentDraft();
  }

  protected updateDiscountAmount(event: Event): void {
    this.discountDraft.set((event.target as HTMLInputElement).value);
    if (this.discountAmount() === 0) this.discountReason.set('');
    this.persistCurrentDraft();
  }

  protected updateDiscountReason(event: Event): void {
    this.discountReason.set((event.target as HTMLInputElement).value);
    this.persistCurrentDraft();
  }

  protected cancelSale(): void {
    if (!this.canOperate()) return;
    if (!window.confirm('¿Querés cancelar la venta y quitar todos sus productos?')) return;
    if (!this.isDemoPriceList() && this.selectedPriceListId()) {
      this.queueDraftOperation('cancel');
    }
    this.lines.set([]);
    this.discountDraft.set('0');
    this.discountReason.set('');
    this.checkoutNotice.set(false);
    this.errorMessage.set(null);
  }

  protected canSwitchTicket(): boolean {
    return this.canOperate() && this.priceListLoadState() === 'ready' &&
      (this.draftStatus() === 'saved' || (this.draftStatus() === 'demo' && this.lines().length === 0)) &&
      !this.selectedProduct() && !this.checkoutNotice() && !this.checkoutBusy() &&
      !this.confirmedSale() && !this.shiftDialog() && !this.inventoryOpen();
  }

  protected switchTicket(slot: SaleTicketSlot): void {
    if (slot === this.activeTicketSlot() || !this.canSwitchTicket()) return;
    this.ticketViewRevision++;
    this.activeTicketSlot.set(slot);
    this.rememberActiveTicket(slot);
    this.lines.set([]);
    this.discountDraft.set('0');
    this.discountReason.set('');
    this.persistedLines.set([]);
    this.persistedDiscountAmount.set(0);
    this.persistedDiscountReason.set('');
    this.draftId.set('');
    this.errorMessage.set(null);
    this.searchText.set('');
    this.realSearchSubmitted.set(false);
    this.realProducts.set([]);
    this.selectedPriceListId.set(this.realPriceLists()[0]?.id ?? '');
    if (this.selectedPriceListId()) this.loadCategories(this.selectedPriceListId());
    this.loadSavedDraft();
    this.refreshStock();
    this.focusScanInput();
  }

  protected loadTicketSlots(): void {
    const requestRevision = ++this.ticketSlotsRequestRevision;
    this.ticketSlotsLoadState.set('loading');
    this.saleDraftClient.list().subscribe({
      next: (drafts) => {
        if (requestRevision !== this.ticketSlotsRequestRevision) return;
        this.savedTicketSlots.set(new Set(drafts.map((draft) => draft.ticketSlot)
          .filter((slot) => this.ticketSlots.includes(slot))));
        this.ticketSlotsLoadState.set('ready');
      },
      error: () => {
        if (requestRevision !== this.ticketSlotsRequestRevision) return;
        this.savedTicketSlots.set(new Set());
        this.ticketSlotsLoadState.set('error');
      },
    });
  }

  protected openInventory(): void {
    if (!this.canOperate()) return;
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
    if (!this.canOperate()) return;
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
    this.scaleRequestRevision++;
    this.selectedProduct.set(null);
    this.scannedPieceIdentifier.set(null);
    this.scannedPieceId.set(null);
    this.errorMessage.set(null);
    this.focusScanInput();
  }

  private focusScanInput(): void {
    setTimeout(() => {
      if (this.canOperate() && this.canSearchCatalog() && !this.selectedProduct() &&
          !this.checkoutNotice() && !this.shiftDialog() && !this.inventoryOpen() && !this.confirmedSale())
        this.scanInput?.nativeElement.focus();
    }, 0);
  }

  protected continueToCheckout(): void {
    if (!this.canOperate()) return;
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
    if (this.discountProblem()) {
      this.errorMessage.set(this.discountProblem());
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
    this.accountChargeDraft.set('0');
    this.creditAppliedDraft.set('0');
    this.creditCustomerId.set('');
    this.creditCustomerAccount.set(null);
    this.creditCustomerAccountStatus.set('idle');
    this.creditCustomerSearch.set('');
    this.creditCustomerOptions.set([]);
    this.creditCustomerLoadError.set(null);
    this.documentType.set('nonFiscalTicket');
    this.recipientTaxStatus.set('finalConsumer');
    this.recipientName.set('');
    this.recipientDocumentNumber.set('');
    this.recipientAddress.set('');
    if (this.canChargeToAccount()) this.searchCreditCustomers();
    this.checkoutError.set(null);
    this.checkoutNotice.set(true);
  }

  protected retryDraftSave(): void {
    if (this.errorMessage() === 'No se pudo recuperar el ticket. Intentá nuevamente.') this.loadSavedDraft();
    else this.persistCurrentDraft();
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
    if (!this.terminal()) return;
    this.shiftDialog.set(true);
    this.shiftError.set(null);
    this.loadCashierShift();
  }

  protected openCashierShift(): void {
    if (!this.terminal()) return;
    const amount = Number(this.shiftOpeningCash());
    if (!Number.isFinite(amount) || amount < 0 || amount > 9_999_999_999.99 || Math.round(amount * 100) !== amount * 100) {
      this.shiftError.set('Ingresá un fondo inicial válido (puede ser $0).');
      return;
    }
    this.shiftBusy.set(true);
    this.shiftError.set(null);
    this.cashierShiftClient.open(amount).subscribe({
      next: (shift) => {
        this.cashierShift.set(shift);
        this.shiftLoadState.set('ready');
        this.shiftBusy.set(false);
        this.shiftDialog.set(false);
        this.loadPriceLists();
        this.refreshStock();
      },
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
        this.shiftDialog.set(false);
        void this.router.navigateByUrl('/');
      },
      error: (error: HttpErrorResponse) => {
        this.shiftBusy.set(false);
        this.shiftError.set(error.error?.error?.code === 'CASHIER_SHIFT_HAS_DRAFT'
          ? 'Hay un ticket guardado pendiente. Confirmalo o cancelalo antes de cerrar el turno.'
          : error.status === 409 ? 'El turno cambió. Actualizá su estado e intentá de nuevo.'
            : 'No se pudo cerrar el turno. Intentá de nuevo.');
      },
    });
  }

  protected togglePayment(method: SalePaymentMethod, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    this.selectedPayments.update((payments) => checked
      ? [...payments, { method, amount: method === 'cash' ? this.dueNow() :
        Math.max(0, this.dueNow() - payments.reduce((sum, item) => sum + item.amount, 0)) }]
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

  protected selectDocumentType(event: Event): void {
    this.documentType.set((event.target as HTMLSelectElement).value as SaleDocumentType);
    this.checkoutError.set(null);
  }

  protected selectRecipientTaxStatus(event: Event): void {
    this.recipientTaxStatus.set((event.target as HTMLSelectElement).value as SaleRecipientTaxStatus);
    this.checkoutError.set(null);
  }

  protected updateRecipientName(event: Event): void {
    this.recipientName.set((event.target as HTMLInputElement).value.slice(0, 201));
  }

  protected updateRecipientDocumentNumber(event: Event): void {
    this.recipientDocumentNumber.set((event.target as HTMLInputElement).value.slice(0, 20));
  }

  protected updateRecipientAddress(event: Event): void {
    this.recipientAddress.set((event.target as HTMLInputElement).value.slice(0, 201));
  }

  private validCuit(value: string): boolean {
    if (!/^\d{11}$/.test(value)) return false;
    const weights = [5, 4, 3, 2, 7, 6, 5, 4, 3, 2];
    const sum = weights.reduce((total, weight, index) => total + Number(value[index]) * weight, 0);
    const check = 11 - sum % 11;
    return (check === 11 ? 0 : check === 10 ? 9 : check) === Number(value[10]);
  }

  protected updateCreditSearch(event: Event): void {
    this.creditCustomerSearch.set((event.target as HTMLInputElement).value.slice(0, 100));
  }

  protected searchCreditCustomers(): void {
    this.creditCustomerLoadError.set(null);
    this.creditCustomerLoading.set(true);
    this.creditCustomerClient.search(this.creditCustomerSearch().trim()).subscribe({
      next: (options) => {
        this.creditCustomerOptions.set(options);
        if (!options.some((option) => option.id === this.creditCustomerId())) {
          this.creditCustomerId.set('');
          this.creditCustomerAccount.set(null);
          this.creditCustomerAccountStatus.set('idle');
        }
        this.creditCustomerLoading.set(false);
      },
      error: () => {
        this.creditCustomerLoading.set(false);
        this.creditCustomerLoadError.set('No se pudieron consultar los clientes habilitados.');
      },
    });
  }

  protected selectCreditCustomer(event: Event): void {
    const customerId = (event.target as HTMLSelectElement).value;
    this.creditCustomerId.set(customerId);
    this.creditCustomerAccount.set(null);
    this.creditCustomerAccountStatus.set(customerId ? 'loading' : 'idle');
    if (customerId) this.creditCustomerClient.account(customerId).subscribe({
      next: (account) => {
        if (this.creditCustomerId() !== customerId) return;
        this.creditCustomerAccount.set(account);
        this.creditCustomerAccountStatus.set('idle');
      },
      error: () => {
        if (this.creditCustomerId() !== customerId) return;
        this.creditCustomerAccountStatus.set('error');
      },
    });
    this.checkoutError.set(null);
  }

  protected updateAccountCharge(event: Event): void {
    const previousDueNow = this.dueNow();
    this.accountChargeDraft.set((event.target as HTMLInputElement).value);
    this.adjustCashToDue(previousDueNow);
    this.checkoutError.set(null);
  }

  protected updateCreditApplied(event: Event): void {
    const previousDueNow = this.dueNow();
    this.creditAppliedDraft.set((event.target as HTMLInputElement).value);
    this.adjustCashToDue(previousDueNow);
    this.checkoutError.set(null);
  }

  private adjustCashToDue(previousDueNow: number): void {
    const newDueNow = this.dueNow();
    const payments = this.selectedPayments();
    if (payments.length === 1 && payments[0].method === 'cash' && payments[0].amount === previousDueNow &&
        Number.isFinite(newDueNow) && newDueNow >= 0) {
      this.selectedPayments.set(newDueNow === 0 ? [] : [{ method: 'cash', amount: newDueNow }]);
    }
  }

  protected confirmSale(): void {
    if (!this.draftId() || !this.canOperate() || this.checkoutBusy()) return;
    const problem = this.documentProblem() ?? this.paymentProblem();
    if (problem) {
      this.checkoutError.set(problem);
      return;
    }
    const payments = this.selectedPayments();
    const accountCharge = this.accountChargeAmount();
    const creditApplied = this.creditAppliedAmount();
    const customer = this.creditCustomerOptions().find((item) => item.id === this.creditCustomerId());
    if (accountCharge > 0 && !window.confirm(
      `¿Cargar ${this.formatMoney(accountCharge)} a la cuenta corriente de ${customer?.name ?? 'este cliente'}?`,
    )) return;
    this.checkoutBusy.set(true);
    this.checkoutError.set(null);
    this.saleDraftClient.confirm(this.draftId(), payments,
      accountCharge > 0 || creditApplied > 0
        ? { customerId: this.creditCustomerId(), amount: accountCharge, creditAppliedAmount: creditApplied }
        : undefined,
      { documentType: this.documentType(), recipientTaxStatus: this.recipientTaxStatus(),
        recipientName: this.recipientName().trim() || undefined,
        recipientDocumentNumber: this.recipientDocumentNumber().trim() || undefined,
        recipientAddress: this.recipientAddress().trim() || undefined }).subscribe({
      next: (sale) => this.finishConfirmedSale(sale),
      error: (error: HttpErrorResponse) => {
        this.checkoutBusy.set(false);
        this.checkoutError.set(error.status === 409 || error.status === 400 || error.status === 403
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
    this.fiscalDocument.set(null);
    this.fiscalError.set(null);
    this.receiptPdfPath.set(null);
    this.receiptPdfError.set(null);
    this.serialPrintResult.set(null);
    this.serialPrintError.set(null);
    this.lines.set([]);
    this.discountDraft.set('0');
    this.discountReason.set('');
    this.persistedLines.set([]);
    this.persistedDiscountAmount.set(0);
    this.persistedDiscountReason.set('');
    this.draftId.set('');
    this.draftStatus.set('saved');
    this.selectedPayments.set([]);
    this.accountChargeDraft.set('0');
    this.creditAppliedDraft.set('0');
    this.creditCustomerId.set('');
    this.creditCustomerAccount.set(null);
    this.creditCustomerAccountStatus.set('idle');
    this.documentType.set('nonFiscalTicket');
    this.recipientTaxStatus.set('finalConsumer');
    this.recipientName.set('');
    this.recipientDocumentNumber.set('');
    this.recipientAddress.set('');
    this.errorMessage.set(null);
    this.loadCashierShift();
    this.loadPriceLists();
    if (sale.documentType === 'fiscalTicket' || sale.documentType === 'electronicInvoice')
      this.issueFiscalDocument(sale);
  }

  protected issueFiscalDocument(sale: ConfirmedSale): void {
    if (this.fiscalBusy()) return;
    this.fiscalBusy.set(true);
    this.fiscalError.set(null);
    this.fiscalDocumentClient.issue(sale.id).subscribe({
      next: (document) => {
        if (this.confirmedSale()?.id !== sale.id) return;
        this.fiscalDocument.set(document);
        this.fiscalBusy.set(false);
        if (document.status === 'Authorized') {
          if (sale.documentType === 'fiscalTicket') this.printSerialReceipt(sale);
          else this.saveReceiptPdf(sale);
        } else if (document.status === 'Rejected') {
          this.fiscalError.set(`ARCA rechazó la emisión (código ${document.errorCodes ?? 'sin detalle'}). La venta sigue registrada.`);
        }
      },
      error: (error: HttpErrorResponse) => {
        if (this.confirmedSale()?.id !== sale.id) return;
        this.fiscalBusy.set(false);
        const code = error.error?.error?.code;
        this.fiscalError.set(code === 'FISCAL_RECONCILIATION_PENDING'
          ? 'No se pudo confirmar si ARCA autorizó el número. Usá «Consultar ARCA» antes de volver a emitir.'
          : code === 'INCOMPLETE_TAX_SNAPSHOT'
            ? 'La venta quedó registrada, pero faltan reglas de IVA en el catálogo; no se solicitó CAE.'
            : `No se pudo emitir en ARCA (${code ?? 'conexión'}). La venta sigue registrada; podés reintentar.`);
      },
    });
  }

  protected saveReceiptPdf(sale: ConfirmedSale): void {
    const fiscal = this.fiscalDocument();
    if (this.receiptPdfBusy() || ((sale.documentType === 'fiscalTicket' ||
      sale.documentType === 'electronicInvoice') && fiscal?.status !== 'Authorized')) return;
    this.receiptPdfBusy.set(true);
    this.receiptPdfError.set(null);
    const branch = this.session()?.context?.branchName ?? 'Sucursal';
    const terminal = this.terminal()?.name ?? 'Caja';
    const cashier = this.session()?.username ?? 'Cajero';
    const save = fiscal ? this.receiptPdfClient.save(sale, branch, terminal, cashier, fiscal)
      : this.receiptPdfClient.save(sale, branch, terminal, cashier);
    void save.then((path) => this.receiptPdfPath.set(path)).catch(() => {
      this.receiptPdfError.set('No se pudo generar el PDF. La venta sigue confirmada; podés reintentar.');
    }).finally(() => this.receiptPdfBusy.set(false));
  }

  protected printSerialReceipt(sale: ConfirmedSale): void {
    const fiscal = this.fiscalDocument();
    if (this.serialPrintBusy() || ((sale.documentType === 'fiscalTicket' ||
      sale.documentType === 'electronicInvoice') && fiscal?.status !== 'Authorized')) return;
    this.serialPrintBusy.set(true);
    this.serialPrintError.set(null);
    const branch = this.session()?.context?.branchName ?? 'Sucursal';
    const terminal = this.terminal()?.name ?? 'Caja';
    const cashier = this.session()?.username ?? 'Cajero';
    const print = fiscal ? this.posDeviceClient.print(sale, branch, terminal, cashier, fiscal)
      : this.posDeviceClient.print(sale, branch, terminal, cashier);
    void print.then((result) => this.serialPrintResult.set(result)).catch(() => {
      this.serialPrintError.set('No se pudo completar el envío por COM1. La venta sigue confirmada; revisá PuTTY y el puerto antes de reintentar para evitar duplicados.');
    }).finally(() => this.serialPrintBusy.set(false));
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
    if (code === 'CUSTOMER_CREDIT_UNAVAILABLE') return 'La cuenta corriente del cliente ya no está habilitada. Elegí otro medio de pago.';
    if (code === 'CUSTOMER_CREDIT_INSUFFICIENT') return 'El saldo a favor del cliente cambió. Consultá la cuenta y ajustá el importe aplicado.';
    if (code === 'DOCUMENT_DETAILS_INVALID') return 'Revisá el tipo de comprobante y los datos fiscales del cliente.';
    if (code === 'ACCOUNT_CHARGE_FORBIDDEN') return 'Tu rol no tiene permiso para cargar ventas a cuenta corriente.';
    if (code === 'IDEMPOTENCY_CONFLICT') return 'La venta ya fue confirmada con otro pago. Actualizá el estado antes de continuar.';
    return 'El ticket cambió o ya se procesó. Revisá el estado de la venta e intentá de nuevo.';
  }

  protected loadCashierShift(): void {
    this.shiftLoadState.set('loading');
    this.cashierShiftClient.current().subscribe({
      next: (shift) => {
        this.cashierShift.set(shift);
        if (shift) {
          this.restoreActiveTicket(shift.id);
          this.loadTicketSlots();
        } else {
          this.ticketSlotsRequestRevision++;
          this.savedTicketSlots.set(new Set());
          this.ticketSlotsLoadState.set('idle');
        }
        this.shiftLoadState.set('ready');
        if (shift && this.priceListLoadState() === 'loading' && this.realPriceLists().length === 0) {
          this.loadPriceLists();
          this.refreshStock();
        }
        if (!shift) this.loadLastClosedShift();
      },
      error: (error: HttpErrorResponse) => {
        if (error.status === 401) { void this.router.navigateByUrl('/'); return; }
        this.shiftLoadState.set('error');
        this.shiftError.set('No se pudo consultar tu turno de caja.');
      },
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

  private addProductLine(product: PosProduct, quantity: number, pieceId: string | null = null,
    pieceIdentifier: string | null = null): void {
    this.lines.update((lines) => {
      const existing = pieceId ? undefined : lines.find((line) => line.product.id === product.id && !line.inventoryPieceId);
      if (!existing) return [...lines, { id: crypto.randomUUID(), product, quantity,
        ...(pieceId ? { inventoryPieceId: pieceId } : {}),
        ...(pieceIdentifier ? { pieceIdentifier } : {}) }];
      return lines.map((line) => line.id === existing.id
        ? { ...line, quantity: this.roundQuantity(line.quantity + quantity) }
        : line);
    });
    this.persistCurrentDraft();
  }

  private persistCurrentDraft(): void {
    if (!this.canOperate()) return;
    if (!this.selectedPriceListId() || this.isDemoPriceList()) {
      this.draftStatus.set('demo');
      return;
    }
    if (this.lines().length === 0) return;
    if (this.discountProblem()) {
      this.draftStatus.set('error');
      this.errorMessage.set(this.discountProblem());
      return;
    }
    this.queueDraftOperation({ priceListId: this.selectedPriceListId(), lines: this.lines(),
      discountAmount: this.discountAmount(), discountReason: this.discountAmount() > 0
        ? this.discountReason().trim() : null });
  }

  private queueDraftOperation(operation: 'cancel' | { readonly priceListId: string; readonly lines: readonly SaleLine[];
    readonly discountAmount: number; readonly discountReason: string | null }): void {
    if (!this.canOperate()) return;
    this.draftStatus.set('saving');
    this.draftOperations.next({ revision: ++this.draftRevision, slot: this.activeTicketSlot(), operation });
  }

  private loadSavedDraft(): void {
    const slot = this.activeTicketSlot();
    this.draftStatus.set('loading');
    this.saleDraftClient.current(slot).subscribe({
      next: (draft) => {
        if (slot !== this.activeTicketSlot()) return;
        if (!draft) {
          this.lines.set([]);
          this.discountDraft.set('0');
          this.discountReason.set('');
          this.persistedLines.set([]);
          this.persistedDiscountAmount.set(0);
          this.persistedDiscountReason.set('');
          this.draftId.set('');
          this.draftStatus.set('saved');
          return;
        }
        this.draftId.set(draft.id);
        this.discountDraft.set(String(draft.discountAmount ?? 0));
        this.discountReason.set(draft.discountReason ?? '');
        this.persistedDiscountAmount.set(draft.discountAmount ?? 0);
        this.persistedDiscountReason.set(draft.discountReason ?? '');
        this.selectedPriceListId.set(draft.priceListId);
        this.lines.set(draft.lines.map((item: SaleDraftLine) => ({
          id: item.id,
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
          ...(item.inventoryPieceId ? { inventoryPieceId: item.inventoryPieceId } : {}),
          ...(item.pieceIdentifier ? { pieceIdentifier: item.pieceIdentifier } : {}),
        })));
        this.persistedLines.set(this.lines());
        this.draftStatus.set('saved');
        if (this.realPriceLists().some((item) => item.id === draft.priceListId))
          this.loadCategories(draft.priceListId);
      },
      error: () => {
        if (slot !== this.activeTicketSlot()) return;
        this.draftStatus.set('error');
        this.errorMessage.set('No se pudo recuperar el ticket. Intentá nuevamente.');
      },
    });
  }

  private activeTicketStorageKey(shiftId: string): string {
    return `carnicerias:ticket-slot:${this.terminal()?.id}:${shiftId}:${this.session()?.userId}`;
  }

  private restoreActiveTicket(shiftId: string): void {
    try {
      const saved = window.localStorage.getItem(this.activeTicketStorageKey(shiftId));
      this.activeTicketSlot.set(this.ticketSlots.find((slot) => slot === saved) ?? 'A');
    } catch {
      this.activeTicketSlot.set('A');
    }
  }

  private rememberActiveTicket(slot: SaleTicketSlot): void {
    const shift = this.cashierShift();
    if (!shift) return;
    try { window.localStorage.setItem(this.activeTicketStorageKey(shift.id), slot); }
    catch { /* The tickets themselves remain in PostgreSQL. */ }
  }
}
