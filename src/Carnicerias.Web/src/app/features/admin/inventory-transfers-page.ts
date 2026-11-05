import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { InventoryTransfer, InventoryTransferClient, TransferBranchOption,
  TransferLineInput, TransferPage, TransferPieceOption, TransferProductOption,
} from '../../core/inventory/inventory-transfer-client';
import { CurrentSession, SessionClient } from '../../core/session/session-client';
import { formatLocalizedDecimal, parseLocalizedDecimal } from '../../shared/localized-decimal';
import { AdminDetailDialog } from './admin-detail-dialog';

interface EditableLine {
  readonly productId: string;
  readonly quantity: number;
  readonly pieceId: string | null;
  readonly pieceLabel: string | null;
}

@Component({
  selector: 'app-inventory-transfers-page',
  imports: [RouterLink, AdminDetailDialog],
  templateUrl: './inventory-transfers-page.html',
  styleUrls: ['./admin-page.scss', './categories-page.scss',
    './inventory-receipts-page.scss', './inventory-transfers-page.scss'],
})
export class InventoryTransfersPage implements OnInit {
  private readonly client = inject(InventoryTransferClient);
  private readonly sessions = inject(SessionClient);
  private readonly router = inject(Router);

  protected readonly session = signal<CurrentSession | null>(null);
  protected readonly home = computed(() => this.session()?.context?.permissions
    .includes('platform.users.manage') ? '/admin' : '/');
  protected readonly products = signal<readonly TransferProductOption[]>([]);
  protected readonly branches = signal<readonly TransferBranchOption[]>([]);
  protected readonly pieces = signal<readonly TransferPieceOption[]>([]);
  protected readonly page = signal<TransferPage | null>(null);
  protected readonly selected = signal<InventoryTransfer | null>(null);
  protected readonly editingLines = signal<readonly EditableLine[]>([]);
  protected readonly lineDirty = signal(false);
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly destinationBranchId = signal('');
  protected readonly destinationName = signal('');
  protected readonly documentDate = signal(this.localToday());
  protected readonly newProductId = signal('');
  protected readonly newQuantity = signal('');
  protected readonly pieceQuery = signal('');
  protected readonly newPieceId = signal('');
  private pendingCreate: { signature: string; operationId: string } | null = null;

  ngOnInit(): void {
    this.sessions.current().subscribe({
      next: session => {
        if (!session.context?.permissions.includes('inventory.transfers.manage')) {
          void this.router.navigateByUrl('/');
          return;
        }
        this.session.set(session);
        this.client.products().subscribe({
          next: products => this.products.set(products),
          error: () => this.error.set('No se pudieron cargar los artículos.'),
        });
        this.client.branches().subscribe({
          next: branches => this.branches.set(branches),
          error: () => this.error.set('No se pudieron cargar las sucursales.'),
        });
        this.loadPage(1);
      },
      error: () => void this.router.navigateByUrl('/'),
    });
  }

  protected loadPage(page: number): void {
    this.loading.set(true);
    this.client.list(page).subscribe({
      next: result => { this.page.set(result); this.loading.set(false); },
      error: () => {
        this.loading.set(false);
        this.error.set('No se pudieron consultar las transferencias.');
      },
    });
  }

  protected setDestinationBranch(value: string): void {
    this.destinationBranchId.set(value);
    this.destinationName.set(this.branches().find(item => item.id === value)?.name ?? '');
  }

  protected create(): void {
    const destinationName = this.destinationName().trim();
    const documentDate = this.documentDate();
    if (this.saving() || !destinationName || destinationName.length > 200 ||
        !/^\d{4}-\d{2}-\d{2}$/.test(documentDate)) {
      this.error.set('Indicá el destino y una fecha válida.');
      return;
    }
    const branchId = this.destinationBranchId() || null;
    const signature = [destinationName, branchId, documentDate].join('|');
    if (this.pendingCreate?.signature !== signature)
      this.pendingCreate = { signature, operationId: crypto.randomUUID() };
    this.saving.set(true);
    this.error.set(null);
    this.client.create({ operationId: this.pendingCreate.operationId, destinationName,
      destinationBranchId: branchId, documentDate }).subscribe({
      next: transfer => {
        this.pendingCreate = null;
        this.saving.set(false);
        this.select(transfer);
        this.notice.set('Borrador creado. El stock aún no cambió.');
        this.loadPage(1);
      },
      error: () => {
        this.saving.set(false);
        this.error.set('No se pudo crear el remito. Podés reintentar sin duplicarlo.');
      },
    });
  }

  protected open(id: string): void {
    if (this.lineDirty() && !window.confirm('Hay renglones sin guardar. ¿Descartarlos?')) return;
    this.client.get(id).subscribe({
      next: transfer => { this.select(transfer); this.error.set(null); },
      error: () => this.error.set('No se pudo abrir el remito.'),
    });
  }

  protected close(): void {
    if (this.lineDirty() && !window.confirm('Hay renglones sin guardar. ¿Descartarlos?')) return;
    this.selected.set(null);
    this.editingLines.set([]);
    this.lineDirty.set(false);
  }

  private select(transfer: InventoryTransfer): void {
    this.selected.set(transfer);
    this.editingLines.set(transfer.lines.map(line => ({
      productId: line.productId, quantity: line.quantity, pieceId: line.pieceId,
      pieceLabel: line.pieceIdentifier,
    })));
    this.lineDirty.set(false);
  }

  protected setProduct(value: string): void {
    this.newProductId.set(value);
    this.newQuantity.set('');
    this.newPieceId.set('');
    this.pieceQuery.set('');
    this.pieces.set([]);
  }

  protected searchPieces(): void {
    const product = this.products().find(item => item.productId === this.newProductId());
    if (!product || product.saleMode !== 'weight') return;
    this.client.pieces(product.productId, this.pieceQuery().trim()).subscribe({
      next: pieces => this.pieces.set(pieces),
      error: () => this.error.set('No se pudieron consultar las piezas disponibles.'),
    });
  }

  protected setPiece(value: string): void {
    this.newPieceId.set(value);
    const piece = this.pieces().find(item => item.id === value);
    this.newQuantity.set(piece ? formatLocalizedDecimal(piece.weightKg, 3) : '');
  }

  protected addLine(): void {
    const product = this.products().find(item => item.productId === this.newProductId());
    const piece = this.pieces().find(item => item.id === this.newPieceId());
    if (!product || this.newPieceId() && !piece) {
      this.error.set('Elegí un artículo y una pieza válida.');
      return;
    }
    const quantity = parseLocalizedDecimal(this.newQuantity(), product.saleMode === 'unit' ? 0 : 3);
    if (quantity === null || quantity <= 0 || quantity > 100000 ||
        piece && piece.weightKg !== quantity ||
        piece && this.editingLines().some(line => line.pieceId === piece.id)) {
      this.error.set('Revisá la cantidad o la pieza antes de agregar el renglón.');
      return;
    }
    this.editingLines.update(lines => [...lines, { productId: product.productId,
      quantity, pieceId: piece?.id ?? null, pieceLabel: piece?.externalIdentifier ?? null }]);
    this.newQuantity.set('');
    this.newPieceId.set('');
    this.lineDirty.set(true);
    this.error.set(null);
  }

  protected removeLine(index: number): void {
    this.editingLines.update(lines => lines.filter((_, position) => position !== index));
    this.lineDirty.set(true);
  }

  protected saveLines(): void {
    const transfer = this.selected();
    if (!transfer || transfer.status !== 'Draft' || this.saving()) return;
    const lines: TransferLineInput[] = this.editingLines().map(line => ({
      productId: line.productId, quantity: line.quantity, pieceId: line.pieceId,
    }));
    this.saving.set(true);
    this.error.set(null);
    this.client.replaceLines(transfer.id, transfer.version, lines).subscribe({
      next: updated => {
        this.saving.set(false);
        this.select(updated);
        this.notice.set('Renglones guardados. El stock aún no cambió.');
      },
      error: (response: HttpErrorResponse) => {
        this.saving.set(false);
        this.error.set(response.error?.error?.code === 'TRANSFER_VERSION_CHANGED'
          ? 'Otra persona cambió el borrador. Reabrilo y revisá el detalle.'
          : 'No se pudieron guardar los renglones.');
      },
    });
  }

  protected dispatch(): void {
    const transfer = this.selected();
    if (!transfer || transfer.status !== 'Draft' || this.lineDirty() ||
        this.saving() || transfer.lines.length === 0 ||
        !window.confirm('¿Confirmar el despacho? El stock del origen se descontará definitivamente.'))
      return;
    this.saving.set(true);
    this.error.set(null);
    this.client.dispatch(transfer.id, transfer.version).subscribe({
      next: updated => {
        this.saving.set(false);
        this.select(updated);
        this.notice.set('Despacho confirmado. El stock de esta sucursal se descontó una vez.');
        this.loadPage(1);
      },
      error: (response: HttpErrorResponse) => {
        this.saving.set(false);
        const code = response.error?.error?.code;
        this.error.set(code === 'INSUFFICIENT_TRANSFER_STOCK'
          ? 'No hay stock físico disponible para todos los renglones.'
          : code === 'TRANSFER_PIECE_NOT_AVAILABLE'
          ? 'Una pieza ya no está disponible o su peso no coincide. Reabrí el remito.'
          : code === 'ARTICLE_RECONCILIATION_IN_PROGRESS'
          ? 'Un artículo está en conteo físico. Reintentá al finalizar.'
          : 'No se pudo confirmar el despacho. Reabrí el remito antes de reintentar.');
      },
    });
  }

  protected cancel(): void {
    const transfer = this.selected();
    if (!transfer || transfer.status !== 'Draft' || this.saving() ||
        !window.confirm('¿Cancelar el borrador? No se descontará stock.')) return;
    this.saving.set(true);
    this.client.cancel(transfer.id, transfer.version).subscribe({
      next: updated => {
        this.saving.set(false);
        this.select(updated);
        this.notice.set('Borrador cancelado. No se descontó stock.');
        this.loadPage(1);
      },
      error: () => {
        this.saving.set(false);
        this.error.set('No se pudo cancelar. Reabrí el remito y revisá su estado.');
      },
    });
  }

  protected productName(productId: string): string {
    const product = this.products().find(item => item.productId === productId);
    return product ? `${product.code} · ${product.name}` : productId;
  }

  protected quantity(line: EditableLine): string {
    const mode = this.products().find(item => item.productId === line.productId)?.saleMode;
    return formatLocalizedDecimal(line.quantity, mode === 'unit' ? 0 : 3);
  }

  private localToday(): string {
    const today = new Date();
    return `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`;
  }
}
