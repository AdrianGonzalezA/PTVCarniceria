import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, ElementRef, inject, OnInit, signal, ViewChild } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { CreateReceiptInput, InventoryReceipt, InventoryReceiptClient, ReceiptLineInput,
  ReceiptImportPreview, ReceiptPage, ReceiptProductOption,
  ReceiptSupplierOption, ReceiptBarcodeProfileOption, ReceiptPieceRead } from '../../core/inventory/inventory-receipt-client';
import { CurrentSession, SessionClient } from '../../core/session/session-client';
import { formatLocalizedDecimal, parseLocalizedDecimal } from '../../shared/localized-decimal';
import { AdminDetailDialog } from './admin-detail-dialog';

interface EditableLine {
  readonly id?: string;
  readonly productId: string;
  readonly expected: string;
  readonly received: string;
  readonly differenceStatus?: 'None' | 'PendingReview' | 'Resolved';
}

@Component({
  selector: 'app-inventory-receipts-page',
  imports: [RouterLink, AdminDetailDialog],
  templateUrl: './inventory-receipts-page.html',
  styleUrls: ['./admin-page.scss', './categories-page.scss',
    './admin-excel-import-actions.scss', './inventory-receipts-page.scss'],
})
export class InventoryReceiptsPage implements OnInit {
  private readonly receiptsClient = inject(InventoryReceiptClient);
  private readonly sessions = inject(SessionClient);
  private readonly router = inject(Router);
  @ViewChild('receiptScanInput') private receiptScanInput?: ElementRef<HTMLInputElement>;
  @ViewChild('packageScanInput') private packageScanInput?: ElementRef<HTMLInputElement>;

  protected readonly session = signal<CurrentSession | null>(null);
  protected readonly suppliers = signal<readonly ReceiptSupplierOption[]>([]);
  protected readonly products = signal<readonly ReceiptProductOption[]>([]);
  protected readonly barcodeProfiles = signal<readonly ReceiptBarcodeProfileOption[]>([]);
  protected readonly page = signal<ReceiptPage | null>(null);
  protected readonly selected = signal<InventoryReceipt | null>(null);
  protected readonly editingLines = signal<readonly EditableLine[]>([]);
  protected readonly lineDirty = signal(false);
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly supplierId = signal('');
  protected readonly documentType = signal('remito');
  protected readonly documentNumber = signal('');
  protected readonly documentDate = signal(this.localToday());
  protected readonly destination = signal('');
  protected readonly externalReference = signal('');
  protected readonly newProductId = signal('');
  protected readonly newExpected = signal('');
  protected readonly newReceived = signal('');
  protected readonly scanProductId = signal('');
  protected readonly scanBarcodeProfileId = signal('');
  protected readonly scanSourceSystem = signal('Frigorífico ciclo 2');
  protected readonly scanIdentifierField = signal('pro_identif');
  protected readonly scanCode = signal('');
  protected readonly packageCode = signal('');
  protected readonly packageCount = signal('1');
  private pendingScan: { signature: string; operationId: string } | null = null;
  private pendingPackage: { signature: string; operationId: string } | null = null;
  protected readonly importOpen = signal(false);
  protected readonly importFile = signal<File | null>(null);
  protected readonly importPreviewResult = signal<ReceiptImportPreview | null>(null);
  protected readonly importBusy = signal<'preview' | 'apply' | null>(null);
  protected readonly importError = signal<string | null>(null);
  protected readonly hasAdmin = computed(() =>
    !!this.session()?.context?.permissions.includes('platform.users.manage'));
  protected readonly home = computed(() => this.hasAdmin() ? '/admin' : '/');
  private pendingCreate: { signature: string; operationId: string } | null = null;

  ngOnInit(): void {
    this.sessions.current().subscribe({
      next: (session) => {
        if (!session.context?.permissions.includes('inventory.receipts.manage')) {
          void this.router.navigateByUrl('/');
          return;
        }
        this.session.set(session);
        this.loadOptions();
        this.loadPage(1);
      },
      error: () => void this.router.navigateByUrl('/'),
    });
  }

  private loadOptions(): void {
    this.receiptsClient.suppliers().subscribe({
      next: (items) => this.suppliers.set(items),
      error: () => this.error.set('No se pudo cargar el catálogo de proveedores.'),
    });
    this.receiptsClient.products().subscribe({
      next: (items) => this.products.set(items),
      error: () => this.error.set('No se pudieron cargar los artículos de la sucursal.'),
    });
    this.receiptsClient.barcodeProfiles().subscribe({
      next: (items) => this.barcodeProfiles.set(items),
      error: () => this.error.set('No se pudieron cargar los perfiles de códigos trazables.'),
    });
  }

  protected loadPage(page: number): void {
    this.loading.set(true);
    this.receiptsClient.list(page).subscribe({
      next: (result) => {
        this.page.set(result);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('No se pudieron consultar las recepciones.');
        this.loading.set(false);
      },
    });
  }

  protected create(): void {
    const input = this.createInput();
    if (!input || this.saving()) {
      this.error.set('Elegí proveedor, fecha y completá tipo y número de comprobante.');
      return;
    }
    this.saving.set(true);
    this.error.set(null);
    this.receiptsClient.create(input).subscribe({
      next: (receipt) => {
        this.pendingCreate = null;
        this.saving.set(false);
        this.select(receipt);
        this.notice.set('Borrador creado. Cargá las cantidades y guardá antes de aprobar.');
        this.loadPage(1);
      },
      error: (error: HttpErrorResponse) => {
        this.saving.set(false);
        this.error.set(error.error?.error?.code === 'OPERATION_ID_REUSED'
          ? 'La operación ya se usó con otros datos. Volvé a intentar.'
          : 'No se pudo crear el borrador. Podés reintentar sin duplicarlo.');
      },
    });
  }

  private createInput(): CreateReceiptInput | null {
    const supplierId = this.supplierId();
    const documentType = this.documentType().trim();
    const documentNumber = this.documentNumber().trim();
    const documentDate = this.documentDate();
    if (!supplierId || !this.suppliers().some(item => item.id === supplierId) ||
        !documentType || documentType.length > 40 || !documentNumber ||
        documentNumber.length > 80 || !/^\d{4}-\d{2}-\d{2}$/.test(documentDate) ||
        this.destination().length > 120 || this.externalReference().length > 120)
      return null;
    const signature = [supplierId, documentType, documentNumber, documentDate,
      this.destination(), this.externalReference()].join('|');
    if (this.pendingCreate?.signature !== signature)
      this.pendingCreate = { signature, operationId: crypto.randomUUID() };
    return { operationId: this.pendingCreate.operationId, supplierId, documentType,
      documentNumber, documentDate, destination: this.destination(),
      externalReference: this.externalReference() };
  }

  protected open(id: string): void {
    if (this.lineDirty() && !window.confirm('Hay cantidades sin guardar. ¿Descartarlas?')) return;
    this.error.set(null);
    this.receiptsClient.get(id).subscribe({
      next: (receipt) => this.select(receipt),
      error: () => this.error.set('No se pudo abrir la recepción.'),
    });
  }

  private select(receipt: InventoryReceipt): void {
    this.selected.set(receipt);
    const capturedLineIds = new Set([
      ...(receipt.pieceReads ?? []).map(read => read.lineId),
      ...(receipt.packageReads ?? []).map(read => read.lineId),
    ]);
    this.editingLines.set(receipt.lines.filter(line => !capturedLineIds.has(line.id))
      .map(line => ({ id: line.id, productId: line.productId,
      expected: line.expectedQuantity === null ? '' : String(line.expectedQuantity),
      received: String(line.receivedQuantity), differenceStatus: line.differenceStatus })));
    this.lineDirty.set(false);
    this.closeImport();
  }

  protected close(): void {
    if (this.lineDirty() && !window.confirm('Hay cantidades sin guardar. ¿Descartarlas?')) return;
    this.selected.set(null);
    this.editingLines.set([]);
    this.lineDirty.set(false);
  }

  protected importTemplateUrl(id: string): string {
    return this.receiptsClient.templateUrl(id);
  }

  protected startImport(): void {
    if (this.lineDirty() || this.selected()?.status !== 'Draft') return;
    this.importFile.set(null);
    this.importPreviewResult.set(null);
    this.importError.set(null);
    this.importOpen.set(true);
  }

  protected closeImport(): void {
    if (this.importBusy()) return;
    this.importOpen.set(false);
    this.importFile.set(null);
    this.importPreviewResult.set(null);
    this.importError.set(null);
  }

  protected chooseImportFile(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0] ?? null;
    this.importFile.set(file);
    this.importPreviewResult.set(null);
    this.importError.set(file && (!file.name.toLowerCase().endsWith('.xlsx') ||
      file.size > 5 * 1024 * 1024) ? 'Elegí un archivo .xlsx de hasta 5 MiB.' : null);
  }

  protected previewImport(): void {
    const receipt = this.selected();
    const file = this.importFile();
    if (!receipt || !file || this.importBusy() || this.importError()) return;
    this.importBusy.set('preview');
    this.importPreviewResult.set(null);
    this.receiptsClient.previewImport(receipt.id, receipt.version, file).subscribe({
      next: (preview) => {
        this.importBusy.set(null);
        this.importPreviewResult.set(preview);
      },
      error: (error: HttpErrorResponse) => {
        this.importBusy.set(null);
        if (Array.isArray(error.error?.issues)) {
          this.importPreviewResult.set(error.error as ReceiptImportPreview);
          return;
        }
        this.importError.set(error.status === 409
          ? 'El borrador cambió. Cerrá esta ventana y volvé a abrirlo.'
          : 'No se pudo analizar el Excel. Revisá la plantilla.');
      },
    });
  }

  protected applyImport(): void {
    const receipt = this.selected();
    const file = this.importFile();
    if (!receipt || !file || !this.importPreviewResult()?.canApply || this.importBusy()) return;
    this.importBusy.set('apply');
    this.receiptsClient.applyImport(receipt.id, receipt.version, file).subscribe({
      next: (updated) => {
        this.importBusy.set(null);
        this.select(updated);
        this.notice.set('El Excel se guardó en el borrador. El stock todavía no cambió.');
      },
      error: (error: HttpErrorResponse) => {
        this.importBusy.set(null);
        this.importPreviewResult.set(null);
        this.importError.set(error.status === 409
          ? 'El borrador cambió. Reabrilo y repetí la previsualización.'
          : 'No se pudo guardar el Excel. Previsualizá de nuevo antes de reintentar.');
      },
    });
  }

  protected addLine(): void {
    const productId = this.newProductId();
    const proposed = { productId, expected: this.newExpected().trim(),
      received: this.newReceived().trim() };
    if (!this.validLine(proposed)) {
      this.error.set('Revisá el artículo y las cantidades. Usá punto o coma como decimal, sin separador de miles.');
      return;
    }
    this.editingLines.update(lines => [...lines, proposed]);
    this.lineDirty.set(true);
    this.newProductId.set('');
    this.newExpected.set('');
    this.newReceived.set('');
    this.error.set(null);
  }

  protected scanPiece(): void {
    const receipt = this.selected();
    const productId = this.scanProductId();
    const barcodeProfileId = this.scanBarcodeProfileId();
    const sourceSystem = this.scanSourceSystem().trim();
    const identifierField = this.scanIdentifierField().trim();
    const code = this.scanCode().trim();
    if (!receipt || receipt.status !== 'Draft' || this.saving() || this.lineDirty() ||
        !this.products().some(product => product.productId === productId &&
          product.saleMode === 'weight') ||
        !this.barcodeProfiles().some(profile => profile.id === barcodeProfileId) ||
        !sourceSystem || sourceSystem.length > 120 || !identifierField ||
        identifierField.length > 80 || !code || code.length > 80) {
      this.error.set('Elegí artículo por peso y perfil, completá origen, identificador y código. Guardá antes las cantidades pendientes.');
      return;
    }
    const signature = [receipt.id, productId, barcodeProfileId, sourceSystem,
      identifierField, code].join('|');
    if (this.pendingScan?.signature !== signature)
      this.pendingScan = { signature, operationId: crypto.randomUUID() };
    this.saving.set(true);
    this.error.set(null);
    this.receiptsClient.scanPiece(receipt.id, { version: receipt.version,
      operationId: this.pendingScan.operationId, productId, barcodeProfileId,
      sourceSystem, identifierField, code }).subscribe({
      next: (updated) => {
        this.saving.set(false);
        this.pendingScan = null;
        this.scanCode.set('');
        if (this.receiptScanInput) this.receiptScanInput.nativeElement.value = '';
        this.select(updated);
        this.notice.set('Pieza agregada al borrador. El stock cambia al aprobar.');
        queueMicrotask(() => this.receiptScanInput?.nativeElement.focus());
      },
      error: (error: HttpErrorResponse) => {
        this.saving.set(false);
        const code = error.error?.error?.code;
        this.error.set(code === 'PIECE_ALREADY_RECEIVED' ? 'Esta pieza ya ingresó al stock.'
          : code === 'PIECE_ALREADY_IN_RECEIPT' ? 'Esta pieza ya está en el borrador.'
          : code === 'PIECE_READ_CONFLICT' ? 'La lectura entró en conflicto. Reabrí el borrador y verificá si quedó guardada.'
          : code === 'RECEIPT_VERSION_CHANGED' ? 'El borrador cambió. Reabrilo y verificá las lecturas.'
          : code === 'INVALID_BARCODE' ? 'El código no coincide con el perfil configurado.'
          : 'No se pudo guardar la lectura. Reintentá con el mismo código.');
      },
    });
  }

  protected pieceProductName(read: ReceiptPieceRead): string {
    const line = this.selected()?.lines.find(item => item.id === read.lineId);
    return line?.productName ?? 'Artículo no disponible';
  }

  protected scanPackage(): void {
    const receipt = this.selected();
    const code = this.packageCode().trim();
    const count = Number(this.packageCount());
    if (!receipt || receipt.status !== 'Draft' || this.saving() || this.lineDirty() ||
        !code || code.length > 80 || !Number.isInteger(count) || count < 1 || count > 100_000) {
      this.error.set('Ingresá el código de bulto y una cantidad entera. Guardá primero los cambios pendientes.');
      return;
    }
    const signature = [receipt.id, code.toUpperCase(), count].join('|');
    if (this.pendingPackage?.signature !== signature)
      this.pendingPackage = { signature, operationId: crypto.randomUUID() };
    this.saving.set(true);
    this.error.set(null);
    this.receiptsClient.scanPackage(receipt.id, receipt.version,
      this.pendingPackage.operationId, code, count).subscribe({
      next: (updated) => {
        this.saving.set(false);
        this.pendingPackage = null;
        this.packageCode.set('');
        if (this.packageScanInput) this.packageScanInput.nativeElement.value = '';
        this.select(updated);
        this.notice.set('Bulto agregado al borrador. Las unidades ingresan al stock al aprobar.');
        queueMicrotask(() => this.packageScanInput?.nativeElement.focus());
      },
      error: (error: HttpErrorResponse) => {
        this.saving.set(false);
        const responseCode = error.error?.error?.code;
        this.error.set(responseCode === 'PACKAGE_CODE_NOT_AVAILABLE'
          ? 'El código de bulto no existe o está inactivo.'
          : responseCode === 'INVALID_PACKAGE_QUANTITY'
          ? 'La cantidad supera el máximo permitido por recepción.'
          : responseCode === 'RECEIPT_VERSION_CHANGED'
          ? 'El borrador cambió. Reabrilo antes de repetir la lectura.'
          : 'No se pudo guardar el bulto. Reabrí el borrador si hubo un corte de comunicación.');
      },
    });
  }

  protected packageProductName(lineId: string): string {
    const line = this.selected()?.lines.find(item => item.id === lineId);
    return line ? `${line.productCode} · ${line.productName}` : 'Artículo no disponible';
  }


  protected changeLine(index: number, field: 'expected' | 'received', value: string): void {
    this.editingLines.update(lines => lines.map((line, position) => position === index
      ? { ...line, [field]: value } : line));
    this.lineDirty.set(true);
  }

  protected removeLine(index: number): void {
    this.editingLines.update(lines => lines.filter((_, position) => position !== index));
    this.lineDirty.set(true);
  }

  protected saveLines(): void {
    const receipt = this.selected();
    if (!receipt || receipt.status !== 'Draft' || this.saving()) return;
    const lines = this.editingLines();
    if (lines.length > 500 || lines.some(line => !this.validLine(line))) {
      this.error.set('Revisá las cantidades de cada renglón antes de guardar.');
      return;
    }
    const payload: ReceiptLineInput[] = lines.map(line => {
      const product = this.products().find(item => item.productId === line.productId)!;
      const digits = product.saleMode === 'unit' ? 0 : 3;
      return { productId: line.productId,
        expectedQuantity: line.expected ? parseLocalizedDecimal(line.expected, digits) : null,
        receivedQuantity: parseLocalizedDecimal(line.received, digits)! };
    });
    this.saving.set(true);
    this.error.set(null);
    this.receiptsClient.replaceLines(receipt.id, receipt.version, payload).subscribe({
      next: (updated) => {
        this.saving.set(false);
        this.select(updated);
        this.notice.set('Cantidades guardadas. El stock todavía no cambió.');
      },
      error: (error: HttpErrorResponse) => {
        this.saving.set(false);
        this.error.set(error.error?.error?.code === 'RECEIPT_VERSION_CHANGED'
          ? 'Otra persona modificó este borrador. Volvé a abrirlo y revisá los cambios.'
          : 'No se pudieron guardar las cantidades. Podés reintentar.');
      },
    });
  }

  protected approve(): void {
    const receipt = this.selected();
    if (!receipt || receipt.status !== 'Draft' || this.lineDirty() || this.saving()) return;
    if (!receipt.lines.some(line => line.receivedQuantity > 0)) {
      this.error.set('Cargá al menos una cantidad recibida antes de aprobar.');
      return;
    }
    this.saving.set(true);
    this.error.set(null);
    this.receiptsClient.approve(receipt.id, receipt.version).subscribe({
      next: (updated) => {
        this.saving.set(false);
        this.select(updated);
        this.notice.set('Recepción aprobada. Se registró el ingreso efectivo una sola vez.');
        this.loadPage(1);
      },
      error: (error: HttpErrorResponse) => {
        this.saving.set(false);
        this.error.set(error.error?.error?.code === 'ARTICLE_RECONCILIATION_IN_PROGRESS'
          ? 'Un artículo está en conteo físico. Reintentá al finalizar.'
          : 'No se pudo confirmar. Reabrí la recepción para comprobar el estado antes de reintentar.');
      },
    });
  }

  protected cancel(): void {
    const receipt = this.selected();
    if (!receipt || receipt.status !== 'Draft' || this.saving() ||
        !window.confirm('¿Cancelar este borrador? No se registrará stock.')) return;
    this.saving.set(true);
    this.error.set(null);
    this.receiptsClient.cancel(receipt.id, receipt.version).subscribe({
      next: (updated) => {
        this.saving.set(false);
        this.select(updated);
        this.notice.set('Borrador cancelado. No se registró stock.');
        this.loadPage(1);
      },
      error: () => {
        this.saving.set(false);
        this.error.set('No se pudo cancelar. Reabrí el documento y revisá su estado.');
      },
    });
  }

  private validLine(line: EditableLine): boolean {
    const product = this.products().find(item => item.productId === line.productId);
    if (!product) return false;
    const digits = product.saleMode === 'unit' ? 0 : 3;
    const received = parseLocalizedDecimal(line.received, digits);
    const expected = line.expected ? parseLocalizedDecimal(line.expected, digits) : null;
    return received !== null && received >= 0 && received <= 100000 &&
      (line.expected === '' || expected !== null && expected >= 0 && expected <= 100000) &&
      !(received === 0 && (expected === null || expected === 0));
  }

  protected productName(productId: string): string {
    const saved = this.selected()?.lines.find(line => line.productId === productId);
    if (saved) return `${saved.productCode} · ${saved.productName}`;
    const product = this.products().find(item => item.productId === productId);
    return product ? `${product.code} · ${product.name}` : productId;
  }
  protected productMode(productId: string): 'unit' | 'weight' {
    return this.selected()?.lines.find(line => line.productId === productId)?.saleMode ??
      this.products().find(item => item.productId === productId)?.saleMode ?? 'weight';
  }
  protected quantity(value: number, mode: 'unit' | 'weight'): string {
    return formatLocalizedDecimal(value, mode === 'unit' ? 0 : 3);
  }

  private localToday(): string {
    const today = new Date();
    return `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`;
  }
}
