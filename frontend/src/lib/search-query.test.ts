import { isSearchId, parseSearchQuery } from '@/lib/search-query'

describe('parseSearchQuery', () => {
  it.each([
    ['Interstellar (2014)', 'Interstellar', '2014'],
    ['  Interstellar (2014)  ', 'Interstellar', '2014'],
    ['Interstellar(2014)', 'Interstellar', '2014'],
    ['Nosferatu (1922)', 'Nosferatu', '1922'],
    ['Metropolis (1900)', 'Metropolis', '1900'],
    ['Future (2099)', 'Future', '2099'],
    ['Blade Runner 2049 (2017)', 'Blade Runner 2049', '2017'],
    ['Movie (2014) (2015)', 'Movie (2014)', '2015'],
  ])('takes the bracketed year that ends “%s” as the year, and the rest as the title', (line, text, year) => {
    // Arrange
    const typed = line

    // Act
    const request = parseSearchQuery(typed)

    // Assert
    expect(request).toEqual({ text, year })
  })

  it.each([
    ['Blade Runner 2049'],
    ['Interstellar 2014'],
    ['Wonder Woman 1984'],
    ['1917'],
    ['2014'],
    ['(2014)'],
  ])('keeps a year that is not in brackets after a title as part of the title: “%s”', (line) => {
    // Arrange
    const typed = line

    // Act
    const request = parseSearchQuery(typed)

    // Assert
    expect(request).toEqual({ text: line })
  })

  it.each([
    ['Interstellar (2014'],
    ['Interstellar 2014)'],
    ['Interstellar (2014))'],
    ['Interstellar [2014]'],
    ['Interstellar (2014) Remastered'],
  ])('keeps a year in brackets that do not match, or that do not end the line, in the title: “%s”', (line) => {
    // Arrange
    const typed = line

    // Act
    const request = parseSearchQuery(typed)

    // Assert
    expect(request).toEqual({ text: line })
  })

  it.each([
    ['Metropolis (1899)'],
    ['Futurama (2100)'],
    ['Futurama (3000)'],
    ['Movie (201)'],
    ['Movie (20145)'],
    ['Movie (２０１４)'],
  ])('keeps a bracketed number outside 1900–2099, or not four ASCII digits, in the title: “%s”', (line) => {
    // Arrange
    const typed = line

    // Act
    const request = parseSearchQuery(typed)

    // Assert
    expect(request).toEqual({ text: line })
  })

  it('sends an id typed as the line as the title, as the header has no field for it', () => {
    // Arrange
    const typed = ' tt0816692 '

    // Act
    const request = parseSearchQuery(typed)

    // Assert
    expect(request).toEqual({ text: 'tt0816692' })
  })
})

describe('isSearchId', () => {
  it('takes a search id in any letter case', () => {
    // Arrange
    const ids = ['5ea4c000-0000-4000-8000-000000000001', '5EA4C000-0000-4000-8000-00000000000A']

    // Act
    const taken = ids.map(isSearchId)

    // Assert
    expect(taken).toEqual([true, true])
  })

  it.each([
    ['a way out to another API path', '../library/works'],
    ['the parent of the searches\' path', '..'],
    ['not an id at all', 'aliens'],
    ['an id with something after it', '5ea4c000-0000-4000-8000-000000000001/releases'],
    ['nothing', ''],
  ])('refuses %s', (_, value) => {
    // Arrange
    const id = value

    // Act
    const taken = isSearchId(id)

    // Assert
    expect(taken).toBe(false)
  })
})
