import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AdminAreaTabs } from './admin-area-tabs';
import { Observable } from 'rxjs';
import { AdminBranch, AdminCompany, AdminCompanyCreated, AdminOrganizationClient } from '../../core/admin/admin-organization-client';
import { AdminTerminal, AdminTerminalClient, AdminTerminalUpdate, ProvisionedTerminal } from '../../core/admin/admin-terminal-client';
import { CurrentSession, SessionClient } from '../../core/session/session-client';
import { AdminDetailDialog } from './admin-detail-dialog';

@Component({
  selector: 'app-organization-page',
  imports: [RouterLink, AdminAreaTabs, AdminDetailDialog, ReactiveFormsModule],
  templateUrl: './organization-page.html',
  styleUrls: ['./admin-page.scss', './categories-page.scss', './organization-page.scss'],
})
export class OrganizationPage implements OnInit {
  private readonly client = inject(AdminOrganizationClient);
  private readonly terminalsClient = inject(AdminTerminalClient);
  private readonly sessions = inject(SessionClient);
  private readonly router = inject(Router);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly companyForm = this.formBuilder.nonNullable.group({
    name: ['', [Validators.required, Validators.pattern(/\S/), Validators.maxLength(200)]],
    initialBranchName: ['', Validators.maxLength(200)],
  });
  protected readonly branchForm = this.formBuilder.nonNullable.group({
    name: ['', [Validators.required, Validators.pattern(/\S/), Validators.maxLength(200)]],
  });
  protected readonly terminalForm = this.formBuilder.nonNullable.group({
    name: ['', [Validators.required, Validators.pattern(/\S/), Validators.maxLength(120)]],
  });
  protected readonly session = signal<CurrentSession | null>(null);
  protected readonly companies = signal<readonly AdminCompany[]>([]);
  protected readonly companiesLoading = signal(true);
  protected readonly companiesError = signal<string | null>(null);
  protected readonly selectedCompany = signal<AdminCompany | null>(null);
  protected readonly branches = signal<readonly AdminBranch[]>([]);
  protected readonly branchesLoading = signal(false);
  protected readonly branchesError = signal<string | null>(null);
  protected readonly companyEditorOpen = signal(false);
  protected readonly editingCompany = signal<AdminCompany | null>(null);
  protected readonly branchEditorOpen = signal(false);
  protected readonly editingBranch = signal<AdminBranch | null>(null);
  protected readonly companySaving = signal(false);
  protected readonly branchSaving = signal(false);
  protected readonly companyActionId = signal<string | null>(null);
  protected readonly branchActionId = signal<string | null>(null);
  protected readonly actionMessage = signal<string | null>(null);
  protected readonly actionError = signal<string | null>(null);
  protected readonly selectedTerminalBranch = signal<AdminBranch | null>(null);
  protected readonly terminals = signal<readonly AdminTerminal[]>([]);
  protected readonly terminalsLoading = signal(false);
  protected readonly terminalsError = signal<string | null>(null);
  protected readonly terminalEditorOpen = signal(false);
  protected readonly editingTerminal = signal<AdminTerminal | null>(null);
  protected readonly terminalSaving = signal(false);
  protected readonly terminalActionId = signal<string | null>(null);
  protected readonly shownCredential = signal<string | null>(null);

  ngOnInit(): void {
    this.sessions.current().subscribe({
      next: (session) => {
        if (!session.context?.permissions.includes('organization.manage')) {
          void this.router.navigateByUrl('/');
          return;
        }
        this.session.set(session);
        this.loadCompanies();
      },
      error: () => void this.router.navigateByUrl('/'),
    });
  }

  protected selectCompany(company: AdminCompany): void {
    this.selectedCompany.set(company);
    this.branchEditorOpen.set(false);
    this.closeTerminals();
    this.loadBranches(company.id);
  }

  protected closeCompany(): void {
    if (this.branchSaving() || this.branchActionId() || this.selectedTerminalBranch()) return;
    this.selectedCompany.set(null);
    this.branches.set([]);
  }

  protected startCreateCompany(): void {
    this.editingCompany.set(null);
    this.companyForm.reset({ name: '', initialBranchName: '' });
    this.actionError.set(null);
    this.companyEditorOpen.set(true);
  }

  protected startEditCompany(company: AdminCompany): void {
    this.editingCompany.set(company);
    this.companyForm.reset({ name: company.name, initialBranchName: '' });
    this.actionError.set(null);
    this.companyEditorOpen.set(true);
  }

  protected cancelCompany(): void {
    if (!this.companySaving()) this.companyEditorOpen.set(false);
  }

  protected saveCompany(): void {
    const values = this.companyForm.getRawValue();
    const name = values.name.trim();
    const initialBranchName = values.initialBranchName.trim();
    const editing = this.editingCompany();
    if (this.companySaving() || this.companyForm.invalid || !name ||
        (!editing && (!initialBranchName || initialBranchName.length > 200))) {
      this.companyForm.markAllAsTouched();
      if (!editing && !initialBranchName) this.actionError.set('Ingresá la primera sucursal.');
      return;
    }
    this.companySaving.set(true);
    this.actionError.set(null);
    const operation: Observable<AdminCompany | AdminCompanyCreated> = editing
      ? this.client.updateCompany(editing.id, { name })
      : this.client.createCompany(name, initialBranchName);
    operation.subscribe({
      next: (result) => {
        this.companySaving.set(false);
        this.companyEditorOpen.set(false);
        if ('activeBranchCount' in result) this.replaceCompany(result);
        else this.loadCompanies();
        this.actionMessage.set(editing ? 'Empresa actualizada.' :
          'Empresa y sucursal inicial creadas. El administrador ya tiene acceso.');
      },
      error: (error: HttpErrorResponse) => {
        this.companySaving.set(false);
        this.actionError.set(error.status === 409 ? 'Ya existe una empresa con ese nombre.' :
          'No se pudo guardar la empresa. Intentá de nuevo.');
      },
    });
  }

  protected toggleCompany(company: AdminCompany): void {
    if (this.companyActionId() || this.session()?.context?.companyId === company.id) return;
    if (company.isActive && !window.confirm('¿Inactivar esta empresa y dejar de ofrecer sus sucursales?'))
      return;
    this.companyActionId.set(company.id);
    this.actionError.set(null);
    this.client.updateCompany(company.id, { isActive: !company.isActive }).subscribe({
      next: (updated) => {
        this.replaceCompany(updated);
        this.companyActionId.set(null);
        this.actionMessage.set(updated.isActive ? 'Empresa activada.' : 'Empresa inactivada.');
      },
      error: (error: HttpErrorResponse) => {
        this.companyActionId.set(null);
        this.actionError.set(this.organizationError(error));
      },
    });
  }

  protected startCreateBranch(): void {
    this.editingBranch.set(null);
    this.branchForm.reset({ name: '' });
    this.actionError.set(null);
    this.branchEditorOpen.set(true);
  }

  protected startEditBranch(branch: AdminBranch): void {
    this.editingBranch.set(branch);
    this.branchForm.reset({ name: branch.name });
    this.actionError.set(null);
    this.branchEditorOpen.set(true);
  }

  protected cancelBranch(): void {
    if (!this.branchSaving()) this.branchEditorOpen.set(false);
  }

  protected saveBranch(): void {
    const company = this.selectedCompany();
    const name = this.branchForm.controls.name.value.trim();
    if (!company || this.branchSaving() || this.branchForm.invalid || !name) {
      this.branchForm.markAllAsTouched();
      return;
    }
    const editing = this.editingBranch();
    this.branchSaving.set(true);
    this.actionError.set(null);
    const operation = editing
      ? this.client.updateBranch(company.id, editing.id, { name })
      : this.client.createBranch(company.id, name);
    operation.subscribe({
      next: (branch) => {
        if (editing) this.branches.set(this.branches().map((item) =>
          item.id === branch.id ? branch : item));
        else {
          this.branches.set([...this.branches(), branch].sort((a, b) => a.name.localeCompare(b.name)));
          this.replaceCompany({ ...company, activeBranchCount: company.activeBranchCount + 1 });
        }
        this.branchSaving.set(false);
        this.branchEditorOpen.set(false);
        this.actionMessage.set(editing ? 'Sucursal actualizada.' : 'Sucursal creada.');
      },
      error: (error: HttpErrorResponse) => {
        this.branchSaving.set(false);
        this.actionError.set(error.status === 409 ? 'Ya existe una sucursal con ese nombre.' :
          'No se pudo guardar la sucursal. Intentá de nuevo.');
      },
    });
  }

  protected toggleBranch(branch: AdminBranch): void {
    const company = this.selectedCompany();
    if (!company || this.branchActionId() ||
        (this.session()?.context?.companyId === company.id &&
         this.session()?.context?.branchId === branch.id)) return;
    if (branch.isActive && !window.confirm(
      '¿Inactivar esta sucursal? No debe tener turnos ni tickets abiertos.',
    )) return;
    this.branchActionId.set(branch.id);
    this.actionError.set(null);
    this.client.updateBranch(company.id, branch.id, { isActive: !branch.isActive }).subscribe({
      next: (updated) => {
        this.branches.set(this.branches().map((item) => item.id === updated.id ? updated : item));
        this.replaceCompany({ ...company,
          activeBranchCount: company.activeBranchCount + (updated.isActive ? 1 : -1) });
        this.branchActionId.set(null);
        this.actionMessage.set(updated.isActive ? 'Sucursal activada.' : 'Sucursal inactivada.');
      },
      error: (error: HttpErrorResponse) => {
        this.branchActionId.set(null);
        this.actionError.set(this.organizationError(error));
      },
    });
  }

  protected showTerminals(branch: AdminBranch): void {
    const company = this.selectedCompany();
    if (!company) return;
    this.selectedTerminalBranch.set(branch);
    this.terminalEditorOpen.set(false);
    this.clearCredential();
    this.loadTerminals(company.id, branch.id);
  }

  protected closeTerminals(): void {
    if (this.terminalSaving() || this.terminalActionId() || this.shownCredential()) return;
    this.selectedTerminalBranch.set(null);
    this.terminals.set([]);
    this.terminalEditorOpen.set(false);
    this.clearCredential();
  }

  protected clearCredential(): void {
    this.shownCredential.set(null);
  }

  protected startCreateTerminal(): void {
    this.editingTerminal.set(null);
    this.terminalForm.reset({ name: '' });
    this.actionError.set(null);
    this.terminalEditorOpen.set(true);
  }

  protected startEditTerminal(terminal: AdminTerminal): void {
    this.editingTerminal.set(terminal);
    this.terminalForm.reset({ name: terminal.name });
    this.actionError.set(null);
    this.terminalEditorOpen.set(true);
  }

  protected cancelTerminal(): void {
    if (!this.terminalSaving()) this.terminalEditorOpen.set(false);
  }

  protected saveTerminal(): void {
    const company = this.selectedCompany();
    const branch = this.selectedTerminalBranch();
    const name = this.terminalForm.controls.name.value.trim();
    const editing = this.editingTerminal();
    if (!company || !branch || !name || this.terminalForm.invalid || this.terminalSaving()) {
      this.terminalForm.markAllAsTouched();
      return;
    }
    this.terminalSaving.set(true);
    this.actionError.set(null);
    const operation: Observable<ProvisionedTerminal | AdminTerminalUpdate> = editing
      ? this.terminalsClient.update(company.id, branch.id, editing.id, { name })
      : this.terminalsClient.create(company.id, branch.id, name);
    operation.subscribe({
      next: (result) => {
        if ('credential' in result) {
          this.terminals.set([...this.terminals(), { id: result.id, name: result.name,
            isActive: true, isHistorical: false, hasCredential: true }]);
          this.shownCredential.set(result.credential);
          this.updateBranchTerminalCount(branch.id, 1);
        } else {
          this.replaceTerminal(result);
        }
        this.terminalSaving.set(false);
        this.terminalEditorOpen.set(false);
        this.actionMessage.set(editing ? 'Caja actualizada.' :
          'Caja creada. Guardá la credencial ahora: no volverá a mostrarse.');
      },
      error: (error: HttpErrorResponse) => {
        this.terminalSaving.set(false);
        this.actionError.set(error.status === 409 ? 'El nombre de caja ya existe o la sucursal no está disponible.' :
          'No se pudo guardar la caja. Intentá de nuevo.');
      },
    });
  }

  protected toggleTerminal(terminal: AdminTerminal): void {
    const company = this.selectedCompany();
    const branch = this.selectedTerminalBranch();
    if (!company || !branch || this.terminalActionId()) return;
    if (terminal.isActive && !window.confirm(
      '¿Inactivar esta caja? Su credencial y sesiones dejarán de funcionar.',
    )) return;
    this.terminalActionId.set(terminal.id);
    this.actionError.set(null);
    this.terminalsClient.update(company.id, branch.id, terminal.id,
      { isActive: !terminal.isActive }).subscribe({
      next: (updated) => {
        this.replaceTerminal(updated);
        this.updateBranchTerminalCount(branch.id, updated.isActive ? 1 : -1);
        if (updated.newCredential) this.shownCredential.set(updated.newCredential);
        this.terminalActionId.set(null);
        this.actionMessage.set(updated.isActive
          ? 'Caja reactivada con credencial nueva. Guardala ahora.' : 'Caja inactivada.');
      },
      error: (error: HttpErrorResponse) => {
        this.terminalActionId.set(null);
        this.actionError.set(error.error?.error?.code === 'TERMINAL_HAS_OPEN_OPERATIONS'
          ? 'La caja tiene turno o ticket abierto. Cerralo antes de inactivar.'
          : 'No se pudo cambiar el estado de la caja.');
      },
    });
  }

  protected rotateTerminal(terminal: AdminTerminal): void {
    const company = this.selectedCompany();
    const branch = this.selectedTerminalBranch();
    if (!company || !branch || this.terminalActionId() || !window.confirm(
      '¿Rotar la credencial? La anterior y las sesiones de esta caja dejarán de funcionar.',
    )) return;
    this.terminalActionId.set(terminal.id);
    this.actionError.set(null);
    this.terminalsClient.rotate(company.id, branch.id, terminal.id).subscribe({
      next: (result) => {
        this.shownCredential.set(result.credential);
        this.terminalActionId.set(null);
        this.actionMessage.set('Credencial rotada. Guardá la nueva ahora.');
      },
      error: (error: HttpErrorResponse) => {
        this.terminalActionId.set(null);
        this.actionError.set(error.error?.error?.code === 'TERMINAL_HAS_OPEN_OPERATIONS'
          ? 'Cerrá el turno y los tickets de esa caja antes de rotar.'
          : 'No se pudo rotar la credencial.');
      },
    });
  }

  private loadCompanies(): void {
    this.companiesLoading.set(true);
    this.companiesError.set(null);
    this.client.companies().subscribe({
      next: (companies) => {
        this.companies.set(companies);
        this.companiesLoading.set(false);
      },
      error: () => {
        this.companiesLoading.set(false);
        this.companiesError.set('No se pudieron cargar las empresas.');
      },
    });
  }

  private loadBranches(companyId: string): void {
    this.branchesLoading.set(true);
    this.branchesError.set(null);
    this.client.branches(companyId).subscribe({
      next: (branches) => {
        if (this.selectedCompany()?.id !== companyId) return;
        this.branches.set(branches);
        this.branchesLoading.set(false);
      },
      error: () => {
        this.branchesLoading.set(false);
        this.branchesError.set('No se pudieron cargar las sucursales.');
      },
    });
  }

  private loadTerminals(companyId: string, branchId: string): void {
    this.terminalsLoading.set(true);
    this.terminalsError.set(null);
    this.terminalsClient.list(companyId, branchId).subscribe({
      next: (result) => {
        if (this.selectedCompany()?.id !== companyId ||
            this.selectedTerminalBranch()?.id !== branchId) return;
        this.terminals.set(result);
        this.terminalsLoading.set(false);
      },
      error: () => {
        this.terminalsLoading.set(false);
        this.terminalsError.set('No se pudieron cargar las cajas de esta sucursal.');
      },
    });
  }

  private replaceTerminal(terminal: AdminTerminal): void {
    this.terminals.set(this.terminals().map((item) => item.id === terminal.id ? terminal : item));
  }

  private updateBranchTerminalCount(branchId: string, delta: number): void {
    this.branches.set(this.branches().map((branch) => branch.id === branchId
      ? { ...branch, activeTerminalCount: branch.activeTerminalCount + delta }
      : branch));
    if (this.selectedTerminalBranch()?.id === branchId) {
      const updated = this.branches().find((branch) => branch.id === branchId);
      if (updated) this.selectedTerminalBranch.set(updated);
    }
  }

  private replaceCompany(company: AdminCompany): void {
    this.companies.set(this.companies().map((item) => item.id === company.id ? company : item));
    if (this.selectedCompany()?.id === company.id) this.selectedCompany.set(company);
  }

  private organizationError(error: HttpErrorResponse): string {
    const code = error.error?.error?.code;
    if (code === 'BRANCH_HAS_OPEN_OPERATIONS' || code === 'COMPANY_HAS_OPEN_OPERATIONS')
      return 'Hay turnos o tickets abiertos. Resolvelos antes de inactivar.';
    if (code === 'CURRENT_BRANCH_CANNOT_BE_INACTIVATED' ||
        code === 'CURRENT_COMPANY_CANNOT_BE_INACTIVATED')
      return 'No se puede inactivar la empresa o sucursal de la sesión actual.';
    return 'No se pudo cambiar el estado. Intentá de nuevo.';
  }
}
