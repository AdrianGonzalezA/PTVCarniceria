import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, DestroyRef, inject, input, OnInit, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import QRCode from 'qrcode';
import { CreditCustomerAccount, CreditCustomerClient, CreditCustomerOption,
  CustomerCollectionReceipt, CustomerCollectionRequest } from '../../core/customers/credit-customer-client';
import { IntegratedGatewayClient } from '../../core/sales/integrated-gateway-client';
import { MercadoPagoClient, MercadoPagoIntent, MercadoPagoMode } from '../../core/sales/mercado-pago-client';

const methods = [
  { id: 'cash', label: 'Efectivo' },
  { id: 'debit', label: 'Débito' },
  { id: 'credit', label: 'Crédito' },
  { id: 'transfer', label: 'Transferencia' },
  { id: 'mercadoPago', label: 'Mercado Pago' },
  { id: 'cheque', label: 'Cheque' },
] as const;
type CollectionPaymentMethod = typeof methods[number]['id'];
interface CollectionPayment { readonly method: CollectionPaymentMethod; readonly amount: number }
interface CollectionMercadoPagoDraft {
  readonly operationId: string;
  readonly customer: CreditCustomerOption;
  readonly amount: string;
  readonly page: number;
  readonly payments: readonly CollectionPayment[];
  readonly manual: boolean;
  readonly allocations: Readonly<Record<string, string>>;
}

@Component({
  selector: 'app-account-collection-dialog',
  templateUrl: './account-collection-dialog.html',
})
export class AccountCollectionDialog implements OnInit {
  private readonly client = inject(CreditCustomerClient);
  private readonly gatewayClient = inject(IntegratedGatewayClient);
  private readonly mercadoPagoClient = inject(MercadoPagoClient);
  private readonly destroyRef = inject(DestroyRef);
  readonly scopeKey = input.required<string>();
  readonly closed = output<void>();
  readonly completed = output<void>();
  protected readonly methods = methods;
  protected readonly search = signal('');
  protected readonly options = signal<readonly CreditCustomerOption[]>([]);
  protected readonly customer = signal<CreditCustomerOption | null>(null);
  protected readonly account = signal<CreditCustomerAccount | null>(null);
  protected readonly page = signal(1);
  protected readonly selectedPayments = signal<readonly CollectionPayment[]>([{ method: 'cash', amount: 0 }]);
  protected readonly availableMercadoPagoModes = signal<readonly MercadoPagoMode[]>([]);
  protected readonly mercadoPagoMode = signal<MercadoPagoMode>('qr');
  protected readonly mercadoPagoIntent = signal<MercadoPagoIntent | null>(null);
  protected readonly mercadoPagoBusy = signal(false);
  protected readonly mercadoPagoLoadState = signal<'idle' | 'loading' | 'ready' | 'error'>('idle');
  protected readonly mercadoPagoMessage = signal<string | null>(null);
  protected readonly mercadoPagoQrImage = signal<string | null>(null);
  protected readonly gatewayLoadState = signal<'idle' | 'loading' | 'ready' | 'error'>('idle');
  protected readonly mercadoPagoOrderActive = computed(() => !!this.mercadoPagoIntent() &&
    !['Rejected', 'Expired', 'Canceled', 'Refunded'].includes(this.mercadoPagoIntent()!.status));
  protected readonly visibleMethods = computed(() => methods.filter((item) =>
    item.id !== 'mercadoPago' || this.availableMercadoPagoModes().length > 0 || !!this.mercadoPagoIntent()));
  protected readonly visibleMercadoPagoModes = computed(() => {
    const modes = this.availableMercadoPagoModes();
    const current = this.mercadoPagoIntent()?.mode;
    return current && !modes.includes(current) ? [...modes, current] : modes;
  });
  protected readonly amount = signal('');
  protected readonly manual = signal(false);
  protected readonly allocations = signal<Readonly<Record<string, string>>>({});
  protected readonly busy = signal(false);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly receipt = signal<CustomerCollectionReceipt | null>(null);
  protected readonly pending = signal<(CustomerCollectionRequest & { readonly customerId: string }) | null>(null);
  private operationId: string = crypto.randomUUID();
  private customerSearchRevision = 0;
  private accountLoadRevision = 0;
  private mercadoPagoPollTimer: ReturnType<typeof setTimeout> | null = null;

  protected readonly parsedAmount = computed(() => this.parseMoney(this.amount()));
  protected readonly paymentBalance = computed(() => Math.max(0, Math.round(((this.parsedAmount() ?? 0) -
    this.selectedPayments().reduce((sum, payment) => sum + payment.amount, 0)) * 100) / 100));
  protected readonly changePreview = computed(() => Math.max(0, Math.round((
    this.selectedPayments().reduce((sum, payment) => sum + payment.amount, 0) -
    (this.parsedAmount() ?? 0)) * 100) / 100));
  protected readonly paymentSplitProblem = computed(() => {
    const amount = this.parsedAmount();
    if (amount === null || amount <= 0) return 'Ingresá un importe válido.';
    const payments = this.selectedPayments();
    if (payments.length === 0 || payments.some((payment) => !Number.isFinite(payment.amount) ||
        payment.amount <= 0 || Math.round(payment.amount * 100) !== payment.amount * 100))
      return 'Completá cada medio de pago con un importe válido.';
    const nonCash = payments.filter((payment) => payment.method !== 'cash')
      .reduce((sum, payment) => sum + payment.amount, 0);
    if (nonCash > amount) return 'Los medios sin efectivo no pueden superar el importe a cobrar.';
    if (this.paymentBalance() > 0) return 'Los medios de pago todavía no cubren el importe.';
    return null;
  });
  protected readonly allocatedAmount = computed(() => Object.values(this.allocations())
    .reduce((total, value) => total + (this.parseMoney(value) ?? 0), 0));
  protected readonly creditPreview = computed(() => {
    const amount = this.parsedAmount();
    if (amount === null) return 0;
    return Math.max(0, amount - (this.manual() ? this.allocatedAmount() : this.account()?.totalDebt ?? 0));
  });
  protected readonly problem = computed(() => {
    const amount = this.parsedAmount();
    if (!this.customer() || !this.account()) return 'Elegí un cliente y consultá su cuenta.';
    if (amount === null || amount <= 0 || amount > 9_999_999_999.99)
      return 'Ingresá un importe mayor a cero, con hasta dos decimales.';
    if (this.mercadoPagoLoadState() === 'loading') return 'Consultando el cobro previo de Mercado Pago.';
    if (this.mercadoPagoLoadState() === 'error') return 'No se pudo verificar el cobro previo de Mercado Pago.';
    if (this.paymentSplitProblem()) return this.paymentSplitProblem();
    const payments = this.selectedPayments();
    const mercadoPago = payments.find((payment) => payment.method === 'mercadoPago');
    if (mercadoPago && (!this.mercadoPagoIntent()?.approved ||
        this.mercadoPagoIntent()?.amount !== mercadoPago.amount))
      return 'Confirmá el cobro de Mercado Pago antes de registrar el recibo.';
    if (!mercadoPago && this.mercadoPagoOrderActive())
      return 'Hay una orden de Mercado Pago activa. Consultá su estado antes de cambiar de medio.';
    if (this.manual()) {
      const due = new Map(this.account()!.sales.map((sale) => [sale.saleId, sale.outstandingAmount]));
      for (const [saleId, value] of Object.entries(this.allocations())) {
        if (value.trim() === '') continue;
        const applied = this.parseMoney(value);
        if (applied === null || applied < 0 || applied > (due.get(saleId) ?? 0))
          return 'Revisá las asignaciones: no pueden superar el saldo de cada venta.';
      }
      if (this.allocatedAmount() > amount) return 'La asignación supera el importe recibido.';
    }
    return null;
  });

  ngOnInit(): void {
    this.restorePending();
    this.findCustomers();
    this.loadGateways();
    if (!this.pending()) this.restoreMercadoPagoDraft();
    this.destroyRef.onDestroy(() => this.clearMercadoPagoPoll());
  }

  protected updateSearch(event: Event): void {
    this.search.set((event.target as HTMLInputElement).value);
  }

  protected findCustomers(): void {
    const revision = ++this.customerSearchRevision;
    this.error.set(null);
    this.loading.set(true);
    this.client.searchAccounts(this.search().trim()).subscribe({
      next: (options) => {
        if (revision !== this.customerSearchRevision) return;
        const selected = this.customer();
        this.options.set(selected && !options.some((item) => item.id === selected.id)
          ? [selected, ...options] : options);
        this.loading.set(false);
      },
      error: () => {
        if (revision !== this.customerSearchRevision) return;
        this.options.set([]);
        this.loading.set(false);
        this.error.set('No se pudo buscar clientes. Reintentá.');
      },
    });
  }

  protected chooseCustomer(event: Event): void {
    if (this.mercadoPagoOrderActive() || this.mercadoPagoLoadState() === 'error' ||
        this.mercadoPagoLoadState() === 'loading') return;
    const id = (event.target as HTMLSelectElement).value;
    this.clearMercadoPagoDraft();
    this.clearMercadoPagoPoll();
    this.mercadoPagoIntent.set(null);
    this.accountLoadRevision++;
    this.customer.set(this.options().find((item) => item.id === id) ?? null);
    this.account.set(null);
    this.allocations.set({});
    this.page.set(1);
    this.operationId = crypto.randomUUID();
    if (id) this.loadAccount();
  }

  protected loadAccount(): void {
    const customer = this.customer();
    if (!customer) return;
    const revision = ++this.accountLoadRevision;
    this.account.set(null);
    this.error.set(null);
    this.loading.set(true);
    this.client.account(customer.id, this.page()).subscribe({
      next: (account) => {
        if (revision !== this.accountLoadRevision) return;
        this.account.set(account);
        this.allocations.set({});
        this.manual.set(false);
        this.loading.set(false);
      },
      error: () => {
        if (revision !== this.accountLoadRevision) return;
        this.loading.set(false);
        this.error.set('No se pudo consultar la cuenta. Reintentá.');
      },
    });
  }

  protected changePage(delta: number): void {
    if (this.mercadoPagoOrderActive()) return;
    this.page.update((value) => value + delta);
    this.loadAccount();
  }

  protected updateAmount(event: Event): void {
    if (this.mercadoPagoOrderActive()) return;
    const previous = this.parsedAmount();
    this.amount.set((event.target as HTMLInputElement).value);
    if (this.selectedPayments().length === 1 && this.selectedPayments()[0].method === 'cash' &&
        (this.selectedPayments()[0].amount === 0 || this.selectedPayments()[0].amount === previous))
      this.selectedPayments.set([{ method: 'cash', amount: this.parsedAmount() ?? 0 }]);
  }
  protected hasPayment(method: CollectionPaymentMethod): boolean {
    return this.selectedPayments().some((payment) => payment.method === method);
  }
  protected paymentAmount(method: CollectionPaymentMethod): number {
    return this.selectedPayments().find((payment) => payment.method === method)?.amount ?? 0;
  }
  protected togglePayment(method: CollectionPaymentMethod, event: Event): void {
    const checkbox = event.target as HTMLInputElement;
    if (this.mercadoPagoOrderActive() || method === 'mercadoPago' && checkbox.checked &&
        this.availableMercadoPagoModes().length === 0) {
      checkbox.checked = this.hasPayment(method);
      return;
    }
    this.selectedPayments.update((payments) => checkbox.checked
      ? [...payments, { method, amount: Math.max(0, Math.round(((this.parsedAmount() ?? 0) -
        payments.reduce((sum, payment) => sum + payment.amount, 0)) * 100) / 100) }]
      : payments.filter((payment) => payment.method !== method));
  }
  protected updatePaymentAmount(method: CollectionPaymentMethod, event: Event): void {
    if (method === 'mercadoPago' && this.mercadoPagoOrderActive()) return;
    const amount = Number((event.target as HTMLInputElement).value.replace(',', '.'));
    this.selectedPayments.update((payments) => payments.map((payment) =>
      payment.method === method ? { ...payment, amount } : payment));
    if (this.mercadoPagoOrderActive()) this.saveMercadoPagoDraft();
  }

  protected selectMercadoPagoMode(event: Event): void {
    const mode = (event.target as HTMLSelectElement).value as MercadoPagoMode;
    if (!this.mercadoPagoOrderActive() && this.availableMercadoPagoModes().includes(mode))
      this.mercadoPagoMode.set(mode);
  }

  protected startMercadoPago(): void {
    const customer = this.customer();
    const amount = this.paymentAmount('mercadoPago');
    if (!customer || !this.account() || this.paymentSplitProblem() || !this.hasPayment('mercadoPago') ||
        !Number.isFinite(amount) || amount <= 0 || amount > this.parsedAmount()! || this.mercadoPagoBusy() ||
        this.mercadoPagoLoadState() === 'error' || this.mercadoPagoOrderActive() ||
        !this.availableMercadoPagoModes().includes(this.mercadoPagoMode())) return;
    if (!this.saveMercadoPagoDraft()) return;
    this.mercadoPagoBusy.set(true);
    this.mercadoPagoMessage.set(null);
    this.mercadoPagoClient.startCollection(this.operationId, customer.id, this.mercadoPagoMode(), amount)
      .pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
        next: (intent) => {
          this.mercadoPagoBusy.set(false);
          this.acceptMercadoPagoIntent(intent);
        },
        error: (response: HttpErrorResponse) => {
          this.mercadoPagoBusy.set(false);
          const code = response.error?.error?.code;
          this.mercadoPagoMessage.set(code === 'MERCADO_PAGO_REGISTER_NOT_CONFIGURED'
            ? 'Esta caja no tiene configurado ese modo de Mercado Pago.'
            : code === 'MERCADO_PAGO_AMOUNT_TOO_SMALL'
              ? 'Point requiere un cobro mínimo de $ 15.'
              : code === 'MERCADO_PAGO_ORDER_ACTIVE'
                ? 'Ya hay una orden activa con otro importe o modo. Consultá su estado.'
                : 'No se confirmó la creación de la orden. Consultando si quedó registrada…');
          this.recoverMercadoPagoOrder();
        },
      });
  }

  protected checkMercadoPago(): void {
    const intent = this.mercadoPagoIntent();
    if (!intent || this.mercadoPagoBusy()) return;
    this.mercadoPagoBusy.set(true);
    this.mercadoPagoMessage.set(null);
    this.mercadoPagoClient.checkCollection(this.operationId, intent.id)
      .pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
        next: (updated) => {
          this.mercadoPagoBusy.set(false);
          if (this.mercadoPagoIntent()?.id === intent.id) this.acceptMercadoPagoIntent(updated);
        },
        error: () => {
          this.mercadoPagoBusy.set(false);
          this.mercadoPagoMessage.set('No se pudo verificar el estado en Mercado Pago. Consultá nuevamente.');
          this.scheduleMercadoPagoPoll(15000);
        },
      });
  }

  protected cancelMercadoPago(): void {
    const intent = this.mercadoPagoIntent();
    if (!intent || intent.approved || !this.mercadoPagoOrderActive() || this.mercadoPagoBusy()) return;
    this.mercadoPagoBusy.set(true);
    this.mercadoPagoMessage.set(null);
    this.mercadoPagoClient.cancelCollection(this.operationId, intent.id)
      .pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
        next: (updated) => {
          this.mercadoPagoBusy.set(false);
          this.acceptMercadoPagoIntent(updated);
        },
        error: () => {
          this.mercadoPagoBusy.set(false);
          this.mercadoPagoMessage.set('No se pudo confirmar la cancelación. Consultá el pago antes de cambiar de medio.');
        },
      });
  }
  protected updateAllocation(saleId: string, event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.allocations.update((current) => ({ ...current, [saleId]: value }));
    if (this.mercadoPagoOrderActive()) this.saveMercadoPagoDraft();
  }

  protected setManual(event: Event): void {
    this.manual.set((event.target as HTMLInputElement).checked);
    this.allocations.set({});
    if (this.mercadoPagoOrderActive()) this.saveMercadoPagoDraft();
  }

  protected collect(): void {
    const customer = this.customer();
    const amount = this.parsedAmount();
    if (!customer || amount === null || this.problem() || this.busy()) return;
    if (localStorage.getItem(this.storageKey())) {
      this.error.set('Existe un cobro anterior sin resolver en esta caja. Revisalo antes de iniciar otro.');
      return;
    }
    const allocations = this.manual()
      ? Object.entries(this.allocations())
          .filter(([, value]) => (this.parseMoney(value) ?? 0) > 0)
          .map(([saleId, value]) => ({ saleId, amount: this.parseMoney(value)! }))
      : undefined;
    const request = { customerId: customer.id, operationId: this.operationId,
      amount, allocations,
      payments: this.selectedPayments().map((payment) => ({ method: payment.method, amount: payment.amount })),
      mercadoPagoIntentId: this.mercadoPagoIntent()?.approved ? this.mercadoPagoIntent()!.id : undefined };
    try {
      localStorage.setItem(this.storageKey(), JSON.stringify(request));
    } catch {
      this.error.set('No se puede proteger el reintento en este Electron. Liberá espacio y volvé a intentar.');
      return;
    }
    this.pending.set(request);
    this.submitPending(request);
  }

  protected retryPending(): void {
    const pending = this.pending();
    if (pending) this.submitPending(pending);
  }

  private submitPending(request: CustomerCollectionRequest & { readonly customerId: string }): void {
    this.busy.set(true);
    this.error.set(null);
    this.client.collect(request.customerId, request).subscribe({
      next: (receipt) => {
        this.finishReceipt(receipt);
      },
      error: (response: HttpErrorResponse) => {
        this.busy.set(false);
        const code = response.error?.error?.code;
        if (code === 'ACCOUNT_ALLOCATION_CONFLICT' && !request.mercadoPagoIntentId) {
          this.clearPending();
          this.operationId = crypto.randomUUID();
          this.loadAccount();
        }
        this.error.set(code === 'ACCOUNT_ALLOCATION_CONFLICT'
          ? 'El saldo cambió o la asignación ya no es válida. Consultá la cuenta y armá un nuevo cobro.'
          : code === 'CASHIER_SHIFT_REQUIRED' ? 'El turno debe estar abierto para cobrar.'
          : code === 'IDEMPOTENCY_CONFLICT' ? 'La clave del cobro existe con otros datos. Revisá el recibo antes de continuar.'
          : 'No se pudo confirmar el cobro. Reintentá la misma operación para evitar duplicarlo.');
      },
    });
  }

  private finishReceipt(receipt: CustomerCollectionReceipt): void {
    this.clearPending();
    this.clearMercadoPagoDraft();
    this.clearMercadoPagoPoll();
    this.receipt.set(receipt);
    this.busy.set(false);
    this.completed.emit();
  }

  private restorePending(): void {
    try {
      const raw = localStorage.getItem(this.storageKey());
      if (!raw) return;
      const parsed: unknown = JSON.parse(raw);
      if (typeof parsed !== 'object' || parsed === null) return;
      const item = parsed as Record<string, unknown>;
      if (typeof item['customerId'] !== 'string' || typeof item['operationId'] !== 'string' ||
          typeof item['amount'] !== 'number' ||
          !Number.isFinite(item['amount']) || item['amount'] <= 0 ||
          (typeof item['method'] === 'string' ? !methods.some((method) => method.id === item['method']) :
            !Array.isArray(item['payments']) || item['payments'].length === 0 ||
            item['payments'].some((value: unknown) => typeof value !== 'object' || value === null ||
              !methods.some((method) => method.id === (value as Record<string, unknown>)['method']) ||
              typeof (value as Record<string, unknown>)['amount'] !== 'number')) ||
          (item['allocations'] !== undefined && (!Array.isArray(item['allocations']) ||
            item['allocations'].some((value: unknown) => typeof value !== 'object' || value === null ||
              typeof (value as Record<string, unknown>)['saleId'] !== 'string' ||
              typeof (value as Record<string, unknown>)['amount'] !== 'number')))) return;
      const pending = item as unknown as CustomerCollectionRequest & { readonly customerId: string };
      this.pending.set(pending);
      this.operationId = pending.operationId;
      this.client.findCollection(pending.operationId).subscribe({
        next: (receipt) => { if (receipt) this.finishReceipt(receipt); },
        error: () => this.error.set('No se pudo comprobar el cobro anterior. Reintentá la misma operación.'),
      });
    } catch {
      this.error.set('Hay un cobro pendiente que no se pudo leer. Revisá la caja antes de continuar.');
    }
  }

  private clearPending(): void {
    localStorage.removeItem(this.storageKey());
    this.pending.set(null);
  }

  private storageKey(): string { return `carnicerias:collection:${this.scopeKey()}`; }

  protected newCollection(): void {
    this.operationId = crypto.randomUUID();
    this.receipt.set(null);
    this.amount.set('');
    this.selectedPayments.set([{ method: 'cash', amount: 0 }]);
    this.mercadoPagoIntent.set(null);
    this.mercadoPagoQrImage.set(null);
    this.mercadoPagoMessage.set(null);
    this.allocations.set({});
    this.manual.set(false);
    this.page.set(1);
    this.loadAccount();
  }

  private loadGateways(): void {
    this.gatewayLoadState.set('loading');
    this.gatewayClient.available().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: ({ gateways }) => {
        const modes = gateways.find((gateway) => gateway.provider === 'mercadoPago')?.modes ?? [];
        this.availableMercadoPagoModes.set(modes);
        if (!this.mercadoPagoIntent() && !modes.includes(this.mercadoPagoMode()) && modes.length > 0)
          this.mercadoPagoMode.set(modes[0]);
        this.gatewayLoadState.set('ready');
      },
      error: () => {
        this.availableMercadoPagoModes.set([]);
        this.gatewayLoadState.set('error');
      },
    });
  }

  private saveMercadoPagoDraft(): boolean {
    const customer = this.customer();
    if (!customer) return false;
    const draft: CollectionMercadoPagoDraft = {
      operationId: this.operationId, customer, amount: this.amount(), page: this.page(),
      payments: this.selectedPayments(), manual: this.manual(), allocations: this.allocations(),
    };
    try {
      localStorage.setItem(this.mercadoPagoStorageKey(), JSON.stringify(draft));
      return true;
    } catch {
      this.mercadoPagoMessage.set('No se puede proteger el cobro ante un reinicio de Electron. Liberá espacio y reintentá.');
      return false;
    }
  }

  private restoreMercadoPagoDraft(): void {
    let raw: string | null;
    try { raw = localStorage.getItem(this.mercadoPagoStorageKey()); }
    catch { this.mercadoPagoLoadState.set('error'); return; }
    if (!raw) return;
    try {
      const value: unknown = JSON.parse(raw);
      if (!this.isMercadoPagoDraft(value)) throw new Error('Invalid collection draft');
      this.operationId = value.operationId;
      this.customer.set(value.customer);
      this.options.update((current) => current.some((item) => item.id === value.customer.id)
        ? current : [value.customer, ...current]);
      this.amount.set(value.amount);
      this.page.set(value.page);
      this.selectedPayments.set(value.payments);
      this.manual.set(value.manual);
      this.allocations.set(value.allocations);
      this.mercadoPagoLoadState.set('loading');
      this.loadAccountForRecovery(value.customer.id);
      this.client.findCollection(value.operationId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
        next: (receipt) => receipt ? this.finishReceipt(receipt) : this.recoverMercadoPagoOrder(),
        error: () => this.recoverMercadoPagoOrder(),
      });
    } catch {
      this.mercadoPagoLoadState.set('error');
      this.mercadoPagoMessage.set('No se pudo leer el cobro previo. Revisá esta caja antes de iniciar otro.');
    }
  }

  private isMercadoPagoDraft(value: unknown): value is CollectionMercadoPagoDraft {
    if (typeof value !== 'object' || value === null) return false;
    const item = value as Record<string, unknown>;
    const customer = item['customer'] as Record<string, unknown> | null;
    return typeof item['operationId'] === 'string' && !!customer &&
      typeof customer['id'] === 'string' && typeof customer['code'] === 'string' &&
      typeof customer['name'] === 'string' && typeof item['amount'] === 'string' &&
      Number.isInteger(item['page']) && (item['page'] as number) >= 1 &&
      typeof item['manual'] === 'boolean' && Array.isArray(item['payments']) &&
      item['payments'].length > 0 && item['payments'].some((payment: CollectionPayment) =>
        payment?.method === 'mercadoPago') && item['payments'].every((payment: CollectionPayment) =>
        payment && methods.some((method) => method.id === payment.method) &&
        typeof payment.amount === 'number' && Number.isFinite(payment.amount)) &&
      typeof item['allocations'] === 'object' && item['allocations'] !== null &&
      !Array.isArray(item['allocations']) &&
      Object.values(item['allocations']).every((allocation) => typeof allocation === 'string');
  }

  private loadAccountForRecovery(customerId: string): void {
    const revision = ++this.accountLoadRevision;
    this.loading.set(true);
    this.client.account(customerId, this.page()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (account) => {
        if (revision !== this.accountLoadRevision) return;
        this.account.set(account);
        this.loading.set(false);
      },
      error: () => {
        if (revision !== this.accountLoadRevision) return;
        this.loading.set(false);
        this.error.set('No se pudo consultar la cuenta. Reintentá la consulta antes de cobrar.');
      },
    });
  }

  private recoverMercadoPagoOrder(): void {
    this.mercadoPagoLoadState.set('loading');
    this.mercadoPagoClient.getCollection(this.operationId)
      .pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
        next: (intent) => {
          this.mercadoPagoLoadState.set('ready');
          if (intent) this.acceptMercadoPagoIntent(intent);
          else {
            this.clearMercadoPagoDraft();
            this.mercadoPagoMessage.set('No hay una orden activa para este cobro. Podés solicitarla nuevamente.');
          }
        },
        error: () => {
          this.mercadoPagoLoadState.set('error');
          this.mercadoPagoMessage.set('No se pudo verificar la orden previa. Reintentá la consulta antes de cobrar.');
        },
      });
  }

  protected retryMercadoPagoRecovery(): void { this.recoverMercadoPagoOrder(); }

  private acceptMercadoPagoIntent(intent: MercadoPagoIntent): void {
    this.mercadoPagoIntent.set(intent);
    this.mercadoPagoMode.set(intent.mode);
    this.mercadoPagoMessage.set(intent.approved ? 'Pago acreditado. Ya podés confirmar el cobro.'
      : ['Expired', 'Rejected', 'Canceled'].includes(intent.status)
        ? 'La orden terminó sin cobro. Podés solicitar otra o elegir otro medio.'
        : 'Orden pendiente. Se consulta automáticamente cada 5 segundos.');
    if (intent.qrData && !intent.approved) {
      void QRCode.toDataURL(intent.qrData, { width: 240, margin: 2 }).then((image) => {
        if (this.mercadoPagoIntent()?.id === intent.id) this.mercadoPagoQrImage.set(image);
      }).catch(() => this.mercadoPagoMessage.set('No se pudo dibujar el QR. Consultá la orden.'));
    } else this.mercadoPagoQrImage.set(null);
    if (!this.mercadoPagoOrderActive()) this.clearMercadoPagoDraft();
    this.scheduleMercadoPagoPoll();
  }

  private clearMercadoPagoPoll(): void {
    if (this.mercadoPagoPollTimer !== null) clearTimeout(this.mercadoPagoPollTimer);
    this.mercadoPagoPollTimer = null;
  }

  private scheduleMercadoPagoPoll(delayMs = 5000): void {
    this.clearMercadoPagoPoll();
    if (!this.mercadoPagoOrderActive() || this.mercadoPagoIntent()?.approved) return;
    this.mercadoPagoPollTimer = setTimeout(() => {
      this.mercadoPagoPollTimer = null;
      if (this.mercadoPagoBusy()) this.scheduleMercadoPagoPoll();
      else this.checkMercadoPago();
    }, delayMs);
  }

  private clearMercadoPagoDraft(): void {
    try { localStorage.removeItem(this.mercadoPagoStorageKey()); } catch { /* Preservar el recibo. */ }
  }

  private mercadoPagoStorageKey(): string { return `${this.storageKey()}:mercado-pago`; }

  protected formatMoney(value: number): string {
    return value.toLocaleString('es-AR', { style: 'currency', currency: 'ARS' });
  }

  protected paymentLabel(method: string): string {
    return methods.find((item) => item.id === method)?.label ?? method;
  }

  protected proposedAllocation(): number {
    return Math.min(this.parsedAmount() ?? 0, this.account()?.totalDebt ?? 0);
  }

  protected pageCount(): number {
    return Math.ceil((this.account()?.saleCount ?? 0) / 50);
  }

  private parseMoney(value: string): number | null {
    const normalized = value.trim();
    if (!/^\d+(?:[.,]\d{1,2})?$/.test(normalized)) return null;
    const parsed = Number(normalized.replace(',', '.'));
    return Number.isFinite(parsed) ? parsed : null;
  }
}
