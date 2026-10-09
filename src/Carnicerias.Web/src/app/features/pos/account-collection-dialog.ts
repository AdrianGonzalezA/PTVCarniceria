import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, input, OnInit, output, signal } from '@angular/core';
import { CreditCustomerAccount, CreditCustomerClient, CreditCustomerOption,
  CustomerCollectionReceipt, CustomerCollectionRequest } from '../../core/customers/credit-customer-client';

const methods = [
  { id: 'cash', label: 'Efectivo' },
  { id: 'debit', label: 'Débito' },
  { id: 'credit', label: 'Crédito' },
  { id: 'transfer', label: 'Transferencia' },
  { id: 'mercadoPago', label: 'Mercado Pago' },
] as const;

@Component({
  selector: 'app-account-collection-dialog',
  templateUrl: './account-collection-dialog.html',
})
export class AccountCollectionDialog implements OnInit {
  private readonly client = inject(CreditCustomerClient);
  readonly scopeKey = input.required<string>();
  readonly closed = output<void>();
  readonly completed = output<void>();
  protected readonly methods = methods;
  protected readonly search = signal('');
  protected readonly options = signal<readonly CreditCustomerOption[]>([]);
  protected readonly customer = signal<CreditCustomerOption | null>(null);
  protected readonly account = signal<CreditCustomerAccount | null>(null);
  protected readonly page = signal(1);
  protected readonly method = signal<string>('cash');
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

  protected readonly parsedAmount = computed(() => this.parseMoney(this.amount()));
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
        this.options.set(options);
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
    const id = (event.target as HTMLSelectElement).value;
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
    this.page.update((value) => value + delta);
    this.loadAccount();
  }

  protected updateAmount(event: Event): void { this.amount.set((event.target as HTMLInputElement).value); }
  protected updateMethod(event: Event): void { this.method.set((event.target as HTMLSelectElement).value); }
  protected updateAllocation(saleId: string, event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.allocations.update((current) => ({ ...current, [saleId]: value }));
  }

  protected setManual(event: Event): void {
    this.manual.set((event.target as HTMLInputElement).checked);
    this.allocations.set({});
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
      method: this.method(), amount, allocations };
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
        if (code === 'ACCOUNT_ALLOCATION_CONFLICT') {
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
          typeof item['method'] !== 'string' || typeof item['amount'] !== 'number' ||
          !Number.isFinite(item['amount']) || item['amount'] <= 0 ||
          !methods.some((method) => method.id === item['method']) ||
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
    this.allocations.set({});
    this.manual.set(false);
    this.page.set(1);
    this.loadAccount();
  }

  protected formatMoney(value: number): string {
    return value.toLocaleString('es-AR', { style: 'currency', currency: 'ARS' });
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
