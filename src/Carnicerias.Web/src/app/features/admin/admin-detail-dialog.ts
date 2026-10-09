import { AfterViewInit, Component, ElementRef, input, OnDestroy, output, viewChild } from '@angular/core';

@Component({
  selector: 'app-admin-detail-dialog',
  template: `<dialog #dialog class="admin-detail-dialog" [attr.aria-labelledby]="labelledBy()"
    (cancel)="cancel($event)"><ng-content /></dialog>`,
  styleUrl: './admin-detail-dialog.scss',
})
export class AdminDetailDialog implements AfterViewInit, OnDestroy {
  readonly labelledBy = input.required<string>();
  readonly canClose = input(true);
  readonly closed = output<void>();
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  ngAfterViewInit(): void {
    const dialog = this.dialog().nativeElement;
    if (dialog.showModal) dialog.showModal();
    else dialog.setAttribute('open', ''); // jsdom does not implement showModal.
  }

  ngOnDestroy(): void {
    const dialog = this.dialog().nativeElement;
    if (!dialog.open) return;
    if (dialog.close) dialog.close();
    else dialog.removeAttribute('open');
  }

  protected cancel(event: Event): void {
    event.preventDefault();
    event.stopPropagation();
    if (this.canClose()) this.closed.emit();
  }
}
