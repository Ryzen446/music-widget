import { useCallback, useEffect, useRef, useState } from 'react';
import { resolveLanguage, translate, type Language, type LanguagePreference } from './locale';
import { defaultShortcuts, type Shortcut, type ShortcutAction, type ShortcutError, type Shortcuts } from './shortcuts';

export type Pane = 'player' | 'menu' | 'volume' | 'settings' | 'library' | 'import' | 'createPlaylist' | 'playlistTracks' | 'delete' | 'search';
export type Track = { id: number; title: string; name: string };
export type OnlineTrack = { id: string; title: string; artist: string; durationLabel: string; url: string };
export type SearchState = { query: string; page: number; pageSize: number; hasNext: boolean; busy: boolean; error: string; items: OnlineTrack[] };
export type TrackMove = { from: string; to: string; after: boolean };
type Playlist = { name: string; count: number; tracks?: string[] };
export const minimalPlacementLabels = {
  TopLeft: 'positionTopLeft',
  TopCenter: 'positionTopCenter',
  TopRight: 'positionTopRight',
  BottomLeft: 'positionBottomLeft',
  BottomCenter: 'positionBottomCenter',
  BottomRight: 'positionBottomRight',
} as const;
export type PlayerState = {
  type: 'state'; title: string; subtitle: string; isPlaying: boolean; volume: number;
  language: Language; languagePreference: LanguagePreference;
  shortcuts: Shortcuts; shortcutCapturing: boolean; shortcutInactive: ShortcutAction[]; shortcutError: ShortcutError;
  isMuted: boolean; position: number; duration: number; positionLabel: string; durationLabel: string;
  shuffle: boolean; repeatMode: number; design: 'Minimalist' | 'Compact'; theme: 'Glass';
  sizeScale: number; auraColor: string; auraMode: 'Static' | 'RGB';
  placement: keyof typeof minimalPlacementLabels; topmost: boolean; reducedMotion: boolean;
  speed: number; sleepMinutes: number; pane: Pane; activePlaylist: string; currentIndex: number;
  library: Track[]; playlists: Playlist[];
  download: { busy: boolean; succeeded: boolean; status: string; error: string; received: number; total: number }; error?: string;
  presentationGeneration: number; interactive: boolean; playerInset: number; search: SearchState;
  trackPreview: { id: string; phase: 'idle' | 'loading' | 'playing' | 'error'; error: string; generation?: number; url?: string };
};
export type Command = { command: string; value?: unknown; index?: number; name?: string; url?: string };
type NativeWebView = {
  postMessage: (message: Command & { v: 1; id: number }) => void;
  addEventListener: (type: 'message', callback: (event: MessageEvent) => void) => void;
  removeEventListener: (type: 'message', callback: (event: MessageEvent) => void) => void;
};
declare global { interface Window { chrome?: { webview?: NativeWebView }; __musicCommands?: Command[] } }

const initialState: PlayerState = {
  type: 'state', title: translate('en', 'emptyTitle'), subtitle: translate('en', 'connecting'), isPlaying: false,
  language: 'en', languagePreference: 'auto', shortcuts: defaultShortcuts,
  shortcutCapturing: false, shortcutInactive: [], shortcutError: '',
  volume: 20, isMuted: false, position: 0, duration: 0, positionLabel: '0:00', durationLabel: '0:00',
  shuffle: false, repeatMode: 0, design: 'Minimalist', theme: 'Glass', placement: 'TopCenter',
  sizeScale: 1, auraColor: '#B9C7E8', auraMode: 'Static',
  topmost: true, reducedMotion: false, speed: 1, sleepMinutes: 0, pane: 'player', activePlaylist: 'ALL',
  currentIndex: -1, library: [], playlists: [], download: { busy: false, succeeded: false, status: '', error: '', received: 0, total: 0 },
  presentationGeneration: 0, interactive: false, playerInset: 0, search: { query: '', page: 1, pageSize: 8, hasNext: false, busy: false, error: '', items: [] },
  trackPreview: { id: '', phase: 'idle', error: '' },
};
const parameters = new URLSearchParams(location.search);
export const isPreview = parameters.has('preview');
const previewDeviceLanguage = parameters.get('locale') ?? 'fr';
const previewTracks = [
  { id: 0, title: 'Midnight Drive · Studio Sessions', name: 'Midnight Drive.wav' },
  { id: 1, title: 'Golden Hour · Slow Motion', name: 'Golden Hour.wav' },
  { id: 2, title: 'Ocean Eyes · Live Session', name: 'Ocean Eyes.wav' },
];
const previewPlaylists: Playlist[] = [
  { name: 'Favoris', count: 3, tracks: previewTracks.map(track => track.name) },
  { name: 'Calme', count: 1, tracks: [previewTracks[2].name] },
];
const format = (value: number) => `${Math.floor(value / 60)}:${String(Math.floor(value % 60)).padStart(2, '0')}`;

function selectPreviewPlaylist(state: PlayerState, name: string): PlayerState {
  const members = state.playlists.find(playlist => playlist.name === name)?.tracks ?? [];
  const library = name === 'ALL' ? previewTracks : members.flatMap(member => previewTracks.filter(track => track.name === member));
  const current = state.library.find(track => track.id === state.currentIndex)?.name;
  return { ...state, activePlaylist: name, currentIndex: library.findIndex(track => track.name === current), library: library.map((track, id) => ({ ...track, id })) };
}

function simulate(state: PlayerState, command: Command): PlayerState {
  switch (command.command) {
    case 'togglePlay': return { ...state, isPlaying: !state.isPlaying };
    case 'volume': return { ...state, volume: Number(command.value), isMuted: false };
    case 'mute': return state.volume > 0 ? { ...state, isMuted: !state.isMuted } : { ...state, volume: 20, isMuted: false };
    case 'seek':
    case 'seekRelative': {
      const target = Number(command.value) + (command.command === 'seekRelative' ? state.position : 0);
      const position = Math.max(0, Math.min(state.duration, target));
      return { ...state, position, positionLabel: format(position) };
    }
    case 'shuffle': return { ...state, shuffle: Boolean(command.value) };
    case 'repeat': return { ...state, repeatMode: Number(command.value) };
    case 'design': return { ...state, design: command.value as PlayerState['design'] };
    case 'theme': return { ...state, theme: command.value as PlayerState['theme'] };
    case 'resize': return { ...state, sizeScale: Number(command.value) };
    case 'auraColor': return { ...state, auraColor: String(command.value) };
    case 'auraMode': return { ...state, auraMode: command.value as PlayerState['auraMode'] };
    case 'placement': return { ...state, placement: command.value as PlayerState['placement'] };
    case 'topmost': return { ...state, topmost: Boolean(command.value) };
    case 'motion': return { ...state, reducedMotion: Boolean(command.value) };
    case 'speed': return { ...state, speed: Number(command.value) };
    case 'sleep': return { ...state, sleepMinutes: Number(command.value) };
    case 'pane': {
      const pane = command.value as Pane;
      const next = { ...state, pane, trackPreview: initialState.trackPreview, shortcutCapturing: false };
      if (pane === 'playlistTracks') return { ...next, library: previewTracks };
      return state.pane === 'playlistTracks' ? selectPreviewPlaylist(next, state.activePlaylist) : next;
    }
    case 'language': {
      const languagePreference = command.value as LanguagePreference;
      const language = resolveLanguage(languagePreference, previewDeviceLanguage);
      return { ...state, language, languagePreference, subtitle: translate(language, 'trackLabel', { index: state.currentIndex + 1, count: state.library.length }) };
    }
    case 'shortcutCapture': return { ...state, shortcutCapturing: Boolean(command.value), shortcutError: '' };
    case 'shortcutSet': {
      const action = command.name as ShortcutAction;
      const binding = command.value as Shortcut | null;
      const duplicate = binding && Object.entries(state.shortcuts).some(([id, other]) => id !== action && other?.key === binding.key && other.modifiers === binding.modifiers);
      return { ...state, shortcutCapturing: false,
        shortcutError: duplicate ? 'shortcutDuplicate' : '',
        shortcuts: duplicate ? state.shortcuts : { ...state.shortcuts, [action]: binding },
      };
    }
    case 'selectPlaylist': return selectPreviewPlaylist(state, command.name ?? 'ALL');
    case 'selectTrack': {
      const track = state.library.find(track => track.id === command.index);
      return track ? { ...state, title: track.title, currentIndex: track.id, position: 0, isPlaying: true } : state;
    }
    case 'next': case 'prev': {
      const index = (state.currentIndex + (command.command === 'next' ? 1 : state.library.length - 1)) % state.library.length;
      return simulate(state, { command: 'selectTrack', index });
    }
    case 'createPlaylist': {
      const name = command.name!;
      const existing = state.playlists.find(playlist => playlist.name.toLowerCase() === name.toLowerCase());
      const playlists = existing ? state.playlists : [...state.playlists, { name, count: 0, tracks: [] }];
      return selectPreviewPlaylist({ ...state, playlists, pane: 'library' }, existing?.name ?? name);
    }
    case 'deleteTrack': return { ...state, library: state.library.filter(track => track.id !== command.index) };
    case 'deletePlaylist': return { ...state, playlists: state.playlists.filter(playlist => playlist.name !== command.name), activePlaylist: 'ALL' };
    case 'addPlaylistTracks': {
      const selected = command.value as string[];
      const playlists = state.playlists.map(playlist => {
        if (playlist.name !== command.name) return playlist;
        const tracks = [...new Set([...(playlist.tracks ?? []), ...selected])];
        return { ...playlist, tracks, count: tracks.length };
      });
      return selectPreviewPlaylist({ ...state, playlists, pane: 'library' }, state.activePlaylist);
    }
    case 'addToPlaylist': case 'removeFromPlaylist': {
      const member = String(command.value);
      const playlists = state.playlists.map(playlist => {
        if (playlist.name !== command.name || !playlist.tracks) return playlist;
        const tracks = command.command === 'addToPlaylist'
          ? [...new Set([...playlist.tracks, member])]
          : playlist.tracks.filter(name => name !== member);
        return { ...playlist, tracks, count: tracks.length };
      });
      const next = { ...state, playlists };
      return state.activePlaylist === command.name ? selectPreviewPlaylist(next, state.activePlaylist) : next;
    }
    case 'previewTrack': return { ...state, trackPreview: state.trackPreview.id === command.value ? initialState.trackPreview : { id: String(command.value), phase: 'playing', error: '' } };
    case 'moveTrack': {
      const move = command.value as TrackMove;
      const picked = state.library.find(item => item.name === move.from);
      if (!picked || move.from === move.to) return state;
      const ordered = state.library.filter(item => item.name !== move.from);
      const destination = ordered.findIndex(item => item.name === move.to);
      if (destination < 0) return state;
      ordered.splice(destination + Number(move.after), 0, picked);
      const playlists = state.playlists.map(playlist => playlist.name === state.activePlaylist ? { ...playlist, tracks: ordered.map(item => item.name) } : playlist);
      return { ...state, playlists, currentIndex: ordered.findIndex(item => item.id === state.currentIndex), library: ordered.map((item, id) => ({ ...item, id })) };
    }
    case 'search': {
      const page = command.index ?? 1;
      return { ...state, trackPreview: initialState.trackPreview, search: { query: String(command.value), page, pageSize: 8, hasNext: page < 3, busy: false, error: '', items: Array.from({ length: 8 }, (_, index) => ({ id: `preview-${page}-${index}`, title: `${String(command.value)} · Session ${(page - 1) * 8 + index + 1}`, artist: translate(state.language, 'previewArtist'), durationLabel: '3:24', url: `https://www.youtube.com/watch?v=preview${page}${index}` })) } };
    }
    default: return state;
  }
}

export function usePlayerBridge() {
  const [state, setState] = useState<PlayerState>(() => isPreview ? {
    ...initialState, interactive: true, language: resolveLanguage('auto', previewDeviceLanguage), title: previewTracks[0].title,
    subtitle: translate(resolveLanguage('auto', previewDeviceLanguage), 'trackLabel', { index: 1, count: 3 }), currentIndex: 0,
    position: 42, duration: 203, positionLabel: '0:42', durationLabel: '3:23',
    library: previewTracks, playlists: previewPlaylists,
  } : initialState);
  const id = useRef(0);
  const presented = useRef(0);
  const send = useCallback((command: Command) => {
    if (isPreview) {
      window.__musicCommands ??= [];
      window.__musicCommands.push(command);
      setState(previous => simulate(previous, command));
    } else {
      window.chrome?.webview?.postMessage({ v: 1, id: ++id.current, ...command });
      if (command.command === 'pane' && command.value !== 'playlistTracks') setState(previous =>
        previous.pane === 'playlistTracks' ? previous : { ...previous, pane: command.value as Pane, trackPreview: initialState.trackPreview });
    }
  }, []);
  useEffect(() => {
    const host = window.chrome?.webview;
    if (!host) return;
    const onMessage = (event: MessageEvent) => {
      const message = event.data;
      if (message?.type === 'state') setState(previous => ({ ...previous, ...message,
        ...(Array.isArray(message.playlists) ? { playlists: message.playlists.filter((playlist: { name: string }) => playlist.name !== 'ALL') } : {}),
      }));
      else if (message?.type === 'ack' && !message.ok) setState(previous => ({ ...previous, error: message.error || translate(previous.language, 'actionUnavailable') }));
    };
    host.addEventListener('message', onMessage);
    send({ command: 'ready' });
    return () => host.removeEventListener('message', onMessage);
  }, [send]);
  useEffect(() => {
    const generation = state.presentationGeneration;
    if (isPreview || generation <= 0 || presented.current === generation) return;
    let second = 0;
    const first = requestAnimationFrame(() => {
      second = requestAnimationFrame(() => {
        presented.current = generation;
        send({ command: 'presentationReady', value: { target: 'main', generation } });
      });
    });
    return () => { cancelAnimationFrame(first); cancelAnimationFrame(second); };
  }, [state.presentationGeneration, send]);
  useEffect(() => {
    if (!isPreview || !state.isPlaying) return;
    const timer = setInterval(() => setState(previous => {
      const position = Math.min(previous.duration, previous.position + 0.25 * previous.speed);
      return { ...previous, position, positionLabel: format(position) };
    }), 250);
    return () => clearInterval(timer);
  }, [state.isPlaying, state.speed]);
  return { state, send };
}
