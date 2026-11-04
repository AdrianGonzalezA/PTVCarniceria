import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { CashWithdrawalsPage } from './cash-withdrawals-page';

describe('CashWithdrawalsPage', () => {
  const session = {
    userId: 'admin-id', username: 'admin', expiresAtUtc: '2026-11-04T21:00:00Z',
    context: { userId: 'admin-id', companyId: 'company-id', companyName: 'La Rústica',
      branchId: 'branch-id', branchName: 'Alta Córdoba',
      permissions: ['platform.users.manage', 'cash.withdrawal.approve'], sessionId: 'session-id' },
  };
  const pending = {
    id: 'withdrawal-id', operationId: 'operation-id', cashierShiftId: 'shift-id', method: 'cash',
    amount: 250, reason: 'Compra urgente', status: 'pending', requestedByUserId: 'cashier-id',
    requesterName: 'cajero1', requestedAtUtc: '2026-11-04T18:00:00Z',
    decidedByUserId: null, decisionByName: null, decidedAtUtc: null, decisionReason: null,
    ledgerMovementId: null,
  };

  beforeEach(() => TestBed.configureTestingModule({
    imports: [CashWithdrawalsPage],
    providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
  }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  function open() {
    const fixture = TestBed.createComponent(CashWithdrawalsPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/sessions/current').flush(session);
    http.expectOne(request => request.url === '/api/admin/cash-withdrawals' &&
      request.params.get('status') === 'pending').flush({
      items: [pending], page: 1, pageSize: 30, totalItems: 1,
    });
    fixture.detectChanges();
    return { fixture, http, host: fixture.nativeElement as HTMLElement };
  }

  it('lists the pending request and approves it with a single decision request', () => {
    const { fixture, http, host } = open();
    expect(host.textContent).toContain('Compra urgente');
    expect(host.textContent).toContain('250,00');
    host.querySelector<HTMLButtonElement>('tbody button')!.click();
    fixture.detectChanges();
    expect(host.querySelector('#withdrawal-decision-title')?.textContent).toContain('Aprobar');
    host.querySelector<HTMLButtonElement>('.withdrawal-decision .row-actions button')!.click();
    const decision = http.expectOne('/api/admin/cash-withdrawals/withdrawal-id/decision');
    expect(decision.request.body).toMatchObject({ action: 'approve' });
    decision.flush({ ...pending, status: 'approved' });
    http.expectOne(request => request.url === '/api/admin/cash-withdrawals').flush({
      items: [], page: 1, pageSize: 30, totalItems: 0,
    });
    fixture.detectChanges();
    expect(host.querySelector('#withdrawal-decision-title')).toBeNull();
  });

  it('requires a reason to reject and sends it with the decision', () => {
    const { fixture, http, host } = open();
    host.querySelectorAll<HTMLButtonElement>('tbody button')[1].click();
    fixture.detectChanges();
    host.querySelector<HTMLButtonElement>('.withdrawal-decision .row-actions button')!.click();
    fixture.detectChanges();
    expect(host.querySelector('[role="alert"]')?.textContent).toContain('motivo');
    http.expectNone('/api/admin/cash-withdrawals/withdrawal-id/decision');
    const textarea = host.querySelector<HTMLTextAreaElement>('textarea')!;
    textarea.value = 'No corresponde';
    textarea.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    host.querySelector<HTMLButtonElement>('.withdrawal-decision .row-actions button')!.click();
    const decision = http.expectOne('/api/admin/cash-withdrawals/withdrawal-id/decision');
    expect(decision.request.body).toEqual({ action: 'reject', reason: 'No corresponde' });
    decision.flush({ ...pending, status: 'rejected', decisionReason: 'No corresponde' });
    http.expectOne(request => request.url === '/api/admin/cash-withdrawals').flush({
      items: [], page: 1, pageSize: 30, totalItems: 0,
    });
  });

  it("does not enable a requester's own approval", () => {
    const fixture = TestBed.createComponent(CashWithdrawalsPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/sessions/current').flush(session);
    http.expectOne(request => request.url === '/api/admin/cash-withdrawals').flush({
      items: [{ ...pending, requestedByUserId: session.userId }], page: 1, pageSize: 30,
      totalItems: 1,
    });
    fixture.detectChanges();
    const buttons = (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('tbody button');
    expect(buttons[0].disabled).toBe(true);
    expect(buttons[1].disabled).toBe(true);
  });
});
