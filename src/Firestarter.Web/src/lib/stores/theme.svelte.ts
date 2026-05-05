import { api } from '../api';
import { CHROME_BY_THEME, type UiChrome } from '../theme/chrome';
import type { Theme } from '../theme/types';
import { THEMES } from '../theme/types';

export type { Theme } from '../theme/types';
export type { UiChrome } from '../theme/chrome';

const ALLOWED = THEMES;
const LS_KEY = 'firestarter.theme';

function apply(theme: Theme) {
  document.documentElement.setAttribute('data-theme', theme);
}

function readLocal(): Theme {
  const raw = typeof localStorage !== 'undefined' ? localStorage.getItem(LS_KEY) : null;
  return raw && (ALLOWED as readonly string[]).includes(raw) ? (raw as Theme) : 'ember';
}

const initial = readLocal();
let current = $state<Theme>(initial);
apply(initial);

export const theme = {
  get current() {
    return current;
  },

  /** Layout / chrome flags for the active theme — prefer this over comparing `current` to a theme id. */
  get chrome(): UiChrome {
    return CHROME_BY_THEME[current];
  },

  list: ALLOWED,

  async hydrate() {
    try {
      const res = await api.getUi();
      if ((ALLOWED as readonly string[]).includes(res.theme) && res.theme !== current) {
        current = res.theme as Theme;
        apply(current);
      }
    } catch {
      // backend unreachable — keep local choice
    }
  },

  async set(next: Theme) {
    if (!(ALLOWED as readonly string[]).includes(next)) return;
    current = next;
    apply(current);
    try {
      localStorage.setItem(LS_KEY, next);
    } catch {}
    try {
      await api.setUiTheme(next);
    } catch {
      // ignore — local state still updated
    }
  },
};
