import type { CSSProperties } from 'react';
const paths = {
  play: 'M8 5v14l11-7Z', pause: 'M8 5v14M16 5v14', next: 'M5 5l11 7-11 7ZM19 5v14',
  prev: 'M19 5 8 12l11 7ZM5 5v14', volume: 'M3 9h4l5-4v14l-5-4H3ZM16 8c3 2 3 6 0 8M19 5c5 4 5 10 0 14',
  muted: 'M3 9h4l5-4v14l-5-4H3ZM17 9l5 6m0-6-5 6', more: 'M5 12h.01M12 12h.01M19 12h.01',
  close: 'm6 6 12 12M6 18 18 6', back: 'm14 6-6 6 6 6', chevron: 'm8 5 7 7-7 7',
  settings: 'M9 3h6l1 3 3 1 2 5-2 5-3 1-1 3H9l-1-3-3-1-2-5 2-5 3-1ZM9 12a3 3 0 1 0 6 0 3 3 0 1 0-6 0',
  library: 'M4 4v16M9 4v16M14 4v16m4-15 3 14', plus: 'M12 5v14M5 12h14',
  shuffle: 'M3 6h3l12 12h3m-4-4 4 4-4 4M3 18h3L18 6h3m-4-4 4 4-4 4',
  repeat: 'm17 2 4 4-4 4M3 11V8a2 2 0 0 1 2-2h16M7 22l-4-4 4-4m14-1v3a2 2 0 0 1-2 2H3',
  hide: 'M5 12h14', upload: 'M12 16V3m-5 5 5-5 5 5M4 16v5h16v-5',
  link: 'M10 13a4 4 0 0 0 6 0l4-4a4 4 0 0 0-6-6l-2 2M14 11a4 4 0 0 0-6 0l-4 4a4 4 0 0 0 6 6l2-2',
  check: 'm5 12 4 4L19 6', trash: 'M3 6h18M9 6V3h6v3M6 6l1 15h10l1-15M10 10v7m4-7v7',
  note: 'M9 18V5l11-2v13M9 9l11-2M9 18a3 2 0 1 1-6 0 3 2 0 1 1 6 0m11-2a3 2 0 1 1-6 0 3 2 0 1 1 6 0',
  search: 'M21 21l-5-5M18 10a8 8 0 1 1-16 0 8 8 0 0 1 16 0',
  download: 'M12 3v12m-5-5 5 5 5-5M4 17v4h16v-4',
  grip: 'M9 5h.01M15 5h.01M9 12h.01M15 12h.01M9 19h.01M15 19h.01',
  stop: 'M6 6h12v12H6Z',
};
export type IconName = keyof typeof paths;
export function Icon({ name, size = 16, style }: { name: IconName; size?: number; style?: CSSProperties }) {
  return <svg width={size} height={size} viewBox="0 0 24 24" aria-hidden="true" style={style} fill={name === 'play' ? 'currentColor' : 'none'} stroke="currentColor" strokeWidth={name === 'more' ? 3.6 : 1.7} strokeLinecap="round" strokeLinejoin="round"><path d={paths[name]} /></svg>;
}
