interface EmulatorUiBridge {
  getState(): Promise<{ weightKg: number; stable: boolean }>;
  setWeight(weightKg: number, stable: boolean): Promise<{ weightKg: number; stable: boolean }>;
  makeKretzQr(items: readonly { plu: string; weightKg: number }[], ticketNumber: number,
    scaleNumber: number): Promise<{ payload: string; imageUrl: string }>;
  copyKretzQr(payload: string): Promise<void>;
}

const bridge = (window as unknown as Window & { carniceriasEmulator: EmulatorUiBridge }).carniceriasEmulator;
const form = document.querySelector<HTMLFormElement>('#weight-form')!;
const weight = document.querySelector<HTMLInputElement>('#weight')!;
const stable = document.querySelector<HTMLInputElement>('#stable')!;
const statusElement = document.querySelector<HTMLElement>('#status')!;
const kretzForm = document.querySelector<HTMLFormElement>('#kretz-item-form')!;
const kretzPlu = document.querySelector<HTMLInputElement>('#kretz-plu')!;
const kretzWeight = document.querySelector<HTMLInputElement>('#kretz-weight')!;
const kretzItems = document.querySelector<HTMLOListElement>('#kretz-items')!;
const kretzTicket = document.querySelector<HTMLInputElement>('#kretz-ticket')!;
const kretzScale = document.querySelector<HTMLInputElement>('#kretz-scale')!;
const kretzGenerate = document.querySelector<HTMLButtonElement>('#kretz-generate')!;
const kretzCopy = document.querySelector<HTMLButtonElement>('#kretz-copy')!;
const kretzClear = document.querySelector<HTMLButtonElement>('#kretz-clear')!;
const kretzStatus = document.querySelector<HTMLElement>('#kretz-status')!;
const kretzImage = document.querySelector<HTMLImageElement>('#kretz-image')!;
const kretzPayload = document.querySelector<HTMLTextAreaElement>('#kretz-payload')!;
const cuts: { plu: string; weightKg: number }[] = [];

function clearKretzPreview(): void {
  kretzImage.hidden = true;
  kretzPayload.hidden = true;
  kretzCopy.disabled = true;
  kretzPayload.value = '';
  kretzImage.removeAttribute('src');
}

function renderCuts(): void {
  kretzItems.replaceChildren(...cuts.map((cut, index) => {
    const item = document.createElement('li');
    item.append(document.createTextNode(`PLU ${cut.plu.padStart(5, '0')} · ` +
      `${cut.weightKg.toLocaleString('es-AR', { minimumFractionDigits: 3, maximumFractionDigits: 3 })} kg `));
    const remove = document.createElement('button');
    remove.type = 'button';
    remove.className = 'secondary';
    remove.textContent = 'Quitar';
    remove.setAttribute('aria-label', `Quitar corte ${index + 1}`);
    remove.addEventListener('click', () => {
      cuts.splice(index, 1);
      clearKretzPreview();
      renderCuts();
    });
    item.append(remove);
    return item;
  }));
}

kretzForm.addEventListener('submit', (event) => {
  event.preventDefault();
  const plu = kretzPlu.value.trim();
  const input = kretzWeight.value.trim();
  const weightKg = Number(input.replace(',', '.'));
  if (!/^\d{5}$/.test(plu) || !/^\d{1,2}([.,]\d{1,3})?$/.test(input) ||
      weightKg <= 0 || weightKg > 99.999 || cuts.length >= 99) {
    kretzStatus.textContent = 'Ingresá un PLU de 5 dígitos y un peso de 0,001 a 99,999 kg.';
    return;
  }
  cuts.push({ plu, weightKg });
  clearKretzPreview();
  renderCuts();
  kretzStatus.textContent = `${cuts.length} cortes listos para generar el QR.`;
  kretzPlu.value = '';
  kretzWeight.value = '';
  kretzPlu.focus();
});

kretzGenerate.addEventListener('click', () => {
  if (cuts.length === 0) {
    kretzStatus.textContent = 'Agregá al menos un corte.';
    return;
  }
  kretzGenerate.disabled = true;
  void bridge.makeKretzQr(cuts, Number(kretzTicket.value), Number(kretzScale.value))
    .then((preview) => {
      kretzPayload.value = preview.payload;
      kretzPayload.hidden = false;
      kretzImage.src = preview.imageUrl;
      kretzImage.hidden = false;
      kretzCopy.disabled = false;
      kretzStatus.textContent = `QR generado: ${cuts.length} cortes. Escaneá la imagen o copiá su lectura.`;
    })
    .catch(() => { kretzStatus.textContent = 'No se pudo generar el QR. Revisá los datos del ticket.'; })
    .finally(() => { kretzGenerate.disabled = false; });
});

kretzCopy.addEventListener('click', () => {
  void bridge.copyKretzQr(kretzPayload.value)
    .then(() => { kretzStatus.textContent = 'Lectura del QR copiada.'; })
    .catch(() => { kretzStatus.textContent = 'No se pudo copiar la lectura.'; });
});

kretzClear.addEventListener('click', () => {
  cuts.length = 0;
  clearKretzPreview();
  renderCuts();
  kretzStatus.textContent = 'Agregá uno o más cortes para generar el QR.';
});

async function refresh(): Promise<void> {
  const state = await bridge.getState();
  statusElement.textContent = state.stable
    ? `Balanza estable: ${state.weightKg.toLocaleString('es-AR', { minimumFractionDigits: 3, maximumFractionDigits: 3 })} kg`
    : 'Balanza sin lectura estable';
}

form.addEventListener('submit', (event) => {
  event.preventDefault();
  const quantity = Number(weight.value.replace(',', '.'));
  void bridge.setWeight(quantity, stable.checked).then(() => refresh()).catch(() => {
    statusElement.textContent = 'Ingresá un peso entre 0 y 10.000 kg con hasta tres decimales.';
  });
});

void refresh();
setInterval(() => { void refresh().catch(() => { statusElement.textContent = 'Emulador sin conexión'; }); }, 1000);
