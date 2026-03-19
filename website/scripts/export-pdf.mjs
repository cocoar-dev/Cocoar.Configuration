#!/usr/bin/env node

/**
 * Exports the VitePress documentation as a single, professionally-formatted PDF.
 *
 * Usage:  node scripts/export-pdf.mjs [output-path]
 * Default: ./dist/cocoar-configuration-docs.pdf
 */

import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';
import MarkdownIt from 'markdown-it';
import hljs from 'highlight.js';
import puppeteer from 'puppeteer-core';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(__dirname, '..');
const OUTPUT = process.argv[2] || path.join(ROOT, 'dist', 'cocoar-configuration-docs.pdf');

// ─── Document structure (matches VitePress sidebar) ─────────────────────────

const structure = [
  {
    part: 'Introduction',
    pages: [
      { file: 'guide/getting-started.md', title: 'Getting Started' },
      { file: 'guide/why-cocoar.md', title: 'Why Cocoar?' },
    ],
  },
  {
    part: 'Configuration',
    pages: [
      { file: 'guide/configuration/rules.md', title: 'Rules & Layering' },
      { file: 'guide/configuration/required-optional.md', title: 'Required vs Optional' },
      { file: 'guide/configuration/config-aware.md', title: 'Config-Aware Rules' },
      { file: 'guide/configuration/conditional-rules.md', title: 'Conditional Rules' },
      { file: 'guide/configuration/setup.md', title: 'Setup & Type Exposure' },
    ],
  },
  {
    part: 'Providers',
    pages: [
      { file: 'guide/providers/overview.md', title: 'Overview' },
      { file: 'guide/providers/file.md', title: 'File' },
      { file: 'guide/providers/environment.md', title: 'Environment Variables' },
      { file: 'guide/providers/command-line.md', title: 'Command Line' },
      { file: 'guide/providers/http-polling.md', title: 'HTTP Polling' },
      { file: 'guide/providers/microsoft-adapter.md', title: 'Microsoft IConfiguration' },
      { file: 'guide/providers/static-observable.md', title: 'Static & Observable' },
      { file: 'guide/providers/custom.md', title: 'Building Custom Providers' },
    ],
  },
  {
    part: 'Dependency Injection',
    pages: [
      { file: 'guide/di/setup.md', title: 'DI Setup' },
      { file: 'guide/di/lifetimes.md', title: 'Lifetimes & Registration' },
      { file: 'guide/di/aspnetcore.md', title: 'ASP.NET Core' },
    ],
  },
  {
    part: 'Reactive Updates',
    pages: [
      { file: 'guide/reactive/basics.md', title: 'IReactiveConfig<T>' },
      { file: 'guide/reactive/tuples.md', title: 'Reactive Tuples' },
      { file: 'guide/reactive/debouncing.md', title: 'Debouncing' },
    ],
  },
  {
    part: 'Feature Flags & Entitlements',
    pages: [
      { file: 'guide/flags/concepts.md', title: 'Concepts' },
      { file: 'guide/flags/defining-flags.md', title: 'Defining Flags' },
      { file: 'guide/flags/defining-entitlements.md', title: 'Defining Entitlements' },
      { file: 'guide/flags/registration.md', title: 'Registration' },
      { file: 'guide/flags/context-resolvers.md', title: 'Context Resolvers' },
      { file: 'guide/flags/rest-endpoints.md', title: 'REST Endpoints' },
      { file: 'guide/flags/expiry-health.md', title: 'Expiry & Health' },
    ],
  },
  {
    part: 'Secrets',
    pages: [
      { file: 'guide/secrets/overview.md', title: 'Overview' },
      { file: 'guide/secrets/secret-type.md', title: 'Secret<T> & Leases' },
      { file: 'guide/secrets/encryption-setup.md', title: 'Encryption Setup' },
      { file: 'guide/secrets/cli.md', title: 'CLI Tools' },
      { file: 'guide/secrets/security-model.md', title: 'Security Model' },
    ],
  },
  {
    part: 'Health Monitoring',
    pages: [
      { file: 'guide/health/overview.md', title: 'Overview' },
      { file: 'guide/health/aspnetcore.md', title: 'ASP.NET Core Health Checks' },
    ],
  },
  {
    part: 'Testing',
    pages: [
      { file: 'guide/testing/overrides.md', title: 'Test Overrides' },
      { file: 'guide/testing/integration.md', title: 'Integration Testing' },
    ],
  },
  {
    part: 'Analyzers',
    pages: [
      { file: 'guide/analyzers/overview.md', title: 'Overview' },
      { file: 'guide/analyzers/configuration.md', title: 'Configuration Diagnostics' },
      { file: 'guide/analyzers/flags.md', title: 'Flags Diagnostics' },
    ],
  },
  // Migration guides excluded — historical content, not relevant for consolidated doc
  {
    part: 'Reference',
    pages: [
      { file: 'reference/packages.md', title: 'Package Overview' },
      { file: 'reference/health-api.md', title: 'Health API' },
      { file: 'reference/cli-commands.md', title: 'CLI Commands' },
      { file: 'reference/analyzer-diagnostics.md', title: 'Analyzer Diagnostics' },
    ],
  },
  {
    part: 'Roadmap',
    pages: [
      { file: 'roadmap/overview.md', title: 'Overview' },
      { file: 'roadmap/confighub.md', title: 'ConfigHub' },
      { file: 'roadmap/cloud-providers.md', title: 'Cloud Providers' },
      { file: 'roadmap/database-provider.md', title: 'Database Provider' },
      { file: 'roadmap/push-delivery.md', title: 'Push-Based Delivery' },
      { file: 'roadmap/dotnet8.md', title: '.NET 8 LTS' },
    ],
  },
];

// ─── Markdown setup ─────────────────────────────────────────────────────────

const md = new MarkdownIt({
  html: true,
  linkify: true,
  typographer: true,
  highlight(str, lang) {
    if (lang && hljs.getLanguage(lang)) {
      try {
        return `<pre class="hljs"><code>${hljs.highlight(str, { language: lang }).value}</code></pre>`;
      } catch (_) { /* fall through */ }
    }
    return `<pre class="hljs"><code>${md.utils.escapeHtml(str)}</code></pre>`;
  },
});

// ─── Markdown pre-processing ────────────────────────────────────────────────

function slugify(text) {
  return text.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/(^-|-$)/g, '');
}

function stripFrontmatter(content) {
  return content.replace(/^---[\s\S]*?---\n*/, '');
}

function convertContainers(content) {
  const lines = content.split('\n');
  const result = [];
  const stack = [];

  for (const line of lines) {
    const openMatch = line.match(/^::: (\w+)\s*(.*)$/);
    if (openMatch) {
      const type = openMatch[1];
      const title = openMatch[2]?.trim() ||
        { tip: 'Tip', warning: 'Warning', info: 'Info', danger: 'Danger', details: 'Details' }[type] ||
        type.charAt(0).toUpperCase() + type.slice(1);
      stack.push(type);
      result.push(`<div class="callout callout-${type}"><p class="callout-title">${title}</p>`);
      result.push('');
      continue;
    }
    if (line.trim() === ':::' && stack.length > 0) {
      stack.pop();
      result.push('');
      result.push('</div>');
      continue;
    }
    result.push(line);
  }

  return result.join('\n');
}

function rewriteInternalLinks(content) {
  // Convert VitePress links like [text](/guide/flags/concepts) to #anchors
  return content.replace(/\[([^\]]+)\]\(\/([^)#]+)(#[^)]+)?\)/g, (_, text, linkPath, anchor) => {
    const slug = slugify(linkPath.replace(/\//g, '-').replace(/\.md$/, ''));
    return `[${text}](#${slug}${anchor || ''})`;
  });
}

function processMarkdown(filePath) {
  let content = fs.readFileSync(path.join(ROOT, filePath), 'utf-8');
  content = stripFrontmatter(content);
  content = convertContainers(content);
  content = rewriteInternalLinks(content);
  return content;
}

// ─── HTML generation ────────────────────────────────────────────────────────

function generateCoverPage() {
  return `
    <div class="cover">
      <div class="cover-content">
        <div class="cover-badge">Documentation</div>
        <h1 class="cover-title">Cocoar.Configuration</h1>
        <p class="cover-subtitle">Reactive, strongly-typed configuration for .NET</p>
        <div class="cover-meta">
          <span class="cover-version">v5.0</span>
          <span class="cover-sep">&middot;</span>
          <span class="cover-date">${new Date().toLocaleDateString('en-US', { year: 'numeric', month: 'long' })}</span>
        </div>
      </div>
      <div class="cover-footer">
        <p>Apache-2.0 License</p>
      </div>
    </div>`;
}

function generateToc() {
  let html = '<div class="toc"><h1 class="toc-title">Table of Contents</h1>';
  for (const section of structure) {
    html += `<div class="toc-part">${section.part}</div>`;
    html += '<ul class="toc-list">';
    for (const page of section.pages) {
      const slug = slugify(page.file.replace(/\//g, '-').replace(/\.md$/, ''));
      html += `<li><a href="#${slug}">${page.title}</a></li>`;
    }
    html += '</ul>';
  }
  html += '</div>';
  return html;
}

function generateBody() {
  let html = '';
  for (const section of structure) {
    html += `<div class="part-divider"><span>${section.part}</span></div>`;
    for (const page of section.pages) {
      const slug = slugify(page.file.replace(/\//g, '-').replace(/\.md$/, ''));
      const content = processMarkdown(page.file);
      const rendered = md.render(content);
      html += `<section class="chapter" id="${slug}">${rendered}</section>`;
    }
  }
  return html;
}

// ─── CSS ────────────────────────────────────────────────────────────────────

const CSS = `
/* ── Page setup ── */
@page {
  size: A4;
  margin: 22mm 18mm 22mm 18mm;
}

/* ── Reset ── */
* { box-sizing: border-box; }
html { -webkit-print-color-adjust: exact; print-color-adjust: exact; }
body {
  font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Oxygen, Ubuntu, Cantarell, sans-serif;
  font-size: 9.5pt;
  line-height: 1.65;
  color: #1a1a2e;
  margin: 0;
  padding: 0;
}

/* ── Cover page ── */
.cover {
  page-break-after: always;
  display: flex;
  flex-direction: column;
  justify-content: center;
  align-items: center;
  min-height: 100vh;
  text-align: center;
  position: relative;
}
.cover-content { margin-top: -80px; }
.cover-badge {
  display: inline-block;
  font-size: 10pt;
  font-weight: 600;
  letter-spacing: 2px;
  text-transform: uppercase;
  color: #5672cd;
  border: 2px solid #5672cd;
  border-radius: 4px;
  padding: 4px 16px;
  margin-bottom: 24px;
}
.cover-title {
  font-size: 36pt;
  font-weight: 700;
  color: #1a1a2e;
  margin: 0 0 12px 0;
  letter-spacing: -0.5px;
}
.cover-subtitle {
  font-size: 14pt;
  color: #64748b;
  font-weight: 400;
  margin: 0 0 32px 0;
}
.cover-meta {
  font-size: 11pt;
  color: #94a3b8;
}
.cover-version { font-weight: 600; color: #5672cd; }
.cover-sep { margin: 0 8px; }
.cover-footer {
  position: absolute;
  bottom: 0;
  font-size: 8.5pt;
  color: #94a3b8;
}

/* ── Table of contents ── */
.toc {
  page-break-after: always;
}
.toc-title {
  font-size: 22pt;
  font-weight: 700;
  color: #1a1a2e;
  margin: 0 0 28px 0;
  padding-bottom: 12px;
  border-bottom: 2px solid #e2e8f0;
}
.toc-part {
  font-size: 10.5pt;
  font-weight: 700;
  color: #5672cd;
  text-transform: uppercase;
  letter-spacing: 1px;
  margin: 20px 0 6px 0;
}
.toc-list {
  list-style: none;
  padding: 0;
  margin: 0 0 0 0;
}
.toc-list li {
  margin: 0;
  padding: 4px 0 4px 16px;
  border-bottom: 1px dotted #e2e8f0;
}
.toc-list a {
  color: #1a1a2e;
  text-decoration: none;
  font-size: 9.5pt;
}

/* ── Part dividers ── */
.part-divider {
  page-break-before: always;
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 35vh;
  text-align: center;
}
.part-divider span {
  font-size: 28pt;
  font-weight: 700;
  color: #1a1a2e;
  letter-spacing: -0.3px;
  position: relative;
}
.part-divider span::after {
  content: '';
  display: block;
  width: 60px;
  height: 3px;
  background: #5672cd;
  margin: 16px auto 0;
  border-radius: 2px;
}

/* ── Chapters ── */
.chapter {
  page-break-before: always;
}

/* ── Headings ── */
h1 {
  font-size: 20pt;
  font-weight: 700;
  color: #1a1a2e;
  margin: 0 0 16px 0;
  padding-bottom: 8px;
  border-bottom: 2px solid #e2e8f0;
}
h2 {
  font-size: 14pt;
  font-weight: 700;
  color: #1a1a2e;
  margin: 28px 0 10px 0;
  padding-bottom: 5px;
  border-bottom: 1px solid #f1f5f9;
}
h3 {
  font-size: 11.5pt;
  font-weight: 600;
  color: #334155;
  margin: 22px 0 8px 0;
}
h4 {
  font-size: 10pt;
  font-weight: 600;
  color: #475569;
  margin: 16px 0 6px 0;
}

/* ── Paragraphs ── */
p { margin: 8px 0; orphans: 3; widows: 3; }

/* ── Links ── */
a { color: #5672cd; text-decoration: none; }

/* ── Code ── */
code {
  font-family: 'Cascadia Code', 'JetBrains Mono', ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;
  font-size: 8.5pt;
  background: #f1f5f9;
  border: 1px solid #e2e8f0;
  border-radius: 3px;
  padding: 1px 4px;
}
pre.hljs {
  background: #f8fafc;
  border: 1px solid #e2e8f0;
  border-radius: 6px;
  padding: 14px 16px;
  margin: 12px 0;
  overflow-x: auto;
  break-inside: avoid;
}
pre.hljs code {
  background: none;
  border: none;
  padding: 0;
  font-size: 8pt;
  line-height: 1.55;
  color: #1e293b;
}

/* ── Syntax highlighting (light theme) ── */
.hljs-keyword { color: #8250df; font-weight: 600; }
.hljs-built_in { color: #8250df; }
.hljs-type { color: #0550ae; }
.hljs-title { color: #0550ae; }
.hljs-title.class_ { color: #0550ae; }
.hljs-title.function_ { color: #6639ba; }
.hljs-string { color: #0a3069; }
.hljs-number { color: #0550ae; }
.hljs-literal { color: #0550ae; }
.hljs-comment { color: #6e7781; font-style: italic; }
.hljs-attr { color: #0550ae; }
.hljs-attribute { color: #0550ae; }
.hljs-meta { color: #6e7781; }
.hljs-selector-tag { color: #116329; }
.hljs-selector-class { color: #0550ae; }
.hljs-params { color: #24292f; }
.hljs-property { color: #0550ae; }
.hljs-variable { color: #953800; }
.hljs-regexp { color: #0a3069; }
.hljs-symbol { color: #0550ae; }

/* ── Tables ── */
table {
  width: 100%;
  border-collapse: collapse;
  margin: 12px 0;
  font-size: 8.5pt;
  break-inside: avoid;
}
th {
  background: #f1f5f9;
  font-weight: 600;
  text-align: left;
  padding: 8px 10px;
  border: 1px solid #e2e8f0;
}
td {
  padding: 7px 10px;
  border: 1px solid #e2e8f0;
  vertical-align: top;
}
tr:nth-child(even) td { background: #f8fafc; }

/* ── Lists ── */
ul, ol { margin: 8px 0; padding-left: 24px; }
li { margin: 3px 0; }
li > p { margin: 2px 0; }

/* ── Blockquotes ── */
blockquote {
  border-left: 3px solid #e2e8f0;
  margin: 12px 0;
  padding: 4px 16px;
  color: #64748b;
}
blockquote p { margin: 4px 0; }

/* ── Horizontal rules ── */
hr {
  border: none;
  border-top: 1px solid #e2e8f0;
  margin: 24px 0;
}

/* ── Callout boxes ── */
.callout {
  border-left: 4px solid;
  border-radius: 0 6px 6px 0;
  padding: 12px 16px;
  margin: 14px 0;
  break-inside: avoid;
}
.callout p { margin: 4px 0; }
.callout-title {
  font-weight: 700;
  font-size: 9pt;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  margin: 0 0 6px 0 !important;
}

.callout-tip {
  border-color: #10b981;
  background: #ecfdf5;
}
.callout-tip .callout-title { color: #059669; }

.callout-warning {
  border-color: #f59e0b;
  background: #fffbeb;
}
.callout-warning .callout-title { color: #d97706; }

.callout-info {
  border-color: #3b82f6;
  background: #eff6ff;
}
.callout-info .callout-title { color: #2563eb; }

.callout-danger {
  border-color: #ef4444;
  background: #fef2f2;
}
.callout-danger .callout-title { color: #dc2626; }

.callout-details {
  border-color: #94a3b8;
  background: #f8fafc;
}
.callout-details .callout-title { color: #64748b; }

/* ── Images ── */
img { max-width: 100%; height: auto; }

/* ── Strong / em ── */
strong { font-weight: 600; }

/* ── Avoid awkward page breaks ── */
h1, h2, h3, h4 { page-break-after: avoid; }
pre, table, .callout { page-break-inside: avoid; }
`;

// ─── HTML assembly ──────────────────────────────────────────────────────────

function buildHtml() {
  const cover = generateCoverPage();
  const toc = generateToc();
  const body = generateBody();

  return `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <title>Cocoar.Configuration Documentation</title>
  <style>${CSS}</style>
</head>
<body>
  ${cover}
  ${toc}
  ${body}
</body>
</html>`;
}

// ─── PDF generation ─────────────────────────────────────────────────────────

function findChrome() {
  const candidates = [
    process.env.CHROME_PATH,
    'C:/Program Files/Google/Chrome/Application/chrome.exe',
    'C:/Program Files (x86)/Google/Chrome/Application/chrome.exe',
    '/usr/bin/google-chrome',
    '/usr/bin/chromium-browser',
    '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
  ].filter(Boolean);

  for (const p of candidates) {
    if (fs.existsSync(p)) return p;
  }
  throw new Error('Chrome not found. Set CHROME_PATH environment variable.');
}

async function generatePdf(html) {
  const chromePath = findChrome();
  console.log(`  Chrome: ${chromePath}`);

  const browser = await puppeteer.launch({
    executablePath: chromePath,
    headless: true,
    args: ['--no-sandbox', '--disable-setuid-sandbox'],
  });

  const page = await browser.newPage();
  await page.setContent(html, { waitUntil: 'networkidle0' });

  fs.mkdirSync(path.dirname(OUTPUT), { recursive: true });

  await page.pdf({
    path: OUTPUT,
    format: 'A4',
    margin: { top: '22mm', right: '18mm', bottom: '22mm', left: '18mm' },
    printBackground: true,
    displayHeaderFooter: true,
    headerTemplate: '<span></span>',
    footerTemplate: `
      <div style="width: 100%; text-align: center; font-size: 8pt; color: #94a3b8; font-family: sans-serif;">
        <span>Cocoar.Configuration</span>
        <span style="margin: 0 8px;">&middot;</span>
        <span class="pageNumber"></span> / <span class="totalPages"></span>
      </div>`,
  });

  await browser.close();
  return OUTPUT;
}

// ─── Main ───────────────────────────────────────────────────────────────────

async function main() {
  console.log('Exporting Cocoar.Configuration documentation to PDF...\n');

  // Count pages
  const pageCount = structure.reduce((sum, s) => sum + s.pages.length, 0);
  console.log(`  Sections: ${structure.length}`);
  console.log(`  Pages:    ${pageCount}`);

  // Build HTML
  console.log('\n  Building HTML...');
  const html = buildHtml();

  // Generate PDF
  console.log('  Generating PDF...');
  const outputPath = await generatePdf(html);

  const stats = fs.statSync(outputPath);
  const sizeMb = (stats.size / 1024 / 1024).toFixed(1);
  console.log(`\n  Output: ${outputPath} (${sizeMb} MB)\n`);
}

main().catch((err) => {
  console.error('\nExport failed:', err.message);
  process.exit(1);
});
