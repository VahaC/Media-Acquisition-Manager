/**
 * MAM-9 — `GET /api/settings/allowed-roots`: the folders inside the app where it may read library
 * files, in the order the owner entered them and in canonical form. None until the owner names one,
 * and then the app reads nothing.
 */
export interface AllowedRoots {
  paths: string[]
}
