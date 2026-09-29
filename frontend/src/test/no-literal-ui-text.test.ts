import { ESLint } from 'eslint'

// Pins the lint rule of ADR 0001 through the project's own eslint.config.js: a later config block
// that sets `no-restricted-syntax` for another reason replaces these selectors silently, and a
// selector typo matches nothing without any error. `npm test` runs from frontend/, where ESLint
// finds the config.

const eslint = new ESLint()

/** The source lines that hold a literal-UI-text finding, once per finding. */
async function literalTextFindings(code: string, filePath: string) {
  const [result] = await eslint.lintText(code, { filePath })
  return result.messages
    .filter((message) => message.ruleId === 'no-restricted-syntax')
    .map((message) => code.split('\n')[message.line - 1].trim())
}

/** `lines` as the markup of a component that receives every variable they use. */
function component(lines: string[]): string {
  return [
    "import { cn } from '@/lib/utils'",
    'export function Example({ a, b, ok, n, name, status }: {',
    '  a: boolean; b: boolean; ok: boolean; n: number; name?: string; status: string',
    '}) {',
    '  return (',
    '    <div>',
    ...lines.map((line) => `      ${line}`),
    '    </div>',
    '  )',
    '}',
  ].join('\n')
}

// ESLint loads the whole config — typescript-eslint and its parser included — on the first lint of
// the file: 2.9 s on the CI agent, and past Vitest's default 5 s once in a cold, fully parallel run.
describe('the literal UI text lint rule', { timeout: 30_000 }, () => {
  it('flags text written between tags in a component', async () => {
    // Arrange
    const code = [
      'export function Example() {',
      '  return <p>Hello</p>',
      '}',
    ].join('\n')

    // Act
    const findings = await literalTextFindings(code, 'src/pages/Example.tsx')

    // Assert
    expect(findings).toEqual(['return <p>Hello</p>'])
  })

  it('flags every attribute a user reads or hears', async () => {
    // Arrange
    const lines = [
      '<img alt="Logo" />',
      '<span title="Version" />',
      '<input placeholder={`Search ${name}`} />',
      "<button aria-label={'Close'} />",
      '<option label="Newest first" />',
      '<p aria-description="Updated every minute" />',
      '<p aria-placeholder="Type a title" />',
      '<p aria-roledescription="slide" />',
      '<p aria-valuetext="Half done" />',
      '<p title={ok ? `Saved ${n}` : undefined} />',
    ]

    // Act
    const findings = await literalTextFindings(component(lines), 'src/components/Example.tsx')

    // Assert
    expect(findings).toEqual(lines)
  })

  it('flags strings and templates placed in an element or a fragment', async () => {
    // Arrange
    const lines = [
      "<p>{'Saved'}</p>",
      '<p>{`Plain`}</p>',
      "<>{'In a fragment'}</>",
    ]

    // Act
    const findings = await literalTextFindings(component(lines), 'src/components/Example.tsx')

    // Assert
    expect(findings).toEqual(lines)
  })

  it('follows ternaries, logical operators and concatenation down to every branch', async () => {
    // Arrange
    const code = component([
      "<p>{ok ? 'Yes' : 'No'}</p>",
      "<p>{name ?? 'Unknown'}</p>",
      '<li>{name ?? `The ${n} check failed.`}</li>',
      "<p>{a ? 'One' : b ? 'Two' : 'Three'}</p>",
      "<p>{a && (b ? 'Busy' : 'Idle')}</p>",
      "<p>{n + ' files'}</p>",
    ])

    // Act
    const findings = await literalTextFindings(code, 'src/components/Example.tsx')

    // Assert
    expect(findings).toEqual([
      "<p>{ok ? 'Yes' : 'No'}</p>",
      "<p>{ok ? 'Yes' : 'No'}</p>",
      "<p>{name ?? 'Unknown'}</p>",
      // One finding per piece of text in the template: "The " and " check failed.".
      '<li>{name ?? `The ${n} check failed.`}</li>',
      '<li>{name ?? `The ${n} check failed.`}</li>',
      "<p>{a ? 'One' : b ? 'Two' : 'Three'}</p>",
      "<p>{a ? 'One' : b ? 'Two' : 'Three'}</p>",
      "<p>{a ? 'One' : b ? 'Two' : 'Three'}</p>",
      "<p>{a && (b ? 'Busy' : 'Idle')}</p>",
      "<p>{a && (b ? 'Busy' : 'Idle')}</p>",
      "<p>{n + ' files'}</p>",
    ])
  })

  it('allows translated text, whitespace, numbers and strings nobody reads', async () => {
    // Arrange
    const code = [
      "import { useTranslation } from 'react-i18next'",
      "import { cn } from '@/lib/utils'",
      '',
      'export function Example({ count, ok, status }: { count: number; ok: boolean; status: string }) {',
      "  const { t } = useTranslation('home')",
      '  return (',
      '    <p className="home-lead" aria-hidden="true" data-status="Healthy" role="alert">',
      "      {t('title')}{' '}{count}{`\\n`}{count + 1}{ok ? 0 : 1}",
      "      <span title={t('status.title')} className={ok ? 'active' : 'idle'}>{t('lead')}</span>",
      "      <b className={cn('home-muted', ok && 'is-ok')} />",
      "      {status === 'Healthy' && <b />}",
      '    </p>',
      '  )',
      '}',
    ].join('\n')

    // Act
    const findings = await literalTextFindings(code, 'src/pages/Example.tsx')

    // Assert
    expect(findings).toEqual([])
  })

  it.each([
    ['a colocated test', 'src/pages/Example.test.tsx'],
    ['a test helper', 'src/test/render-helpers.tsx'],
  ])('leaves the fixture markup of %s alone', async (_, filePath) => {
    // Arrange
    const code = [
      'export function Fixture() {',
      '  return <p>Page content</p>',
      '}',
    ].join('\n')

    // Act
    const findings = await literalTextFindings(code, filePath)

    // Assert
    expect(findings).toEqual([])
  })
})
