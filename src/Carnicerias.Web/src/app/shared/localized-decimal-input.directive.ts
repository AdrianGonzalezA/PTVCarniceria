import { Directive, ElementRef, HostListener, Input, OnChanges, inject } from '@angular/core';
import { formatLocalizedDecimal, parseLocalizedDecimal } from './localized-decimal';

/** Shows es-AR grouping when idle; leaves a plain decimal value while editing. */
@Directive({ selector: 'input[appLocalizedDecimal]', host: { class: 'numeric-entry' } })
export class LocalizedDecimalInputDirective implements OnChanges {
  private readonly element = inject<ElementRef<HTMLInputElement>>(ElementRef);
  private editing = false;

  @Input() appLocalizedDecimal: number | string | null = null;
  @Input() decimalPlaces = 2;

  ngOnChanges(): void {
    const input = this.element.nativeElement;
    if (!this.editing) {
      this.displayValue();
      return;
    }
    const current = parseLocalizedDecimal(input.value, this.decimalPlaces);
    const next = this.numericValue();
    if (next !== current && next !== null) input.value = String(next);
  }

  @HostListener('focus')
  protected beginEditing(): void {
    const input = this.element.nativeElement;
    if (input.disabled || input.readOnly) return;
    const parsed = this.numericValue();
    if (parsed !== null) input.value = String(parsed);
    this.editing = true;
    input.select();
  }

  @HostListener('blur')
  protected endEditing(): void {
    this.editing = false;
    const input = this.element.nativeElement;
    const parsed = parseLocalizedDecimal(input.value, this.decimalPlaces);
    if (parsed !== null) input.value = formatLocalizedDecimal(parsed, this.decimalPlaces);
  }

  private displayValue(): void {
    const input = this.element.nativeElement;
    const parsed = this.numericValue();
    if (typeof this.appLocalizedDecimal === 'number' && !Number.isFinite(this.appLocalizedDecimal)) return;
    input.value = parsed === null ? String(this.appLocalizedDecimal ?? '') : formatLocalizedDecimal(parsed, this.decimalPlaces);
  }

  private numericValue(): number | null {
    if (typeof this.appLocalizedDecimal === 'number')
      return Number.isFinite(this.appLocalizedDecimal) ? this.appLocalizedDecimal : null;
    return parseLocalizedDecimal(this.appLocalizedDecimal ?? '', this.decimalPlaces);
  }
}
