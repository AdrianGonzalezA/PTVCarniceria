// Read-only end-to-end lab check: existing Caja A credential -> assigned driver -> local scale pipe.
const path = require('node:path');
const { writeFileSync } = require('node:fs');
const { app, net, safeStorage } = require('electron');
const { loadTerminalCredential } = require('../dist/terminal-credential.js');
const { fetchAssignedDevices, TerminalDevices } = require('../dist/terminal-devices.js');

const profile = path.join(app.getPath('appData'), 'carnicerias-pos', 'profiles', 'caja-rustica-a');
const resultFile = path.resolve(__dirname, '../../../.local-backups/virtual-scale-smoke-result.json');
app.setPath('userData', profile);

app.whenReady().then(async () => {
  const credential = await loadTerminalCredential(profile, undefined, safeStorage);
  if (!credential) throw new Error('CAJA_A_NOT_PROVISIONED');
  const assigned = await fetchAssignedDevices('http://localhost:5197', credential,
    request => net.fetch(request));
  if (assigned.scale?.driver !== 'virtual-scale') throw new Error('VIRTUAL_SCALE_NOT_ASSIGNED');
  const devices = new TerminalDevices();
  await devices.apply(assigned);
  const reading = await devices.readScaleAsync();
  if (!reading.stable || reading.weightKg <= 0) throw new Error('VIRTUAL_SCALE_NOT_STABLE');
  writeFileSync(resultFile, JSON.stringify({ ok: true, weightKg: reading.weightKg,
    observedAtUtc: reading.observedAtUtc }));
  console.log(`Caja A: ${reading.weightKg.toFixed(3)} kg estables desde balanza virtual`);
  await devices.stop();
}).catch(error => {
  const message = error instanceof Error ? error.message : 'VIRTUAL_SCALE_SMOKE_FAILED';
  writeFileSync(resultFile, JSON.stringify({ ok: false, error: message }));
  console.error(message);
  process.exitCode = 1;
}).finally(() => app.quit());
