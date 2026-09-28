import js from '@eslint/js'
import globals from 'globals'
import reactHooks from 'eslint-plugin-react-hooks'
import reactRefresh from 'eslint-plugin-react-refresh'
import tseslint from 'typescript-eslint'
import { defineConfig, globalIgnores } from 'eslint/config'

// ADR 0001 — every UI string comes from a locale resource. Text typed straight into JSX would ship
// in English under every other language, so lint refuses it in components. Covered: text between
// tags, the attributes a user reads or hears, and a string or template placed in the markup —
// directly, or through up to three levels of ternaries, &&, || and ?? and + concatenation. Only
// those pass-through nodes are followed: a string inside a call or a comparison
// ({cn('a', ok && 'b')}, {status === 'Healthy' && …}) is not text. Whitespace such as {' '} is not
// text either. Strings built outside the markup (an object of labels, a helper's return value)
// are out of this rule's reach.
const UI_TEXT_ATTRIBUTE =
  'JSXAttribute[name.name=/^(alt|title|placeholder|label|aria-label|aria-description|aria-placeholder|aria-roledescription|aria-valuetext)$/]'
const IN_MARKUP = `:matches(JSXElement, JSXFragment, ${UI_TEXT_ATTRIBUTE}) > JSXExpressionContainer`
const PASS_THROUGH = ':matches(ConditionalExpression, LogicalExpression, BinaryExpression[operator="+"])'
const LITERAL_UI_TEXT_MESSAGE =
  'UI text belongs in a locale resource (src/locales/en/*.json): render it with t() or <Trans>. See docs/adr/0001-every-ui-string-comes-from-a-locale-resource.md.'
const literalUiText = [
  'JSXText[value=/\\S/]',
  `${UI_TEXT_ATTRIBUTE} > Literal[value=/\\S/]`,
  // esquery has no "descendant through these node types only" combinator, so each depth is spelled
  // out; a bare descendant combinator would also reach strings inside calls and comparisons.
  ...[0, 1, 2, 3].flatMap((depth) => {
    const path = [IN_MARKUP, ...Array(depth).fill(PASS_THROUGH)].join(' > ')
    return [
      `${path} > Literal[value=/\\S/]`,
      // `cooked`, not `raw`: an escape such as \n is whitespace, like the same escape in a string.
      `${path} > TemplateLiteral > TemplateElement[value.cooked=/\\S/]`,
    ]
  }),
].map((selector) => ({ selector, message: LITERAL_UI_TEXT_MESSAGE }))

export default defineConfig([
  globalIgnores(['dist']),
  {
    files: ['**/*.{ts,tsx}'],
    extends: [
      js.configs.recommended,
      tseslint.configs.recommended,
      // Includes the React Compiler rules, so code the compiler would skip fails lint.
      reactHooks.configs.flat.recommended,
      reactRefresh.configs.vite,
    ],
    languageOptions: {
      globals: globals.browser,
    },
  },
  {
    files: ['src/**/*.tsx'],
    // Tests render their own fixture markup, and assert on the English copy on purpose.
    ignores: ['src/**/*.test.tsx', 'src/test/**'],
    rules: {
      'no-restricted-syntax': ['error', ...literalUiText],
    },
  },
])
