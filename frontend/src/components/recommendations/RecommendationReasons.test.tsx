import { render, screen, within } from '@testing-library/react'
import { I18nextProvider } from 'react-i18next'
import { RecommendationReasons } from '@/components/recommendations/RecommendationReasons'
import type { RecommendationReason } from '@/lib/types'
import { pluralFormProbe } from '@/test/plural-probe'
import { daysAgo, reasonOf } from '@/test/recommendations'

/** The chips on screen, as their text. */
const chips = () =>
  within(screen.getByRole('list', { name: 'Why it is recommended' })).getAllByRole('listitem').map((chip) => chip.textContent)

describe('RecommendationReasons', () => {
  it.each<[string, RecommendationReason, string]>([
    ['a collection with the parts held of the released ones', reasonOf('Collection', { name: 'Guardians of the Galaxy', held: 2, total: 3 }), 'Guardians of the Galaxy · 2 of 3'],
    ['a director with their films in the library', reasonOf('Director', { name: 'Ridley Scott', held: 12 }), 'Ridley Scott · 12 in your library'],
    ['a creator with their shows in the library', reasonOf('Creator', { name: 'Taylor Sheridan', held: 2 }), 'Taylor Sheridan · 2 in your library'],
    ['one recommending work', reasonOf('Recommended', { held: 1, titles: ['Alien'] }), 'Because you have Alien'],
    ['two recommending works', reasonOf('Recommended', { held: 2, titles: ['Alien', 'Prometheus'] }), 'Because you have Alien and Prometheus'],
    ['three recommending works', reasonOf('Recommended', { held: 3, titles: ['Alien', 'Prometheus', 'Aliens'] }), 'Because you have Alien, Prometheus and Aliens'],
    ['five recommending works', reasonOf('Recommended', { held: 5, titles: ['Alien', 'Prometheus', 'Aliens', 'Alien³', 'Alien Resurrection'] }), 'Because you have Alien, Prometheus and 3 more'],
    ['twelve recommending works, five of them named', reasonOf('Recommended', { held: 12, titles: ['Alien', 'Prometheus', 'Aliens', 'Alien³', 'Alien Resurrection'] }), 'Because you have Alien, Prometheus and 10 more'],
    ['two missing seasons in a row', reasonOf('MissingSeasons', { seasons: [4, 5] }), 'Missing seasons 4–5'],
    ['nine missing seasons in a row', reasonOf('MissingSeasons', { seasons: [3, 4, 5, 6, 7, 8, 9, 10, 11] }), 'Missing seasons 3–11'],
    ['one missing season', reasonOf('MissingSeasons', { seasons: [2] }), 'Missing season 2'],
    ['missing seasons apart', reasonOf('MissingSeasons', { seasons: [5, 7] }), 'Missing seasons 5, 7'],
    ['a top rated work', reasonOf('TopRated'), 'Top rated'],
    ['a work new at a source two days ago', reasonOf('NewAtSource', { name: 'Toloka.to', dateUtc: daysAgo(2) }), 'New on Toloka.to · 2 days ago'],
    ['a work new at another source today', reasonOf('NewAtSource', { name: 'Hurtom', dateUtc: daysAgo(0) }), 'New on Hurtom · today'],
    ['a work new at a source yesterday', reasonOf('NewAtSource', { name: 'Toloka.to', dateUtc: daysAgo(1) }), 'New on Toloka.to · yesterday'],
  ])('words %s', (_, reason, expected) => {
    // Arrange
    const reasons = [reason]

    // Act
    render(<RecommendationReasons reasons={reasons} />)

    // Assert
    expect(chips()).toEqual([expected])
  })

  it.each<[string, RecommendationReason, string]>([
    ['a collection without its counts', reasonOf('Collection', { name: 'Alien Collection' }), 'Alien Collection'],
    ['a collection without its name', reasonOf('Collection', { held: 1, total: 2 }), 'Collections'],
    ['a director without the count', reasonOf('Director', { name: 'Ridley Scott' }), 'Ridley Scott'],
    ['recommending works without their titles', reasonOf('Recommended', { held: 4 }), 'Because of 4 works in your library'],
    ['one title of two recommending works', reasonOf('Recommended', { held: 2, titles: ['Alien'] }), 'Because you have Alien and 1 more'],
    ['missing seasons without their numbers', reasonOf('MissingSeasons'), 'Missing seasons'],
    ['a new work without its date', reasonOf('NewAtSource', { name: 'Toloka.to' }), 'New on Toloka.to'],
    ['a new work without its source', reasonOf('NewAtSource', { dateUtc: daysAgo(2) }), 'New at a release source'],
  ])('still says something for %s', (_, reason, expected) => {
    // Arrange
    const reasons = [reason]

    // Act
    render(<RecommendationReasons reasons={reasons} />)

    // Assert
    expect(chips()).toEqual([expected])
  })

  // MAM-46 — Ukrainian words "and N more" by N's plural form, which English never shows: 21 takes the
  // singular, 3 another form than 5.
  it.each<[string, RecommendationReason, string]>([
    ['21 more after two titles', reasonOf('Recommended', { held: 23, titles: ['Alien', 'Prometheus', 'Aliens'] }), 'one: 21'],
    ['3 more after two titles', reasonOf('Recommended', { held: 5, titles: ['Alien', 'Prometheus', 'Aliens'] }), 'few: 3'],
    ['5 more after two titles', reasonOf('Recommended', { held: 7, titles: ['Alien', 'Prometheus', 'Aliens'] }), 'many: 5'],
    ['1 more after one title', reasonOf('Recommended', { held: 2, titles: ['Alien'] }), 'one: 1'],
    ['2 more after one title', reasonOf('Recommended', { held: 3, titles: ['Alien'] }), 'few: 2'],
    ['11 more after one title', reasonOf('Recommended', { held: 12, titles: ['Alien'] }), 'many: 11'],
  ])('picks the plural form of the titles left out by their count: %s', (_, reason, expected) => {
    // Arrange
    const key = reason.titles.length === 1 ? 'reasons.recommendedOneMore' : 'reasons.recommendedMore'
    const probe = pluralFormProbe('recommendations', key, (form) => `${form}: {{more}}`)

    // Act
    render(<I18nextProvider i18n={probe}><RecommendationReasons reasons={[reason]} /></I18nextProvider>)

    // Assert
    expect(chips()).toEqual([expected])
  })

  it('lists every reason in the server\'s order, each saying on hover what kind it is', () => {
    // Arrange
    const reasons = [
      reasonOf('MissingSeasons', { seasons: [4, 5] }),
      reasonOf('Creator', { name: 'Taylor Sheridan', held: 2 }),
      reasonOf('TopRated'),
    ]

    // Act
    render(<RecommendationReasons reasons={reasons} />)

    // Assert
    expect(chips()).toEqual(['Missing seasons 4–5', 'Taylor Sheridan · 2 in your library', 'Top rated'])
    expect(screen.getAllByRole('listitem').map((chip) => chip.getAttribute('title'))).toEqual([
      'Aired seasons of a series you have, with no episode in the library',
      'A creator whose shows you have',
      'Highly rated in the catalog',
    ])
  })

  it('shows nothing for a work without reasons', () => {
    // Arrange
    const reasons: RecommendationReason[] = []

    // Act
    const { container } = render(<RecommendationReasons reasons={reasons} />)

    // Assert
    expect(container).toBeEmptyDOMElement()
  })
})
