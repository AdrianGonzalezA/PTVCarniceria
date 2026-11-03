import { HttpErrorResponse } from '@angular/common/http';
import { AfterViewInit, Component, ElementRef, inject, OnDestroy, OnInit, signal, ViewChild } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AdminAreaTabs } from './admin-area-tabs';
import { BarcodeLayoutClient, BarcodeProfile } from '../../core/inventory/barcode-layout-client';
import { InventoryClient, InventoryStockItem } from '../../core/inventory/inventory-client';
import { InventoryPieceClient, InventoryPieceEvent, InventoryPieceListItem,
  InventoryPieceReconciliationLease,
  InventoryPieceReceipt, ReconcilePieceRequest } from '../../core/inventory/inventory-piece-client';
import { CurrentSession, SessionClient } from '../../core/session/session-client';

@Component({
  selector: 'app-piece-receipt-page',
  imports: [RouterLink, AdminAreaTabs, ReactiveFormsModule],
  templateUrl: './piece-receipt-page.html',
  styleUrls: ['./admin-page.scss', './categories-page.scss', './piece-receipt-page.scss'],
})
export class PieceReceiptPage implements OnInit, AfterViewInit, OnDestroy {
  private readonly sessions = inject(SessionClient);
  private readonly router = inject(Router);
  private readonly layouts = inject(BarcodeLayoutClient);
  private readonly inventory = inject(InventoryClient);
  private readonly piecesClient = inject(InventoryPieceClient);
  private readonly formBuilder = inject(FormBuilder);
  @ViewChild('scanInput') private scanInput?: ElementRef<HTMLInputElement>;

  protected readonly session = signal<CurrentSession | null>(null);
  protected readonly profiles = signal<readonly BarcodeProfile[]>([]);
  protected readonly products = signal<readonly InventoryStockItem[]>([]);
  protected readonly stockItems = signal<readonly InventoryStockItem[]>([]);
  protected readonly pieces = signal<readonly InventoryPieceListItem[]>([]);
  protected readonly historicalPieces = signal<readonly InventoryPieceListItem[]>([]);
  protected readonly historicalPage = signal(1);
  protected readonly historicalTotal = signal(0);
  protected readonly historicalLoaded = signal(false);
  protected readonly loadingError = signal<string | null>(null);
  protected readonly saveError = signal<string | null>(null);
  protected readonly received = signal<InventoryPieceReceipt | null>(null);
  protected readonly saving = signal(false);
  protected readonly selectedPiece = signal<InventoryPieceListItem | null>(null);
  protected readonly pieceEvents = signal<readonly InventoryPieceEvent[]>([]);
  protected readonly pieceEventsError = signal<string | null>(null);
  protected readonly releaseReason = signal('');
  protected readonly releaseError = signal<string | null>(null);
  protected readonly releaseNotice = signal<string | null>(null);
  protected readonly releasing = signal(false);
  protected readonly reconciliationLease = signal<InventoryPieceReconciliationLease | null>(null);
  protected readonly reconciliationOutcome = signal<'available' | 'unavailable'>('available');
  protected readonly observedWeight = signal('');
  protected readonly countedOnHand = signal('');
  protected readonly evidenceReference = signal('');
  protected readonly reconciliationReason = signal('');
  protected readonly reconciliationError = signal<string | null>(null);
  protected readonly uncertainReconciliation = signal(false);
  protected readonly acquiringReconciliation = signal(false);
  protected readonly reconciling = signal(false);
  private renewalTimer: ReturnType<typeof setInterval> | null = null;
  private pendingReconciliationRequest: ReconcilePieceRequest | null = null;
  protected readonly form = this.formBuilder.nonNullable.group({
    barcodeProfileId: ['', Validators.required],
    productId: ['', Validators.required],
    sourceSystem: ['Frigorífico ciclo 2', [Validators.required, Validators.maxLength(120)]],
    identifierField: ['pro_identif', [Validators.required, Validators.maxLength(80)]],
    code: ['', [Validators.required, Validators.maxLength(80)]],
  });
  private pendingOperation: { signature: string; id: string } | null = null;

  ngAfterViewInit(): void {
    this.scanInput?.nativeElement.focus();
  }

  ngOnDestroy(): void {
    this.stopRenewal();
    const piece = this.selectedPiece();
    const lease = this.reconciliationLease();
    if (piece && lease) this.piecesClient.releaseReconciliationLease(piece.id, lease.lockId).subscribe();
  }

  ngOnInit(): void {
    this.sessions.current().subscribe({
      next: (session) => {
        if (!session.context?.permissions.includes('inventory.stock.manage')) {
          void this.router.navigateByUrl('/');
          return;
        }
        this.session.set(session);
        this.loadOptions();
        this.loadPieces();
      },
      error: () => void this.router.navigateByUrl('/'),
    });
  }

  private loadOptions(): void {
    this.layouts.profiles().subscribe({
      next: (profiles) => this.profiles.set(profiles),
      error: () => this.loadingError.set('No se pudieron cargar los perfiles de lectura.'),
    });
    this.inventory.stock().subscribe({
      next: (items) => {
        this.stockItems.set(items);
        this.products.set(items.filter(item => item.saleMode === 'weight'));
      },
      error: () => this.loadingError.set('No se pudieron cargar los artículos por peso.'),
    });
  }

  private loadPieces(): void {
    this.piecesClient.list().subscribe({
      next: (page) => this.pieces.set(page.items),
      error: () => this.loadingError.set('No se pudieron cargar las piezas recibidas.'),
    });
  }

  protected loadHistorical(page = this.historicalPage()): void {
    this.piecesClient.list(page, 20, 'HistoricalUnverified').subscribe({
      next: (result) => {
        this.historicalPieces.set(result.items);
        this.historicalPage.set(result.page);
        this.historicalTotal.set(result.total);
        this.historicalLoaded.set(true);
      },
      error: () => this.loadingError.set('No se pudieron consultar las piezas históricas pendientes.'),
    });
  }

  protected historicalPageCount(): number {
    return Math.ceil(this.historicalTotal() / 20);
  }

  protected openPiece(piece: InventoryPieceListItem): void {
    this.cancelReconciliation();
    this.reconciliationOutcome.set('available');
    this.observedWeight.set('');
    this.countedOnHand.set('');
    this.evidenceReference.set('');
    this.reconciliationReason.set('');
    this.reconciliationError.set(null);
    this.selectedPiece.set(piece);
    this.pieceEvents.set([]);
    this.pieceEventsError.set(null);
    this.releaseReason.set('');
    this.releaseError.set(null);
    this.piecesClient.events(piece.id).subscribe({
      next: (events) => {
        if (this.selectedPiece()?.id === piece.id) this.pieceEvents.set(events);
      },
      error: () => {
        if (this.selectedPiece()?.id === piece.id)
          this.pieceEventsError.set('No se pudo cargar la auditoría de esta pieza.');
      },
    });
  }

  protected closePiece(): void {
    if (this.releasing() || this.reconciling()) return;
    this.cancelReconciliation();
    this.selectedPiece.set(null);
  }

  protected beginReconciliation(): void {
    const piece = this.selectedPiece();
    if (!piece || piece.status !== 'HistoricalUnverified' || this.reconciliationLease() ||
        this.acquiringReconciliation()) return;
    this.reconciliationError.set(null);
    this.acquiringReconciliation.set(true);
    const lockId = crypto.randomUUID();
    this.piecesClient.acquireReconciliationLease(piece.id, lockId).subscribe({
      next: (lease) => {
        this.acquiringReconciliation.set(false);
        if (this.selectedPiece()?.id !== piece.id) {
          this.piecesClient.releaseReconciliationLease(piece.id, lease.lockId).subscribe();
          return;
        }
        this.reconciliationLease.set(lease);
        this.pendingReconciliationRequest = null;
        this.uncertainReconciliation.set(false);
        this.startRenewal(piece.id, lease.lockId);
      },
      error: (response: HttpErrorResponse) => {
        this.acquiringReconciliation.set(false);
        this.reconciliationError.set(response.error?.error?.code === 'ARTICLE_HAS_OPEN_TICKETS'
          ? 'Este artículo figura en un ticket abierto. Terminá o cancelá esos tickets antes del conteo.'
          : 'No se pudo iniciar el conteo. Verificá que el artículo no esté bloqueado en otra sesión.');
      },
    });
  }

  protected cancelReconciliation(): void {
    if (this.reconciling()) return;
    this.stopRenewal();
    const piece = this.selectedPiece();
    const lease = this.reconciliationLease();
    this.reconciliationLease.set(null);
    this.pendingReconciliationRequest = null;
    this.uncertainReconciliation.set(false);
    if (piece && lease) this.piecesClient.releaseReconciliationLease(piece.id, lease.lockId).subscribe();
  }

  protected finishReconciliation(): void {
    const piece = this.selectedPiece();
    const lease = this.reconciliationLease();
    const observed = this.parseWeight(this.observedWeight());
    const onHand = this.parseWeight(this.countedOnHand());
    const reason = this.reconciliationReason().trim();
    const evidence = this.evidenceReference().trim();
    if (!piece || !lease || this.reconciling() || !this.pendingReconciliationRequest &&
        (observed === null || onHand === null || !reason || reason.length > 240 ||
        !evidence || evidence.length > 512 ||
        this.reconciliationOutcome() === 'available' && observed !== piece.receivedWeightKg)) {
      this.reconciliationError.set('Ingresá el peso observado, el conteo físico total, un motivo y la referencia del acta. Para habilitarla, la pieza debe conservar su peso completo.');
      return;
    }
    this.reconciling.set(true);
    this.reconciliationError.set(null);
    this.pendingReconciliationRequest ??= {
      lockId: lease.lockId,
      operationId: crypto.randomUUID(),
      outcome: this.reconciliationOutcome(),
      observedWeightKg: observed!,
      countedOnHand: onHand!,
      reason,
      evidenceReference: evidence,
    };
    this.piecesClient.reconcile(piece.id, this.pendingReconciliationRequest).subscribe({
      next: (result) => {
        this.stopRenewal();
        this.reconciling.set(false);
        this.reconciliationLease.set(null);
        this.pendingReconciliationRequest = null;
        this.uncertainReconciliation.set(false);
        this.selectedPiece.set(null);
        this.releaseNotice.set(result.status === 'Available'
          ? 'Pieza verificada y habilitada. La cantidad inmovilizada volvió a estar disponible.'
          : 'Pieza conciliada como no disponible. La venta histórica no se modificó.');
        this.loadPieces();
        this.loadOptions();
        if (this.historicalLoaded()) this.loadHistorical();
      },
      error: (response: HttpErrorResponse) => {
        this.reconciling.set(false);
        const uncertain = response.status === 0 || response.status >= 500;
        this.uncertainReconciliation.set(uncertain);
        if (!uncertain) this.pendingReconciliationRequest = null;
        this.reconciliationError.set(response.error?.error?.code === 'RECONCILIATION_LEASE_EXPIRED'
          ? 'Venció el bloqueo temporal. Repetí el conteo desde el inicio.'
          : response.error?.error?.code === 'STOCK_COUNT_BELOW_PROTECTED'
            ? 'El conteo informado es menor que el stock reservado e inmovilizado. Revisá el conteo.'
            : uncertain
              ? 'No llegó una confirmación. Reintentá la misma conciliación sin cambiar los datos; el identificador de operación se conserva.'
              : 'No se pudo conciliar la pieza. Revisá los datos y reintentá.');
      },
    });
  }

  private startRenewal(pieceId: string, lockId: string): void {
    this.stopRenewal();
    this.renewalTimer = setInterval(() => {
      if (this.reconciliationLease()?.lockId !== lockId) return;
      this.piecesClient.renewReconciliationLease(pieceId, lockId).subscribe({
        next: (result) => {
          const current = this.reconciliationLease();
          if (current?.lockId === lockId)
            this.reconciliationLease.set({ ...current, expiresAtUtc: result.expiresAtUtc });
        },
        error: () => {
          this.stopRenewal();
          this.reconciliationLease.set(null);
          this.reconciliationError.set('Se perdió el bloqueo del artículo. No confirmes este conteo; iniciá uno nuevo.');
        },
      });
    }, 45_000);
  }

  private stopRenewal(): void {
    if (this.renewalTimer) clearInterval(this.renewalTimer);
    this.renewalTimer = null;
  }

  private parseWeight(value: string): number | null {
    const number = Number(value.trim().replace(',', '.'));
    return value.trim() && Number.isFinite(number) && number >= 0 &&
      Math.abs(Math.round(number * 1000) - number * 1000) < 0.000001 ? number : null;
  }

  protected releasePiece(): void {
    const piece = this.selectedPiece();
    const reason = this.releaseReason().trim();
    if (!piece || piece.status !== 'Reserved' || this.releasing()) return;
    if (!reason || reason.length > 240) {
      this.releaseError.set('Ingresá un motivo de hasta 240 caracteres.');
      return;
    }
    this.releasing.set(true);
    this.releaseError.set(null);
    this.piecesClient.release(piece.id, reason).subscribe({
      next: (result) => {
        this.releasing.set(false);
        this.selectedPiece.set(null);
        this.releaseNotice.set(result.discountReset
          ? 'La pieza quedó disponible. Se anuló el descuento del borrador porque el nuevo total no lo admite.'
          : 'La pieza quedó disponible y se quitó del borrador; los demás renglones se conservaron.');
        this.loadPieces();
      },
      error: (response: HttpErrorResponse) => {
        this.releasing.set(false);
        const code = response.error?.error?.code;
        this.releaseError.set(code === 'MERCADO_PAGO_ORDER_ACTIVE'
          ? 'Hay un pago activo o pendiente de conciliación. Resolvelo antes de liberar la pieza.'
          : code === 'PIECE_OPERATION_IN_PROGRESS' || code === 'SALE_ALREADY_CONFIRMED'
            ? 'La venta puede estar confirmándose. Actualizá el estado antes de reintentar.'
            : code === 'ADMINISTRATOR_REQUIRED'
              ? 'Solo un administrador de esta empresa puede liberar la reserva.'
              : 'No se pudo liberar la pieza. Actualizá su estado y volvé a intentar.');
      },
    });
  }

  protected receive(): void {
    if (this.saving() || this.form.invalid) {
      this.form.markAllAsTouched();
      this.saveError.set('Elegí perfil y artículo; completá origen, campo identificador y código.');
      return;
    }
    const raw = this.form.getRawValue();
    const request = {
      barcodeProfileId: raw.barcodeProfileId,
      productId: raw.productId,
      sourceSystem: raw.sourceSystem.trim(),
      identifierField: raw.identifierField.trim(),
      code: raw.code.trim(),
    };
    if (!request.sourceSystem || !request.identifierField || !request.code) {
      this.saveError.set('Origen, campo identificador y código no pueden estar vacíos.');
      return;
    }
    const signature = JSON.stringify(request);
    if (this.pendingOperation?.signature !== signature)
      this.pendingOperation = { signature, id: crypto.randomUUID() };
    this.saving.set(true);
    this.saveError.set(null);
    this.received.set(null);
    this.piecesClient.receive({ ...request, operationId: this.pendingOperation.id }).subscribe({
      next: (piece) => {
        this.saving.set(false);
        this.received.set(piece);
        this.form.controls.code.setValue('');
        this.pendingOperation = null;
        this.loadPieces();
        this.scanInput?.nativeElement.focus();
      },
      error: (response: HttpErrorResponse) => {
        this.saving.set(false);
        const code = response.error?.error?.code;
        this.saveError.set(code === 'PIECE_ALREADY_RECEIVED'
          ? 'Esa pieza ya fue recibida para el origen indicado; no se sumó stock.'
          : code === 'INVALID_BARCODE' || code === 'INVALID_PIECE_WEIGHT_OR_IDENTIFIER'
            ? 'El código, identificador o peso no coincide con el perfil guardado.'
            : code === 'IDENTIFIER_FIELD_NOT_IN_PROFILE'
              ? 'El campo identificador no existe en la fórmula del perfil.'
              : 'No se pudo registrar la pieza. Podés reintentar sin duplicar el ingreso.');
        this.scanInput?.nativeElement.focus();
      },
    });
  }

  protected formatWeight(weightKg: number): string {
    return weightKg.toLocaleString('es-AR', {
      minimumFractionDigits: 3, maximumFractionDigits: 3,
    });
  }

  protected statusLabel(status: string): string {
    return ({ Available: 'Disponible', Reserved: 'Reservada', Sold: 'Vendida',
      Transferred: 'Transferida', Consumed: 'Consumida',
      HistoricalUnverified: 'Histórica sin verificar',
      ReconciledUnavailable: 'Conciliada: no disponible' } as Record<string, string>)[status]
      ?? 'Estado desconocido';
  }

  protected formatEventTime(value: string): string {
    return new Date(value).toLocaleString('es-AR');
  }

  protected selectedStock(): InventoryStockItem | null {
    const piece = this.selectedPiece();
    return this.stockItems().find(product => product.productId === piece?.productId) ?? null;
  }
}
