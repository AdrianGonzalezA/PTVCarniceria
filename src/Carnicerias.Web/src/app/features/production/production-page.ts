import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ProductionBarcodeProfile, ProductionClassifier, ProductionClient, ProductionLine,
  ProductionLineInput, ProductionOrder, ProductionOrderPage, ProductionPiece,
  ProductionProduct, ProductionSummary } from '../../core/production/production-client';
import { CurrentSession, SessionClient } from '../../core/session/session-client';
import { formatLocalizedDecimal, parseLocalizedDecimal } from '../../shared/localized-decimal';
import { AdminDetailDialog } from '../admin/admin-detail-dialog';
import { ProductionLabelDialog } from './production-label-dialog';

@Component({
  selector: 'app-production-page',
  imports: [RouterLink, AdminDetailDialog, ProductionLabelDialog, DatePipe],
  templateUrl: './production-page.html',
  styleUrls: ['../admin/admin-page.scss', '../admin/categories-page.scss',
    '../admin/inventory-receipts-page.scss', './production-page.scss'],
})
export class ProductionPage implements OnInit {
  private readonly client = inject(ProductionClient);
  private readonly sessions = inject(SessionClient);
  private readonly router = inject(Router);
  private pendingCreate: { signature: string; operationId: string } | null = null;

  protected readonly session = signal<CurrentSession | null>(null);
  protected readonly home = computed(() => this.session()?.context?.permissions
    .includes('platform.users.manage') ? '/admin' : '/production');
  protected readonly classifiers = signal<readonly ProductionClassifier[]>([]);
  protected readonly products = signal<readonly ProductionProduct[]>([]);
  protected readonly profiles = signal<readonly ProductionBarcodeProfile[]>([]);
  protected readonly pieces = signal<readonly ProductionPiece[]>([]);
  protected readonly labelLine = signal<ProductionLine | null>(null);
  protected readonly page = signal<ProductionOrderPage | null>(null);
  protected readonly summary = signal<readonly ProductionSummary[]>([]);
  protected readonly selected = signal<ProductionOrder | null>(null);
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly classifierId = signal('');
  protected readonly status = signal<'Open' | 'Closed' | 'Canceled' | ''>('');
  protected readonly direction = signal<'Input' | 'Output'>('Input');
  protected readonly productId = signal('');
  protected readonly quantity = signal('');
  protected readonly massKg = signal('');
  protected readonly pieceQuery = signal('');
  protected readonly pieceId = signal('');
  protected readonly profileId = signal('');
  protected readonly sourceSystem = signal('PRODUCCION');
  protected readonly rawBarcode = signal('');
  protected readonly identifierField = signal('pro_identif');
  protected readonly wasteKg = signal('0');
  protected readonly wasteReason = signal('');
  protected readonly activeProduct = computed(() => this.products()
    .find(item => item.id === this.productId()) ?? null);
  protected readonly activeProfile = computed(() => this.profiles()
    .find(item => item.id === this.profileId()) ?? null);

  ngOnInit(): void {
    this.sessions.current().subscribe({
      next: session => {
        if (!session.context?.permissions.includes('production.operate')) {
          void this.router.navigateByUrl('/'); return;
        }
        this.session.set(session);
        this.client.classifiers(true).subscribe({
          next: rows => this.classifiers.set(rows),
          error: () => this.error.set('No se pudieron cargar los tipos de producción.'),
        });
        this.client.options().subscribe({
          next: options => { this.products.set(options.products);
            this.profiles.set(options.profiles); },
          error: () => this.error.set('No se pudieron cargar los artículos y perfiles.'),
        });
        this.loadPage(1);
        this.loadSummary();
      },
      error: () => void this.router.navigateByUrl('/'),
    });
  }

  protected signOut(): void {
    if (this.saving()) return;
    this.sessions.logout().subscribe({
      next: () => void this.router.navigateByUrl('/'),
      error: () => this.error.set('No se pudo cerrar sesión.'),
    });
  }

  protected loadPage(pageNumber: number): void {
    this.loading.set(true);
    this.client.list(pageNumber, this.status() || undefined).subscribe({
      next: result => { this.page.set(result); this.loading.set(false); },
      error: () => { this.loading.set(false);
        this.error.set('No se pudieron consultar las órdenes.'); },
    });
  }

  private loadSummary(): void {
    this.client.summary().subscribe({
      next: rows => this.summary.set(rows),
      error: () => this.error.set('No se pudo cargar el resumen de producción.'),
    });
  }

  protected filterStatus(value: string): void {
    if (value !== '' && value !== 'Open' && value !== 'Closed' && value !== 'Canceled') return;
    this.status.set(value);
    this.loadPage(1);
  }

  protected create(): void {
    if (!this.classifierId() || this.saving()) return;
    this.createOrder(this.classifierId(), null);
  }

  protected correct(): void {
    const order = this.selected();
    if (order?.status !== 'Closed' || this.saving()) return;
    this.createOrder(null, order.id);
  }

  private createOrder(classifierId: string | null, correctsOrderId: string | null): void {
    const signature = `${classifierId ?? ''}|${correctsOrderId ?? ''}`;
    if (this.pendingCreate?.signature !== signature) {
      this.pendingCreate = { signature, operationId: crypto.randomUUID() };
    }
    this.saving.set(true); this.error.set(null); this.notice.set(null);
    this.client.create({ operationId: this.pendingCreate.operationId,
      classifierId, correctsOrderId }).subscribe({
      next: order => { this.pendingCreate = null; this.saving.set(false);
        this.selected.set(order); this.classifierId.set('');
        this.wasteKg.set('0'); this.wasteReason.set(''); this.resetLine();
        this.notice.set('Orden abierta. Podés agregar entradas y salidas.');
        this.loadPage(1); },
      error: error => { this.saving.set(false);
        this.error.set(this.message(error, 'No se pudo abrir la orden.')); },
    });
  }

  protected open(id: string): void {
    this.error.set(null);
    this.client.get(id).subscribe({
      next: order => { this.selected.set(order); this.wasteKg.set(
        formatLocalizedDecimal(order.reportedWasteKg, 3));
        this.wasteReason.set(order.wasteReason); },
      error: error => this.error.set(this.message(error, 'No se pudo abrir el detalle.')),
    });
  }

  protected dismiss(): void {
    this.labelLine.set(null); this.selected.set(null); this.pieces.set([]); this.resetLine();
  }

  protected chooseDirection(value: string): void {
    if (value !== 'Input' && value !== 'Output') return;
    this.direction.set(value); this.pieceId.set(''); this.pieces.set([]);
    this.profileId.set(''); this.rawBarcode.set('');
  }

  protected chooseProduct(value: string): void {
    this.productId.set(value); this.pieceId.set(''); this.pieces.set([]);
    this.quantity.set(''); this.massKg.set('');
  }

  protected searchPieces(): void {
    const product = this.activeProduct();
    if (!product || product.saleMode !== 'Weight') return;
    this.client.pieces(product.id, this.selected()?.id, this.pieceQuery()).subscribe({
      next: pieces => this.pieces.set(pieces),
      error: () => this.error.set('No se pudieron buscar las piezas disponibles.'),
    });
  }

  protected choosePiece(id: string): void {
    this.pieceId.set(id);
    const piece = this.pieces().find(item => item.id === id);
    if (piece) {
      this.quantity.set(formatLocalizedDecimal(piece.weightKg, 3));
      this.massKg.set(formatLocalizedDecimal(piece.weightKg, 3));
    }
  }

  protected addLine(): void {
    const order = this.selected(); const product = this.activeProduct();
    if (!order || order.status !== 'Open' || !product || this.saving()) return;
    const quantity = parseLocalizedDecimal(this.quantity(),
      product.saleMode === 'Unit' ? 0 : 3);
    const massKg = parseLocalizedDecimal(this.massKg(), 3);
    if (quantity === null || quantity <= 0 || massKg === null || massKg <= 0 ||
        product.saleMode === 'Weight' && quantity !== massKg) {
      this.error.set('Cantidad y kilos deben ser positivos. En artículos por peso deben coincidir.');
      return;
    }
    if (this.direction() === 'Output' && this.profileId() &&
        (!this.sourceSystem().trim() || !this.rawBarcode().trim() ||
          !this.identifierField().trim())) {
      this.error.set('La salida trazable necesita origen, etiqueta e identificador.');
      return;
    }
    const line: ProductionLineInput = {
      productId: product.id, direction: this.direction(), quantity, massKg,
      inputPieceId: this.direction() === 'Input' ? this.pieceId() || null : null,
      outputBarcodeProfileId: this.direction() === 'Output' ? this.profileId() || null : null,
      outputSourceSystem: this.direction() === 'Output' && this.profileId()
        ? this.sourceSystem().trim() : null,
      outputRawBarcode: this.direction() === 'Output' && this.profileId()
        ? this.rawBarcode().trim() : null,
      outputIdentifierField: this.direction() === 'Output' && this.profileId()
        ? this.identifierField().trim() : null,
    };
    this.saveLines([...order.lines.map(item => this.lineInput(item)), line]);
  }

  protected removeLine(id: string): void {
    const order = this.selected();
    if (!order || order.status !== 'Open' || this.saving()) return;
    this.saveLines(order.lines.filter(item => item.id !== id)
      .map(item => this.lineInput(item)));
  }

  private saveLines(lines: readonly ProductionLineInput[]): void {
    const order = this.selected(); if (!order || this.saving()) return;
    this.saving.set(true); this.error.set(null); this.notice.set(null);
    this.client.replaceLines(order, lines).subscribe({
      next: updated => { this.selected.set(updated); this.saving.set(false);
        this.resetLine(); this.notice.set('Orden guardada.');
        this.loadPage(this.page()?.page ?? 1); },
      error: error => { this.saving.set(false);
        this.error.set(this.message(error, 'No se pudo guardar la orden.')); },
    });
  }

  protected closeOrder(): void {
    const order = this.selected(); if (!order || this.saving()) return;
    const waste = parseLocalizedDecimal(this.wasteKg(), 3);
    if (waste === null || waste < 0 || waste > 100_000 ||
        waste > 0 && !this.wasteReason().trim()) {
      this.error.set('Ingresá una merma válida y un motivo cuando sea mayor a cero.');
      return;
    }
    this.saving.set(true); this.error.set(null); this.notice.set(null);
    this.client.close(order, waste, this.wasteReason().trim()).subscribe({
      next: updated => { this.selected.set(updated); this.saving.set(false);
        this.notice.set('Orden cerrada. Los movimientos y piezas quedaron registrados.');
        this.loadPage(this.page()?.page ?? 1); this.loadSummary(); },
      error: error => { this.saving.set(false);
        this.error.set(this.message(error, 'No se pudo cerrar la orden.')); },
    });
  }

  protected cancelOrder(): void {
    const order = this.selected(); if (!order || this.saving()) return;
    this.saving.set(true); this.error.set(null); this.notice.set(null);
    this.client.cancel(order).subscribe({
      next: updated => { this.selected.set(updated); this.saving.set(false);
        this.notice.set('Orden cancelada. Las piezas reservadas quedaron liberadas.');
        this.loadPage(this.page()?.page ?? 1); },
      error: error => { this.saving.set(false);
        this.error.set(this.message(error, 'No se pudo cancelar la orden.')); },
    });
  }

  protected format(value: number): string { return formatLocalizedDecimal(value, 3); }

  protected classifierName(id: string | null): string {
    return this.classifiers().find(item => item.id === id)?.name ??
      (id === null ? 'Correcciones' : 'Tipo desactivado');
  }

  private lineInput(line: ProductionLine): ProductionLineInput {
    return { productId: line.productId, direction: line.direction,
      quantity: line.quantity, massKg: line.massKg,
      inputPieceId: line.inputPieceId,
      outputBarcodeProfileId: line.outputBarcodeProfileId,
      outputSourceSystem: line.outputSourceSystem,
      outputRawBarcode: line.outputRawBarcode,
      outputIdentifierField: line.outputIdentifierField };
  }

  private resetLine(): void {
    this.productId.set(''); this.quantity.set(''); this.massKg.set('');
    this.pieceId.set(''); this.pieceQuery.set(''); this.pieces.set([]);
    this.profileId.set(''); this.rawBarcode.set('');
  }

  private message(error: unknown, fallback: string): string {
    if (!(error instanceof HttpErrorResponse)) return fallback;
    const code = error.error?.error?.code as string | undefined;
    const messages: Record<string, string> = {
      PRODUCTION_ORDER_VERSION_CHANGED: 'La orden cambió en otra sesión. Cerrá el detalle y volvé a abrirlo.',
      PRODUCTION_PIECE_NOT_AVAILABLE: 'La pieza ya no está disponible para esta producción.',
      PRODUCTION_PIECE_RESERVATION_MISSING: 'La reserva de una pieza cambió. Volvé a abrir la orden.',
      PRODUCTION_PIECE_IDENTIFIER_USED: 'El identificador de una salida trazable ya está en uso.',
      ARTICLE_RECONCILIATION_IN_PROGRESS: 'Hay un conteo físico en curso para uno de los artículos.',
      INVALID_PRODUCTION_BARCODE: 'La etiqueta no cumple la fórmula del perfil elegido.',
      PRODUCTION_PIECE_WEIGHT_MISMATCH: 'Los kilos de la etiqueta no coinciden con la salida.',
    };
    return code && messages[code] ? messages[code] : fallback;
  }
}
