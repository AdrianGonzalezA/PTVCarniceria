import { cp, mkdir, rm } from 'node:fs/promises';

export async function stageWebFiles(build, staged, { clean = false } = {}) {
  if (clean) await rm(staged, { force: true, recursive: true });
  await mkdir(staged, { recursive: true });
  await cp(build, staged, { recursive: true });
}
