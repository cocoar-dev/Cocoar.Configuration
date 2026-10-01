// Generates the Agent Skill that ships inside the Cocoar.Configuration package from the docs.
//
// The documentation is the only source. Every page under website/guide and website/reference
// becomes a reference file of the skill, and the page's frontmatter description — the same line
// that feeds llms.txt on the docs site — becomes its entry in the skill's index. The only
// hand-written part is website/skill/SKILL.header.md: name, trigger description, package table,
// the gotchas. A hand-maintained summary would drift from the docs with every release.
//
//   node website/scripts/sync-skill.mjs          regenerate skills/cocoar-configuration/
//   node website/scripts/sync-skill.mjs --check  exit 1 if skills/cocoar-configuration/ is out of date (CI)
//
// Agent Skills are progressive: an agent loads only the skill's name and description at startup,
// SKILL.md when a task matches, and a reference file when the index says it is relevant. That is
// why the index carries the descriptions and why the pages stay separate files.

import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const websiteDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const repoDir = path.resolve(websiteDir, '..')
const skillName = 'cocoar-configuration'
const skillDir = path.join(repoDir, 'skills', skillName)
const headerFile = path.join(websiteDir, 'skill', 'SKILL.header.md')
const docsBaseUrl = 'https://docs.cocoar.dev/configuration'
const check = process.argv.includes('--check')

// Mirrors the site's sidebar (.vitepress/config.ts). Listing pages explicitly is deliberate: a
// docs page that is neither here nor in EXCLUDED fails the run, so a new page cannot silently
// stay out of the skill.
const SECTIONS = [
  { title: 'Introduction', pages: ['guide/getting-started.md', 'guide/certificates.md'] },
  {
    title: 'Configuration',
    pages: [
      'guide/configuration/rules.md',
      'guide/configuration/required-optional.md',
      'guide/configuration/setup.md',
      'guide/configuration/config-aware.md',
      'guide/configuration/conditional-rules.md',
      'guide/configuration/aggregate-rules.md',
    ],
  },
  {
    title: 'Providers',
    pages: [
      'guide/providers/overview.md',
      'guide/providers/file.md',
      'guide/providers/yaml.md',
      'guide/providers/toml.md',
      'guide/providers/dotenv.md',
      'guide/providers/ini.md',
      'guide/providers/environment.md',
      'guide/providers/command-line.md',
      'guide/providers/confighub.md',
      'guide/providers/http-polling.md',
      'guide/providers/microsoft-adapter.md',
      'guide/providers/static-observable.md',
      'guide/providers/writable-store.md',
      'guide/providers/marten-store.md',
      'guide/providers/custom.md',
    ],
  },
  {
    title: 'Dependency injection',
    pages: ['guide/di/setup.md', 'guide/di/aspnetcore.md', 'guide/di/lifetimes.md', 'guide/di/service-backed.md'],
  },
  { title: 'Reactive updates', pages: ['guide/reactive/basics.md', 'guide/reactive/tuples.md', 'guide/reactive/debouncing.md'] },
  {
    title: 'Feature flags & entitlements',
    pages: [
      'guide/flags/concepts.md',
      'guide/flags/defining-flags.md',
      'guide/flags/defining-entitlements.md',
      'guide/flags/registration.md',
      'guide/flags/context-resolvers.md',
      'guide/flags/rest-endpoints.md',
      'guide/flags/expiry-health.md',
    ],
  },
  { title: 'Multi-tenancy', pages: ['guide/multi-tenancy/overview.md'] },
  {
    title: 'Secrets',
    pages: [
      'guide/secrets/overview.md',
      'guide/secrets/secret-type.md',
      'guide/secrets/encryption-setup.md',
      'guide/secrets/key-publishing.md',
      'guide/secrets/client-encryption.md',
      'guide/secrets/cli.md',
      'guide/secrets/certificate-caching.md',
      'guide/secrets/security-model.md',
    ],
  },
  {
    title: 'Health monitoring',
    pages: ['guide/health/overview.md', 'guide/health/aspnetcore.md', 'guide/health/logging.md', 'guide/health/performance.md'],
  },
  { title: 'Testing', pages: ['guide/testing/overrides.md', 'guide/testing/integration.md', 'guide/testing/strategy.md'] },
  {
    title: 'Analyzers',
    pages: ['guide/analyzers/overview.md', 'guide/analyzers/configuration.md', 'guide/analyzers/flags.md'],
  },
  { title: 'How-to', pages: ['guide/how-to/from-ioptions.md'] },
  {
    title: 'Migration',
    pages: ['guide/migration/v4-to-v5.md', 'guide/migration/v3-to-v4.md', 'guide/migration/v2-to-v3.md'],
  },
  {
    title: 'Reference',
    pages: [
      'reference/packages.md',
      'reference/health-api.md',
      'reference/cli-commands.md',
      'reference/analyzer-diagnostics.md',
      'reference/examples.md',
    ],
  },
]

// Pages that exist for readers deciding whether to use the library or what is coming next, not
// for an agent already using it.
const EXCLUDED = new Set(['guide/why-cocoar.md', 'guide/roadmap.md'])

// --- Collect the pages ---

const listed = new Set(SECTIONS.flatMap(s => s.pages))
const onDisk = [...walk(path.join(websiteDir, 'guide')), ...walk(path.join(websiteDir, 'reference'))]
  .map(f => path.relative(websiteDir, f).split(path.sep).join('/'))
  .filter(f => f.endsWith('.md'))

const unaccounted = onDisk.filter(f => !listed.has(f) && !EXCLUDED.has(f))
const missing = [...listed].filter(f => !onDisk.includes(f))
if (unaccounted.length || missing.length) {
  if (unaccounted.length) console.error(`sync-skill: pages not in SECTIONS or EXCLUDED: ${unaccounted.join(', ')}`)
  if (missing.length) console.error(`sync-skill: pages in SECTIONS that do not exist: ${missing.join(', ')}`)
  process.exit(1)
}

const pages = new Map()
for (const rel of listed) {
  // Line endings are normalized on the way in: the pages are edited on Windows and Linux alike,
  // and the generated files must not differ by that. Git normalizes them on the way out.
  const raw = normalizeNewlines(fs.readFileSync(path.join(websiteDir, rel), 'utf8'))
  const { description, body } = splitFrontmatter(raw, rel)
  const title = (body.match(/^# (.+)$/m) || [])[1]
  if (!title) fail(`${rel}: no H1 title`)
  pages.set(rel, { rel, title: stripVitePressInline(title).trim(), description, body })
}

// --- Generate ---

const output = new Map() // skill-relative path -> content

for (const page of pages.values()) {
  const target = `references/${page.rel}`
  output.set(target, renderReference(page, target))
}

output.set('SKILL.md', renderSkill())

// --- Write or check ---

if (check) {
  const problems = []
  for (const [rel, content] of output) {
    const file = path.join(skillDir, rel)
    if (!fs.existsSync(file)) problems.push(`missing: ${rel}`)
    else if (normalizeNewlines(fs.readFileSync(file, 'utf8')) !== content) problems.push(`outdated: ${rel}`)
  }
  for (const existing of walk(skillDir)) {
    const rel = path.relative(skillDir, existing).split(path.sep).join('/')
    if (!output.has(rel)) problems.push(`stale: ${rel}`)
  }
  if (problems.length) {
    console.error(`sync-skill: skills/${skillName} is out of date with the docs. Run \`node website/scripts/sync-skill.mjs\` and commit the result.`)
    for (const p of problems) console.error(`  ${p}`)
    process.exit(1)
  }
  console.log(`sync-skill: skills/${skillName} is up to date (${output.size} files)`)
} else {
  fs.rmSync(skillDir, { recursive: true, force: true })
  for (const [rel, content] of output) {
    const file = path.join(skillDir, rel)
    fs.mkdirSync(path.dirname(file), { recursive: true })
    fs.writeFileSync(file, content)
  }
  console.log(`sync-skill: wrote ${output.size} files to skills/${skillName}`)
}

// --- Rendering ---

function renderSkill() {
  if (!fs.existsSync(headerFile)) fail(`${headerFile} not found`)
  const header = normalizeNewlines(fs.readFileSync(headerFile, 'utf8')).replace(/\s+$/, '')

  const lines = [
    header,
    '',
    '<!-- Everything below is generated by website/scripts/sync-skill.mjs from the docs frontmatter. Edit the docs, not this file. -->',
    '',
    '## Reference documentation',
    '',
    'Each file under `references/` is one page of the documentation, copied verbatim. Read the one',
    'whose description matches the task; they are independent of each other.',
    '',
  ]

  for (const section of SECTIONS) {
    lines.push(`### ${section.title}`, '')
    for (const rel of section.pages) {
      const page = pages.get(rel)
      lines.push(`- [${page.title}](references/${rel}) — ${page.description}`)
    }
    lines.push('')
  }

  lines.push(`The same content is online at ${docsBaseUrl}/ (index for LLMs: ${docsBaseUrl}/llms.txt).`, '')
  return lines.join('\n')
}

function renderReference(page, target) {
  const banner =
    `<!-- Generated from website/${page.rel} by website/scripts/sync-skill.mjs. Do not edit; edit the docs page. -->\n\n`

  const body = rewriteVitePress(rewriteLinks(page.body, target))
  return banner + body.replace(/\s+$/, '') + '\n'
}

// `](/guide/x/y)`, `](/guide/x/y#frag)`, `](/reference/x.md)` → a relative link to the sibling
// reference file when the page is in the skill, the docs URL otherwise.
function rewriteLinks(body, target) {
  return body.replace(/\]\((\/[^)\s#]*)(#[^)\s]*)?\)/g, (match, sitePath, fragment = '') => {
    const rel = sitePath.replace(/^\//, '').replace(/\.(md|html)$/, '') + '.md'
    if (pages.has(rel)) {
      const from = path.posix.dirname(target)
      let relative = path.posix.relative(from, `references/${rel}`)
      if (!relative.startsWith('.')) relative = `./${relative}`
      return `](${relative}${fragment})`
    }
    return `](${docsBaseUrl}${toSiteUrlPath(sitePath)}${fragment})`
  })
}

// Directory links (`/adr/`) and real files (`/llms.txt`) keep their path; pages get `.html`.
function toSiteUrlPath(sitePath) {
  if (sitePath.endsWith('/')) return sitePath
  const withoutMd = sitePath.replace(/\.md$/, '')
  return /\.[a-z0-9]+$/i.test(withoutMd) ? withoutMd : `${withoutMd}.html`
}

// VitePress-only syntax, outside fenced code blocks. `::: code-group` only groups the code blocks
// that follow, whose info strings already carry their labels, so the container lines simply go.
// The admonitions become a block quote with the kind in bold, which reads the same without the
// plugin. `<Badge>` components and `{#custom-id}` heading anchors are dropped.
function rewriteVitePress(body) {
  const out = []
  let admonition = null
  let fence = null

  for (const line of body.split('\n')) {
    const fenceMatch = line.match(/^\s*(`{3,}|~{3,})/)
    if (fenceMatch) {
      if (!fence) fence = fenceMatch[1][0]
      else if (fenceMatch[1][0] === fence) fence = null
    }
    const inCode = fence !== null || fenceMatch !== null

    let text = line
    if (!inCode) {
      const open = line.match(/^:::\s*(code-group|info|tip|warning|danger|details)\b\s*(.*)$/)
      if (open) {
        const [, kind, title] = open
        admonition = kind
        if (kind !== 'code-group') {
          const label = kind.charAt(0).toUpperCase() + kind.slice(1)
          out.push(`> **${label}${title ? `: ${title.trim()}` : ''}**`, '>')
        }
        continue
      }
      if (line.trim() === ':::' && admonition) {
        admonition = null
        continue
      }
      if (line.startsWith(':::')) fail(`unsupported VitePress container: "${line}"`)
      text = stripVitePressInline(line)
      if (/^#{1,6} /.test(text)) text = text.replace(/\s*\{#[^}]+\}\s*$/, '')
    }

    if (admonition && admonition !== 'code-group') out.push(text.trim() === '' ? '>' : `> ${text}`)
    else out.push(text)
  }

  return out.join('\n')
}

function stripVitePressInline(text) {
  return text.replace(/\s*<Badge\b[^>]*\/>/g, '')
}

// --- Helpers ---

function splitFrontmatter(raw, rel) {
  const m = raw.match(/^---\n([\s\S]*?)\n---\n/)
  if (!m) fail(`${rel}: no frontmatter — every docs page needs a description`)
  const desc = m[1].match(/^description:\s*(.+)$/m)
  if (!desc) fail(`${rel}: frontmatter has no description`)
  let description = desc[1].trim()
  if ((description.startsWith('"') && description.endsWith('"')) || (description.startsWith("'") && description.endsWith("'"))) {
    description = description.slice(1, -1).replace(/\\"/g, '"').replace(/\\\\/g, '\\')
  }
  return { description, body: raw.slice(m[0].length).replace(/^\n+/, '') }
}

function normalizeNewlines(text) {
  return text.replace(/\r\n/g, '\n')
}

function walk(dir) {
  if (!fs.existsSync(dir)) return []
  return fs.readdirSync(dir, { withFileTypes: true }).flatMap(entry => {
    const full = path.join(dir, entry.name)
    return entry.isDirectory() ? walk(full) : [full]
  })
}

function fail(message) {
  console.error(`sync-skill: ${message}`)
  process.exit(1)
}
