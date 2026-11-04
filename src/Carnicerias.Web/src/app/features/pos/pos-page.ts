import { HttpErrorResponse } from '@angular/common/http';
import { DatePipe } from '@angular/common';
import { Component, computed, DestroyRef, ElementRef, inject, HostListener, OnInit, signal, ViewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom, Observable, of, Subject } from 'rxjs';
import { catchError, concatMap, finalize, map, timeout } from 'rxjs/operators';
import { CatalogCategory, CatalogClient, PriceListOption } from '../../core/catalog/catalog-client';
import { InventoryClient, InventoryStockItem } from '../../core/inventory/inventory-client';
import { InventoryPieceClient, PosPieceLookup } from '../../core/inventory/inventory-piece-client';
import { PosTerminal, PosTerminalClient } from '../../core/pos/pos-terminal-client';
import { POS_ELECTRON_MODE } from '../../core/pos/pos-electron-mode';
import { SelectOnFocusDirective } from '../../shared/select-on-focus.directive';
import { LocalizedDecimalInputDirective } from '../../shared/localized-decimal-input.directive';
import { formatLocalizedDecimal, parseLocalizedDecimal } from '../../shared/localized-decimal';
import { HasarSalePrintResult, PosDeviceAvailability, PosDeviceClient, SerialPrintResult } from '../../core/pos/pos-device-client';
import { ConfirmedSale, SaleDocumentType, SaleDraft, SaleDraftClient, SaleDraftLine, SalePaymentMethod, SaleRecipientTaxStatus, SaleTicketSlot } from '../../core/sales/sale-draft-client';
import { ReceiptPdfClient } from '../../core/sales/receipt-pdf-client';
import { AuthorizedFiscalDocument, FiscalDocumentClient } from '../../core/sales/fiscal-document-client';
import { CashierShift, CashierShiftClient } from '../../core/sales/cashier-shift-client';
import { CurrentSession, SessionClient } from '../../core/session/session-client';
import { CreditCustomerAccount, CreditCustomerClient, CreditCustomerOption, FiscalCustomerOption } from '../../core/customers/credit-customer-client';
import { AccountCollectionDialog } from './account-collection-dialog';
import QRCode from 'qrcode';
import { MercadoPagoClient, MercadoPagoIntent, MercadoPagoMode } from '../../core/sales/mercado-pago-client';
import { IntegratedGatewayClient } from '../../core/sales/integrated-gateway-client';
import { CheckoutInstrumentStatus, SaleCheckout, SaleCheckoutClient } from '../../core/sales/sale-checkout-client';

type SaleMode = 'weight' | 'unit';

interface SelectedPayment {
  readonly id: string;
  readonly method: SalePaymentMethod;
  readonly amount: number;
  readonly reference?: string | null;
  readonly status?: CheckoutInstrumentStatus;
  readonly pointPaymentIntentId?: string | null;
}

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
  readonly pieceCount?: number | null;
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

const catalogRefreshIntervalMs = 15_000;

@Component({
  selector: 'app-pos-page',
  imports: [RouterLink, AccountCollectionDialog, DatePipe, SelectOnFocusDirective, LocalizedDecimalInputDirective],
  templateUrl: './pos-page.html',
})
export class PosPage implements OnInit {
  protected readonly isPosElectron = inject(POS_ELECTRON_MODE);
  private readonly sessionClient = inject(SessionClient);
  private readonly catalogClient = inject(CatalogClient);
  private readonly saleDraftClient = inject(SaleDraftClient);
  private readonly saleCheckoutClient = inject(SaleCheckoutClient);
  private readonly mercadoPagoClient = inject(MercadoPagoClient);
  private readonly integratedGatewayClient = inject(IntegratedGatewayClient);
  private readonly receiptPdfClient = inject(ReceiptPdfClient);
  private readonly fiscalDocumentClient = inject(FiscalDocumentClient);
  private readonly posDeviceClient = inject(PosDeviceClient);
  private readonly cashierShiftClient = inject(CashierShiftClient);
  private readonly inventoryClient = inject(InventoryClient);
  private readonly pieceClient = inject(InventoryPieceClient);
  private readonly terminalClient = inject(PosTerminalClient);
  private readonly creditCustomerClient = inject(CreditCustomerClient);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  @ViewChild('scanInput') private scanInput?: ElementRef<HTMLInputElement>;
  @ViewChild('quantityInput') private quantityInput?: ElementRef<HTMLInputElement>;
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
  private liveRefreshTimer: ReturnType<typeof setInterval> | null = null;
  private priceListRefreshInFlight = false;
  private categoryRefreshInFlight = false;
  private lastCategoryRefreshAt = 0;
  private productRefreshInFlight = false;
  private lastProductRefreshAt = 0;
  private stockRefreshInFlight = false;
  private initialDraftLoadRequested = false;
  private scaleRequestRevision = 0;
  private scannedCode = '';
  private lastScanKeyAt = 0;
  private mercadoPagoPollTimer: ReturnType<typeof setTimeout> | null = null;
  private gatewayRequestId = 0;

  protected readonly session = signal<CurrentSession | null>(null);
  protected readonly terminal = signal<PosTerminal | null>(null);
  protected readonly terminalError = signal<string | null>(null);
  protected readonly devices = signal<PosDeviceAvailability>({ scale: null, printer: null });
  protected readonly deviceConfigError = signal(false);
  protected readonly demoPriceLists = demoPriceLists;
  protected readonly realPriceLists = signal<readonly PriceListOption[]>([]);
  protected readonly unavailablePriceListName = signal('');
  protected readonly catalogNotice = signal<string | null>(null);
  protected readonly catalogCategories = signal<readonly CatalogCategory[]>([]);
  protected readonly realProducts = signal<readonly PosProduct[]>([]);
  private readonly stockByProduct = signal<ReadonlyMap<string, number>>(new Map());
  protected readonly stockLoadState = signal<'loading' | 'ready' | 'stale'>('loading');
  protected readonly priceListLoadState = signal<'loading' | 'ready' | 'error'>('loading');
  protected readonly catalogLoadState = signal<'idle' | 'loading' | 'ready' | 'error'>('idle');
  protected readonly selectedPriceListId = signal('');
  protected readonly priceListUnavailable = computed(() => this.priceListLoadState() === 'ready' &&
    !!this.selectedPriceListId() && !this.selectedPriceListId().startsWith('demo:') &&
    !this.realPriceLists().some((list) => list.id === this.selectedPriceListId()));
  protected readonly liveDataProblem = computed(() => {
    if (this.priceListLoadState() === 'error') return 'No se pudieron verificar las listas de precios. Reintentá la sincronización.';
    if (this.priceListUnavailable()) return 'Esta lista de precios ya no está habilitada. Conservamos el ticket; cancelá la venta para elegir otra lista.';
    if (this.stockLoadState() === 'stale') return 'Stock desactualizado. No agregues productos ni cobres hasta recuperar la conexión.';
    if (this.stockLoadState() === 'loading') return 'Verificando stock de la sucursal…';
    return null;
  });
  protected readonly isDemoPriceList = computed(() => this.selectedPriceListId().startsWith('demo:'));
  protected readonly selectedPriceListName = computed(
    () => this.realPriceLists().find((list) => list.id === this.selectedPriceListId())?.name ??
      this.demoPriceLists.find((list) => list.id === this.selectedPriceListId())?.name ??
      (this.unavailablePriceListName() || (this.selectedPriceListId() ? `Lista ${this.selectedPriceListId()}` : '')),
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
    this.isDemoPriceList() || (this.selectedPriceListId().length > 0 && !this.liveDataProblem() &&
      this.catalogLoadState() === 'ready'),
  );
  protected readonly activeCategory = signal<string>('res');
  protected readonly searchText = signal('');
  protected readonly lines = signal<readonly SaleLine[]>([]);
  private readonly persistedLines = signal<readonly SaleLine[]>([]);
  protected readonly selectedProduct = signal<PosProduct | null>(null);
  protected readonly scannedPieceIdentifier = signal<string | null>(null);
  private readonly scannedPieceId = signal<string | null>(null);
  protected readonly quantityDraft = signal('1');
  protected readonly pieceCountDraft = signal('');
  protected readonly scaleBusy = signal(false);
  protected readonly scaleNotice = signal<string | null>(null);
  protected readonly scaleError = signal<string | null>(null);
  protected readonly quantityProblem = computed(() => {
    const product = this.selectedProduct();
    if (!product) return null;
    if (!this.isDemoPriceList() && this.liveDataProblem()) return this.liveDataProblem();
    const quantity = parseLocalizedDecimal(this.quantityDraft(), product.mode === 'weight' ? 3 : 0) ?? NaN;
    if (!Number.isFinite(quantity) || quantity <= 0 || quantity > 10000 ||
        Math.abs(Math.round(quantity * 1000) - quantity * 1000) > 0.000001)
      return product.mode === 'weight' ? 'Ingresá un peso válido mayor a cero (hasta 3 decimales).' : 'Ingresá una cantidad entera válida mayor a cero.';
    if (product.mode === 'unit' && !Number.isInteger(quantity)) return 'Ingresá una cantidad entera de unidades de venta.';
    if (product.mode === 'weight' && this.pieceCountDraft().trim()) {
      const pieces = parseLocalizedDecimal(this.pieceCountDraft(), 0) ?? NaN;
      if (!Number.isInteger(pieces) || pieces < 1 || pieces > 10000)
        return 'Ingresá una cantidad de piezas entera entre 1 y 10000.';
    }
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
  protected readonly checkout = signal<SaleCheckout | null>(null);
  private readonly legacyPayment = signal(false);
  protected readonly checkoutLoadState = signal<'idle' | 'loading' | 'ready' | 'error'>('idle');
  protected readonly checkoutCommitted = computed(() => !!this.checkout() &&
    this.checkout()!.status !== 'Prepared');
  protected readonly extraPayments = computed(() => {
    const seen = new Set<SalePaymentMethod>();
    return this.selectedPayments().filter((payment) => {
      if (seen.has(payment.method)) return true;
      seen.add(payment.method);
      return false;
    });
  });
  protected readonly mercadoPagoPayments = computed(() =>
    this.selectedPayments().filter((payment) => payment.method === 'mercadoPago'));
  protected readonly hasRejectedPayment = computed(() =>
    this.checkout()?.instruments.some((item) => item.status === 'Rejected') ?? false);
  protected readonly replacementMethod = signal<SalePaymentMethod>('cash');
  private checkoutCommitKey: string | null = null;
  private mercadoPagoPollIndex = 0;
  private readonly mercadoPagoIntentsByOperation = signal<Record<string, MercadoPagoIntent>>({});
  protected readonly mercadoPagoIntent = signal<MercadoPagoIntent | null>(null);
  protected readonly mercadoPagoMode = signal<MercadoPagoMode>('qr');
  protected readonly availableMercadoPagoModes = signal<readonly MercadoPagoMode[]>([]);
  protected readonly gatewayLoadState = signal<'idle' | 'loading' | 'ready' | 'error'>('idle');
  protected readonly mercadoPagoBusy = signal(false);
  protected readonly mercadoPagoLoadState = signal<'idle' | 'loading' | 'ready' | 'error'>('idle');
  protected readonly mercadoPagoOrderActive = computed(() => !!this.mercadoPagoIntent() &&
    !['Rejected', 'Expired', 'Canceled', 'Refunded'].includes(this.mercadoPagoIntent()!.status));
  protected readonly mercadoPagoQrImage = signal<string | null>(null);
  protected readonly mercadoPagoMessage = signal<string | null>(null);
  protected readonly documentType = signal<SaleDocumentType>('nonFiscalTicket');
  protected readonly recipientTaxStatus = signal<SaleRecipientTaxStatus>('finalConsumer');
  protected readonly recipientName = signal('');
  protected readonly recipientDocumentNumber = signal('');
  protected readonly recipientAddress = signal('');
  protected readonly recipientLocality = signal('');
  protected readonly recipientProvince = signal('');
  protected readonly purchaseOrderNumber = signal('');
  protected readonly fiscalCustomerSearch = signal('');
  protected readonly fiscalCustomerOptions = signal<readonly FiscalCustomerOption[]>([]);
  protected readonly fiscalCustomerId = signal('');
  protected readonly fiscalCustomerLoading = signal(false);
  protected readonly fiscalCustomerError = signal<string | null>(null);
  protected readonly fiscalRegistryLoading = signal(false);
  protected readonly fiscalRegistryNotice = signal<string | null>(null);
  private fiscalLookupRequestId = 0;
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
    if (this.recipientLocality().length > 100 || this.recipientProvince().length > 100 ||
        this.purchaseOrderNumber().length > 80)
      return 'Localidad, provincia o referencia comercial demasiado extensa.';
    if (this.fiscalCustomerId() && this.creditCustomerId() &&
        this.fiscalCustomerId() !== this.creditCustomerId() &&
        this.accountChargeAmount() + this.creditAppliedAmount() > 0)
      return 'El cliente fiscal debe coincidir con la cuenta corriente usada en esta venta.';
    return null;
  });
  protected readonly confirmedSale = signal<ConfirmedSale | null>(null);
  protected readonly fiscalDocument = signal<AuthorizedFiscalDocument | null>(null);
  protected readonly fiscalBusy = signal(false);
  protected readonly fiscalError = signal<string | null>(null);
  protected readonly receiptPdfBusy = signal(false);
  protected readonly receiptPdfDownloaded = signal(false);
  protected readonly receiptPdfError = signal<string | null>(null);
  protected readonly serialPrintBusy = signal(false);
  protected readonly serialPrintResult = signal<SerialPrintResult | HasarSalePrintResult | null>(null);
  protected readonly serialPrintError = signal<string | null>(null);
  protected readonly selectedPayments = signal<readonly SelectedPayment[]>([]);
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
  protected readonly discountAmount = computed(() => this.decimalOrZero(this.discountDraft(), 2));
  protected readonly canDiscount = computed(() =>
    !!this.session()?.context?.permissions.includes('pos.discount.apply'));
  protected readonly accountChargeAmount = computed(() => this.decimalOrZero(this.accountChargeDraft(), 2));
  protected readonly creditAppliedDraft = signal('0');
  protected readonly creditAppliedAmount = computed(() => this.decimalOrZero(this.creditAppliedDraft(), 2));
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
  protected readonly visiblePaymentMethods = computed(() => this.paymentMethods.filter((method) =>
    method.id !== 'mercadoPago' || this.availableMercadoPagoModes().length > 0 || !!this.mercadoPagoIntent()));
  protected readonly visibleMercadoPagoModes = computed(() => {
    const modes = this.availableMercadoPagoModes();
    const intentMode = this.mercadoPagoIntent()?.mode;
    return intentMode && !modes.includes(intentMode) ? [...modes, intentMode] : modes;
  });
  protected readonly changePreview = computed(() => {
    const selected = this.selectedPayments();
    const cash = selected.filter((payment) => payment.method === 'cash')
      .reduce((sum, payment) => sum + payment.amount, 0);
    const nonCash = selected.filter((payment) => payment.method !== 'cash').reduce((sum, payment) => sum + payment.amount, 0);
    const dueNow = this.dueNow();
    return nonCash > dueNow ? 0 : Math.max(0, Math.round((cash + nonCash - dueNow) * 100) / 100);
  });
  protected readonly paymentBalance = computed(() => this.checkoutCommitted()
    ? this.checkout()?.remainingAmount ?? 0 : Math.max(0, Math.round((this.dueNow() -
    this.selectedPayments().reduce((sum, payment) => sum + payment.amount, 0)) * 100) / 100));
  protected readonly paymentProblem = computed(() => {
    if (this.checkoutLoadState() === 'loading') return 'Recuperando el proceso de cobro.';
    if (this.checkoutLoadState() === 'error') return 'No se pudo recuperar el proceso de cobro.';
    if (this.checkoutCommitted()) return null;
    if (!this.isDemoPriceList() && this.liveDataProblem()) return this.liveDataProblem();
    if (this.mercadoPagoLoadState() === 'loading') return 'Consultando si hay un cobro de Mercado Pago previo.';
    if (this.mercadoPagoLoadState() === 'error') return 'No se pudo verificar el cobro previo. Reabrí la ventana de cobro.';
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
    const mercadoPago = payments.filter((payment) => payment.method === 'mercadoPago');
    if (mercadoPago.some((payment) => !this.mercadoPagoIntentsByOperation()[payment.id ?? '']?.approved &&
        !(this.mercadoPagoIntent()?.approved && mercadoPago.length === 1 &&
          this.mercadoPagoIntent()?.amount === payment.amount)))
      return 'Confirmá el cobro de Mercado Pago antes de cerrar la venta.';
    if (mercadoPago.length === 0 && this.mercadoPagoOrderActive())
      return 'Hay una orden de Mercado Pago activa. Consultá su estado antes de cambiar el medio.';
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
    this.destroyRef.onDestroy(() => {
      this.clearMercadoPagoPoll();
      if (this.liveRefreshTimer) clearInterval(this.liveRefreshTimer);
    });
    this.draftOperations.pipe(
      concatMap(({ revision, slot, operation }) => {
        const request: Observable<SaleDraft | void> = operation === 'cancel'
          ? this.saleDraftClient.cancel(slot)
          : this.saleDraftClient.save(operation.priceListId, operation.lines.map((line) => ({
              productId: line.product.id,
              quantity: line.quantity,
              ...(line.pieceCount != null ? { pieceCount: line.pieceCount } : {}),
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
          if (['PIECE_RELEASED_BY_ADMIN', 'PIECE_RESERVATION_MISSING',
            'SALE_DRAFT_CONFLICT'].includes(error.error?.error?.code)) {
            this.loadSavedDraft();
            this.errorMessage.set(error.error?.error?.code === 'PIECE_RELEASED_BY_ADMIN'
              ? 'Administración liberó una pieza de este ticket. Se recuperó el resto de la venta guardada.'
              : 'El ticket cambió en otra operación. Se recuperó la venta guardada.');
            this.refreshStock();
            return;
          }
          if (operation === 'cancel') this.lines.set(this.persistedLines());
          if (['INSUFFICIENT_STOCK', 'PIECE_ALREADY_USED', 'PIECE_NOT_AVAILABLE',
            'PIECE_RECONCILIATION_REQUIRED', 'ARTICLE_RECONCILIATION_IN_PROGRESS',
            'MERCADO_PAGO_ORDER_ACTIVE'].includes(error.error?.error?.code)) {
            this.lines.set(this.persistedLines());
            this.discountDraft.set(String(this.persistedDiscountAmount()));
            this.discountReason.set(this.persistedDiscountReason());
            this.draftStatus.set('saved');
            this.errorMessage.set(error.error?.error?.code === 'MERCADO_PAGO_ORDER_ACTIVE'
              ? 'Hay un cobro de Mercado Pago activo. Consultá o cancelá la orden desde el cierre antes de modificar el ticket.'
              : error.error?.error?.code === 'ARTICLE_RECONCILIATION_IN_PROGRESS'
              ? 'Este artículo está temporalmente bloqueado por un conteo físico. Conservamos el ticket; reintentá cuando termine.'
              : error.error?.error?.code === 'PIECE_RECONCILIATION_REQUIRED'
              ? 'Esta pieza histórica está inmovilizada hasta que Administración concilie su estado.'
              : error.error?.error?.code === 'INSUFFICIENT_STOCK'
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
        if (operation === 'cancel') this.clearDocumentChoice();
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
          pieceCount: item.pieceCount ?? null,
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
    this.liveRefreshTimer = setInterval(() => this.refreshLiveData(), 5000);
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
            void this.refreshDevices().catch(() => undefined);
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

  private async refreshDevices(): Promise<PosDeviceAvailability> {
    try {
      const devices = await this.posDeviceClient.deviceAvailability();
      this.devices.set(devices);
      this.deviceConfigError.set(false);
      return devices;
    } catch (error) {
      this.devices.set({ scale: null, printer: null });
      this.deviceConfigError.set(true);
      throw error;
    }
  }

  protected loadPriceLists(): void {
    if (!this.initialDraftLoadRequested && this.realPriceLists().length === 0)
      this.priceListLoadState.set('loading');
    this.refreshPriceLists();
  }

  @HostListener('window:focus')
  @HostListener('window:online')
  protected refreshLiveData(): void {
    if (!this.canOperate()) return;
    this.refreshPriceLists();
    this.refreshStock();
    this.refreshCategories();
    this.refreshCurrentPrices();
  }

  private refreshCategories(): void {
    const priceListId = this.selectedPriceListId();
    const now = Date.now();
    if (!priceListId || this.isDemoPriceList() || this.priceListUnavailable() ||
        this.categoryRefreshInFlight || now - this.lastCategoryRefreshAt < catalogRefreshIntervalMs)
      return;
    this.categoryRefreshInFlight = true;
    this.lastCategoryRefreshAt = now;
    const viewRevision = this.ticketViewRevision;
    this.catalogClient.categories(priceListId).pipe(
      timeout(4000), takeUntilDestroyed(this.destroyRef),
      finalize(() => { this.categoryRefreshInFlight = false; }),
    ).subscribe({
      next: (items) => {
        if (!this.canOperate() || this.selectedPriceListId() !== priceListId ||
            viewRevision !== this.ticketViewRevision) return;
        const previous = this.catalogCategories();
        if (previous.length === items.length && previous.every((category, index) =>
          category.id === items[index].id && category.name === items[index].name &&
          category.productCount === items[index].productCount)) return;
        this.catalogCategories.set(items);
        const activeIds = new Set(items.map((category) => category.id));
        this.realProducts.update((products) => {
          const visible = products.filter((product) => activeIds.has(product.category));
          return visible.length === products.length ? products : visible;
        });
        if (!activeIds.has(this.activeCategory())) {
          const nextCategoryId = items[0]?.id ?? '';
          this.activeCategory.set(nextCategoryId);
          if (!this.searchReady()) {
            this.realProducts.set([]);
            if (nextCategoryId) this.loadProducts(priceListId, nextCategoryId);
            else this.catalogLoadState.set('ready');
          }
        }
      },
      error: () => undefined,
    });
  }

  private refreshCurrentPrices(): void {
    const priceListId = this.selectedPriceListId();
    const categoryId = this.activeCategory();
    const query = this.searchText().trim();
    const searchSubmitted = this.realSearchSubmitted();
    const now = Date.now();
    if (!priceListId || this.isDemoPriceList() || this.priceListUnavailable() ||
        this.catalogLoadState() !== 'ready' || this.categoryRefreshInFlight ||
        this.productRefreshInFlight || now - this.lastProductRefreshAt < catalogRefreshIntervalMs ||
        (query && !searchSubmitted) || (!categoryId && !searchSubmitted)) return;
    const selectedCode = searchSubmitted && !query && this.realProducts().length === 1
      ? this.realProducts()[0].code : null;
    if (searchSubmitted && !query && !selectedCode) return;
    const filters = selectedCode ? { priceListId, code: selectedCode }
      : query ? query.length >= 3 ? { priceListId, q: query } : { priceListId, code: query }
      : { priceListId, categoryId };
    this.productRefreshInFlight = true;
    this.lastProductRefreshAt = now;
    const viewRevision = this.ticketViewRevision;
    this.catalogClient.products(filters).pipe(
      timeout(4000), takeUntilDestroyed(this.destroyRef),
      finalize(() => { this.productRefreshInFlight = false; }),
    ).subscribe({
      next: (page) => {
        if (!this.canOperate() || this.selectedPriceListId() !== priceListId ||
            viewRevision !== this.ticketViewRevision || this.activeCategory() !== categoryId ||
            this.searchText().trim() !== query || this.realSearchSubmitted() !== searchSubmitted) return;
        const prices = new Map(page.items.map((item) => [item.id, item.price]));
        this.realProducts.update((products) => {
          let changed = false;
          const updated = products.map((product) => {
            const price = prices.get(product.id);
            if (price === undefined || price === product.price) return product;
            changed = true;
            return { ...product, price };
          });
          return changed ? updated : products;
        });
        this.selectedProduct.update((product) => {
          if (!product) return null;
          const price = prices.get(product.id);
          return price === undefined || price === product.price ? product : { ...product, price };
        });
      },
      error: () => undefined,
    });
  }

  private refreshPriceLists(): void {
    if (this.priceListRefreshInFlight || !this.canOperate()) return;
    this.priceListRefreshInFlight = true;
    this.catalogClient.priceLists().pipe(
      timeout(4000), takeUntilDestroyed(this.destroyRef),
      finalize(() => { this.priceListRefreshInFlight = false; }),
    ).subscribe({
      next: (lists) => {
        const previousId = this.selectedPriceListId();
        const previousName = this.realPriceLists().find((list) => list.id === previousId)?.name ??
          this.unavailablePriceListName();
        this.realPriceLists.set(lists);
        if (previousId && !lists.some((list) => list.id === previousId)) {
          if (this.lines().length > 0) {
            this.unavailablePriceListName.set(previousName);
            this.catalogNotice.set(`La lista ${previousName || 'seleccionada'} ya no está habilitada. El detalle y sus precios se conservan; cancelá la venta para elegir otra lista.`);
          } else {
            const nextList = lists[0];
            this.selectedPriceListId.set(nextList?.id ?? '');
            this.unavailablePriceListName.set('');
            this.catalogNotice.set(`La lista ${previousName || 'seleccionada'} ya no está habilitada. ${nextList
              ? `Se seleccionó ${nextList.name}.` : 'No hay otra lista disponible.'}`);
            this.resetCatalogForPriceList(nextList?.id ?? '');
          }
        } else if (!previousId && lists.length > 0) {
          this.selectedPriceListId.set(lists[0].id);
          this.resetCatalogForPriceList(lists[0].id);
        } else if (previousId && this.unavailablePriceListName()) {
          this.unavailablePriceListName.set('');
          this.catalogNotice.set(null);
        }
        this.priceListLoadState.set('ready');
        if (!this.initialDraftLoadRequested) {
          this.initialDraftLoadRequested = true;
          this.loadSavedDraft();
        }
      },
      error: () => this.priceListLoadState.set('error'),
    });
  }

  private resetCatalogForPriceList(priceListId: string): void {
    this.ticketViewRevision++;
    this.searchText.set('');
    this.selectedProduct.set(null);
    this.scannedPieceIdentifier.set(null);
    this.scannedPieceId.set(null);
    this.realSearchSubmitted.set(false);
    this.realProducts.set([]);
    if (priceListId) this.loadCategories(priceListId);
    else {
      this.catalogCategories.set([]);
      this.catalogLoadState.set('idle');
    }
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
    this.unavailablePriceListName.set('');
    this.catalogNotice.set(null);
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
          this.lastProductRefreshAt = Date.now();
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
            this.lastProductRefreshAt = Date.now();
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
            : error.error?.error?.code === 'PIECE_RESERVED'
              ? 'Esta pieza ya está reservada en un ticket.'
              : error.error?.error?.code === 'PIECE_RECONCILIATION_REQUIRED'
                ? 'Esta pieza histórica está inmovilizada hasta que Administración concilie su estado.'
              : error.error?.error?.code === 'PIECE_NOT_AVAILABLE'
                ? 'Esta pieza no está disponible para la venta.'
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
        this.lastCategoryRefreshAt = Date.now();
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
    this.lastProductRefreshAt = Date.now();
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
    if (!this.isDemoPriceList() && this.liveDataProblem()) {
      this.errorMessage.set(this.liveDataProblem());
      return;
    }
    if (!this.isDemoPriceList() && (this.availableStock(product) ?? 0) <= 0) {
      this.errorMessage.set('No hay stock disponible para este producto en la sucursal.');
      return;
    }
    this.selectedProduct.set(product);
    this.scaleRequestRevision++;
    this.scannedPieceIdentifier.set(pieceIdentifier);
    this.scannedPieceId.set(pieceId);
    this.quantityDraft.set(String(quantity));
    this.pieceCountDraft.set(pieceId ? '1' : '');
    this.scaleNotice.set(null);
    this.scaleError.set(null);
    if (!pieceIdentifier) setTimeout(() => {
      if (this.selectedProduct()?.id === product.id && !this.scannedPieceIdentifier() && this.canOperate())
        this.quantityInput?.nativeElement.focus();
    }, 0);
  }

  protected updateQuantity(event: Event): void {
    if (this.scannedPieceIdentifier()) {
      (event.target as HTMLInputElement).value = this.quantityDraft();
      return;
    }
    this.quantityDraft.set((event.target as HTMLInputElement).value);
    this.scaleNotice.set(null);
  }

  protected updatePieceCount(event: Event): void {
    if (this.scannedPieceId()) {
      (event.target as HTMLInputElement).value = '1';
      return;
    }
    this.pieceCountDraft.set((event.target as HTMLInputElement).value);
  }

  protected readSerialScale(): void {
    if (this.scaleBusy() || !this.devices().scale || this.selectedProduct()?.mode !== 'weight' ||
        this.scannedPieceIdentifier()) return;
    const productId = this.selectedProduct()?.id;
    const revision = ++this.scaleRequestRevision;
    this.scaleBusy.set(true);
    this.scaleError.set(null);
    this.scaleNotice.set(null);
    void this.posDeviceClient.readScale().then((weightKg) => {
      if (this.selectedProduct()?.id !== productId || this.scaleRequestRevision !== revision) return;
      this.quantityDraft.set(String(weightKg));
      this.scaleNotice.set(`Balanza: ${weightKg.toLocaleString('es-AR', { minimumFractionDigits: 3, maximumFractionDigits: 3 })} kg`);
    }).catch(() => {
      if (this.selectedProduct()?.id === productId && this.scaleRequestRevision === revision)
        this.scaleError.set('No hay una lectura estable y reciente en la balanza configurada. Ingresá el peso manualmente o revisá la conexión.');
    }).finally(() => this.scaleBusy.set(false));
  }

  protected addSelectedProduct(): void {
    if (!this.canOperate()) return;
    if (!this.isDemoPriceList() && this.liveDataProblem()) {
      this.errorMessage.set(this.liveDataProblem());
      return;
    }
    const product = this.selectedProduct();
    const quantity = parseLocalizedDecimal(this.quantityDraft(), product?.mode === 'weight' ? 3 : 0) ?? NaN;
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
    const pieceCount = product.mode === 'weight' && this.pieceCountDraft().trim()
      ? parseLocalizedDecimal(this.pieceCountDraft(), 0) : null;
    this.addProductLine(product, quantity, pieceId, this.scannedPieceIdentifier(), pieceCount);
    this.selectedProduct.set(null);
    this.scannedPieceIdentifier.set(null);
    this.scannedPieceId.set(null);
    this.focusScanInput();
  }

  protected updateLineQuantity(lineId: string, event: Event): void {
    if (!this.canOperate()) return;
    const input = event.target as HTMLInputElement;
    const line = this.lines().find((item) => item.id === lineId);
    const quantity = parseLocalizedDecimal(input.value, line?.product.mode === 'weight' ? 3 : 0) ?? NaN;
    if (!this.isDemoPriceList() && this.liveDataProblem()) {
      if (line) input.value = String(line.quantity);
      this.errorMessage.set(this.liveDataProblem());
      return;
    }
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
    if (!this.isDemoPriceList() && this.liveDataProblem()) return;
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
    if (this.liveDataProblem()) return;
    this.discountDraft.set((event.target as HTMLInputElement).value);
    if (this.discountAmount() === 0) this.discountReason.set('');
    this.persistCurrentDraft();
  }

  protected updateDiscountReason(event: Event): void {
    if (this.liveDataProblem()) return;
    this.discountReason.set((event.target as HTMLInputElement).value);
    this.persistCurrentDraft();
  }

  protected cancelSale(): void {
    if (!this.canOperate()) return;
    if (this.mercadoPagoOrderActive()) {
      this.errorMessage.set('Hay un cobro de Mercado Pago activo. Consultá o cancelá la orden antes de cancelar el ticket.');
      return;
    }
    if (!window.confirm('¿Querés cancelar la venta y quitar todos sus productos?')) return;
    if (!this.isDemoPriceList() && this.selectedPriceListId()) {
      this.queueDraftOperation('cancel');
    } else this.clearDocumentChoice();
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
    this.restoreDocumentChoice(slot);
    this.lines.set([]);
    this.discountDraft.set('0');
    this.discountReason.set('');
    this.persistedLines.set([]);
    this.persistedDiscountAmount.set(0);
    this.persistedDiscountReason.set('');
    this.draftId.set('');
    this.mercadoPagoIntent.set(null);
    this.mercadoPagoQrImage.set(null);
    this.mercadoPagoLoadState.set('idle');
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
    this.adjustmentQuantity.set((event.target as HTMLInputElement).value);
  }

  protected updateAdjustmentProduct(event: Event): void {
    this.adjustmentProductId.set((event.target as HTMLSelectElement).value);
  }

  protected updateAdjustmentReason(event: Event): void {
    this.adjustmentReason.set((event.target as HTMLInputElement).value.slice(0, 240));
  }

  protected submitStockAdjustment(): void {
    if (!this.canOperate()) return;
    const quantityDelta = parseLocalizedDecimal(this.adjustmentQuantity(), 3) ?? NaN;
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
    if (this.liveDataProblem()) {
      this.errorMessage.set(this.liveDataProblem());
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
    this.selectedPayments.set([this.newPayment('cash', this.subtotal())]);
    this.checkout.set(null);
    this.legacyPayment.set(false);
    this.checkoutLoadState.set('loading');
    this.checkoutCommitKey = null;
    this.mercadoPagoIntentsByOperation.set({});
    this.mercadoPagoPollIndex = 0;
    this.accountChargeDraft.set('0');
    this.creditAppliedDraft.set('0');
    this.creditCustomerId.set('');
    this.creditCustomerAccount.set(null);
    this.creditCustomerAccountStatus.set('idle');
    this.creditCustomerSearch.set('');
    this.creditCustomerOptions.set([]);
    this.creditCustomerLoadError.set(null);
    this.recipientName.set('');
    this.recipientDocumentNumber.set('');
    this.recipientAddress.set('');
    this.recipientLocality.set('');
    this.recipientProvince.set('');
    this.purchaseOrderNumber.set('');
    this.fiscalCustomerId.set('');
    this.fiscalCustomerSearch.set('');
    this.fiscalCustomerOptions.set([]);
    this.fiscalCustomerError.set(null);
    this.fiscalRegistryNotice.set(null);
    this.fiscalRegistryLoading.set(false);
    this.fiscalLookupRequestId++;
    if (this.documentType() !== 'nonFiscalTicket') this.searchFiscalCustomers();
    if (this.canChargeToAccount()) this.searchCreditCustomers();
    this.checkoutError.set(null);
    this.checkoutNotice.set(true);
    this.clearMercadoPagoPoll();
    this.mercadoPagoIntent.set(null);
    this.mercadoPagoQrImage.set(null);
    this.mercadoPagoMessage.set(null);
    this.mercadoPagoLoadState.set('loading');
    this.gatewayLoadState.set('loading');
    this.availableMercadoPagoModes.set([]);
    const gatewayRequestId = ++this.gatewayRequestId;
    this.integratedGatewayClient.available().subscribe({
      next: ({ gateways }) => {
        if (!this.checkoutNotice() || gatewayRequestId !== this.gatewayRequestId) return;
        const modes = gateways.find((gateway) => gateway.provider === 'mercadoPago')?.modes ?? [];
        this.availableMercadoPagoModes.set(modes);
        if (!this.mercadoPagoIntent() && !modes.includes(this.mercadoPagoMode()) && modes.length > 0)
          this.mercadoPagoMode.set(modes[0]);
        this.gatewayLoadState.set('ready');
      },
      error: () => {
        if (!this.checkoutNotice() || gatewayRequestId !== this.gatewayRequestId) return;
        this.gatewayLoadState.set('error');
      },
    });
    const openedDraftId = this.draftId();
    this.saleCheckoutClient.current(openedDraftId).subscribe({
      next: (checkout) => {
        if (!this.checkoutNotice() || this.draftId() !== openedDraftId) return;
        this.checkoutLoadState.set('ready');
        if (checkout) {
          this.restoreCheckout(checkout);
          this.mercadoPagoLoadState.set('ready');
        } else this.recoverLegacyMercadoPago(openedDraftId);
      },
      error: () => {
        this.checkoutLoadState.set('error');
        this.mercadoPagoLoadState.set('error');
        this.checkoutError.set('No se pudo recuperar el proceso de cobro. Cerrá y volvé a abrir el ticket.');
      },
    });
  }

  private recoverLegacyMercadoPago(draftId: string): void {
    this.mercadoPagoClient.get(draftId).subscribe({
      next: (intent) => {
        this.mercadoPagoLoadState.set('ready');
        if (!intent) return;
        this.legacyPayment.set(true);
        this.acceptMercadoPagoIntent(intent);
        const rest = Math.round((this.subtotal() - intent.amount) * 100) / 100;
        this.selectedPayments.set([this.newPayment('mercadoPago', intent.amount),
          ...(rest > 0 ? [this.newPayment('cash', rest)] : [])]);
      },
      error: () => {
        this.mercadoPagoLoadState.set('error');
        this.mercadoPagoMessage.set('No se pudo recuperar el cobro anterior de Mercado Pago.');
      },
    });
  }

  private newPayment(method: SalePaymentMethod, amount: number): SelectedPayment {
    return { id: crypto.randomUUID(), method, amount };
  }

  private restoreCheckout(checkout: SaleCheckout): void {
    this.checkout.set(checkout);
    this.checkoutCommitKey = checkout.commitKey;
    this.legacyPayment.set(false);
    this.selectedPayments.set(checkout.instruments.map((item) => ({
      id: item.id, method: item.method, amount: item.amount, reference: item.reference,
      status: item.status, pointPaymentIntentId: item.pointPaymentIntentId,
    })));
    const terms = checkout.terms;
    this.accountChargeDraft.set(String(terms.accountChargeAmount));
    this.creditAppliedDraft.set(String(terms.creditAppliedAmount));
    this.creditCustomerId.set(terms.customerId ?? '');
    if (terms.customerId && this.creditCustomerAccount()?.customerId !== terms.customerId) {
      this.creditCustomerAccountStatus.set('loading');
      this.creditCustomerClient.account(terms.customerId).subscribe({
        next: (account) => {
          if (this.creditCustomerId() === terms.customerId) {
            this.creditCustomerAccount.set(account);
            this.creditCustomerAccountStatus.set('idle');
          }
        },
        error: () => this.creditCustomerAccountStatus.set('error'),
      });
    }
    this.documentType.set(terms.documentType as SaleDocumentType);
    this.recipientTaxStatus.set(terms.recipientTaxStatus as SaleRecipientTaxStatus);
    this.recipientName.set(terms.recipientName ?? '');
    this.recipientDocumentNumber.set(terms.recipientDocumentNumber ?? '');
    this.recipientAddress.set(terms.recipientAddress ?? '');
    this.recipientLocality.set(terms.recipientLocality ?? '');
    this.recipientProvince.set(terms.recipientProvince ?? '');
    this.fiscalCustomerId.set(terms.fiscalCustomerId ?? '');
    this.purchaseOrderNumber.set(terms.purchaseOrderNumber ?? '');
    for (const payment of checkout.instruments.filter((item) => item.pointPaymentIntentId)) {
      this.mercadoPagoClient.checkoutPayment(checkout.saleDraftId, checkout.id, payment.id).subscribe({
        next: (intent) => { if (intent) this.acceptMercadoPagoIntent(intent, payment.id); },
        error: () => this.mercadoPagoMessage.set('No se pudo recuperar una orden de Mercado Pago.'),
      });
    }
  }

  private checkoutTerms() {
    return {
      customerId: this.creditCustomerId() || null,
      accountChargeAmount: this.accountChargeAmount(),
      creditAppliedAmount: this.creditAppliedAmount(),
      accountChargeConfirmed: this.accountChargeAmount() > 0,
      documentType: this.documentType(), recipientTaxStatus: this.recipientTaxStatus(),
      recipientName: this.recipientName().trim() || null,
      recipientDocumentNumber: this.recipientDocumentNumber().trim() || null,
      recipientAddress: this.recipientAddress().trim() || null,
      recipientLocality: this.recipientLocality().trim() || null,
      recipientProvince: this.recipientProvince().trim() || null,
      fiscalCustomerId: this.fiscalCustomerId() || null,
      purchaseOrderNumber: this.purchaseOrderNumber().trim() || null,
    };
  }

  private async ensureCheckoutCommitted(): Promise<SaleCheckout> {
    const draftId = this.draftId();
    if (!draftId) throw new Error('No hay un ticket guardado para cobrar.');
    const remote = await firstValueFrom(this.saleCheckoutClient.current(draftId));
    if (remote && remote.status !== 'Prepared') {
      this.restoreCheckout(remote);
      if (remote.status === 'Canceled' || remote.status === 'Completed')
        throw new Error('El proceso de cobro ya terminó. Recuperá el ticket.');
      return remote;
    }
    const prepared = await firstValueFrom(this.saleCheckoutClient.prepare(draftId,
      remote?.revision ?? 0, this.checkoutTerms(), this.selectedPayments().map((payment) => ({
        id: payment.id ?? crypto.randomUUID(), method: payment.method,
        amount: payment.amount, reference: payment.reference,
      }))));
    this.restoreCheckout(prepared);
    const key = this.checkoutCommitKey ?? crypto.randomUUID();
    this.checkoutCommitKey = key;
    const committed = await firstValueFrom(this.saleCheckoutClient.commit(draftId,
      prepared.id, prepared.revision, key));
    this.restoreCheckout(committed);
    return committed;
  }

  private async refreshCheckout(): Promise<SaleCheckout | null> {
    if (!this.draftId()) return null;
    const checkout = await firstValueFrom(this.saleCheckoutClient.current(this.draftId()));
    if (checkout) this.restoreCheckout(checkout);
    return checkout;
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
    if (code === 'ARTICLE_RECONCILIATION_IN_PROGRESS') return 'El artículo está temporalmente bloqueado por un conteo físico. El ticket se conserva.';
    if (code === 'PIECE_RECONCILIATION_REQUIRED') return 'La pieza histórica está inmovilizada hasta su conciliación.';
    if (code === 'STOCK_RESERVATION_MISSING') return 'La reserva de stock cambió. Actualizá el ticket e intentá nuevamente.';
    if (code === 'PRICE_LIST_NOT_AVAILABLE') return 'La lista de precios ya no está habilitada para esta sucursal.';
    if (code === 'PRODUCT_PRICE_NOT_AVAILABLE') return 'Hay un producto sin precio vigente en esta lista.';
    if (code === 'PRODUCT_NOT_AVAILABLE') return 'Uno de los productos ya no está disponible para la venta.';
    return error.status === 0
      ? 'No hay conexión con el servidor. El detalle se conserva; podés reintentar.'
      : `No se pudo guardar el ticket (HTTP ${error.status}). El detalle se conserva; podés reintentar.`;
  }

  protected formatQuantityDraft(): string {
    const quantity = parseLocalizedDecimal(this.quantityDraft(), 3);
    return quantity === null ? this.quantityDraft() : formatLocalizedDecimal(quantity, 3);
  }

  private decimalOrZero(value: string, fractionDigits: number): number {
    return value.trim() === '' ? 0 : parseLocalizedDecimal(value, fractionDigits) ?? NaN;
  }

  protected updateOpeningCash(event: Event): void {
    this.shiftOpeningCash.set((event.target as HTMLInputElement).value);
  }

  protected openShiftDialog(): void {
    if (!this.terminal()) return;
    this.shiftDialog.set(true);
    this.shiftError.set(null);
    this.loadCashierShift();
  }

  protected openCashierShift(): void {
    if (!this.terminal()) return;
    const amount = parseLocalizedDecimal(this.shiftOpeningCash(), 2) ?? NaN;
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
    if (this.checkoutCommitted()) {
      (event.target as HTMLInputElement).checked = this.hasPayment(method);
      return;
    }
    if (method === 'mercadoPago' && (this.mercadoPagoOrderActive() ||
        checked && this.availableMercadoPagoModes().length === 0)) {
      (event.target as HTMLInputElement).checked = this.hasPayment(method);
      return;
    }
    this.selectedPayments.update((payments) => checked
      ? [...payments, this.newPayment(method, method === 'cash' ? this.dueNow() :
        Math.max(0, Math.round((this.dueNow() - payments.reduce((sum, item) => sum + item.amount, 0)) * 100) / 100))]
      : payments.filter((payment) => payment.method !== method));
    this.checkoutError.set(null);
  }

  protected updatePaymentAmount(method: SalePaymentMethod, event: Event): void {
    if (this.checkoutCommitted()) return;
    const amount = parseLocalizedDecimal((event.target as HTMLInputElement).value, 2) ?? NaN;
    let updated = false;
    this.selectedPayments.update((payments) => payments.map((payment) => {
      if (payment.method !== method || updated) return payment;
      updated = true;
      return { ...payment, amount };
    }));
    this.checkoutError.set(null);
  }

  protected addPaymentOperation(method: SalePaymentMethod): void {
    if (this.checkoutCommitted()) return;
    const remaining = Math.max(0, Math.round((this.dueNow() -
      this.selectedPayments().reduce((sum, item) => sum + item.amount, 0)) * 100) / 100);
    this.selectedPayments.update((payments) => [...payments, this.newPayment(method, remaining)]);
  }

  protected updateOperationAmount(id: string, event: Event): void {
    if (this.checkoutCommitted()) return;
    const amount = parseLocalizedDecimal((event.target as HTMLInputElement).value, 2) ?? NaN;
    this.selectedPayments.update((payments) => payments.map((item) =>
      item.id === id ? { ...item, amount } : item));
  }

  protected updateOperationReference(id: string, event: Event): void {
    if (this.checkoutCommitted()) return;
    const reference = (event.target as HTMLInputElement).value;
    this.selectedPayments.update((payments) => payments.map((item) =>
      item.id === id ? { ...item, reference } : item));
  }

  protected removePaymentOperation(id: string): void {
    if (this.checkoutCommitted()) return;
    this.selectedPayments.update((payments) => payments.filter((item) => item.id !== id));
  }

  protected paymentIntent(id: string): MercadoPagoIntent | null {
    return this.mercadoPagoIntentsByOperation()[id] ?? null;
  }

  protected firstPayment(method: SalePaymentMethod): SelectedPayment | null {
    return this.selectedPayments().find((item) => item.method === method) ?? null;
  }

  protected async addReplacement(): Promise<void> {
    const checkout = this.checkout();
    if (!checkout || !this.draftId() || !this.hasRejectedPayment() || this.checkoutBusy()) return;
    this.checkoutBusy.set(true);
    try {
      const updated = await firstValueFrom(this.saleCheckoutClient.addReplacement(this.draftId(),
        checkout.id, this.newPayment(this.replacementMethod(), checkout.remainingAmount)));
      this.restoreCheckout(updated);
      this.checkoutError.set(null);
    } catch {
      this.checkoutError.set('No se pudo agregar el pago de reemplazo. Recuperá el cobro e intentá de nuevo.');
    } finally { this.checkoutBusy.set(false); }
  }

  protected selectMercadoPagoMode(event: Event): void {
    const mode = (event.target as HTMLSelectElement).value as MercadoPagoMode;
    if (this.availableMercadoPagoModes().includes(mode)) this.mercadoPagoMode.set(mode);
  }

  protected async startMercadoPago(operationId?: string): Promise<void> {
    const payment = this.mercadoPagoPayments().find((item) => operationId
      ? item.id === operationId : item.status !== 'Rejected' && item.status !== 'Approved');
    if (!payment || !this.draftId() || !Number.isFinite(payment.amount) || payment.amount <= 0 ||
        this.mercadoPagoBusy() || this.documentProblem() ||
        !this.availableMercadoPagoModes().includes(this.mercadoPagoMode())) return;
    this.mercadoPagoBusy.set(true);
    this.mercadoPagoMessage.set(null);
    try {
      const draftId = this.draftId();
      const intent = this.legacyPayment()
        ? await firstValueFrom(this.mercadoPagoClient.start(draftId,
          this.mercadoPagoMode(), payment.amount))
        : await (async () => {
          const checkout = await this.ensureCheckoutCommitted();
          const operation = checkout.instruments.find((item) => item.id === payment.id);
          if (!operation) throw new Error('El medio cambió durante la confirmación. Recuperá el cobro.');
          return firstValueFrom(this.mercadoPagoClient.executeCheckout(draftId,
            checkout.id, operation.id, this.mercadoPagoMode(), operation.amount));
        })();
      this.acceptMercadoPagoIntent(intent, payment.id);
      if (!this.legacyPayment()) await this.refreshCheckout();
    } catch (error) {
        const code = (error as HttpErrorResponse).error?.error?.code;
        this.mercadoPagoMessage.set(code === 'MERCADO_PAGO_REGISTER_NOT_CONFIGURED'
          ? 'Esta caja no tiene configurado ese modo de Mercado Pago en Administración.'
          : code === 'MERCADO_PAGO_STATIC_QR_UNAVAILABLE'
            ? 'No se pudo obtener el QR fijo de esta caja en Mercado Pago. Revisá su external_id y que esté activa en modo PDV.'
          : code === 'MERCADO_PAGO_AMOUNT_TOO_SMALL'
            ? 'Point requiere un cobro mínimo de $ 15. Aumentá el importe o elegí otro medio.'
          : code === 'MERCADO_PAGO_NOT_CONFIGURED' || code === 'MERCADO_PAGO_CREDENTIAL_UNAVAILABLE'
            ? 'Faltan credenciales de Mercado Pago o no se pueden leer.'
            : code === 'MERCADO_PAGO_ORDER_ACTIVE'
              ? 'Ya hay una orden activa con otro importe o modo. Consultá su estado.'
              : 'No se confirmó la creación de la orden. Reintentá con la misma operación o consultá su estado.');
        if (!this.legacyPayment()) await this.refreshCheckout().catch(() => undefined);
    } finally {
      this.mercadoPagoBusy.set(false);
    }
  }

  protected checkMercadoPago(operationId?: string): void {
    const intent = operationId
      ? this.mercadoPagoIntentsByOperation()[operationId] : this.mercadoPagoIntent();
    const draftId = this.draftId();
    if (!intent || !draftId || this.mercadoPagoBusy()) return;
    this.mercadoPagoBusy.set(true);
    this.mercadoPagoMessage.set(null);
    this.mercadoPagoClient.check(draftId, intent.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (updated) => {
        this.mercadoPagoBusy.set(false);
        if (this.checkoutNotice() && this.draftId() === draftId) {
          this.acceptMercadoPagoIntent(updated, operationId);
          if (this.checkout()) void this.refreshCheckout();
        }
      },
      error: () => {
        this.mercadoPagoBusy.set(false);
        if (this.checkoutNotice() && this.draftId() === draftId) {
          this.mercadoPagoMessage.set('No se pudo verificar el estado en Mercado Pago. No cierres la venta todavía.');
          this.scheduleMercadoPagoPoll(15000);
        }
      },
    });
  }

  protected cancelMercadoPago(operationId?: string): void {
    const intent = operationId
      ? this.mercadoPagoIntentsByOperation()[operationId] : this.mercadoPagoIntent();
    if (!intent || !this.draftId() || this.mercadoPagoBusy() || intent.approved) return;
    this.mercadoPagoBusy.set(true);
    this.mercadoPagoMessage.set(null);
    this.mercadoPagoClient.cancel(this.draftId(), intent.id).subscribe({
      next: (updated) => {
        this.mercadoPagoBusy.set(false);
        this.acceptMercadoPagoIntent(updated, operationId);
        if (this.checkout()) void this.refreshCheckout();
      },
      error: () => {
        this.mercadoPagoBusy.set(false);
        this.mercadoPagoMessage.set('No se pudo confirmar la cancelación. Consultá el pago antes de cambiar de medio.');
      },
    });
  }

  private acceptMercadoPagoIntent(intent: MercadoPagoIntent, operationId?: string): void {
    if (operationId) this.mercadoPagoIntentsByOperation.update((items) =>
      ({ ...items, [operationId]: intent }));
    if (operationId && operationId !== this.mercadoPagoPayments()[0]?.id) {
      this.scheduleMercadoPagoPoll();
      return;
    }
    this.mercadoPagoIntent.set(intent);
    this.mercadoPagoMode.set(intent.mode);
    this.mercadoPagoMessage.set(intent.approved ? 'Pago acreditado. Ya podés confirmar la venta.'
      : intent.status === 'Expired' || intent.status === 'Rejected' || intent.status === 'Canceled'
        ? 'La orden terminó sin cobro. Podés iniciar otra o elegir otro medio.'
        : 'Orden pendiente. El estado se actualiza automáticamente; también podés consultar el pago.');
    if (intent.qrData) {
      void QRCode.toDataURL(intent.qrData, { width: 240, margin: 2 }).then((image) => {
        if (this.mercadoPagoIntent()?.id === intent.id) this.mercadoPagoQrImage.set(image);
      }).catch(() => this.mercadoPagoMessage.set('No se pudo dibujar el QR. Consultá la orden.'));
    } else this.mercadoPagoQrImage.set(null);
    this.scheduleMercadoPagoPoll();
  }

  private clearMercadoPagoPoll(): void {
    if (this.mercadoPagoPollTimer !== null) clearTimeout(this.mercadoPagoPollTimer);
    this.mercadoPagoPollTimer = null;
  }

  private scheduleMercadoPagoPoll(delayMs = 5000): void {
    this.clearMercadoPagoPoll();
    if (!this.checkoutNotice() || !this.draftId()) return;
    const pending = Object.entries(this.mercadoPagoIntentsByOperation()).filter(([, intent]) =>
      !intent.approved && !['Rejected', 'Expired', 'Canceled', 'Refunded'].includes(intent.status));
    if (pending.length === 0 && (!this.mercadoPagoOrderActive() || this.mercadoPagoIntent()?.approved)) return;
    this.mercadoPagoPollTimer = setTimeout(() => {
      this.mercadoPagoPollTimer = null;
      if (this.mercadoPagoBusy()) this.scheduleMercadoPagoPoll();
      else this.checkMercadoPago(pending.length > 0
        ? pending[this.mercadoPagoPollIndex++ % pending.length][0] : undefined);
    }, delayMs);
  }

  protected paymentAmount(method: SalePaymentMethod): number {
    return this.selectedPayments().find((payment) => payment.method === method)?.amount ?? 0;
  }

  protected hasPayment(method: SalePaymentMethod): boolean {
    return this.selectedPayments().some((payment) => payment.method === method);
  }

  protected selectDocumentType(event: Event): void {
    this.documentType.set((event.target as HTMLSelectElement).value as SaleDocumentType);
    this.rememberDocumentChoice();
    this.checkoutError.set(null);
  }

  protected selectRecipientTaxStatus(event: Event): void {
    this.recipientTaxStatus.set((event.target as HTMLSelectElement).value as SaleRecipientTaxStatus);
    this.fiscalCustomerId.set('');
    this.rememberDocumentChoice();
    this.checkoutError.set(null);
  }

  protected documentTypeName(): string {
    return { nonFiscalTicket: 'Ticket no fiscal', fiscalTicket: 'Ticket fiscal',
      electronicInvoice: 'Factura electrónica' }[this.documentType()];
  }

  protected recipientTaxStatusName(): string {
    return { finalConsumer: 'Consumidor final', registered: 'Responsable inscripto',
      smallTaxpayer: 'Monotributista', exempt: 'Exento' }[this.recipientTaxStatus()];
  }

  protected updateRecipientName(event: Event): void {
    this.recipientName.set((event.target as HTMLInputElement).value.slice(0, 201));
    this.fiscalCustomerId.set('');
  }

  protected updateRecipientDocumentNumber(event: Event): void {
    this.recipientDocumentNumber.set((event.target as HTMLInputElement).value.slice(0, 20));
    this.fiscalCustomerId.set('');
  }

  protected updateRecipientAddress(event: Event): void {
    this.recipientAddress.set((event.target as HTMLInputElement).value.slice(0, 201));
    this.fiscalCustomerId.set('');
  }

  protected updateRecipientLocality(event: Event): void {
    this.recipientLocality.set((event.target as HTMLInputElement).value.slice(0, 101));
    this.fiscalCustomerId.set('');
  }

  protected updateRecipientProvince(event: Event): void {
    this.recipientProvince.set((event.target as HTMLInputElement).value.slice(0, 101));
    this.fiscalCustomerId.set('');
  }

  protected updatePurchaseOrderNumber(event: Event): void {
    this.purchaseOrderNumber.set((event.target as HTMLInputElement).value.slice(0, 81));
  }

  protected updateFiscalCustomerSearch(event: Event): void {
    this.fiscalCustomerSearch.set((event.target as HTMLInputElement).value.slice(0, 100));
  }

  protected searchFiscalCustomers(): void {
    this.fiscalCustomerLoading.set(true);
    this.fiscalCustomerError.set(null);
    this.creditCustomerClient.searchFiscal(this.fiscalCustomerSearch().trim()).subscribe({
      next: (customers) => {
        this.fiscalCustomerOptions.set(customers);
        this.fiscalCustomerLoading.set(false);
      },
      error: () => {
        this.fiscalCustomerLoading.set(false);
        this.fiscalCustomerError.set('No se pudieron consultar las fichas fiscales.');
      },
    });
  }

  protected lookupFiscalCuit(): void {
    const search = this.fiscalCustomerSearch().trim().replace(/[- ]/g, '');
    const entered = this.recipientDocumentNumber().trim().replace(/[- ]/g, '');
    const cuit = /^\d{11}$/.test(search) ? search : entered;
    if (!this.validCuit(cuit)) {
      this.fiscalRegistryNotice.set('Ingresá un CUIT válido para consultar ARCA.');
      return;
    }
    const before = [this.documentType(), this.recipientTaxStatus(), this.fiscalCustomerSearch(),
      this.recipientName(), this.recipientDocumentNumber(), this.recipientAddress(),
      this.recipientLocality(), this.recipientProvince()].join('\u0000');
    const requestId = ++this.fiscalLookupRequestId;
    this.fiscalRegistryLoading.set(true);
    this.fiscalRegistryNotice.set(null);
    this.creditCustomerClient.lookupFiscal(cuit).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (result) => {
        if (requestId !== this.fiscalLookupRequestId || !this.checkoutNotice() ||
            this.checkoutCommitted()) return;
        this.fiscalRegistryLoading.set(false);
        const current = [this.documentType(), this.recipientTaxStatus(), this.fiscalCustomerSearch(),
          this.recipientName(), this.recipientDocumentNumber(), this.recipientAddress(),
          this.recipientLocality(), this.recipientProvince()].join('\u0000');
        if (current !== before) {
          this.fiscalRegistryNotice.set('Los datos se editaron durante la consulta; no se reemplazaron.');
          return;
        }
        this.recipientName.set(result.name ?? this.recipientName());
        this.recipientDocumentNumber.set(result.documentNumber);
        this.recipientAddress.set(result.address ?? this.recipientAddress());
        this.recipientLocality.set(result.locality ?? this.recipientLocality());
        this.recipientProvince.set(result.province ?? this.recipientProvince());
        if (result.taxStatus) this.recipientTaxStatus.set(result.taxStatus);
        this.fiscalCustomerId.set(result.source === 'local' ? result.customerId ?? '' : '');
        if (result.source === 'local' && result.customerId && result.customerCode && result.taxStatus) {
          const option: FiscalCustomerOption = {
            id: result.customerId, code: result.customerCode, name: result.name ?? '',
            fiscalTaxStatus: result.taxStatus, fiscalDocumentNumber: result.documentNumber,
            fiscalAddress: result.address, fiscalLocality: result.locality,
            fiscalProvince: result.province,
          };
          this.fiscalCustomerOptions.update((options) =>
            [option, ...options.filter((item) => item.id !== option.id)]);
        }
        this.fiscalRegistryNotice.set(result.notice ?? (result.source === 'local'
          ? 'Ficha fiscal local encontrada.' : 'Datos de ARCA cargados; verificá antes de emitir.'));
        this.rememberDocumentChoice();
      },
      error: () => {
        if (requestId !== this.fiscalLookupRequestId || !this.checkoutNotice() ||
            this.checkoutCommitted()) return;
        this.fiscalRegistryLoading.set(false);
        this.fiscalRegistryNotice.set('No se pudo consultar el CUIT. Completá los datos manualmente.');
      },
    });
  }

  protected selectFiscalCustomer(event: Event): void {
    const id = (event.target as HTMLSelectElement).value;
    const customer = this.fiscalCustomerOptions().find((option) => option.id === id);
    this.fiscalCustomerId.set(customer?.id ?? '');
    if (!customer) return;
    this.recipientTaxStatus.set(customer.fiscalTaxStatus);
    this.recipientName.set(customer.name);
    this.recipientDocumentNumber.set(customer.fiscalDocumentNumber ?? '');
    this.recipientAddress.set(customer.fiscalAddress ?? '');
    this.recipientLocality.set(customer.fiscalLocality ?? '');
    this.recipientProvince.set(customer.fiscalProvince ?? '');
    this.rememberDocumentChoice();
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
      this.selectedPayments.set(newDueNow === 0 ? [] : [
        { ...payments[0], amount: newDueNow }]);
    }
  }

  protected async confirmSale(): Promise<void> {
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
    try {
      if (this.legacyPayment()) {
        const sale = await firstValueFrom(this.saleDraftClient.confirm(this.draftId(),
          payments.map(({ method, amount }) => ({ method, amount })),
          accountCharge > 0 || creditApplied > 0
            ? { customerId: this.creditCustomerId(), amount: accountCharge, creditAppliedAmount: creditApplied }
            : undefined,
          { documentType: this.documentType(), recipientTaxStatus: this.recipientTaxStatus(),
            recipientName: this.recipientName().trim() || undefined,
            recipientDocumentNumber: this.recipientDocumentNumber().trim() || undefined,
            recipientAddress: this.recipientAddress().trim() || undefined,
            recipientLocality: this.recipientLocality().trim() || undefined,
            recipientProvince: this.recipientProvince().trim() || undefined,
            purchaseOrderNumber: this.purchaseOrderNumber().trim() || undefined,
            fiscalCustomerId: this.fiscalCustomerId() || undefined },
          this.mercadoPagoIntent()?.approved ? this.mercadoPagoIntent()!.id : undefined));
        this.finishConfirmedSale(sale);
        return;
      }
      let checkout = await this.ensureCheckoutCommitted();
      for (const operation of checkout.instruments.filter((item) =>
        item.status === 'Prepared' && item.method !== 'mercadoPago')) {
        checkout = await firstValueFrom(this.saleCheckoutClient.approve(this.draftId(),
          checkout.id, operation.id, operation.reference));
        this.restoreCheckout(checkout);
      }
      if (checkout.status !== 'Ready') {
        this.checkoutError.set(checkout.instruments.some((item) => item.status === 'Uncertain')
          ? 'Hay un pago con resultado incierto. Consultá su estado antes de continuar.'
          : `Venta pendiente: faltan ${this.formatMoney(checkout.remainingAmount)} por acreditar. El turno seguirá abierto.`);
        return;
      }
      const sale = await firstValueFrom(this.saleCheckoutClient.confirm(this.draftId(), checkout.id));
      this.finishConfirmedSale(sale);
    } catch (response) {
      const error = response as HttpErrorResponse;
      this.checkoutError.set(error.status === 409 || error.status === 400 || error.status === 403
        ? this.checkoutConflictMessage(error)
        : response instanceof Error && !(response instanceof HttpErrorResponse)
          ? response.message
          : 'No se pudo confirmar la venta. Los pagos aprobados siguen registrados; recuperá el cobro.');
      if (error.status === 409) { this.loadCashierShift(); this.loadPriceLists(); }
      await this.refreshCheckout().catch(() => undefined);
    } finally {
      this.checkoutBusy.set(false);
    }
  }

  protected closeCheckout(): void {
    if (this.checkoutBusy()) return;
    this.clearMercadoPagoPoll();
    this.checkoutNotice.set(false);
    this.checkoutError.set(null);
  }

  private finishConfirmedSale(sale: ConfirmedSale): void {
    this.checkoutBusy.set(false);
    this.clearMercadoPagoPoll();
    this.checkoutNotice.set(false);
    this.confirmedSale.set(sale);
    this.fiscalDocument.set(null);
    this.fiscalError.set(null);
    this.receiptPdfDownloaded.set(false);
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
    this.checkout.set(null);
    this.checkoutLoadState.set('idle');
    this.checkoutCommitKey = null;
    this.mercadoPagoIntentsByOperation.set({});
    this.mercadoPagoPollIndex = 0;
    this.mercadoPagoIntent.set(null);
    this.mercadoPagoQrImage.set(null);
    this.mercadoPagoLoadState.set('idle');
    this.accountChargeDraft.set('0');
    this.creditAppliedDraft.set('0');
    this.creditCustomerId.set('');
    this.creditCustomerAccount.set(null);
    this.creditCustomerAccountStatus.set('idle');
    this.clearDocumentChoice();
    this.recipientName.set('');
    this.recipientDocumentNumber.set('');
    this.recipientAddress.set('');
    this.recipientLocality.set('');
    this.recipientProvince.set('');
    this.purchaseOrderNumber.set('');
    this.fiscalCustomerId.set('');
    this.errorMessage.set(null);
    this.loadCashierShift();
    this.loadPriceLists();
    if (sale.documentType === 'fiscalTicket' || sale.documentType === 'electronicInvoice')
      this.issueFiscalDocument(sale);
    else this.printSerialReceipt(sale);
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
          this.printSerialReceipt(sale);
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
    this.receiptPdfDownloaded.set(false);
    this.receiptPdfError.set(null);
    const branch = this.session()?.context?.branchName ?? 'Sucursal';
    const terminal = this.terminal()?.name ?? 'Caja';
    const cashier = this.session()?.username ?? 'Cajero';
    const save = fiscal ? this.receiptPdfClient.save(sale, branch, terminal, cashier, fiscal)
      : this.receiptPdfClient.save(sale, branch, terminal, cashier);
    void save.then(() => this.receiptPdfDownloaded.set(true)).catch(() => {
      this.receiptPdfError.set('No se pudo descargar el comprobante. La venta sigue confirmada; podés reintentar.');
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
    void this.refreshDevices().then((devices) => {
      if (!devices.printer || (sale.documentType === 'electronicInvoice' &&
          devices.printer.kind === 'hasar')) return null;
      return fiscal ? this.posDeviceClient.print(sale, branch, terminal, cashier, fiscal)
        : this.posDeviceClient.print(sale, branch, terminal, cashier);
    }).then((result) => {
      if (result) this.serialPrintResult.set(result);
      else void this.refreshDevices().catch(() => undefined);
    }).catch((error: unknown) => {
      const detail = error instanceof Error ? error.message : String(error);
      if (detail.includes('PRINTER_NOT_CONFIGURED')) {
        this.devices.update((devices) => ({ ...devices, printer: null }));
        return;
      }
      const printerStatus = /HASAR_DEVICE_NOT_READY_([0-9A-F]{4})/i.exec(detail)?.[1];
      this.serialPrintError.set(detail.includes('HASAR_FISCAL_TICKET_B_ONLY')
        ? 'La venta y el CAE están registrados, pero esta Hasar sólo admite por ahora el ticket fiscal B para consumidor final. No se abrió un comprobante en COM8.'
        : detail.includes('HASAR_SALE_VAT_MISSING')
          ? 'La venta está registrada, pero falta una alícuota de IVA válida en el detalle para emitir el ticket fiscal Hasar.'
          : detail.includes('HASAR_SALE_RESULT_PENDING') || detail.includes('HASAR_RESULT_UNCERTAIN') ||
              detail.includes('DOCUMENT_CANCELED')
            ? 'La venta está registrada, pero la impresión Hasar no quedó confirmada. Revisá el papel y el estado del equipo; esta venta no se reenvía automáticamente para evitar duplicados.'
            : detail.includes('HASAR_DEVICE_NOT_READY') || detail.includes('HASAR_DOCUMENT_ALREADY_OPEN')
              ? `La Hasar no está lista o tiene un comprobante abierto${printerStatus ? ` (estado ${printerStatus})` : ''}. La venta sigue registrada; revisá el equipo antes de reintentar.`
              : 'La venta está registrada, pero no se pudo imprimir. Revisá la impresora configurada y el dispositivo antes de reintentar.');
    }).finally(() => this.serialPrintBusy.set(false));
  }

  protected paymentMethodName(method: SalePaymentMethod): string {
    return this.paymentMethods.find((item) => item.id === method)?.label ?? method;
  }

  private checkoutConflictMessage(error: HttpErrorResponse): string {
    const code = error.error?.error?.code;
    if (code === 'ARTICLE_RECONCILIATION_IN_PROGRESS') return 'El artículo está temporalmente bloqueado por un conteo físico. El ticket se conserva; reintentá después.';
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
    return this.stockLoadState() === 'ready'
      ? this.stockByProduct().get(product.id) ?? 0
      : undefined;
  }

  private refreshStock(): void {
    if (this.stockRefreshInFlight || !this.canOperate()) return;
    this.stockRefreshInFlight = true;
    const revision = ++this.stockRequestRevision;
    this.inventoryClient.stock().pipe(
      timeout(4000), takeUntilDestroyed(this.destroyRef),
      finalize(() => { this.stockRefreshInFlight = false; }),
    ).subscribe({
      next: (items) => {
        if (revision === this.stockRequestRevision) {
          this.stockByProduct.set(new Map(items.map((item) => [item.productId, item.available])));
          this.stockLoadState.set('ready');
        }
      },
      error: () => {
        if (revision === this.stockRequestRevision) this.stockLoadState.set('stale');
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
    pieceIdentifier: string | null = null, pieceCount: number | null = null): void {
    this.lines.update((lines) => {
      const existing = pieceId ? undefined : lines.find((line) => line.product.id === product.id && !line.inventoryPieceId);
      if (!existing) return [...lines, { id: crypto.randomUUID(), product, quantity,
        pieceCount,
        ...(pieceId ? { inventoryPieceId: pieceId } : {}),
        ...(pieceIdentifier ? { pieceIdentifier } : {}) }];
      return lines.map((line) => line.id === existing.id
        ? { ...line, quantity: this.roundQuantity(line.quantity + quantity),
            pieceCount: line.pieceCount != null && pieceCount != null
              ? line.pieceCount + pieceCount : null }
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
    if (this.liveDataProblem()) {
      this.draftStatus.set('error');
      this.errorMessage.set(this.liveDataProblem());
      return;
    }
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
          pieceCount: item.pieceCount ?? null,
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
    this.restoreDocumentChoice(this.activeTicketSlot());
  }

  private rememberActiveTicket(slot: SaleTicketSlot): void {
    const shift = this.cashierShift();
    if (!shift) return;
    try { window.localStorage.setItem(this.activeTicketStorageKey(shift.id), slot); }
    catch { /* The tickets themselves remain in PostgreSQL. */ }
  }

  private documentChoiceStorageKey(shiftId: string, slot: SaleTicketSlot): string {
    return `carnicerias:ticket-document:${this.terminal()?.id}:${shiftId}:${this.session()?.userId}:${slot}`;
  }

  private restoreDocumentChoice(slot: SaleTicketSlot): void {
    const shift = this.cashierShift();
    let choice: unknown;
    try {
      choice = shift ? JSON.parse(window.localStorage.getItem(this.documentChoiceStorageKey(shift.id, slot)) ?? 'null') : null;
    } catch { choice = null; }
    const saved = Array.isArray(choice) ? choice : [];
    const documentType = saved[0];
    const taxStatus = saved[1];
    this.documentType.set(documentType === 'fiscalTicket' || documentType === 'electronicInvoice'
      ? documentType : 'nonFiscalTicket');
    this.recipientTaxStatus.set(taxStatus === 'registered' || taxStatus === 'smallTaxpayer' || taxStatus === 'exempt'
      ? taxStatus : 'finalConsumer');
  }

  private rememberDocumentChoice(): void {
    const shift = this.cashierShift();
    if (!shift) return;
    try {
      window.localStorage.setItem(this.documentChoiceStorageKey(shift.id, this.activeTicketSlot()),
        JSON.stringify([this.documentType(), this.recipientTaxStatus()]));
    } catch { /* The sale remains usable if local storage is unavailable. */ }
  }

  private clearDocumentChoice(): void {
    const shift = this.cashierShift();
    if (shift) {
      try { window.localStorage.removeItem(this.documentChoiceStorageKey(shift.id, this.activeTicketSlot())); }
      catch { /* The current choice still resets below. */ }
    }
    this.documentType.set('nonFiscalTicket');
    this.recipientTaxStatus.set('finalConsumer');
  }
}
