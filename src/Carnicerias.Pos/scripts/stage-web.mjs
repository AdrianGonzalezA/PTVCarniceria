import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { stageWebFiles } from './stage-web-files.mjs';

const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const angularBuild = path.resolve(projectRoot, '..', 'Carnicerias.Web', 'dist', 'Carnicerias.Web', 'browser');
const stagedWeb = path.join(projectRoot, 'web');
const argumentsList = process.argv.slice(2);
if (argumentsList.length > 1 || argumentsList.some((argument) => argument !== '--clean'))
  throw new Error('Usage: stage-web.mjs [--clean]');

await stageWebFiles(angularBuild, stagedWeb, { clean: argumentsList.length === 1 });
