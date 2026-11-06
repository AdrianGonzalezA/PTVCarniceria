import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Observable } from 'rxjs';
import { InventoryTransferReceiptClient, TransferReceipt, TransferReceiptPage,
} from '../../core/inventory/inventory-transfer-receipt-client';
import { InventoryReceiptClient, ReceiptBarcodeProfileOption,
  ReceiptProductOption } from '../../core/inventory/inventory-receipt-client';
import { CurrentSession, SessionClient } from '../../core/session/session-client';
import { formatLocalizedDecimal, parseLocalizedDecimal } from '../../shared/localized-decimal';
import { AdminDetailDialog } from './admin-detail-dialog';

@Component({
  selector: 'app-inventory-transfer-receipts-page',
  imports: [RouterLink, AdminDetailDialog],
  templateUrl: './inventory-transfer-receipts-page.html',
  styleUrls: ['./admin-page.scss', './categories-page.scss',
    './inventory-receipts-page.scss', './inventory-transfers-page.scss'],
})
export class InventoryTransferReceiptsPage implements OnInit {
  private readonly client = inject(InventoryTransferReceiptClient);
  private readonly receipts = inject(InventoryReceiptClient);
  private readonly sessions = inject(SessionClient);
  private readonly router = inject(Router);

  protected readonly session = signal<CurrentSession | null>(null);
  protected readonly home = computed(() => this.session()?.context?.permissions
    .includes('platform.users.manage') ? '/admin' : '/');
  protected readonly page = signal<TransferReceiptPage | null>(null);
  protected readonly selected = signal<TransferReceipt | null>(null);
  protected readonly products = signal<readonly ReceiptProductOption[]>([]);
  protected readonly profiles = signal<readonly ReceiptBarcodeProfileOption[]>([]);
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly sourceBranchName = signal('');
  protected readonly documentNumber = signal('');
  protected readonly documentDate = signal(new Date().toISOString().slice(0, 10));
  protected readonly newProductId = signal('');
  protected readonly sentQuantity = signal('');
  protected readonly receivedQuantity = signal('');
  protected readonly newIsPiece = signal(false);
  protected readonly profileId = signal('');
  protected readonly sourceSystem = signal('Ciclo 2');
  protected readonly identifierField = signal('pro_identif');
  protected readonly barcode = signal('');
  protected readonly differenceReason = signal('');
  private pendingCreate: { signature: string; operationId: string } | null = null;

  ngOnInit(): void {
    this.sessions.current().subscribe({
      next: session => {
        if (!session.context?.permissions.includes('inventory.receipts.manage')) {
          void this.router.navigateByUrl('/');
          return;
        }
        this.session.set(session);
        this.receipts.products().subscribe({ next: rows => this.products.set(rows),
          error: () => this.error.set('No se pudieron cargar los artículos.') });
        this.receipts.barcodeProfiles().subscribe({ next: rows => {
          this.profiles.set(rows);
          if (rows.length === 1) this.profileId.set(rows[0].id);
        }, error: () => this.error.set('No se pudieron cargar las fórmulas de lectura.') });
        this.loadPage(1);
      },
      error: () => void this.router.navigateByUrl('/'),
    });
  }

  protected loadPage(page: number): void {
    this.loading.set(true);
    this.client.list(page).subscribe({
      next: result => { this.page.set(result); this.loading.set(false); },
      error: () => { this.loading.set(false); this.error.set('No se pudieron consultar los remitos.'); },
    });
  }

  protected open(id: string): void {
    this.client.get(id).subscribe({ next: result => this.selected.set(result),
      error: () => this.error.set('No se pudo abrir el remito.') });
  }

  protected close(): void { this.selected.set(null); }

  protected create(): void {
    const sourceBranchName = this.sourceBranchName().trim();
    const documentNumber = this.documentNumber().trim();
    const documentDate = this.documentDate();
    if (!sourceBranchName || !documentNumber || !/^\d{4}-\d{2}-\d{2}$/.test(documentDate)) {
      this.error.set('Completá origen, número y fecha del remito.');
      return;
    }
    const signature = [sourceBranchName, documentNumber, documentDate].join('|');
    if (this.pendingCreate?.signature !== signature)
      this.pendingCreate = { signature, operationId: crypto.randomUUID() };
    this.mutate(this.client.create({ operationId: this.pendingCreate.operationId,
      sourceBranchName, documentNumber, documentDate }), 'Borrador creado.', true);
  }

  protected async importFile(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    if (file.size > 1_000_000) { this.error.set('El archivo supera 1 MB.'); return; }
    try {
      const remito: unknown = JSON.parse(await file.text());
      if (!remito || typeof remito !== 'object' || Array.isArray(remito))
        throw new Error('invalid');
      this.mutate(this.client.importRemito(crypto.randomUUID(), remito),
        'Remito importado como borrador. Verificá físicamente lo recibido.', true);
    } catch { this.error.set('El archivo no contiene un remito JSON válido.'); }
    finally { input.value = ''; }
  }

  protected addLine(): void {
    const receipt = this.selected();
    if (!receipt || !this.newProductId()) return;
    const product = this.products().find(item => item.productId === this.newProductId());
    if (!product) return;
    const sent = parseLocalizedDecimal(this.sentQuantity(), product.saleMode === 'unit' ? 0 : 3);
    const received = this.newIsPiece() ? 0 : parseLocalizedDecimal(this.receivedQuantity(),
      product.saleMode === 'unit' ? 0 : 3);
    if (sent === null || received === null || sent < 0 || received < 0 ||
        (sent === 0 && received === 0 && !this.newIsPiece())) {
      this.error.set('Ingresá cantidades válidas. La coma y el punto son decimales.');
      return;
    }
    this.mutate(this.client.addLine(receipt.id, { version: receipt.version,
      productId: product.productId, sentQuantity: sent, receivedQuantity: received,
      isPiece: this.newIsPiece() }), 'Renglón agregado.');
  }

  protected setReceived(lineId: string, value: string): void {
    const receipt = this.selected();
    const line = receipt?.lines.find(item => item.id === lineId);
    if (!receipt || !line || line.isPiece) return;
    const quantity = parseLocalizedDecimal(value, line.saleMode === 'unit' ? 0 : 3);
    if (quantity === null || quantity < 0) {
      this.error.set('La cantidad recibida no es válida.'); return;
    }
    this.mutate(this.client.setReceived(receipt.id, lineId, receipt.version, quantity),
      'Cantidad recibida actualizada.');
  }

  protected scan(lineId: string): void {
    const receipt = this.selected();
    if (!receipt || !this.profileId() || !this.barcode().trim() ||
        !this.sourceSystem().trim() || !this.identifierField().trim()) {
      this.error.set('Elegí la fórmula y completá la lectura.'); return;
    }
    this.mutate(this.client.scan(receipt.id, lineId, {
      version: receipt.version, barcodeProfileId: this.profileId(),
      sourceSystem: this.sourceSystem().trim(),
      identifierField: this.identifierField().trim(), code: this.barcode().trim(),
    }), 'Pieza leída.');
    this.barcode.set('');
  }

  protected approve(): void {
    const receipt = this.selected();
    if (receipt) this.mutate(this.client.approve(receipt.id, receipt.version),
      'Recepción aprobada. Se acreditó únicamente lo recibido.');
  }

  protected cancel(): void {
    const receipt = this.selected();
    if (receipt) this.mutate(this.client.cancel(receipt.id, receipt.version),
      'Borrador cancelado.');
  }

  protected resolve(lineId: string): void {
    const receipt = this.selected();
    const reason = this.differenceReason().trim();
    if (!receipt || !reason) { this.error.set('Indicá el motivo de resolución.'); return; }
    this.mutate(this.client.resolve(receipt.id, lineId, receipt.version, reason),
      'Diferencia resuelta con auditoría.');
  }

  protected format(quantity: number, mode: 'unit' | 'weight'): string {
    return formatLocalizedDecimal(quantity, mode === 'unit' ? 0 : 3);
  }

  private mutate(request: Observable<TransferReceipt>, message: string,
    open = false): void {
    if (this.saving()) return;
    this.saving.set(true);
    this.error.set(null);
    this.notice.set(null);
    request.subscribe({
      next: receipt => {
        this.saving.set(false);
        this.selected.set(receipt);
        this.notice.set(message);
        this.loadPage(this.page()?.page ?? 1);
        if (open) this.pendingCreate = null;
      },
      error: error => {
        this.saving.set(false);
        const code = error instanceof HttpErrorResponse ? error.error?.error?.code : null;
        this.error.set(code ? `No se pudo completar: ${code}.` :
          'No se pudo completar la operación. Reintentá.');
      },
    });
  }
}
