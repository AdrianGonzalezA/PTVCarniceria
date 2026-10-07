import '@angular/compiler';
import { HttpErrorResponse } from '@angular/common/http';
import { provideRouter, Router } from '@angular/router';
import { TestBed } from '@angular/core/testing';
import { of, Subject, throwError } from 'rxjs';
import { vi } from 'vitest';
import { CatalogClient } from '../../core/catalog/catalog-client';
import { PosTerminalClient } from '../../core/pos/pos-terminal-client';
import { InventoryClient } from '../../core/inventory/inventory-client';
import { CashierShiftClient } from '../../core/sales/cashier-shift-client';
import { SaleDraftClient } from '../../core/sales/sale-draft-client';
import { CurrentSession, SessionClient } from '../../core/session/session-client';
import { PosPage } from './pos-page';

describe('PosPage', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [
      { provide: PosTerminalClient, useValue: { current: () => of({
        id: 'terminal-id', name: 'Caja 1', companyId: 'company-id', branchId: 'branch-id',
      }) } },
      { provide: CashierShiftClient, useValue: { current: () => of({
        id: 'shift-id', openingCash: 0, openedAtUtc: '2026-10-06T15:00:00Z', closedAtUtc: null,
      }) } },
    ] });
  });

  it('blocks the catalogue and ticket until this cashier opens a shift', async () => {
    const session: CurrentSession = {
      userId: 'user-id', username: 'cajero', expiresAtUtc: '2026-10-06T20:00:00Z',
      context: { userId: 'user-id', companyId: 'company-id', companyName: 'Empresa',
        branchId: 'branch-id', branchName: 'Sucursal', permissions: [], sessionId: 'session-id' },
    };
    let saved = false;
    let opened = false;
    TestBed.configureTestingModule({ providers: [
      provideRouter([]),
      { provide: SessionClient, useValue: { current: () => of(session) } },
      { provide: CashierShiftClient, useValue: {
        current: () => of(null), lastClosed: () => of(null),
        open: () => { opened = true; return of({
          id: 'new-shift', openingCash: 0, openedAtUtc: '2026-10-06T15:00:00Z', closedAtUtc: null,
        }); },
      } },
      { provide: CatalogClient, useValue: { priceLists: () => of([]) } },
      { provide: SaleDraftClient, useValue: { current: () => of(null), save: () => { saved = true; return of(null); } } },
      { provide: InventoryClient, useValue: { stock: () => of([]) } },
    ] });

    const fixture = TestBed.createComponent(PosPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Caja 1');
    expect(fixture.nativeElement.textContent).toContain('Abrí el turno');
    expect(fixture.nativeElement.querySelector('#product-search')).toBeNull();
    expect(fixture.nativeElement.querySelector('.sale-footer')).toBeNull();
    expect(saved).toBe(false);

    (fixture.nativeElement.querySelector('.shift-gate .finish-button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#shift-title').textContent).toContain('Abrir turno');
    (fixture.nativeElement.querySelector('.shift-dialog .finish-button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(opened).toBe(true);
    expect(fixture.nativeElement.querySelector('#product-search')).not.toBeNull();
  });

  it('loads the branch catalog automatically and persists an added sale line', async () => {
    const session: CurrentSession = {
      userId: 'user-id',
      username: 'cajero',
      expiresAtUtc: '2026-10-06T20:00:00Z',
      context: {
        userId: 'user-id',
        companyId: 'company-id',
        companyName: 'Empresa',
        branchId: 'branch-id',
        branchName: 'Sucursal',
        permissions: [],
        sessionId: 'session-id',
      },
    };
    let saveAttempts = 0;
    let confirmationAttempts = 0;
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: SessionClient, useValue: { current: () => of(session) } },
        { provide: CatalogClient, useValue: {
          priceLists: () => of([{ id: 'list-id', name: 'Mostrador (datos ficticios)' }]),
          categories: () => of([{ id: 'category-id', name: 'Almacén', productCount: 1 }]),
          products: () => of({ items: [{
            id: 'product-id', code: '8001', name: 'Pan rallado 1 kg', categoryId: 'category-id',
            saleMode: 'unit' as const, price: 2400, availableStock: 40,
          }], page: 1, pageSize: 50, totalItems: 1 }),
        } },
        { provide: SaleDraftClient, useValue: {
          current: () => of(null),
          save: (priceListId: string, lines: readonly { productId: string; quantity: number }[]) => {
            saveAttempts++;
            return of({ id: 'draft-id', priceListId, updatedAtUtc: '2026-10-06T16:00:00Z', lines: [{
              productId: lines[0].productId, productCode: '8001', productName: 'Pan rallado 1 kg',
              unit: 'unidad', saleMode: 'unit' as const, quantity: lines[0].quantity, unitPrice: 2400,
            }] });
          },
          confirm: () => {
            confirmationAttempts++;
            return of({
              id: 'sale-id', total: 2400, changeAmount: 0, confirmedAtUtc: '2026-10-06T16:00:00Z',
              lines: [{ code: '8001', name: 'Pan rallado 1 kg', unit: 'unidad', quantity: 1, unitPrice: 2400, lineTotal: 2400 }],
              payments: [{ method: 'cash' as const, tenderedAmount: 2400, appliedAmount: 2400 }],
            });
          },
          cancel: () => of(null),
        } },
        { provide: CashierShiftClient, useValue: { current: () => of({
          id: 'shift-id', openingCash: 0, openedAtUtc: '2026-10-06T15:00:00Z', closedAtUtc: null,
        }) } },
        { provide: InventoryClient, useValue: { stock: () => of([]), adjust: () => of(null) } },
      ],
    });
    const fixture = TestBed.createComponent(PosPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    fixture.detectChanges();

    const search = fixture.nativeElement.querySelector('#product-search') as HTMLInputElement;
    const priceList = fixture.nativeElement.querySelector('.price-list-field select') as HTMLSelectElement;
    expect(priceList.value).toBe('list-id');
    expect(search.disabled).toBe(false);
    expect(fixture.nativeElement.querySelector('.product-card').textContent).toContain('Pan rallado');
    expect(fixture.nativeElement.querySelector('.category-icon').textContent).toContain('🧺');
    (fixture.nativeElement.querySelector('.product-card') as HTMLButtonElement).click();
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('.pos-dialog .finish-button') as HTMLButtonElement).click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(saveAttempts).toBe(1);
    expect(fixture.nativeElement.querySelector('.line-table').textContent).toContain('Pan rallado');
    expect(fixture.nativeElement.querySelector('.price-list-lock').textContent).toContain('Borrador guardado');
    expect(priceList.disabled).toBe(true);
    (fixture.nativeElement.querySelector('.sale-footer .finish-button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#checkout-title').textContent).toContain('Cobrar venta');
    const cashAmount = fixture.nativeElement.querySelector('.payment-amount') as HTMLInputElement;
    cashAmount.value = '1000';
    cashAmount.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.payment-balance').textContent).toContain('1.400,00');
    (fixture.nativeElement.querySelector('.checkout-dialog .finish-button') as HTMLButtonElement).click();
    expect(confirmationAttempts).toBe(0);
    cashAmount.value = '2400';
    cashAmount.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('.checkout-dialog .finish-button') as HTMLButtonElement).click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(confirmationAttempts).toBe(1);
    const receipt = fixture.nativeElement.querySelector('.sale-receipt') as HTMLElement;
    expect(receipt.textContent).toContain('Venta confirmada');
    expect(receipt.textContent).toContain('sale-id');
    expect(receipt.textContent).toContain('2.400,00');
    expect(receipt.textContent).toContain('Efectivo');

    (receipt.querySelector('.finish-button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.sale-receipt')).toBeNull();
    (fixture.nativeElement.querySelector('.product-card') as HTMLButtonElement).click();
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('.pos-dialog .finish-button') as HTMLButtonElement).click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(saveAttempts).toBe(2);
    expect(fixture.nativeElement.querySelector('.price-list-lock').textContent).toContain('Borrador guardado');
    (fixture.nativeElement.querySelector('.sale-footer .finish-button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#checkout-title').textContent).toContain('Cobrar venta');
  });

  it('offers lists from the active branch without displaying demo prices as real prices', () => {
    const session: CurrentSession = {
      userId: 'user-id', username: 'cajero', expiresAtUtc: '2026-10-06T20:00:00Z',
      context: {
        userId: 'user-id', companyId: 'company-id', companyName: 'Empresa',
        branchId: 'branch-id', branchName: 'Sucursal', permissions: [], sessionId: 'session-id',
      },
    };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: SessionClient, useValue: { current: () => of(session) } },
        { provide: CatalogClient, useValue: { priceLists: () => of([{ id: 'list-id', name: 'Mayorista' }]), categories: () => of([]), products: () => of({ items: [], page: 1, pageSize: 50, totalItems: 0 }) } },
        { provide: SaleDraftClient, useValue: { current: () => of(null), save: () => of(null), cancel: () => of(null) } },
        { provide: InventoryClient, useValue: { stock: () => of([]), adjust: () => of(null) } },
      ],
    });
    const fixture = TestBed.createComponent(PosPage);
    fixture.detectChanges();

    const priceList = fixture.nativeElement.querySelector('.price-list-field select') as HTMLSelectElement;
    expect(priceList.textContent).toContain('Mayorista');
    expect(priceList.value).toBe('list-id');
    expect(fixture.nativeElement.querySelector('.catalog-badge').textContent).toContain('Catálogo de la sucursal');
    priceList.value = 'list-id';
    priceList.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelectorAll('.product-card')).toHaveLength(0);
    expect(fixture.nativeElement.querySelector('.catalog-empty').textContent).toContain('Sin productos');
    expect((fixture.nativeElement.querySelector('#product-search') as HTMLInputElement).disabled).toBe(false);
  });

  it('restores a persisted ticket using its frozen description and price', async () => {
    const session: CurrentSession = {
      userId: 'user-id', username: 'cajero', expiresAtUtc: '2026-10-06T20:00:00Z',
      context: {
        userId: 'user-id', companyId: 'company-id', companyName: 'Empresa',
        branchId: 'branch-id', branchName: 'Sucursal', permissions: [], sessionId: 'session-id',
      },
    };
    const savedDraft = {
      id: 'draft-id', priceListId: 'list-id', updatedAtUtc: '2026-10-06T16:00:00Z',
      lines: [{
        productId: 'product-id', productCode: 'CAR-01', productName: 'Corte guardado',
        unit: 'kg', saleMode: 'weight' as const, quantity: 1.25, unitPrice: 12345.67,
      }],
    };
    let confirmedDraftId = '';
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: SessionClient, useValue: { current: () => of(session) } },
        { provide: CatalogClient, useValue: { priceLists: () => of([{ id: 'list-id', name: 'Mayorista' }]), categories: () => of([]), products: () => of({ items: [], page: 1, pageSize: 50, totalItems: 0 }) } },
        { provide: SaleDraftClient, useValue: {
          current: () => of(savedDraft), save: () => of(savedDraft), cancel: () => of(null),
          confirm: (draftId: string) => {
            confirmedDraftId = draftId;
            return of({ id: 'sale-id', total: 15432.09, changeAmount: 0,
              confirmedAtUtc: '2026-10-06T16:01:00Z', lines: [], payments: [] });
          },
        } },
        { provide: CashierShiftClient, useValue: { current: () => of({
          id: 'shift-id', openingCash: 0, openedAtUtc: '2026-10-06T15:00:00Z', closedAtUtc: null,
        }) } },
        { provide: InventoryClient, useValue: { stock: () => of([]), adjust: () => of(null) } },
      ],
    });

    const fixture = TestBed.createComponent(PosPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect((fixture.componentInstance as unknown as { selectedPriceListId: () => string }).selectedPriceListId()).toBe('list-id');
    expect(fixture.nativeElement.querySelector('.price-list-field select').textContent).toContain('Mayorista');
    expect(fixture.nativeElement.querySelector('.line-table').textContent).toContain('Corte guardado');
    expect(fixture.nativeElement.querySelector('.line-table').textContent).toContain('12.345,67');
    expect((fixture.nativeElement.querySelector('.quantity-input') as HTMLInputElement).value).toBe('1.25');
    (fixture.nativeElement.querySelector('.sale-footer .finish-button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#checkout-title').textContent).toContain('Cobrar venta');
    (fixture.nativeElement.querySelector('.checkout-dialog .finish-button') as HTMLButtonElement).click();
    expect(confirmedDraftId).toBe('draft-id');
  });

  it('keeps the unsaved ticket visible and offers retry for a server failure', async () => {
    const session: CurrentSession = {
      userId: 'user-id', username: 'cajero', expiresAtUtc: '2026-10-06T20:00:00Z',
      context: {
        userId: 'user-id', companyId: 'company-id', companyName: 'Empresa',
        branchId: 'branch-id', branchName: 'Sucursal', permissions: [], sessionId: 'session-id',
      },
    };
    let saveAttempts = 0;
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: SessionClient, useValue: { current: () => of(session) } },
        { provide: CatalogClient, useValue: {
          priceLists: () => of([{ id: 'list-id', name: 'Mostrador' }]),
          categories: () => of([{ id: 'category-id', name: 'Vacuno', productCount: 1 }]),
          products: () => of({ items: [{
            id: 'product-id', code: '1001', name: 'Bife', categoryId: 'category-id',
            saleMode: 'unit' as const, price: 1000, availableStock: 10,
          }], page: 1, pageSize: 50, totalItems: 1 }),
        } },
        { provide: SaleDraftClient, useValue: {
          current: () => of(null), save: () => {
            saveAttempts++;
            if (saveAttempts === 1) return throwError(() => new HttpErrorResponse({
              status: 500,
            }));
            return of({ id: 'draft-id', priceListId: 'list-id', updatedAtUtc: '2026-10-06T16:00:00Z', lines: [{
              productId: 'product-id', productCode: '1001', productName: 'Bife', unit: 'unidad',
              saleMode: 'unit' as const, quantity: 1, unitPrice: 1000,
            }] });
          },
          cancel: () => of(null),
        } },
        { provide: InventoryClient, useValue: { stock: () => of([]), adjust: () => of(null) } },
      ],
    });

    const fixture = TestBed.createComponent(PosPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const priceList = fixture.nativeElement.querySelector('.price-list-field select') as HTMLSelectElement;
    priceList.value = 'list-id';
    priceList.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('.product-card') as HTMLButtonElement).click();
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('.pos-dialog .finish-button') as HTMLButtonElement).click();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.line-table').textContent).toContain('Bife');
    const retry = fixture.nativeElement.querySelector('.draft-retry') as HTMLButtonElement;
    expect(retry).not.toBeNull();
    expect(fixture.nativeElement.querySelector('.price-list-lock[role="alert"]').textContent).toContain('No se pudo guardar el ticket');
    expect(fixture.nativeElement.querySelector('.price-list-lock[role="alert"]').textContent).not.toContain('No se pudo sincronizar');
    expect(fixture.nativeElement.querySelector('.pos-error')).toBeNull();
    (fixture.nativeElement.querySelector('.sale-footer .finish-button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#checkout-title')).toBeNull();
    expect(fixture.nativeElement.querySelector('.price-list-lock[role="alert"]').textContent).toContain('No se pudo guardar el ticket');
    expect(fixture.nativeElement.querySelector('.pos-error')).toBeNull();
    retry.click();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
    expect(saveAttempts).toBe(2);
    expect(fixture.nativeElement.querySelector('.line-table').textContent).toContain('Bife');
    expect(fixture.nativeElement.querySelector('.price-list-lock').textContent).toContain('Borrador guardado');
    expect(fixture.nativeElement.querySelector('.pos-error')).toBeNull();
  });

  it('checks kilograms and catalog-defined packages before adding a product', async () => {
    const session: CurrentSession = {
      userId: 'user-id', username: 'cajero', expiresAtUtc: '2026-10-06T20:00:00Z',
      context: { userId: 'user-id', companyId: 'company-id', companyName: 'Empresa',
        branchId: 'branch-id', branchName: 'Sucursal', permissions: [], sessionId: 'session-id' },
    };
    let savedLines: readonly { productId: string; quantity: number }[] = [];
    TestBed.configureTestingModule({ providers: [
      provideRouter([]),
      { provide: SessionClient, useValue: { current: () => of(session) } },
      { provide: CatalogClient, useValue: {
        priceLists: () => of([{ id: 'list-id', name: 'Mostrador' }]),
        categories: () => of([{ id: 'category-id', name: 'Productos', productCount: 2 }]),
        products: () => of({ items: [
          { id: 'weight-id', code: '1001', name: 'Asado', categoryId: 'category-id',
            unit: 'kg', saleMode: 'weight', price: 1000, availableStock: 0.5 },
          { id: 'pack-id', code: '7001', name: 'Hamburguesas x 4', categoryId: 'category-id',
            unit: 'paquete', saleMode: 'unit', price: 2000, availableStock: 2 },
        ], page: 1, pageSize: 50, totalItems: 2 }),
      } },
      { provide: SaleDraftClient, useValue: {
        current: () => of(null),
        save: (_: string, lines: readonly { productId: string; quantity: number }[]) => {
          savedLines = lines;
          return of({ id: 'draft-id', priceListId: 'list-id', updatedAtUtc: '2026-10-06T16:00:00Z',
            lines: lines.map((line) => ({ productId: line.productId, productCode: line.productId,
              productName: line.productId, unit: line.productId === 'weight-id' ? 'kg' : 'paquete',
              saleMode: line.productId === 'weight-id' ? 'weight' : 'unit', quantity: line.quantity, unitPrice: 1000 })) });
        },
      } },
      { provide: InventoryClient, useValue: { stock: () => of([]) } },
    ] });
    const fixture = TestBed.createComponent(PosPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    (fixture.nativeElement.querySelectorAll('.product-card')[0] as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.pos-dialog [role="alert"]').textContent).toContain('0,5 kg');
    expect((fixture.nativeElement.querySelector('.pos-dialog .finish-button') as HTMLButtonElement).disabled).toBe(true);
    let quantity = fixture.nativeElement.querySelector('#product-quantity') as HTMLInputElement;
    quantity.value = '0.25';
    quantity.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('.pos-dialog .finish-button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(savedLines).toEqual([{ productId: 'weight-id', quantity: 0.25 }]);

    (fixture.nativeElement.querySelectorAll('.product-card')[1] as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.product-facts').textContent).toContain('/ paquete');
    quantity = fixture.nativeElement.querySelector('#product-quantity') as HTMLInputElement;
    expect(quantity.step).toBe('1');
    quantity.value = '3';
    quantity.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.pos-dialog [role="alert"]').textContent).toContain('2 paquete');
    expect((fixture.nativeElement.querySelector('.pos-dialog .finish-button') as HTMLButtonElement).disabled).toBe(true);
    quantity.value = '1.5';
    quantity.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.pos-dialog [role="alert"]').textContent).toContain('entera');
    expect(savedLines).toEqual([{ productId: 'weight-id', quantity: 0.25 }]);
  });

  it('removes a rejected addition when stock changed after the catalog loaded', async () => {
    const session: CurrentSession = {
      userId: 'user-id', username: 'cajero', expiresAtUtc: '2026-10-06T20:00:00Z',
      context: { userId: 'user-id', companyId: 'company-id', companyName: 'Empresa',
        branchId: 'branch-id', branchName: 'Sucursal', permissions: [], sessionId: 'session-id' },
    };
    TestBed.configureTestingModule({ providers: [
      provideRouter([]),
      { provide: SessionClient, useValue: { current: () => of(session) } },
      { provide: CatalogClient, useValue: {
        priceLists: () => of([{ id: 'list-id', name: 'Mostrador' }]),
        categories: () => of([{ id: 'category-id', name: 'Vacuno', productCount: 1 }]),
        products: () => of({ items: [{ id: 'product-id', code: '1001', name: 'Asado',
          categoryId: 'category-id', unit: 'kg', saleMode: 'weight', price: 1000, availableStock: 10 }],
          page: 1, pageSize: 50, totalItems: 1 }),
      } },
      { provide: SaleDraftClient, useValue: {
        current: () => of(null),
        save: () => throwError(() => new HttpErrorResponse({ status: 409,
          error: { error: { code: 'INSUFFICIENT_STOCK' } } })),
      } },
      { provide: InventoryClient, useValue: { stock: () => of([]) } },
    ] });
    const fixture = TestBed.createComponent(PosPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('.product-card') as HTMLButtonElement).click();
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('.pos-dialog .finish-button') as HTMLButtonElement).click();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.line-table')).toBeNull();
    expect(fixture.nativeElement.querySelector('.pos-error[role="alert"]').textContent).toContain('No hay stock suficiente');
    expect((fixture.nativeElement.querySelector('.sale-footer .finish-button') as HTMLButtonElement).disabled).toBe(true);
  });

  it('does not increase a saved line beyond the available stock plus its own reservation', async () => {
    const session: CurrentSession = {
      userId: 'user-id', username: 'cajero', expiresAtUtc: '2026-10-06T20:00:00Z',
      context: { userId: 'user-id', companyId: 'company-id', companyName: 'Empresa',
        branchId: 'branch-id', branchName: 'Sucursal', permissions: [], sessionId: 'session-id' },
    };
    const draft = { id: 'draft-id', priceListId: 'list-id', updatedAtUtc: '2026-10-06T16:00:00Z',
      lines: [{ productId: 'product-id', productCode: '1001', productName: 'Asado', unit: 'kg',
        saleMode: 'weight', quantity: 1, unitPrice: 1000 }] };
    let savedQuantity = 1;
    TestBed.configureTestingModule({ providers: [
      provideRouter([]),
      { provide: SessionClient, useValue: { current: () => of(session) } },
      { provide: CatalogClient, useValue: {
        priceLists: () => of([{ id: 'list-id', name: 'Mostrador' }]),
        categories: () => of([]),
      } },
      { provide: SaleDraftClient, useValue: {
        current: () => of(draft),
        save: (_: string, lines: readonly { quantity: number }[]) => {
          savedQuantity = lines[0].quantity;
          return of({ ...draft, lines: [{ ...draft.lines[0], quantity: savedQuantity }] });
        },
      } },
      { provide: InventoryClient, useValue: { stock: () => of([{
        productId: 'product-id', code: '1001', name: 'Asado', onHand: 3, reserved: 1, available: 2,
      }]) } },
    ] });
    const fixture = TestBed.createComponent(PosPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const input = fixture.nativeElement.querySelector('.quantity-input') as HTMLInputElement;

    input.value = '4';
    input.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();
    expect(input.value).toBe('1');
    expect(savedQuantity).toBe(1);
    expect(fixture.nativeElement.querySelector('.pos-error[role="alert"]').textContent).toContain('Stock insuficiente');

    input.value = '3';
    input.dispatchEvent(new Event('change', { bubbles: true }));
    await fixture.whenStable();
    fixture.detectChanges();
    expect(savedQuantity).toBe(3);
    expect(fixture.nativeElement.querySelector('.pos-error')).toBeNull();
  });

  it('keeps checkout blocked until the latest ticket change is saved', async () => {
    const session: CurrentSession = {
      userId: 'user-id', username: 'cajero', expiresAtUtc: '2026-10-06T20:00:00Z',
      context: { userId: 'user-id', companyId: 'company-id', companyName: 'Empresa',
        branchId: 'branch-id', branchName: 'Sucursal', permissions: [], sessionId: 'session-id' },
    };
    const saves: Subject<unknown>[] = [];
    TestBed.configureTestingModule({ providers: [
      provideRouter([]),
      { provide: SessionClient, useValue: { current: () => of(session) } },
      { provide: CatalogClient, useValue: {
        priceLists: () => of([{ id: 'list-id', name: 'Mostrador' }]),
        categories: () => of([{ id: 'category-id', name: 'Vacuno', productCount: 1 }]),
        products: () => of({ items: [{ id: 'product-id', code: '1001', name: 'Bife',
          categoryId: 'category-id', saleMode: 'unit', price: 1000, availableStock: 10 }],
          page: 1, pageSize: 50, totalItems: 1 }),
      } },
      { provide: SaleDraftClient, useValue: {
        current: () => of(null),
        save: () => { const response = new Subject<unknown>(); saves.push(response); return response; },
        cancel: () => of(null),
      } },
      { provide: CashierShiftClient, useValue: { current: () => of({
        id: 'shift-id', openingCash: 0, openedAtUtc: '2026-10-06T15:00:00Z', closedAtUtc: null,
      }) } },
      { provide: InventoryClient, useValue: { stock: () => of([]), adjust: () => of(null) } },
    ] });
    const fixture = TestBed.createComponent(PosPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('.product-card') as HTMLButtonElement).click();
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('.pos-dialog .finish-button') as HTMLButtonElement).click();
    fixture.detectChanges();
    const quantity = fixture.nativeElement.querySelector('.quantity-input') as HTMLInputElement;
    quantity.value = '2';
    quantity.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();
    saves[0].next({ id: 'draft-id', priceListId: 'list-id', updatedAtUtc: '2026-10-06T16:00:00Z',
      lines: [{ productId: 'product-id', productCode: '1001', productName: 'Bife', unit: 'unidad',
        saleMode: 'unit', quantity: 1, unitPrice: 1000 }] });
    saves[0].complete();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.price-list-lock').textContent).toContain('Guardando ticket');
    (fixture.nativeElement.querySelector('.sale-footer .finish-button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#checkout-title')).toBeNull();
    expect(fixture.nativeElement.querySelector('.pos-error').textContent).toContain('termine de guardarse');
    saves[1].next({ id: 'draft-id', priceListId: 'list-id', updatedAtUtc: '2026-10-06T16:00:01Z',
      lines: [{ productId: 'product-id', productCode: '1001', productName: 'Bife', unit: 'unidad',
        saleMode: 'unit', quantity: 2, unitPrice: 1000 }] });
    saves[1].complete();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.price-list-lock').textContent).toContain('Borrador guardado');
  });

  it('returns to login after closing and preserves the cash summary for a fresh login', async () => {
    const session: CurrentSession = {
      userId: 'user-id', username: 'cajero', expiresAtUtc: '2026-10-06T20:00:00Z',
      context: { userId: 'user-id', companyId: 'company-id', companyName: 'Empresa',
        branchId: 'branch-id', branchName: 'Sucursal', permissions: [], sessionId: 'session-id' },
    };
    const openShift = { id: 'shift-id', openingCash: 100, cashSales: 1000,
      nonCashSales: 500, salesTotal: 1500, cashBalance: 1100,
      openedAtUtc: '2026-10-06T15:00:00Z', closedAtUtc: null };
    const closedShift = { ...openShift, closedAtUtc: '2026-10-06T17:00:00Z' };
    let isOpen = true;
    TestBed.configureTestingModule({ providers: [
      provideRouter([]),
      { provide: SessionClient, useValue: { current: () => of(session) } },
      { provide: CatalogClient, useValue: { priceLists: () => of([]) } },
      { provide: SaleDraftClient, useValue: { current: () => of(null) } },
      { provide: CashierShiftClient, useValue: {
        current: () => of(isOpen ? openShift : null),
        lastClosed: () => of(isOpen ? null : closedShift),
        close: () => { isOpen = false; return of(closedShift); },
      } },
      { provide: InventoryClient, useValue: { stock: () => of([]) } },
    ] });
    const fixture = TestBed.createComponent(PosPage);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    fixture.detectChanges();
    await fixture.whenStable();
    (fixture.nativeElement.querySelector('.shift-status') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.shift-summary').textContent).toContain('1.100,00');
    (fixture.nativeElement.querySelector('.shift-dialog .finish-button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(navigate).toHaveBeenCalledWith('/');
    expect(fixture.nativeElement.querySelector('.shift-dialog')).toBeNull();

    fixture.destroy();
    const reopened = TestBed.createComponent(PosPage);
    reopened.detectChanges();
    await reopened.whenStable();
    (reopened.nativeElement.querySelector('.shift-status') as HTMLButtonElement).click();
    reopened.detectChanges();
    expect(reopened.nativeElement.querySelector('.closed-shift-summary').textContent).toContain('1.100,00');
  });
});
