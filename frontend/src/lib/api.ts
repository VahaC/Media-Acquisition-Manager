import axios from 'axios'

/**
 * The one HTTP client every `<domain>-api.ts` module goes through. Same origin in every setup:
 * the API serves the SPA in production and Vite proxies `/api` in development.
 */
export const api = axios.create({ baseURL: '/' })
