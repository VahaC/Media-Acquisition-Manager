/** The signed-in user, as every sign-in, setup and refresh answer describes them. */
export interface CurrentUser {
  id: string
  userName: string
  /** The installation owner manages users and settings; there is exactly one. */
  isOwner: boolean
}

/**
 * MAM-16 — the body of `GET /api/auth/me`: the signed-in user, with what the Profile page says about
 * the account and the session the request came through. Every instant is UTC with a trailing `Z`.
 */
export interface CurrentUserProfile extends CurrentUser {
  createdUtc: string
  /** The latest sign-in on any device, not necessarily this session's; null until the first one. */
  lastSignInUtc: string | null
  /** When this session ends unless it is used again; every refresh moves it 30 days on. */
  sessionExpiresUtc: string
}

/** MAM-16 — the body of `POST /api/auth/password`. */
export interface ChangePasswordRequest {
  /** The password the user signs in with now, which proves it is them. */
  currentPassword: string
  newPassword: string
}

/**
 * The body of a successful sign-in, first-run setup or refresh. The refresh token is not in it: it
 * travels only as the HttpOnly `mam_refresh` cookie, out of reach of any script on the page.
 */
export interface SignInResponse {
  accessToken: string
  /** UTC with a trailing `Z`. */
  accessTokenExpiresUtc: string
  user: CurrentUser
}

/** The body of `GET /api/setup`. */
export interface SetupStatus {
  /** True until the installation owner exists. */
  isRequired: boolean
}

/** The body of `POST /api/auth/sign-in`. */
export interface SignInRequest {
  userName: string
  password: string
}

/** The body of `POST /api/setup`: the owner account to create. */
export interface SetupRequest {
  userName: string
  password: string
}
