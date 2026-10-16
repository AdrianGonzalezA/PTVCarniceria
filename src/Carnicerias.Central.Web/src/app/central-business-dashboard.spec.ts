import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { CentralBusinessDashboard } from './central-business-dashboard';

describe('CentralBusinessDashboard', () => {
  function setup() {
    TestBed.configureTestingModule({
      imports: [CentralBusinessDashboard],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    const fixture = TestBed.createComponent(CentralBusinessDashboard);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    const element: HTMLElement = fixture.nativeElement;
    return { fixture, http, element };
  }

  it('loads Argentina today and keeps open cash beside the branch, not in the total', async () => {
    const { fixture, http, element } = setup();
    http.expectOne('/api/v1/business/summary').flush({
      window: { fromUtc: '2026-10-14T03:00:00Z', toUtc: '2026-10-14T18:00:00Z' },
      retrievedAtUtc: '2026-10-14T18:00:00Z', completeness: 'complete',
      aggregate: { currency: 'ARS', saleCount: 2, salesTotal: 150, salePaymentsTotal: 120 },
      branches: [{ installationId: 'a', label: 'Sucursal Centro', status: 'ok',
        generatedAtUtc: '2026-10-14T17:59:59Z', saleCount: 2,
        salesTotal: 150, salePaymentsTotal: 120, openShiftCount: 1,
        openShiftCashBalance: -5, paymentsByMethod: [{ method: 'cash', amount: 120 }] }],
    });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(element.querySelector('#business-title')?.textContent).toContain('Negocio en vivo');
    expect(element.textContent).toContain('Sucursal Centro');
    expect(element.textContent).toContain('Saldo de turnos abiertos');
    expect(element.querySelector('.aggregate')?.textContent).not.toContain('Saldo de turnos abiertos');
    expect(element.querySelector('.aggregate')?.textContent).toContain('Ventas');
    expect(element.querySelector('label[for="business-from-date"]')).not.toBeNull();
    expect(element.querySelector('label[for="business-to-date"]')).not.toBeNull();
    http.verify();
  });

  it('shows a partial warning without a consolidated total or fake zero', async () => {
    const { fixture, http, element } = setup();
    http.expectOne('/api/v1/business/summary').flush({
      window: { fromUtc: '2026-10-14T03:00:00Z', toUtc: '2026-10-14T18:00:00Z' },
      retrievedAtUtc: '2026-10-14T18:00:00Z', completeness: 'incomplete', aggregate: null,
      branches: [{ installationId: 'a', label: 'Sucursal caída', status: 'unavailable',
        generatedAtUtc: null, saleCount: null, salesTotal: null,
        salePaymentsTotal: null, openShiftCount: null,
        openShiftCashBalance: null, paymentsByMethod: null }],
    });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(element.querySelector('[role="alert"]')?.textContent).toContain('incompletos');
    expect(element.querySelector('.aggregate')).toBeNull();
    expect(element.textContent).toContain('Sucursal caída');
    expect(element.textContent).toContain('No disponible');
    expect(element.textContent).not.toContain('$ 0');
    http.verify();
  });

  it('uses selected local dates and emits session expiration on 401', async () => {
    const { fixture, http, element } = setup();
    http.expectOne('/api/v1/business/summary').flush({
      window: { fromUtc: '2026-10-14T03:00:00Z', toUtc: '2026-10-14T18:00:00Z' },
      retrievedAtUtc: '2026-10-14T18:00:00Z', completeness: 'empty', aggregate: null, branches: [],
    });
    await fixture.whenStable();
    fixture.detectChanges();

    for (const [id, value] of [
      ['business-from-date', '2026-10-13'], ['business-to-date', '2026-10-14'],
    ]) {
      const input = element.querySelector<HTMLInputElement>(`#${id}`)!;
      input.value = value;
      input.dispatchEvent(new Event('input', { bubbles: true }));
    }
    let expired = false;
    fixture.componentInstance.sessionExpired.subscribe(() => { expired = true; });
    element.querySelector<HTMLFormElement>('.period-form')!.dispatchEvent(
      new Event('submit', { bubbles: true, cancelable: true }));
    const request = http.expectOne(
      '/api/v1/business/summary?fromDate=2026-10-13&toDate=2026-10-14');
    request.flush(null, { status: 401, statusText: 'Unauthorized' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(expired).toBe(true);
    http.verify();
  });

  it('explains an invalid period and can return to today', async () => {
    const { fixture, http, element } = setup();
    http.expectOne('/api/v1/business/summary').flush({
      window: { fromUtc: '2026-10-14T03:00:00Z', toUtc: '2026-10-14T18:00:00Z' },
      retrievedAtUtc: '2026-10-14T18:00:00Z', completeness: 'empty', aggregate: null, branches: [],
    });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(element.textContent).toContain('Todavía no hay sucursales activas');

    for (const [id, value] of [
      ['business-from-date', '2026-10-01'], ['business-to-date', '2026-11-05'],
    ]) {
      const input = element.querySelector<HTMLInputElement>(`#${id}`)!;
      input.value = value;
      input.dispatchEvent(new Event('input', { bubbles: true }));
    }
    element.querySelector<HTMLFormElement>('.period-form')!.dispatchEvent(
      new Event('submit', { bubbles: true, cancelable: true }));
    http.expectOne('/api/v1/business/summary?fromDate=2026-10-01&toDate=2026-11-05')
      .flush(null, { status: 400, statusText: 'Bad Request' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(element.querySelector('[role="alert"]')?.textContent).toContain('hasta 31 días');
    expect(element.querySelector('.aggregate')).toBeNull();

    element.querySelector<HTMLButtonElement>('.period-controls > button')!.click();
    http.expectOne('/api/v1/business/summary').flush({
      window: { fromUtc: '2026-10-14T03:00:00Z', toUtc: '2026-10-14T18:00:00Z' },
      retrievedAtUtc: '2026-10-14T18:00:00Z', completeness: 'empty', aggregate: null, branches: [],
    });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(element.querySelector('[role="alert"]')).toBeNull();
    http.verify();
  });
});
