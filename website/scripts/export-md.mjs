#!/usr/bin/env node

/**
 * Exports the VitePress documentation as a single Markdown file.
 * Reads the sidebar structure directly from .vitepress/config.ts — no manual list to maintain.
 *
 * Usage:  node scripts/export-md.mjs [output-path]
 * Default: ./dist/cocoar-configuration-docs.md
 */

import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(__dirname, '..');
const OUTPUT = process.argv[2] || path.join(ROOT, 'dist', 'cocoar-configuration-docs.md');

// ─── Extract structure from VitePress config ─────────────────────────────────

function extractStructure() {
  const configPath = path.join(ROOT, '.vitepress', 'config.ts');
  const configSource = fs.readFileSync(configPath, 'utf-8');

  // Extract the sidebar object content between "sidebar: {" and its closing "}"
  const sidebarStart = configSource.indexOf('sidebar: {');
  if (sidebarStart === -1) throw new Error('Could not find sidebar in config.ts');

  // Find all sidebar item links and their group names
  const structure = [];
  let currentGroup = null;

  const lines = configSource.split('\n');
  let inSidebar = false;
  let depth = 0;

  for (const line of lines) {
    const trimmed = line.trim();

    if (trimmed.startsWith('sidebar:')) {
      inSidebar = true;
      continue;
    }
    if (!inSidebar) continue;

    // Track brace depth to know when sidebar ends
    for (const ch of trimmed) {
      if (ch === '{' || ch === '[') depth++;
      if (ch === '}' || ch === ']') depth--;
    }
    if (depth <= 0 && inSidebar && trimmed.includes('}')) break;

    // Match group: text: 'GroupName'
    const groupMatch = trimmed.match(/text:\s*'([^']+)'/);
    if (groupMatch && !trimmed.includes('link:')) {
      const groupName = groupMatch[1];
      currentGroup = { part: groupName, pages: [] };
      structure.push(currentGroup);
    }

    // Match item: { text: '...', link: '/guide/...' }
    const itemMatch = trimmed.match(/text:\s*'([^']*(?:<[^>]*>[^']*)*)',\s*link:\s*'([^']+)'/);
    if (itemMatch && currentGroup) {
      const title = itemMatch[1].replace(/<[^>]*>/g, '').trim();
      const link = itemMatch[2];
      const filePath = link.replace(/^\//, '') + '.md';

      // Skip migration guides (historical)
      if (filePath.includes('migration/')) continue;

      if (fs.existsSync(path.join(ROOT, filePath))) {
        currentGroup.pages.push({ file: filePath, title });
      }
    }
  }

  // Remove empty groups
  return structure.filter(g => g.pages.length > 0);
}

const structure = extractStructure();

// ─── Pre-processing ─────────────────────────────────────────────────────────

function stripFrontmatter(content) {
  return content.replace(/^---[\s\S]*?---\n*/, '');
}

function stripBadges(content) {
  return content.replace(/<Badge[^/]*\/>/g, '');
}

function convertContainers(content) {
  const lines = content.split('\n');
  const result = [];
  const stack = [];

  for (const line of lines) {
    const openMatch = line.match(/^::: (\w+)\s*(.*)$/);
    if (openMatch) {
      const type = openMatch[1];
      const label = openMatch[2]?.trim() ||
        { tip: 'Tip', warning: 'Warning', info: 'Info', danger: 'Danger', details: 'Details' }[type] ||
        type.charAt(0).toUpperCase() + type.slice(1);
      stack.push(type);
      result.push(`> **${label}**`);
      result.push('>');
      continue;
    }
    if (line.trim() === ':::' && stack.length > 0) {
      stack.pop();
      result.push('');
      continue;
    }
    if (stack.length > 0) {
      result.push(line ? `> ${line}` : '>');
    } else {
      result.push(line);
    }
  }

  return result.join('\n');
}

function processPage(filePath) {
  let content = fs.readFileSync(path.join(ROOT, filePath), 'utf-8');
  content = stripFrontmatter(content);
  content = stripBadges(content);
  content = convertContainers(content);
  return content.trim();
}

// ─── Document assembly ──────────────────────────────────────────────────────

function buildDocument() {
  const parts = [];

  // Header
  parts.push(`# Cocoar.Configuration Documentation`);
  parts.push('');
  parts.push(`*Reactive, strongly-typed configuration for .NET — v5.0 — ${new Date().toLocaleDateString('en-US', { year: 'numeric', month: 'long' })}*`);
  parts.push('');
  parts.push('---');
  parts.push('');

  // Table of contents
  parts.push('## Table of Contents');
  parts.push('');
  for (const section of structure) {
    parts.push(`**${section.part}**`);
    for (const page of section.pages) {
      parts.push(`- ${page.title}`);
    }
    parts.push('');
  }
  parts.push('---');
  parts.push('');

  // Content
  for (const section of structure) {
    parts.push(`# ${section.part}`);
    parts.push('');

    for (const page of section.pages) {
      const content = processPage(page.file);
      parts.push(content);
      parts.push('');
      parts.push('---');
      parts.push('');
    }
  }

  return parts.join('\n');
}

// ─── Main ───────────────────────────────────────────────────────────────────

const pageCount = structure.reduce((sum, s) => sum + s.pages.length, 0);
console.log(`Exporting Cocoar.Configuration documentation to Markdown...\n`);
console.log(`  Sections: ${structure.length}`);
console.log(`  Pages:    ${pageCount}`);

const doc = buildDocument();

fs.mkdirSync(path.dirname(OUTPUT), { recursive: true });
fs.writeFileSync(OUTPUT, doc, 'utf-8');

const sizeKb = (Buffer.byteLength(doc, 'utf-8') / 1024).toFixed(0);
console.log(`\n  Output: ${OUTPUT} (${sizeKb} KB)`);

// ─── llms.txt and llms-full.txt generation ───────────────────────────────────

const distDir = path.dirname(OUTPUT);

// llms-full.txt — full documentation export
const llmsFullPath = path.join(distDir, 'llms-full.txt');
fs.writeFileSync(llmsFullPath, doc, 'utf-8');
console.log(`  Output: ${llmsFullPath} (${sizeKb} KB)`);

// llms.txt — short summary with table of contents
function buildLlmsTxt() {
  const parts = [];

  parts.push('# Cocoar.Configuration');
  parts.push('');
  parts.push('> Reactive, strongly-typed configuration for .NET');
  parts.push('');
  parts.push('## Links');
  parts.push('- Documentation: https://cocoar-dev.github.io/Cocoar.Configuration/');
  parts.push('- GitHub: https://github.com/cocoar-dev/Cocoar.Configuration');
  parts.push('- NuGet: https://www.nuget.org/packages/Cocoar.Configuration');
  parts.push('- Full documentation for LLMs: https://cocoar-dev.github.io/Cocoar.Configuration/llms-full.txt');
  parts.push('');
  parts.push('## Overview');
  parts.push('');
  parts.push('Cocoar.Configuration is a reactive, strongly-typed configuration library for .NET 9.0. It provides a fluent API for defining layered configuration rules with multiple providers (file, environment, HTTP, and more). The library supports live reactive updates, feature flags and entitlements, encrypted secrets with memory-safe handling, and integrates seamlessly with ASP.NET Core dependency injection.');
  parts.push('');
  parts.push('## Key Concepts');
  parts.push('');

  for (const section of structure) {
    parts.push(`### ${section.part}`);
    for (const page of section.pages) {
      parts.push(`- ${page.title}`);
    }
    parts.push('');
  }

  return parts.join('\n');
}

const llmsTxt = buildLlmsTxt();
const llmsTxtPath = path.join(distDir, 'llms.txt');
fs.writeFileSync(llmsTxtPath, llmsTxt, 'utf-8');
const llmsSizeKb = (Buffer.byteLength(llmsTxt, 'utf-8') / 1024).toFixed(0);
console.log(`  Output: ${llmsTxtPath} (${llmsSizeKb} KB)`);
console.log('');
