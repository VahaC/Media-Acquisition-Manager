import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ListPager } from '@/components/shared/ListPager'

const PAGE_SIZES = [10, 25, 50]

function renderPager(page: number, pages: number) {
  const onPage = vi.fn()
  const onPageSize = vi.fn()
  const { container } = render(
    <ListPager page={page} pages={pages} pageSize={25} pageSizes={PAGE_SIZES} onPage={onPage} onPageSize={onPageSize} />,
  )
  return { container, onPage, onPageSize, user: userEvent.setup() }
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

  it('disables Previous on the first page and Next on the last', () => {
    // Arrange
    const pages = 3

    // Act
    const first = renderPager(1, pages).container
    const last = renderPager(pages, pages).container

    // Assert
    expect(within(first).getByRole('button', { name: 'Previous' })).toBeDisabled()
    expect(within(first).getByRole('button', { name: 'Next' })).toBeEnabled()
    expect(within(last).getByRole('button', { name: 'Previous' })).toBeEnabled()
    expect(within(last).getByRole('button', { name: 'Next' })).toBeDisabled()
    expect(last).toHaveTextContent('Page 3 of 3')
  })

  it('hands the page and the page size picked to the list, keeping neither itself', async () => {
    // Arrange
    const { onPage, onPageSize, user } = renderPager(2, 3)

    // Act
    await user.click(screen.getByRole('button', { name: 'Next' }))
    await user.click(screen.getByRole('button', { name: 'Previous' }))
    await user.selectOptions(screen.getByRole('combobox', { name: 'Per page' }), '50')

    // Assert
    expect(onPage.mock.calls).toEqual([[3], [1]])
    expect(onPageSize).toHaveBeenCalledExactlyOnceWith(50)
    expect(screen.getByText('Page 2 of 3')).toBeInTheDocument()
    expect(screen.getByRole('combobox', { name: 'Per page' })).toHaveValue('25')
  })
})
