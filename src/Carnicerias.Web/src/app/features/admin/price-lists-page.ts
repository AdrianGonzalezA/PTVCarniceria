import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import {
  AdminPriceHistory, AdminPriceList, AdminPriceListClient, AdminPriceListPage,
  AdminProductPrice, AdminProductPricePage, BranchPriceListAssignment,
} from '../../core/admin/admin-price-list-client';
import { CurrentSession, SessionClient } from '../../core/session/session-client';

@Component({
  selector: 'app-price-lists-page',
  imports: [RouterLink, ReactiveFormsModule, DatePipe],
  templateUrl: './price-lists-page.html',
  styleUrls: ['./admin-page.scss', './categories-page.scss', './price-lists-page.scss'],
})
export class PriceListsPage implements OnInit {
  private readonly client = inject(AdminPriceListClient);
  private readonly sessions = inject(SessionClient);
  private readonly router = inject(Router);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly editorForm = this.formBuilder.nonNullable.group({
    name: ['', [Validators.required, Validators.pattern(/\S/), Validators.maxLength(160)]],
  });
  protected readonly priceForm = this.formBuilder.nonNullable.group({
    amount: ['', [Validators.required, Validators.pattern(/^\d{1,10}(?:[,.]\d{1,2})?$/)]],
  });
  protected readonly session = signal<CurrentSession | null>(null);
  protected readonly pageData = signal<AdminPriceListPage | null>(null);
  protected readonly searchDraft = signal('');
  protected readonly appliedSearch = signal('');
  protected readonly isLoading = signal(true);
  protected readonly loadError = signal<string | null>(null);
  protected readonly editorOpen = signal(false);
  protected readonly editingList = signal<AdminPriceList | null>(null);
  protected readonly isSaving = signal(false);
  protected readonly actionId = signal<string | null>(null);
  protected readonly actionMessage = signal<string | null>(null);
  protected readonly actionError = signal<string | null>(null);
  protected readonly selectedList = signal<AdminPriceList | null>(null);
  protected readonly detailMode = signal<'branches' | 'prices' | null>(null);
  protected readonly branches = signal<readonly BranchPriceListAssignment[]>([]);
  protected readonly branchesLoading = signal(false);
  protected readonly branchesError = signal<string | null>(null);
  protected readonly branchActionId = signal<string | null>(null);
  protected readonly pricePage = signal<AdminProductPricePage | null>(null);
  protected readonly priceSearchDraft = signal('');
  protected readonly priceAppliedSearch = signal('');
  protected readonly priceLoading = signal(false);
  protected readonly priceError = signal<string | null>(null);
  protected readonly priceEditing = signal<AdminProductPrice | null>(null);
  protected readonly priceSaving = signal(false);
  protected readonly historyProduct = signal<AdminProductPrice | null>(null);
  protected readonly historyRows = signal<readonly AdminPriceHistory[]>([]);
  protected readonly historyLoading = signal(false);
  protected readonly historyError = signal<string | null>(null);
  protected readonly firstItem = computed(() => {
    const page = this.pageData();
    return !page || page.totalItems === 0 ? 0 : (page.page - 1) * page.pageSize + 1;
  });
  protected readonly lastItem = computed(() => {
    const page = this.pageData();
    return page ? Math.min(page.page * page.pageSize, page.totalItems) : 0;
  });

  ngOnInit(): void {
    this.sessions.current().subscribe({
      next: (session) => {
        if (!session.context?.permissions.includes('catalog.manage')) {
          void this.router.navigateByUrl('/');
          return;
        }
        this.session.set(session);
        this.load(1);
      },
      error: () => void this.router.navigateByUrl('/'),
    });
  }

  protected updateSearch(event: Event): void {
    this.searchDraft.set((event.target as HTMLInputElement).value.slice(0, 100));
  }

  protected search(): void {
    this.appliedSearch.set(this.searchDraft().trim());
    this.load(1);
  }

  protected goToPage(page: number): void {
    const data = this.pageData();
    if (!data || page < 1 || page > data.totalPages || this.isLoading()) return;
    this.load(page);
  }

  protected startCreate(): void {
    this.editingList.set(null);
    this.editorForm.reset({ name: '' });
    this.actionError.set(null);
    this.actionMessage.set(null);
    this.editorOpen.set(true);
  }

  protected startEdit(list: AdminPriceList): void {
    this.editingList.set(list);
    this.editorForm.reset({ name: list.name });
    this.actionError.set(null);
    this.actionMessage.set(null);
    this.editorOpen.set(true);
  }

  protected cancelEdit(): void {
    if (this.isSaving()) return;
    this.editorOpen.set(false);
  }

  protected save(): void {
    const name = this.editorForm.controls.name.value.trim();
    if (!name || this.editorForm.invalid || this.isSaving()) {
      this.editorForm.markAllAsTouched();
      return;
    }
    const editing = this.editingList();
    this.isSaving.set(true);
    this.actionError.set(null);
    const operation = editing ? this.client.update(editing.id, { name }) : this.client.create(name);
    operation.subscribe({
      next: (list) => {
        this.isSaving.set(false);
        this.editorOpen.set(false);
        this.actionMessage.set(editing ? 'Lista actualizada.' : 'Lista creada. Habilitala en las sucursales correspondientes.');
        if (editing) this.replaceList(list);
        else this.load(1);
      },
      error: (error: HttpErrorResponse) => {
        this.isSaving.set(false);
        this.actionError.set(error.status === 409 ? 'Ya existe una lista con ese nombre.' :
          'No se pudo guardar la lista. Intentá de nuevo.');
      },
    });
  }

  protected toggleList(list: AdminPriceList): void {
    if (this.actionId()) return;
    if (list.isActive && !window.confirm('¿Inactivar esta lista? Dejará de ofrecerse en caja.')) return;
    this.actionId.set(list.id);
    this.actionError.set(null);
    this.client.update(list.id, { isActive: !list.isActive }).subscribe({
      next: (updated) => {
        this.replaceList(updated);
        this.actionId.set(null);
        this.actionMessage.set(updated.isActive ? 'Lista activada.' : 'Lista inactivada.');
      },
      error: (error: HttpErrorResponse) => {
        this.actionId.set(null);
        this.actionError.set(error.error?.error?.code === 'PRICE_LIST_HAS_OPEN_DRAFTS'
          ? 'No se puede inactivar: hay tickets abiertos con esta lista.'
          : 'No se pudo cambiar el estado de la lista.');
      },
    });
  }

  protected showBranches(list: AdminPriceList): void {
    this.selectedList.set(list);
    this.detailMode.set('branches');
    this.loadBranches(list.id);
  }

  protected toggleBranch(branch: BranchPriceListAssignment): void {
    const list = this.selectedList();
    if (!list || this.branchActionId()) return;
    if (branch.isAssigned && !window.confirm(
      '¿Deshabilitar esta lista en la sucursal? Los tickets abiertos deben resolverse primero.',
    )) return;
    this.branchActionId.set(branch.branchId);
    this.branchesError.set(null);
    this.client.assignBranch(list.id, branch.branchId, !branch.isAssigned).subscribe({
      next: (updated) => {
        this.branches.set(this.branches().map((item) =>
          item.branchId === updated.branchId ? updated : item));
        this.branchActionId.set(null);
        const nextList = { ...list, activeBranchCount: this.branches().filter((item) =>
          item.branchActive && item.isAssigned).length };
        this.replaceList(nextList);
        this.actionMessage.set(updated.isAssigned ? 'Lista habilitada en la sucursal.' :
          'Lista deshabilitada en la sucursal.');
      },
      error: (error: HttpErrorResponse) => {
        this.branchActionId.set(null);
        this.branchesError.set(error.error?.error?.code === 'PRICE_LIST_HAS_OPEN_DRAFTS'
          ? 'Hay tickets abiertos con esta lista en la sucursal.'
          : 'No se pudo cambiar la asignación. Intentá de nuevo.');
      },
    });
  }

  protected showPrices(list: AdminPriceList): void {
    this.selectedList.set(list);
    this.detailMode.set('prices');
    this.priceSearchDraft.set('');
    this.priceAppliedSearch.set('');
    this.priceEditing.set(null);
    this.historyProduct.set(null);
    this.loadPrices(1);
  }

  protected updatePriceSearch(event: Event): void {
    this.priceSearchDraft.set((event.target as HTMLInputElement).value.slice(0, 100));
  }

  protected searchPrices(): void {
    this.priceAppliedSearch.set(this.priceSearchDraft().trim());
    this.loadPrices(1);
  }

  protected editPrice(product: AdminProductPrice): void {
    this.priceEditing.set(product);
    this.priceForm.reset({ amount: product.currentPrice?.toFixed(2) ?? '' });
    this.priceError.set(null);
  }

  protected cancelPrice(): void {
    if (!this.priceSaving()) this.priceEditing.set(null);
  }

  protected savePrice(): void {
    const list = this.selectedList();
    const product = this.priceEditing();
    const amount = Number(this.priceForm.controls.amount.value.replace(',', '.'));
    if (!list || !product || this.priceSaving() || this.priceForm.invalid ||
        !Number.isFinite(amount) || amount <= 0) {
      this.priceForm.markAllAsTouched();
      if (amount <= 0) this.priceError.set('Ingresá un precio mayor que cero.');
      return;
    }
    if (amount < product.cost) {
      this.priceError.set('El precio no puede ser menor que el costo.');
      return;
    }
    this.priceSaving.set(true);
    this.priceError.set(null);
    this.client.setPrice(list.id, product.productId, amount).subscribe({
      next: (saved) => {
        const page = this.pricePage();
        if (page) this.pricePage.set({ ...page, items: page.items.map((item) =>
          item.productId === product.productId
            ? { ...item, currentPrice: saved.amount, effectiveFromUtc: saved.effectiveFromUtc }
            : item) });
        if (product.currentPrice === null) this.replaceList({ ...list,
          currentPriceCount: list.currentPriceCount + 1 });
        this.priceEditing.set(null);
        this.priceSaving.set(false);
        this.actionMessage.set('Precio guardado con historial de vigencia.');
      },
      error: (error: HttpErrorResponse) => {
        this.priceSaving.set(false);
        this.priceError.set(error.error?.error?.code === 'PRICE_BELOW_COST'
          ? 'El precio no puede ser menor que el costo vigente.'
          : 'No se pudo guardar el precio. Intentá de nuevo.');
      },
    });
  }

  protected showHistory(product: AdminProductPrice): void {
    const list = this.selectedList();
    if (!list) return;
    this.historyProduct.set(product);
    this.historyRows.set([]);
    this.historyLoading.set(true);
    this.historyError.set(null);
    this.client.history(list.id, product.productId).subscribe({
      next: (rows) => {
        this.historyRows.set(rows);
        this.historyLoading.set(false);
      },
      error: () => {
        this.historyLoading.set(false);
        this.historyError.set('No se pudo cargar el historial de precios.');
      },
    });
  }

  protected closeDetails(): void {
    this.detailMode.set(null);
    this.selectedList.set(null);
  }

  protected load(page: number): void {
    this.isLoading.set(true);
    this.loadError.set(null);
    this.client.list(page, this.appliedSearch()).subscribe({
      next: (result) => {
        this.pageData.set(result);
        this.isLoading.set(false);
      },
      error: () => {
        this.isLoading.set(false);
        this.loadError.set('No se pudieron cargar las listas de precios.');
      },
    });
  }

  protected loadPrices(page: number): void {
    const list = this.selectedList();
    if (!list) return;
    this.priceLoading.set(true);
    this.priceError.set(null);
    this.client.prices(list.id, page, this.priceAppliedSearch()).subscribe({
      next: (result) => {
        this.pricePage.set(result);
        this.priceLoading.set(false);
      },
      error: () => {
        this.priceLoading.set(false);
        this.priceError.set('No se pudieron cargar los precios de esta lista.');
      },
    });
  }

  private loadBranches(listId: string): void {
    this.branchesLoading.set(true);
    this.branchesError.set(null);
    this.client.branches(listId).subscribe({
      next: (result) => {
        this.branches.set(result);
        this.branchesLoading.set(false);
      },
      error: () => {
        this.branchesLoading.set(false);
        this.branchesError.set('No se pudieron cargar las sucursales.');
      },
    });
  }

  private replaceList(list: AdminPriceList): void {
    const page = this.pageData();
    if (page) this.pageData.set({ ...page,
      items: page.items.map((item) => item.id === list.id ? list : item) });
    if (this.selectedList()?.id === list.id) this.selectedList.set(list);
  }
}
