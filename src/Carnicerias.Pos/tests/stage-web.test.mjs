import { afterEach, expect, it } from 'vitest';
import { mkdtemp, mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { stageWebFiles } from '../scripts/stage-web-files.mjs';

const temporaryDirectories = [];

afterEach(async () => {
  for (const directory of temporaryDirectories.splice(0))
    await rm(directory, { recursive: true, force: true });
});

it('keeps chunks referenced by open Electron windows while staging a new build', async () => {
  const directory = await mkdtemp(path.join(os.tmpdir(), 'carnicerias-stage-web-'));
  temporaryDirectories.push(directory);
  const build = path.join(directory, 'build');
  const staged = path.join(directory, 'web');
  await mkdir(build);
  await mkdir(staged);
  await writeFile(path.join(staged, 'index.html'), 'previous version');
  await writeFile(path.join(staged, 'chunk-previous.js'), 'previous chunk');
  await writeFile(path.join(build, 'index.html'), 'current version');
  await writeFile(path.join(build, 'chunk-current.js'), 'current chunk');

  await stageWebFiles(build, staged);

  expect(await readFile(path.join(staged, 'index.html'), 'utf8')).toBe('current version');
  expect(await readFile(path.join(staged, 'chunk-current.js'), 'utf8')).toBe('current chunk');
  expect(await readFile(path.join(staged, 'chunk-previous.js'), 'utf8')).toBe('previous chunk');
});

it('removes obsolete chunks only when staging a clean release package', async () => {
  const directory = await mkdtemp(path.join(os.tmpdir(), 'carnicerias-stage-web-'));
  temporaryDirectories.push(directory);
  const build = path.join(directory, 'build');
  const staged = path.join(directory, 'web');
  await mkdir(build);
  await mkdir(staged);
  await writeFile(path.join(staged, 'chunk-previous.js'), 'previous chunk');
  await writeFile(path.join(build, 'chunk-current.js'), 'current chunk');

  await stageWebFiles(build, staged, { clean: true });

  expect(await readFile(path.join(staged, 'chunk-current.js'), 'utf8')).toBe('current chunk');
  await expect(readFile(path.join(staged, 'chunk-previous.js'))).rejects.toMatchObject({ code: 'ENOENT' });
});
