import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AdminSupplier, AdminSupplierClient, AdminSupplierPage } from '../../core/admin/admin-supplier-client';
import { CurrentSession, SessionClient } from '../../core/session/session-client';
import { AdminAreaTabs } from './admin-area-tabs';
import { AdminDetailDialog } from './admin-detail-dialog';

@Component({
  selector: 'app-suppliers-page',
  imports: [RouterLink, AdminAreaTabs, AdminDetailDialog, ReactiveFormsModule],
  templateUrl: './suppliers-page.html',
  styleUrls: ['./admin-page.scss', './categories-page.scss'],
})
export class SuppliersPage implements OnInit {
  private readonly supplierClient = inject(AdminSupplierClient);
  private readonly sessions = inject(SessionClient);
  private readonly router = inject(Router);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly editorForm = this.formBuilder.nonNullable.group({
    code: ['', [Validators.required, Validators.pattern(/\S/), Validators.maxLength(80)]],
    name: ['', [Validators.required, Validators.pattern(/\S/), Validators.maxLength(200)]],
    identification: ['', [Validators.maxLength(40)]],
  });
  protected readonly session = signal<CurrentSession | null>(null);
  protected readonly pageData = signal<AdminSupplierPage | null>(null);
  protected readonly searchDraft = signal('');
  protected readonly appliedSearch = signal('');
  protected readonly isLoading = signal(true);
  protected readonly loadError = signal<string | null>(null);
  protected readonly editorOpen = signal(false);
  protected readonly editingId = signal<string | null>(null);
  protected readonly isSaving = signal(false);
  protected readonly actionId = signal<string | null>(null);
  protected readonly pendingSupplier = signal<AdminSupplier | null>(null);
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
        if (!session.context?.permissions.includes('organization.manage')) {
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
    this.editorForm.reset({ code: '', name: '', identification: '' });
    this.actionError.set(null);
    this.actionMessage.set(null);
    this.editorOpen.set(true);
  }

  protected startEdit(supplier: AdminSupplier): void {
    this.editingId.set(supplier.id);
    this.editorForm.reset({ code: supplier.code, name: supplier.name,
      identification: supplier.identification ?? '' });
    this.actionError.set(null);
    this.actionMessage.set(null);
    this.editorOpen.set(true);
  }

  protected cancelEdit(): void {
    if (this.isSaving()) return;
    this.editorOpen.set(false);
    this.editingId.set(null);
  }

  protected save(): void {
    if (this.isSaving() || this.editorForm.invalid) {
      this.editorForm.markAllAsTouched();
      return;
    }
    const details = {
      code: this.editorForm.controls.code.value.trim(),
      name: this.editorForm.controls.name.value.trim(),
      identification: this.editorForm.controls.identification.value.trim() || null,
    };
    if (!details.code || !details.name) {
      this.editorForm.markAllAsTouched();
      return;
    }
    this.isSaving.set(true);
    this.actionError.set(null);
    const id = this.editingId();
    const operation = id
      ? this.supplierClient.update(id, details)
      : this.supplierClient.create(details);
    operation.subscribe({
      next: (supplier) => {
        this.isSaving.set(false);
        this.editorOpen.set(false);
        this.editingId.set(null);
        this.actionMessage.set(id ? 'Proveedor actualizado.' : 'Proveedor creado.');
        if (id) this.replaceSupplier(supplier);
        else this.load(1);
      },
      error: (error: HttpErrorResponse) => {
        this.isSaving.set(false);
        this.actionError.set(this.actionErrorMessage(error));
      },
    });
  }

  protected toggleActive(supplier: AdminSupplier): void {
    if (this.actionId() || this.isSaving()) return;
    this.pendingSupplier.set(supplier);
  }

  protected confirmToggle(): void {
    const supplier = this.pendingSupplier();
    if (!supplier || this.actionId() || this.isSaving()) return;
    this.actionId.set(supplier.id);
    this.actionError.set(null);
    this.supplierClient.update(supplier.id, { isActive: !supplier.isActive }).subscribe({
      next: (updated) => {
        this.replaceSupplier(updated);
        this.actionId.set(null);
        this.pendingSupplier.set(null);
        this.actionMessage.set(updated.isActive ? 'Proveedor activado.' : 'Proveedor inactivado.');
      },
      error: (error: HttpErrorResponse) => {
        this.actionId.set(null);
        this.actionError.set(this.actionErrorMessage(error));
      },
    });
  }

  protected closeAction(): void {
    if (!this.actionId()) this.pendingSupplier.set(null);
  }

  protected load(page: number): void {
    this.isLoading.set(true);
    this.loadError.set(null);
    this.supplierClient.list(page, this.appliedSearch()).subscribe({
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
        this.loadError.set('No se pudieron cargar los proveedores. Intentá de nuevo.');
      },
    });
  }

  private replaceSupplier(supplier: AdminSupplier): void {
    const page = this.pageData();
    if (page) this.pageData.set({ ...page,
      items: page.items.map((item) => item.id === supplier.id ? supplier : item),
    });
  }

  private actionErrorMessage(error: HttpErrorResponse): string {
    if (error.status === 409) return 'Ya existe un proveedor con ese código.';
    if (error.status === 400) return 'Revisá los datos del proveedor.';
    if (error.status === 401 || error.status === 403)
      return 'La sesión no tiene permiso para administrar proveedores.';
    return 'No se pudo guardar el cambio. Intentá de nuevo.';
  }
}
