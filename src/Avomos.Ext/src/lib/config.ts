export const config = {
  API_BASE: 'http://localhost:5000',
  INTERCEPT_EVENT: 'avomos:network-data',

  CARD_SELECTORS: [
    'article', '[data-testid*="track"]', '[data-testid*="song"]', '[data-testid*="clip"]',
    'a[href*="/song/"]', 'a[href*="/track/"]', 'a[href*="/clip/"]',
  ],

  MAX_ATTEMPTS: { model: 60, style: 10, lyrics: 10 },

  PANEL_MIN_W: 300,
  PANEL_DEF_W: 520,
} as const;
