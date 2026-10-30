import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AdminAreaTabs } from './admin-area-tabs';
import { AdminDetailDialog } from './admin-detail-dialog';
import { AdminExcelImportActions } from './admin-excel-import-actions';
import { AdminCustomer, AdminCustomerClient, AdminCustomerFiscalProfile,
  AdminCustomerPage } from '../../core/admin/admin-customer-client';
import { CurrentSession, SessionClient } from '../../core/session/session-client';

@Component({
  selector: 'app-customers-page',
  imports: [RouterLink, AdminAreaTabs, AdminDetailDialog, AdminExcelImportActions, ReactiveFormsModule],
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
    fiscalTaxStatus: [''],
    fiscalDocumentNumber: ['', [Validators.maxLength(20)]],
    fiscalAddress: ['', [Validators.maxLength(200)]],
    fiscalLocality: ['', [Validators.maxLength(100)]],
    fiscalProvince: ['', [Validators.maxLength(100)]],
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
  protected readonly pendingAction = signal<{ customer: AdminCustomer; kind: 'active' | 'credit' } | null>(null);
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
    this.editorForm.reset({ code: '', name: '', fiscalTaxStatus: '', fiscalDocumentNumber: '',
      fiscalAddress: '', fiscalLocality: '', fiscalProvince: '' });
    this.actionMessage.set(null);
    this.actionError.set(null);
    this.editorOpen.set(true);
  }

  protected startEdit(customer: AdminCustomer): void {
    this.editingId.set(customer.id);
    this.editorForm.reset({ code: customer.code, name: customer.name,
      fiscalTaxStatus: customer.fiscalTaxStatus ?? '',
      fiscalDocumentNumber: customer.fiscalDocumentNumber ?? '',
      fiscalAddress: customer.fiscalAddress ?? '',
      fiscalLocality: customer.fiscalLocality ?? '',
      fiscalProvince: customer.fiscalProvince ?? '' });
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
    const fiscalTaxStatus = this.editorForm.controls.fiscalTaxStatus.value;
    const fiscalDocumentNumber = this.editorForm.controls.fiscalDocumentNumber.value.trim();
    const fiscalAddress = this.editorForm.controls.fiscalAddress.value.trim();
    const fiscalLocality = this.editorForm.controls.fiscalLocality.value.trim();
    const fiscalProvince = this.editorForm.controls.fiscalProvince.value.trim();
    if (!code || !name || this.editorForm.invalid || this.isSaving()) {
      this.editorForm.markAllAsTouched();
      return;
    }
    if (fiscalTaxStatus && !['finalConsumer', 'registered', 'smallTaxpayer', 'exempt'].includes(fiscalTaxStatus)) {
      this.actionError.set('Elegí una condición IVA válida.');
      return;
    }
    if ((!fiscalTaxStatus && (fiscalDocumentNumber || fiscalAddress || fiscalLocality || fiscalProvince)) ||
        (fiscalTaxStatus && fiscalTaxStatus !== 'finalConsumer' &&
         (!fiscalDocumentNumber || !fiscalAddress))) {
      this.actionError.set('Para una ficha fiscal identificada, indicá condición IVA, CUIT y domicilio.');
      return;
    }
    const fiscal = fiscalTaxStatus ? {
      fiscalTaxStatus: fiscalTaxStatus as AdminCustomerFiscalProfile['fiscalTaxStatus'],
      fiscalDocumentNumber, fiscalAddress, fiscalLocality, fiscalProvince,
    } : undefined;

    this.isSaving.set(true);
    this.actionError.set(null);
    this.actionMessage.set(null);
    const id = this.editingId();
    const operation = id ? this.customers.update(id, { code, name, ...fiscal,
      ...(!fiscal && this.pageData()?.items.some((item) => item.id === id && item.fiscalTaxStatus)
        ? { clearFiscalProfile: true } : {}) }) : this.customers.create(code, name, fiscal);
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
    this.pendingAction.set({ customer, kind: 'active' });
  }

  protected toggleCredit(customer: AdminCustomer): void {
    if (!customer.isActive) return;
    this.pendingAction.set({ customer, kind: 'credit' });
  }

  protected confirmAction(): void {
    const pending = this.pendingAction();
    if (!pending) return;
    const { customer, kind } = pending;
    if (kind === 'active') this.change(customer, { isActive: !customer.isActive },
      customer.isActive ? 'Cliente inactivado.' : 'Cliente activado.');
    else this.change(customer, { creditEnabled: !customer.creditEnabled },
      customer.creditEnabled ? 'Cuenta corriente deshabilitada.' : 'Cuenta corriente habilitada.');
  }

  protected closeAction(): void {
    if (!this.actionId()) this.pendingAction.set(null);
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
        this.pendingAction.set(null);
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
    if (error.status === 400) return 'Revisá el código, el nombre y la ficha fiscal del cliente.';
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
