import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { LocalizedDecimalInputDirective } from './localized-decimal-input.directive';

@Component({
  imports: [LocalizedDecimalInputDirective],
  template: '<input type="text" [appLocalizedDecimal]="value" [decimalPlaces]="places" [readOnly]="locked" (input)="value = $any($event.target).value" />',
})
class DecimalHost {
  value: number | string = 1234.5;
  places = 2;
  locked = false;
}

describe('localized decimal input', () => {
  it('groups on display, selects raw decimal entry on focus and formats on blur', () => {
    const fixture = TestBed.createComponent(DecimalHost);
    fixture.detectChanges();
    const input = fixture.nativeElement.querySelector('input') as HTMLInputElement;
    expect(input.value).toBe('1.234,50');

    input.focus();
    expect(input.value).toBe('1234.5');
    expect(input.selectionStart).toBe(0);
    expect(input.selectionEnd).toBe(input.value.length);

    input.value = '2.75';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(input.value).toBe('2.75');
    input.blur();
    expect(input.value).toBe('2,75');
  });

  it('updates a focused input when a scale or recovered draft changes its value', () => {
    const fixture = TestBed.createComponent(DecimalHost);
    fixture.componentInstance.value = 1;
    fixture.componentInstance.places = 3;
    fixture.detectChanges();
    const input = fixture.nativeElement.querySelector('input') as HTMLInputElement;
    input.focus();
    fixture.componentInstance.value = 0.75;
    fixture.detectChanges();
    expect(input.value).toBe('0.75');
  });

  it('leaves an invalid monetary entry visible for correction', () => {
    const fixture = TestBed.createComponent(DecimalHost);
    fixture.detectChanges();
    const input = fixture.nativeElement.querySelector('input') as HTMLInputElement;
    input.focus();
    input.value = '1.234';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    input.blur();
    expect(input.value).toBe('1.234');
  });

  it('keeps the displayed comma in a locked traceable-piece weight', () => {
    const fixture = TestBed.createComponent(DecimalHost);
    fixture.componentInstance.value = 0.5;
    fixture.componentInstance.places = 3;
    fixture.componentInstance.locked = true;
    fixture.detectChanges();
    const input = fixture.nativeElement.querySelector('input') as HTMLInputElement;
    expect(input.value).toBe('0,500');
    input.focus();
    expect(input.value).toBe('0,500');
  });
});
