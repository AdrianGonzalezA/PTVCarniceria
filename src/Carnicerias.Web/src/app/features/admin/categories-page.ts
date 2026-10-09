import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AdminAreaTabs } from './admin-area-tabs';
import { AdminDetailDialog } from './admin-detail-dialog';
import { AdminCategory, AdminCategoryClient, AdminCategoryPage } from '../../core/admin/admin-category-client';
import { CurrentSession, SessionClient } from '../../core/session/session-client';

@Component({
  selector: 'app-categories-page',
  imports: [RouterLink, AdminAreaTabs, AdminDetailDialog, ReactiveFormsModule],
  templateUrl: './categories-page.html',
  styleUrls: ['./admin-page.scss', './categories-page.scss'],
})
export class CategoriesPage implements OnInit {
  private readonly categoriesClient = inject(AdminCategoryClient);
  private readonly sessions = inject(SessionClient);
  private readonly router = inject(Router);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly editorForm = this.formBuilder.nonNullable.group({
    name: ['', [Validators.required, Validators.pattern(/\S/), Validators.maxLength(120)]],
  });

  protected readonly session = signal<CurrentSession | null>(null);
  protected readonly pageData = signal<AdminCategoryPage | null>(null);
  protected readonly searchDraft = signal('');
  protected readonly appliedSearch = signal('');
  protected readonly isLoading = signal(true);
  protected readonly loadError = signal<string | null>(null);
  protected readonly editorOpen = signal(false);
  protected readonly editingId = signal<string | null>(null);
  protected readonly isSaving = signal(false);
  protected readonly actionId = signal<string | null>(null);
  protected readonly pendingCategory = signal<AdminCategory | null>(null);
  protected readonly actionMessage = signal<string | null>(null);
  protected readonly actionError = signal<string | null>(null);
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
    this.editingId.set(null);
    this.editorForm.reset({ name: '' });
    this.actionMessage.set(null);
    this.actionError.set(null);
    this.editorOpen.set(true);
  }

  protected startEdit(category: AdminCategory): void {
    this.editingId.set(category.id);
    this.editorForm.reset({ name: category.name });
    this.actionMessage.set(null);
    this.actionError.set(null);
    this.editorOpen.set(true);
  }

  protected cancelEdit(): void {
    if (this.isSaving()) return;
    this.editorOpen.set(false);
    this.editingId.set(null);
  }

  protected save(): void {
    const name = this.editorForm.controls.name.value.trim();
    if (!name || name.length > 120 || this.isSaving()) {
      this.editorForm.markAllAsTouched();
      return;
    }

    this.isSaving.set(true);
    this.actionMessage.set(null);
    this.actionError.set(null);
    const id = this.editingId();
    const operation = id
      ? this.categoriesClient.update(id, { name })
      : this.categoriesClient.create(name);
    operation.subscribe({
      next: (category) => {
        this.isSaving.set(false);
        this.editorOpen.set(false);
        this.editingId.set(null);
        this.actionMessage.set(id ? 'Categoría actualizada.' : 'Categoría creada.');
        if (id) this.replaceCategory(category);
        else this.load(1);
      },
      error: (error: HttpErrorResponse) => {
        this.isSaving.set(false);
        this.actionError.set(this.actionErrorMessage(error));
      },
    });
  }

  protected toggleActive(category: AdminCategory): void {
    if (this.actionId() || this.isSaving()) return;
    this.pendingCategory.set(category);
  }

  protected confirmToggle(): void {
    const category = this.pendingCategory();
    if (!category || this.actionId() || this.isSaving()) return;

    this.actionId.set(category.id);
    this.actionMessage.set(null);
    this.actionError.set(null);
    this.categoriesClient.update(category.id, { isActive: !category.isActive }).subscribe({
      next: (updated) => {
        this.replaceCategory(updated);
        this.actionId.set(null);
        this.pendingCategory.set(null);
        this.actionMessage.set(updated.isActive ? 'Categoría activada.' : 'Categoría inactivada.');
      },
      error: (error: HttpErrorResponse) => {
        this.actionId.set(null);
        this.actionError.set(this.actionErrorMessage(error));
      },
    });
  }

  protected closeAction(): void {
    if (!this.actionId()) this.pendingCategory.set(null);
  }

  private replaceCategory(category: AdminCategory): void {
    const page = this.pageData();
    if (page) this.pageData.set({
      ...page,
      items: page.items.map((item) => item.id === category.id ? category : item),
    });
  }

  private actionErrorMessage(error: HttpErrorResponse): string {
    if (error.status === 409) return 'Ya existe una categoría con ese nombre.';
    if (error.status === 401 || error.status === 403) return 'La sesión no tiene permiso para modificar categorías.';
    if (error.status === 400) return 'Revisá el nombre de la categoría.';
    return 'No se pudo guardar el cambio. Intentá de nuevo.';
  }

  protected load(page: number): void {
    this.isLoading.set(true);
    this.loadError.set(null);
    this.categoriesClient.list(page, this.appliedSearch()).subscribe({
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
        this.loadError.set('No se pudieron cargar las categorías. Revisá la conexión e intentá de nuevo.');
      },
    });
  }
}
