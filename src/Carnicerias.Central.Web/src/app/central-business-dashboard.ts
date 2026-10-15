import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, inject, OnInit, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Observable } from 'rxjs';
import { CentralBusinessClient, CentralBusinessSummary } from './central-business-client';

const argentinaDateTime = new Intl.DateTimeFormat('es-AR', {
  dateStyle: 'short', timeStyle: 'short', timeZone: 'America/Argentina/Buenos_Aires',
});
const argentinaMoney = new Intl.NumberFormat('es-AR', {
  style: 'currency', currency: 'ARS', maximumFractionDigits: 2,
});

@Component({
  selector: 'app-central-business-dashboard',
  standalone: true,
  imports: [ReactiveFormsModule],
  templateUrl: './central-business-dashboard.html',
  styleUrl: './central-business-dashboard.scss',
})
export class CentralBusinessDashboard implements OnInit {
  private readonly client = inject(CentralBusinessClient);
  private readonly formBuilder = inject(FormBuilder);
  private readonly destroyRef = inject(DestroyRef);

  readonly sessionExpired = output<void>();
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly summary = signal<CentralBusinessSummary | null>(null);
  protected readonly todaySelected = signal(true);
  protected readonly periodForm = this.formBuilder.nonNullable.group({
    fromDate: ['', Validators.required],
    toDate: ['', Validators.required],
  });

  ngOnInit(): void { this.loadToday(); }

  protected loadToday(): void {
    this.todaySelected.set(true);
    this.load(this.client.today());
  }

  protected loadDates(): void {
    if (this.periodForm.invalid || this.busy()) {
      this.periodForm.markAllAsTouched();
      return;
    }
    const { fromDate, toDate } = this.periodForm.getRawValue();
    if (fromDate > toDate) {
      this.error.set('La fecha inicial debe ser anterior o igual a la final.');
      return;
    }
    this.todaySelected.set(false);
    this.load(this.client.dates(fromDate, toDate));
  }

  protected refresh(): void {
    if (this.todaySelected()) this.loadToday();
    else this.loadDates();
  }

  protected formatInstant(value: string): string {
    return argentinaDateTime.format(new Date(value));
  }

  protected money(value: number): string { return argentinaMoney.format(value); }

  protected methodLabel(method: string): string {
    switch (method) {
      case 'cash': return 'Efectivo';
      case 'debit': return 'Débito';
      case 'credit': return 'Crédito';
      case 'transfer': return 'Transferencia';
      case 'mercadoPago': return 'Mercado Pago';
      case 'cheque': return 'Cheque';
      default: return 'Otro';
    }
  }

  private load(request: Observable<CentralBusinessSummary>): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set(null);
    this.summary.set(null);
    // Destroying this panel on logout cancels the request, so it cannot affect the next account.
    // https://angular.dev/api/core/rxjs-interop/takeUntilDestroyed
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (result) => {
        this.summary.set(result);
        this.busy.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.busy.set(false);
        if (error.status === 401) {
          this.summary.set(null);
          this.sessionExpired.emit();
        } else {
          this.error.set(error.status === 400
            ? 'Elegí un período válido de hasta 31 días.'
            : 'No se pudo consultar el negocio. Reintentá.');
        }
      },
    });
  }
}
