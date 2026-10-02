import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ListPager } from '@/components/shared/ListPager'

const PAGE_SIZES = [10, 25, 50]

function renderPager(page: number, pages: number, totalCount?: number) {
  const onPage = vi.fn()
  const onPageSize = vi.fn()
  const { container } = render(
    <ListPager
      page={page}
      pages={pages}
      pageSize={25}
      pageSizes={PAGE_SIZES}
      totalCount={totalCount}
      onPage={onPage}
      onPageSize={onPageSize}
    />,
  )
  return { container, onPage, onPageSize, user: userEvent.setup() }
}

/** The names of the page-number buttons, in order, without the arrows. */
function pageButtonNames(container: HTMLElement) {
  return within(container)
    .getAllByRole('button', { name: /^Page \d+$/ })
    .map((button) => button.getAttribute('aria-label'))
}

describe('ListPager', () => {
  it('names the page-size list by its visible label', () => {
    // Arrange
    renderPager(1, 3)

    // Act
    const pageSize = screen.getByRole('combobox', { name: 'Per page' })

    // Assert
    expect(pageSize).toHaveValue('25')
    // A browser reads a label that wraps the select together with the select's own value, so its
    // name became "25"; jsdom leaves the value out, so what is pinned here is the label beside it.
    expect(pageSize.closest('label')).toBeNull()
    expect(screen.getByRole('navigation', { name: 'Pages' })).toHaveTextContent('Page 1 of 3')
  })

  it('shows the range on screen when the list total is given, and the page without it', () => {
    // Arrange
    const withTotal = renderPager(2, 33, 812).container

    // Act
    const withoutTotal = renderPager(2, 5).container

    // Assert
    expect(within(withTotal).getByText('26–50 of 812')).toBeInTheDocument()
    expect(within(withoutTotal).getByText('Page 2 of 5')).toBeInTheDocument()
  })

  it('ends the range at the total on the last page', () => {
    // Arrange
    renderPager(33, 33, 812)

    // Act
    const status = screen.getByText('801–812 of 812')

    // Assert
    expect(status).toBeInTheDocument()
  })

  it('says which page it is rather than a range when the page is past the end', () => {
    // Arrange
    renderPager(3, 2, 50)

    // Act
    const nav = screen.getByRole('navigation', { name: 'Pages' })

    // Assert
    expect(nav).toHaveTextContent('Page 3 of 2')
    expect(nav).not.toHaveTextContent('51–50 of 50')
    expect(pageButtonNames(nav)).toEqual(['Page 1', 'Page 2'])
    expect(within(nav).queryByRole('button', { current: 'page' })).not.toBeInTheDocument()
    expect(within(nav).getByRole('button', { name: 'Previous page' })).toBeEnabled()
  })

  it('renders nothing for a list with no items', () => {
    // Arrange
    const { container } = renderPager(1, 1, 0)

    // Act
    const nav = screen.queryByRole('navigation', { name: 'Pages' })

    // Assert
    expect(nav).not.toBeInTheDocument()
    expect(container).toBeEmptyDOMElement()
  })

  it('shows the first and last page and the current page ±1, with hidden gaps between', () => {
    // Arrange
    const { container } = renderPager(5, 33, 812)

    // Act
    const names = pageButtonNames(container)

    // Assert
    expect(names).toEqual(['Page 1', 'Page 4', 'Page 5', 'Page 6', 'Page 33'])
    const gaps = container.querySelectorAll('.list-pager-gap')
    expect(gaps).toHaveLength(2)
    gaps.forEach((gap) => {
      expect(gap).toHaveTextContent('…')
      expect(gap).toHaveAttribute('aria-hidden', 'true')
    })
  })

  it('marks the current page, which does nothing when clicked', async () => {
    // Arrange
    const { onPage, user } = renderPager(5, 33, 812)
    const current = screen.getByRole('button', { name: 'Page 5', current: 'page' })

    // Act
    await user.click(current)

    // Assert
    expect(current).toHaveTextContent('5')
    expect(current).toBeEnabled()
    expect(screen.getAllByRole('button', { current: 'page' })).toEqual([current])
    expect(onPage).not.toHaveBeenCalled()
  })

  it('hands a clicked page number to the list', async () => {
    // Arrange
    const { onPage, user } = renderPager(5, 33, 812)

    // Act
    await user.click(screen.getByRole('button', { name: 'Page 6' }))
    await user.click(screen.getByRole('button', { name: 'Page 33' }))

    // Assert
    expect(onPage.mock.calls).toEqual([[6], [33]])
    expect(screen.getByRole('button', { name: 'Page 5' })).toHaveAttribute('aria-current', 'page')
  })

  it('shows a single page as 1 with both arrows disabled', () => {
    // Arrange
    const { container } = renderPager(1, 1, 3)

    // Act
    const names = pageButtonNames(container)

    // Assert
    expect(names).toEqual(['Page 1'])
    expect(screen.getByRole('button', { name: 'Page 1', current: 'page' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Previous page' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Next page' })).toBeDisabled()
    expect(screen.getByText('1–3 of 3')).toBeInTheDocument()
  })

  it('names the arrows by label and title, and disables Previous on the first page and Next on the last', () => {
    // Arrange
    const pages = 3

    // Act
    const first = renderPager(1, pages).container
    const last = renderPager(pages, pages).container

    // Assert
    expect(within(first).getByRole('button', { name: 'Previous page' })).toBeDisabled()
    expect(within(first).getByRole('button', { name: 'Next page' })).toBeEnabled()
    expect(within(first).getByRole('button', { name: 'Next page' })).toHaveAttribute('title', 'Next page')
    expect(within(last).getByRole('button', { name: 'Previous page' })).toBeEnabled()
    expect(within(last).getByRole('button', { name: 'Previous page' })).toHaveAttribute('title', 'Previous page')
    expect(within(last).getByRole('button', { name: 'Next page' })).toBeDisabled()
    expect(last).toHaveTextContent('Page 3 of 3')
  })

  it('hands the page and the page size picked to the list, keeping neither itself', async () => {
    // Arrange
    const { onPage, onPageSize, user } = renderPager(2, 3)

    // Act
    await user.click(screen.getByRole('button', { name: 'Next page' }))
    await user.click(screen.getByRole('button', { name: 'Previous page' }))
    await user.selectOptions(screen.getByRole('combobox', { name: 'Per page' }), '50')

    // Assert
    expect(onPage.mock.calls).toEqual([[3], [1]])
    expect(onPageSize).toHaveBeenCalledExactlyOnceWith(50)
    expect(screen.getByText('Page 2 of 3')).toBeInTheDocument()
    expect(screen.getByRole('combobox', { name: 'Per page' })).toHaveValue('25')
  })
})
