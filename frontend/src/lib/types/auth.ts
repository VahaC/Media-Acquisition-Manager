/** The signed-in user, as `GET /api/auth/me` and every sign-in answer describe them. */
export interface CurrentUser {
  id: string
  userName: string
  /** The installation owner manages users and settings; there is exactly one. */
  isOwner: boolean
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
