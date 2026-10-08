import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AdminCategoryClient, AdminCategoryOption } from '../../core/admin/admin-category-client';
import { AdminProductCode, AdminProductCodeClient } from '../../core/admin/admin-product-code-client';
import { AdminProduct, AdminProductClient, AdminProductPage, SaleMode } from '../../core/admin/admin-product-client';
import { CurrentSession, SessionClient } from '../../core/session/session-client';

@Component({
  selector: 'app-products-page',
  imports: [RouterLink, ReactiveFormsModule],
  templateUrl: './products-page.html',
  styleUrls: ['./admin-page.scss', './categories-page.scss', './products-page.scss'],
})
export class ProductsPage implements OnInit {
  private readonly productsClient = inject(AdminProductClient);
  private readonly categoriesClient = inject(AdminCategoryClient);
  private readonly codesClient = inject(AdminProductCodeClient);
  private readonly sessions = inject(SessionClient);
  private readonly router = inject(Router);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly editorForm = this.formBuilder.nonNullable.group({
    categoryId: ['', Validators.required],
    code: ['', [Validators.required, Validators.pattern(/\S/), Validators.maxLength(80)]],
    name: ['', [Validators.required, Validators.pattern(/\S/), Validators.maxLength(200)]],
    unit: ['', [Validators.required, Validators.pattern(/\S/), Validators.maxLength(24)]],
    saleMode: ['weight' as SaleMode, Validators.required],
    cost: ['', [Validators.required, Validators.pattern(/^\d{1,10}(?:[,.]\d{1,2})?$/)]],
  });
  protected readonly session = signal<CurrentSession | null>(null);
  protected readonly categories = signal<readonly AdminCategoryOption[]>([]);
  protected readonly categoriesLoading = signal(true);
  protected readonly categoriesError = signal<string | null>(null);
  protected readonly pageData = signal<AdminProductPage | null>(null);
  protected readonly searchDraft = signal('');
  protected readonly appliedSearch = signal('');
  protected readonly categoryFilter = signal('');
  protected readonly statusFilter = signal('');
  protected readonly isLoading = signal(true);
  protected readonly loadError = signal<string | null>(null);
  protected readonly editorOpen = signal(false);
  protected readonly editingProduct = signal<AdminProduct | null>(null);
  protected readonly isSaving = signal(false);
  protected readonly actionId = signal<string | null>(null);
  protected readonly actionMessage = signal<string | null>(null);
  protected readonly actionError = signal<string | null>(null);
  protected readonly alternateCodes = signal<readonly AdminProductCode[]>([]);
  protected readonly alternateDraft = signal('');
  protected readonly codesLoading = signal(false);
  protected readonly codeSaving = signal(false);
  protected readonly codeAction = signal<string | null>(null);
  protected readonly codesError = signal<string | null>(null);
  protected readonly editorCategories = computed(() => {
    const options = this.categories();
    const editing = this.editingProduct();
    return editing && !options.some((category) => category.id === editing.categoryId)
      ? [...options, { id: editing.categoryId, name: `${editing.categoryName} (inactiva)` }]
      : options;
  });
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
        this.loadCategories();
        this.load(1);
      },
      error: () => void this.router.navigateByUrl('/'),
    });
  }

  protected updateSearch(event: Event): void {
    this.searchDraft.set((event.target as HTMLInputElement).value.slice(0, 100));
  }

  protected updateCategoryFilter(event: Event): void {
    this.categoryFilter.set((event.target as HTMLSelectElement).value);
  }

  protected updateStatusFilter(event: Event): void {
    this.statusFilter.set((event.target as HTMLSelectElement).value);
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
    if (this.categoriesLoading()) return;
    if (this.categories().length === 0) {
      this.actionError.set('Primero creá una categoría activa.');
      return;
    }
    this.editingProduct.set(null);
    this.alternateCodes.set([]);
    this.editorForm.reset({ categoryId: this.categories()[0].id, code: '', name: '',
      unit: 'kg', saleMode: 'weight', cost: '' });
    this.actionError.set(null);
    this.actionMessage.set(null);
    this.editorOpen.set(true);
  }

  protected startEdit(product: AdminProduct): void {
    this.editingProduct.set(product);
    this.editorForm.reset({ categoryId: product.categoryId, code: product.code, name: product.name,
      unit: product.unit, saleMode: product.saleMode, cost: product.cost.toFixed(2) });
    this.actionError.set(null);
    this.actionMessage.set(null);
    this.editorOpen.set(true);
    this.alternateDraft.set('');
    this.loadCodes(product.id);
  }

  protected cancelEdit(): void {
    if (this.isSaving() || this.codeSaving()) return;
    this.editorOpen.set(false);
    this.editingProduct.set(null);
  }

  protected save(): void {
    const values = this.editorForm.getRawValue();
    const cost = Number(values.cost.replace(',', '.'));
    if (this.editorForm.invalid || !Number.isFinite(cost) || cost <= 0 ||
        this.isSaving() || this.codeSaving()) {
      this.editorForm.markAllAsTouched();
      if (cost <= 0) this.actionError.set('Ingresá un costo mayor que cero.');
      return;
    }
    this.isSaving.set(true);
    this.actionError.set(null);
    this.actionMessage.set(null);
    const editing = this.editingProduct();
    const details = { categoryId: values.categoryId, name: values.name.trim(),
      unit: values.unit.trim(), saleMode: values.saleMode, cost };
    const operation = editing
      ? this.productsClient.update(editing.id, details)
      : this.productsClient.create({ ...details, code: values.code.trim() });
    operation.subscribe({
      next: (product) => {
        this.isSaving.set(false);
        this.editorOpen.set(false);
        this.editingProduct.set(null);
        this.actionMessage.set(editing ? 'Artículo actualizado.' : 'Artículo creado sin precio ni stock.');
        if (editing) this.replaceProduct(product);
        else this.load(1);
      },
      error: (error: HttpErrorResponse) => {
        this.isSaving.set(false);
        this.actionError.set(this.actionErrorMessage(error));
      },
    });
  }

  protected toggleActive(product: AdminProduct): void {
    if (this.actionId() || this.isSaving()) return;
    if (product.isActive && !window.confirm(
      '¿Inactivar este artículo? Dejará de aparecer en el punto de venta.',
    )) return;
    this.actionId.set(product.id);
    this.actionError.set(null);
    this.actionMessage.set(null);
    this.productsClient.update(product.id, { isActive: !product.isActive }).subscribe({
      next: (updated) => {
        this.replaceProduct(updated);
        this.actionId.set(null);
        this.actionMessage.set(updated.isActive ? 'Artículo activado.' : 'Artículo inactivado.');
      },
      error: (error: HttpErrorResponse) => {
        this.actionId.set(null);
        this.actionError.set(this.actionErrorMessage(error));
      },
    });
  }

  protected updateAlternateDraft(event: Event): void {
    this.alternateDraft.set((event.target as HTMLInputElement).value.slice(0, 80));
  }

  protected addCode(): void {
    const product = this.editingProduct();
    const code = this.alternateDraft().trim();
    if (!product || this.codeSaving() || !code) return;
    this.codeSaving.set(true);
    this.codesError.set(null);
    this.codesClient.create(product.id, code).subscribe({
      next: (created) => {
        const codes = [...this.alternateCodes(), created];
        this.alternateCodes.set(codes);
        this.updateCodeCount(product, codes);
        this.alternateDraft.set('');
        this.codeSaving.set(false);
        this.actionMessage.set('Código alternativo agregado.');
      },
      error: (error: HttpErrorResponse) => {
        this.codeSaving.set(false);
        this.codesError.set(error.status === 409
          ? 'Ese código ya está reservado por otro artículo o código alternativo.'
          : 'No se pudo agregar el código. Intentá de nuevo.');
      },
    });
  }

  protected toggleCode(code: AdminProductCode): void {
    const product = this.editingProduct();
    if (!product || this.codeSaving()) return;
    if (code.isActive && !window.confirm('¿Inactivar este código alternativo para la búsqueda en caja?'))
      return;
    this.codeSaving.set(true);
    this.codeAction.set(code.code);
    this.codesError.set(null);
    this.codesClient.changeState(product.id, code.code, !code.isActive).subscribe({
      next: (updated) => {
        const codes = this.alternateCodes().map((item) =>
          item.code === updated.code ? updated : item);
        this.alternateCodes.set(codes);
        this.updateCodeCount(product, codes);
        this.codeSaving.set(false);
        this.codeAction.set(null);
        this.actionMessage.set(updated.isActive ? 'Código alternativo activado.' : 'Código alternativo inactivado.');
      },
      error: () => {
        this.codeSaving.set(false);
        this.codeAction.set(null);
        this.codesError.set('No se pudo cambiar el estado del código. Intentá de nuevo.');
      },
    });
  }

  protected load(page: number): void {
    this.isLoading.set(true);
    this.loadError.set(null);
    this.productsClient.list(page, this.appliedSearch(), this.categoryFilter(), this.statusFilter()).subscribe({
      next: (result) => {
        this.pageData.set(result);
        this.isLoading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.isLoading.set(false);
        if (error.status === 401 || error.status === 403) {
          void this.router.navigateByUrl('/');
          return;
        }
        this.loadError.set('No se pudieron cargar los artículos. Intentá de nuevo.');
      },
    });
  }

  private loadCategories(): void {
    this.categoriesLoading.set(true);
    this.categoriesError.set(null);
    this.categoriesClient.options().subscribe({
      next: (options) => {
        this.categories.set(options);
        this.categoriesLoading.set(false);
      },
      error: () => {
        this.categoriesLoading.set(false);
        this.categoriesError.set('No se pudieron cargar las categorías para editar artículos.');
      },
    });
  }

  private loadCodes(productId: string): void {
    this.codesLoading.set(true);
    this.codesError.set(null);
    this.codesClient.list(productId).subscribe({
      next: (codes) => {
        if (this.editingProduct()?.id !== productId) return;
        this.alternateCodes.set(codes);
        this.codesLoading.set(false);
      },
      error: () => {
        if (this.editingProduct()?.id !== productId) return;
        this.codesLoading.set(false);
        this.codesError.set('No se pudieron cargar los códigos alternativos.');
      },
    });
  }

  private updateCodeCount(product: AdminProduct, codes: readonly AdminProductCode[]): void {
    const updated = { ...product, alternateCodeCount: codes.filter((code) => code.isActive).length };
    this.editingProduct.set(updated);
    this.replaceProduct(updated);
  }

  private replaceProduct(product: AdminProduct): void {
    const page = this.pageData();
    if (page) this.pageData.set({ ...page,
      items: page.items.map((item) => item.id === product.id ? product : item) });
  }

  private actionErrorMessage(error: HttpErrorResponse): string {
    const code = error.error?.error?.code;
    if (code === 'PRODUCT_QUANTITY_HISTORY_EXISTS')
      return 'No se puede cambiar la unidad o modalidad: el artículo ya tiene stock o tickets.';
    if (code === 'CATEGORY_NOT_ACTIVE') return 'Elegí una categoría activa.';
    if (code === 'COST_ABOVE_CURRENT_PRICE')
      return 'El costo supera un precio vigente. Actualizá primero las listas de precios.';
    if (error.status === 409) return 'El código ya existe o el cambio entra en conflicto con el catálogo.';
    if (error.status === 400) return 'Revisá los datos del artículo.';
    if (error.status === 401 || error.status === 403) return 'La sesión no tiene permiso para editar artículos.';
    return 'No se pudo guardar el artículo. Intentá de nuevo.';
  }
}
