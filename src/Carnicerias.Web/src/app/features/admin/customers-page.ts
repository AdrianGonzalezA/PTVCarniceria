import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AdminAreaTabs } from './admin-area-tabs';
import { AdminCustomer, AdminCustomerClient, AdminCustomerPage } from '../../core/admin/admin-customer-client';
import { CurrentSession, SessionClient } from '../../core/session/session-client';

@Component({
  selector: 'app-customers-page',
  imports: [RouterLink, AdminAreaTabs, ReactiveFormsModule],
  templateUrl: './customers-page.html',
  styleUrls: ['./admin-page.scss', './categories-page.scss'],
})
export class CustomersPage implements OnInit {
  private readonly customers = inject(AdminCustomerClient);
  private readonly sessions = inject(SessionClient);
  private readonly router = inject(Router);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly editorForm = this.formBuilder.nonNullable.group({
    code: ['', [Validators.required, Validators.pattern(/\S/), Validators.maxLength(80)]],
    name: ['', [Validators.required, Validators.pattern(/\S/), Validators.maxLength(200)]],
  });
  protected readonly session = signal<CurrentSession | null>(null);
  protected readonly pageData = signal<AdminCustomerPage | null>(null);
  protected readonly searchDraft = signal('');
  protected readonly appliedSearch = signal('');
  protected readonly isLoading = signal(true);
  protected readonly loadError = signal<string | null>(null);
  protected readonly editorOpen = signal(false);
  protected readonly editingId = signal<string | null>(null);
  protected readonly isSaving = signal(false);
  protected readonly actionId = signal<string | null>(null);
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
    this.editorForm.reset({ code: '', name: '' });
    this.actionMessage.set(null);
    this.actionError.set(null);
    this.editorOpen.set(true);
  }

  protected startEdit(customer: AdminCustomer): void {
    this.editingId.set(customer.id);
    this.editorForm.reset({ code: customer.code, name: customer.name });
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
    const code = this.editorForm.controls.code.value.trim();
    const name = this.editorForm.controls.name.value.trim();
    if (!code || !name || this.editorForm.invalid || this.isSaving()) {
      this.editorForm.markAllAsTouched();
      return;
    }

    this.isSaving.set(true);
    this.actionError.set(null);
    this.actionMessage.set(null);
    const id = this.editingId();
    const operation = id ? this.customers.update(id, { code, name }) : this.customers.create(code, name);
    operation.subscribe({
      next: (customer) => {
        this.isSaving.set(false);
        this.editorOpen.set(false);
        this.editingId.set(null);
        this.actionMessage.set(id ? 'Cliente actualizado.' : 'Cliente creado sin cuenta corriente habilitada.');
        if (id) this.replaceCustomer(customer);
        else this.load(1);
      },
      error: (error: HttpErrorResponse) => {
        this.isSaving.set(false);
        this.actionError.set(this.errorMessage(error));
      },
    });
  }

  protected toggleActive(customer: AdminCustomer): void {
    if (customer.isActive && !window.confirm('¿Inactivar este cliente? Se deshabilitará su cuenta corriente.')) return;
    this.change(customer, { isActive: !customer.isActive }, customer.isActive ? 'Cliente inactivado.' : 'Cliente activado.');
  }

  protected toggleCredit(customer: AdminCustomer): void {
    if (!customer.isActive) return;
    if (!customer.creditEnabled && !window.confirm('¿Habilitar la cuenta corriente de este cliente?')) return;
    this.change(customer, { creditEnabled: !customer.creditEnabled },
      customer.creditEnabled ? 'Cuenta corriente deshabilitada.' : 'Cuenta corriente habilitada.');
  }

  private change(customer: AdminCustomer, changes: { isActive?: boolean; creditEnabled?: boolean }, message: string): void {
    if (this.actionId() || this.isSaving()) return;
    this.actionId.set(customer.id);
    this.actionError.set(null);
    this.actionMessage.set(null);
    this.customers.update(customer.id, changes).subscribe({
      next: (updated) => {
        this.replaceCustomer(updated);
        this.actionId.set(null);
        this.actionMessage.set(message);
      },
      error: (error: HttpErrorResponse) => {
        this.actionId.set(null);
        this.actionError.set(this.errorMessage(error));
      },
    });
  }

  private replaceCustomer(customer: AdminCustomer): void {
    const page = this.pageData();
    if (page) this.pageData.set({ ...page, items: page.items.map((item) => item.id === customer.id ? customer : item) });
  }

  private errorMessage(error: HttpErrorResponse): string {
    if (error.status === 409) return 'El código ya existe o el cliente no admite esa acción.';
    if (error.status === 401 || error.status === 403) return 'La sesión no tiene permiso para modificar clientes.';
    if (error.status === 400) return 'Revisá el código y el nombre del cliente.';
    return 'No se pudo guardar el cambio. Intentá de nuevo.';
  }

  protected load(page: number): void {
    this.isLoading.set(true);
    this.loadError.set(null);
    this.customers.list(page, this.appliedSearch()).subscribe({
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
        this.loadError.set('No se pudieron cargar los clientes. Intentá nuevamente.');
      },
    });
  }
}
