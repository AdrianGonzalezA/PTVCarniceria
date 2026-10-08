import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { BarcodeLayoutClient, BarcodePreviewResponse } from '../../core/inventory/barcode-layout-client';
import { CurrentSession, SessionClient } from '../../core/session/session-client';

@Component({
  selector: 'app-barcode-layout-page',
  imports: [RouterLink, ReactiveFormsModule],
  templateUrl: './barcode-layout-page.html',
  styleUrls: ['./admin-page.scss', './categories-page.scss', './barcode-layout-page.scss'],
})
export class BarcodeLayoutPage implements OnInit {
  private readonly layouts = inject(BarcodeLayoutClient);
  private readonly sessions = inject(SessionClient);
  private readonly router = inject(Router);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly session = signal<CurrentSession | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly result = signal<BarcodePreviewResponse | null>(null);
  protected readonly fields = computed(() => Object.entries(this.result()?.fields ?? {})
    .map(([name, value]) => ({ name, value })));
  protected readonly form = this.formBuilder.nonNullable.group({
    formula: ['', [Validators.required, Validators.maxLength(512)]],
    code: ['', [Validators.required, Validators.maxLength(80)]],
    weightField: ['peso', [Validators.required, Validators.maxLength(80)]],
    weightDecimals: [2, [Validators.required, Validators.min(0), Validators.max(6)]],
  });

  ngOnInit(): void {
    this.sessions.current().subscribe({
      next: (session) => {
        if (!session.context?.permissions.includes('inventory.stock.manage')) {
          void this.router.navigateByUrl('/');
          return;
        }
        this.session.set(session);
      },
      error: () => void this.router.navigateByUrl('/'),
    });
  }

  protected preview(): void {
    if (this.busy() || this.form.invalid) {
      this.form.markAllAsTouched();
      this.error.set('Completá la fórmula, el código y la escala del peso.');
      return;
    }
    const raw = this.form.getRawValue();
    this.busy.set(true);
    this.error.set(null);
    this.result.set(null);
    this.layouts.preview({
      formula: raw.formula.trim(),
      code: raw.code.trim(),
      weightField: raw.weightField.trim(),
      weightDecimals: Number(raw.weightDecimals),
    }).subscribe({
      next: (result) => {
        this.result.set(result);
        this.busy.set(false);
      },
      error: (response: HttpErrorResponse) => {
        const code = response.error?.error?.code;
        this.error.set(code === 'INVALID_BARCODE_LAYOUT'
          ? 'La fórmula no es válida o no incluye el campo de peso indicado.'
          : code === 'INVALID_BARCODE' || code === 'INVALID_BARCODE_WEIGHT'
            ? 'El código no coincide con la fórmula o no contiene un peso válido.'
            : 'No se pudo probar la lectura. Revisá los datos y la conexión.');
        this.busy.set(false);
      },
    });
  }

  protected formatWeight(value: number): string {
    return value.toLocaleString('es-AR', {
      minimumFractionDigits: 2,
      maximumFractionDigits: 3,
    });
  }
}
