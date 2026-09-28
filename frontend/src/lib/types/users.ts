/** One row of the Users settings page. */
export interface UserSummary {
  id: string
  userName: string
  isOwner: boolean
  isDisabled: boolean
  /** UTC with a trailing `Z`. */
  createdUtc: string
  /** UTC with a trailing `Z`; null until the first sign-in. */
  lastSignInUtc: string | null
}

/** One page of `GET /api/settings/users`. */
export interface UserList {
  items: UserSummary[]
  /** Every user matching the search, across all pages. */
  totalCount: number
  page: number
  pageSize: number
}

/**
 * The query params of `GET /api/settings/users`. Search, paging and ordering all happen on the
 * server; `search` is left out rather than sent empty.
 */
export interface UsersQuery {
  search?: string
  page: number
  pageSize: number
}

/** The body of `POST /api/settings/users`. */
export interface CreateUserRequest {
  userName: string
  password: string
}
