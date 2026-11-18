import defaults from '../../lib/shortcuts.json';
import type { Translate, TranslationKey } from './locale';

export const shortcutActions = ['quit', 'toggle', 'volumeUp', 'volumeDown', 'pause', 'next'] as const;
export type ShortcutAction = typeof shortcutActions[number];
export type Shortcut = { modifiers: number; key: number };
export type Shortcuts = Record<ShortcutAction, Shortcut | null>;
export type ShortcutError = '' | 'shortcutDuplicate' | 'shortcutUnavailable';
export const defaultShortcuts: Shortcuts = defaults;
export const shortcutLabels: Record<ShortcutAction, TranslationKey> = {
  quit: 'quit', toggle: 'hideShortcut', volumeUp: 'volumeUp',
  volumeDown: 'volumeDown', pause: 'playPause', next: 'next',
};

const namedKeys: Record<string, number> = {
  Enter: 13, ' ': 32, PageUp: 33, PageDown: 34, End: 35, Home: 36,
  ArrowLeft: 37, ArrowUp: 38, ArrowRight: 39, ArrowDown: 40, Insert: 45, Delete: 46,
};
const keyLabels: Record<number, TranslationKey> = {
  13: 'keyEnter', 32: 'keySpace', 33: 'keyPageUp', 34: 'keyPageDown',
  35: 'keyEnd', 36: 'keyHome', 45: 'keyInsert', 46: 'keyDelete',
};
const arrows: Record<number, string> = { 37: '←', 38: '↑', 39: '→', 40: '↓' };
export function formatShortcut(binding: Shortcut | null, t: Translate) {
  if (!binding) return t('shortcutNone');
  const { modifiers, key } = binding;
  const keyLabel = keyLabels[key] ? t(keyLabels[key]) : arrows[key] ?? (key >= 112 ? 'F' + (key - 111) : String.fromCharCode(key));
  return [modifiers & 2 && 'Ctrl', modifiers & 1 && 'Alt', modifiers & 4 && t('keyShift'), keyLabel].filter(Boolean).join(' + ');
}

export function shortcutFromKey(event: KeyboardEvent): Shortcut | null {
  if (event.metaKey || (!event.ctrlKey && !event.altKey)) return null;
  let key = namedKeys[event.key];
  if (/^[a-z0-9]$/i.test(event.key)) key = event.key.toUpperCase().charCodeAt(0);
  else if (/^F([1-9]|1[0-9]|2[0-4])$/.test(event.key) && event.key !== 'F12') key = 111 + Number(event.key.slice(1));
  else if (!key && /^Digit[0-9]$/.test(event.code)) key = event.code.charCodeAt(5);
  if (!key) return null;
  return { modifiers: Number(event.altKey) | (Number(event.ctrlKey) << 1) | (Number(event.shiftKey) << 2), key };
}
