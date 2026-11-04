import { Directive, ElementRef, HostListener, inject } from '@angular/core';

@Directive({ selector: 'input[appSelectOnFocus]' })
export class SelectOnFocusDirective {
  private readonly element = inject<ElementRef<HTMLInputElement>>(ElementRef);

  @HostListener('focus')
  protected selectValue(): void {
    const input = this.element.nativeElement;
    if (!input.disabled && !input.readOnly) input.select();
  }
}
