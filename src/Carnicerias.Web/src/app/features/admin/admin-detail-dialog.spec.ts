import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { AdminDetailDialog } from './admin-detail-dialog';

@Component({
  imports: [AdminDetailDialog],
  template: `<app-admin-detail-dialog labelledBy="outer" (closed)="outerClosed = outerClosed + 1">
    <h2 id="outer">Outer</h2>
    <app-admin-detail-dialog labelledBy="inner" (closed)="innerClosed = innerClosed + 1">
      <h2 id="inner">Inner</h2>
    </app-admin-detail-dialog>
  </app-admin-detail-dialog>`,
})
class NestedDialogHost {
  outerClosed = 0;
  innerClosed = 0;
}

describe('AdminDetailDialog', () => {
  it('opens as a dialog and closes with Escape when allowed', () => {
    TestBed.configureTestingModule({ imports: [AdminDetailDialog] });
    const fixture = TestBed.createComponent(AdminDetailDialog);
    fixture.componentRef.setInput('labelledBy', 'dialog-title');
    let closes = 0;
    fixture.componentInstance.closed.subscribe(() => closes++);
    fixture.detectChanges();

    const dialog = fixture.nativeElement.querySelector('dialog') as HTMLDialogElement;
    expect(dialog.open).toBe(true);
    expect(dialog.getAttribute('aria-labelledby')).toBe('dialog-title');
    const cancel = new Event('cancel', { cancelable: true });
    dialog.dispatchEvent(cancel);
    expect(cancel.defaultPrevented).toBe(true);
    expect(closes).toBe(1);
    fixture.destroy();
    expect(dialog.open).toBe(false);
  });

  it('keeps the dialog open when Escape is blocked during a pending action', () => {
    TestBed.configureTestingModule({ imports: [AdminDetailDialog] });
    const fixture = TestBed.createComponent(AdminDetailDialog);
    fixture.componentRef.setInput('labelledBy', 'dialog-title');
    fixture.componentRef.setInput('canClose', false);
    let closes = 0;
    fixture.componentInstance.closed.subscribe(() => closes++);
    fixture.detectChanges();

    const dialog = fixture.nativeElement.querySelector('dialog') as HTMLDialogElement;
    dialog.dispatchEvent(new Event('cancel', { cancelable: true }));
    expect(dialog.open).toBe(true);
    expect(closes).toBe(0);
    fixture.destroy();
  });

  it('does not propagate Escape from a nested history dialog to its parent', () => {
    TestBed.configureTestingModule({ imports: [NestedDialogHost] });
    const fixture = TestBed.createComponent(NestedDialogHost);
    fixture.detectChanges();

    const dialogs = fixture.nativeElement.querySelectorAll('dialog') as NodeListOf<HTMLDialogElement>;
    dialogs[1].dispatchEvent(new Event('cancel', { bubbles: true, cancelable: true }));
    expect(fixture.componentInstance.innerClosed).toBe(1);
    expect(fixture.componentInstance.outerClosed).toBe(0);
    fixture.destroy();
  });
});
