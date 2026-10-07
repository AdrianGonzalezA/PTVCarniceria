import { cp, mkdir, rm } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const angularBuild = path.resolve(projectRoot, '..', 'Carnicerias.Web', 'dist', 'Carnicerias.Web', 'browser');
const stagedWeb = path.join(projectRoot, 'web');

await rm(stagedWeb, { force: true, recursive: true });
await mkdir(stagedWeb, { recursive: true });
await cp(angularBuild, stagedWeb, { recursive: true });
