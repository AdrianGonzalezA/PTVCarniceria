import { HttpErrorResponse } from '@angular/common/http';
import { AfterViewInit, Component, ElementRef, inject, OnInit, signal, ViewChild } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AdminAreaTabs } from './admin-area-tabs';
import { BarcodeLayoutClient, BarcodeProfile } from '../../core/inventory/barcode-layout-client';
import { InventoryClient, InventoryStockItem } from '../../core/inventory/inventory-client';
import { InventoryPieceClient, InventoryPieceListItem, InventoryPieceReceipt } from '../../core/inventory/inventory-piece-client';
import { CurrentSession, SessionClient } from '../../core/session/session-client';

@Component({
  selector: 'app-piece-receipt-page',
  imports: [RouterLink, AdminAreaTabs, ReactiveFormsModule],
  templateUrl: './piece-receipt-page.html',
  styleUrls: ['./admin-page.scss', './categories-page.scss', './piece-receipt-page.scss'],
})
export class PieceReceiptPage implements OnInit, AfterViewInit {
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
  protected readonly pieces = signal<readonly InventoryPieceListItem[]>([]);
  protected readonly loadingError = signal<string | null>(null);
  protected readonly saveError = signal<string | null>(null);
  protected readonly received = signal<InventoryPieceReceipt | null>(null);
  protected readonly saving = signal(false);
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
      next: (items) => this.products.set(items.filter(item => item.saleMode === 'weight')),
      error: () => this.loadingError.set('No se pudieron cargar los artículos por peso.'),
    });
  }

  private loadPieces(): void {
    this.piecesClient.list().subscribe({
      next: (page) => this.pieces.set(page.items),
      error: () => this.loadingError.set('No se pudieron cargar las piezas recibidas.'),
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
}
