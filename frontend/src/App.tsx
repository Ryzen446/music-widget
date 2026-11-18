import { Alert, Button, Dialog, GlassSystemProvider, IconButton, Menu, MenuItem, Popover, Progress, SegmentedControl, Slider, Switch, Tabs, TextField } from 'open-glass-ui';
import { useEffect, useLayoutEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { Icon, type IconName } from './Icons';
import { GlassSurface } from './GlassSurface';
import { LocalizedSearchField } from './LocalizedSearchField';
import { AuraPicker } from './AuraPicker';
import { OnlineSearch } from './OnlineSearch';
import { AudioBars } from './AudioBars';
import { WidgetTooltip } from './WidgetTooltip';
import { LanguageProvider, useStrings } from './locale';
import { ShortcutEditor } from './ShortcutEditor';
import { isPreview, minimalPlacementLabels, usePlayerBridge, type Command, type Pane, type PlayerState, type TrackMove } from './bridge';

type Send = (command: Command) => void;
const iconButton = (label: string, icon: IconName, action: () => void, className = '', pressed?: boolean) =>
  <IconButton aria-label={label} data-tooltip={label} variant="secondary" className={className} onClick={action} aria-pressed={pressed}><Icon name={icon} /></IconButton>;

function useWindowShape(state: PlayerState, send: Send) {
  useEffect(() => {
    let frame = 0, previous = '';
    let observedMenu: Element | null = null;
    const zoom = state.design === 'Compact' && (state.pane === 'player' || state.pane === 'menu') ? state.sizeScale : 1;
    const rect = (element: Element) => {
      // The reveal uses a clip path, so these bounds remain stable while it runs.
      const bounds = element.getBoundingClientRect();
      const radius = parseFloat(getComputedStyle(element).borderTopLeftRadius) || 8;
      return Object.fromEntries(Object.entries({ x: bounds.x, y: bounds.y, width: bounds.width, height: bounds.height, radius }).map(([key, value]) => [key, Math.round(value * zoom * 100) / 100]));
    };
    const publish = () => {
      frame = 0; const main = document.querySelector('.player-shell, .pane'); if (!main) return;
      const content = document.querySelector('[role="menu"], .mini-volume-panel');
      const menu = content?.closest('.ogui-disclosure__surface') ?? content;
      if (menu !== observedMenu) {
        if (observedMenu) sizes.unobserve(observedMenu);
        observedMenu = menu;
        if (menu) sizes.observe(menu);
      }
      const menuBounds = menu ? rect(menu) : null;
      // The library positions portaled menus off-screen before measuring.
      // Wait for its final style update instead of clipping to that placeholder.
      if (menuBounds && (menuBounds.x < 0 || menuBounds.y < 0)) return;
      const value = { main: rect(main), menu: menuBounds };
      const json = JSON.stringify(value); if (json === previous) return; previous = json;
      send({ command: 'shape', value });
    };
    const schedule = () => { if (!frame) frame = requestAnimationFrame(publish); };
    const sizes = new ResizeObserver(schedule);
    const main = document.querySelector('.player-shell, .pane');
    if (main) sizes.observe(main);
    const observer = new MutationObserver(records => {
      // Audio and progress updates stay within the player's existing bounds.
      if (records.some(({ target }) => !(target instanceof Element) || !target.closest('.audio-visualizer, .playback-progress'))) schedule();
    });
    observer.observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['style', 'class'] });
    document.addEventListener('animationend', schedule);
    const onTransitionEnd = (event: TransitionEvent) => {
      if (!(event.target instanceof Element) || !event.target.closest('.playback-progress')) schedule();
    };
    document.addEventListener('transitionend', onTransitionEnd);
    window.addEventListener('resize', schedule); schedule();
    return () => { observer.disconnect(); sizes.disconnect(); document.removeEventListener('animationend', schedule); document.removeEventListener('transitionend', onTransitionEnd); window.removeEventListener('resize', schedule); cancelAnimationFrame(frame); };
  }, [state.pane, state.design, state.sizeScale, state.playerInset, send]);
}

function GlassChoice({ label, value, options, onChange, fullWidth = false }: {
  label: string; value: string | number; options: { value: string | number; label: string }[];
  onChange: (value: string | number) => void; fullWidth?: boolean;
}) {
  const field = useRef<HTMLDivElement>(null);
  useLayoutEffect(() => {
    const element = field.current;
    if (!fullWidth || !element) return;
    // OpenGlass puts the menu in a portal outside the field.
    const root = document.documentElement;
    const update = () => {
      const inset = window.innerWidth - element.getBoundingClientRect().width;
      root.style.setProperty('--position-menu-inset', `${inset}px`);
    };
    update();
    const observer = new ResizeObserver(update);
    observer.observe(element);
    return () => { observer.disconnect(); root.style.removeProperty('--position-menu-inset'); };
  }, [fullWidth]);
  const current = options.find(option => option.value === value)?.label ?? String(value);
  return <div className="choice-field" ref={fullWidth ? field : undefined}><span className="field-label">{label}</span>
    <Menu label={label} material="frosted" placement={fullWidth ? 'start' : 'end'} trigger={<Button variant="secondary" className="choice-trigger" trailingIcon={<Icon name="chevron" size={12} />}>{current}</Button>}>
      {options.map(option => <MenuItem key={option.value} className={fullWidth ? 'position-choice-option' : undefined} aria-current={option.value === value ? true : undefined} onClick={() => onChange(option.value)}><span>{option.label}</span>{option.value === value && <Icon name="check" size={14} />}</MenuItem>)}
    </Menu>
  </div>;
}

function PaneHeader({ title, subtitle, send, backTo = 'player' }: { title: string; subtitle?: string; send: Send; backTo?: Pane }) {
  const t = useStrings();
  return <header className="pane-header"><div className="pane-heading">{iconButton(backTo === 'library' ? t('backLibrary') : t('backPlayer'), 'back', () => send({ command: 'pane', value: backTo }), 'back-button')}<div><h1>{title}</h1>{subtitle && <p>{subtitle}</p>}</div></div>{iconButton(t('closePanel'), 'close', () => send({ command: 'pane', value: 'player' }))}</header>;
}

function MiniTrackTitle({ title, reducedMotion }: { title: string; reducedMotion: boolean }) {
  const viewport = useRef<HTMLDivElement>(null);
  const text = useRef<HTMLSpanElement>(null);
  useLayoutEffect(() => {
    const box = viewport.current;
    const label = text.current;
    if (!box || !label || reducedMotion) return;
    const measure = () => {
      const overflow = Math.ceil(label.scrollWidth - box.clientWidth);
      if (overflow <= 1 || box.clientWidth === 0) { box.removeAttribute('data-scroll-title'); return; }
      box.style.setProperty('--title-travel', `${-overflow}px`);
      box.style.setProperty('--title-duration', `${Math.min(36, 4 + overflow / 15)}s`);
      box.setAttribute('data-scroll-title', '');
    };
    measure();
    const observer = new ResizeObserver(measure);
    observer.observe(box);
    return () => { observer.disconnect(); box.removeAttribute('data-scroll-title'); };
  }, [title, reducedMotion]);
  return <div className="track-title mini-title" ref={viewport} aria-label={title} data-tooltip={title}><span ref={text} key={title}>{title}</span></div>;
}

function TrackTime({ state }: { state: PlayerState }) {
  const t = useStrings();
  return <span className="track-time" aria-label={t('timeDescription', { current: state.positionLabel, total: state.durationLabel })}>{state.positionLabel} <span>/ {state.durationLabel}</span></span>;
}

function MiniPlaybackProgress({ state, hidden, send }: { state: PlayerState; hidden: boolean; send: Send }) {
  const t = useStrings();
  const { duration, speed, title } = state;
  const position = Math.max(0, Math.min(state.position, duration));
  const fill = useRef<HTMLSpanElement>(null);
  const motion = useRef<{ title: string; duration: number; animation: Animation } | null>(null);
  const [systemReducedMotion, setSystemReducedMotion] = useState(() => matchMedia('(prefers-reduced-motion: reduce)').matches);
  useEffect(() => {
    const query = matchMedia('(prefers-reduced-motion: reduce)');
    const update = () => setSystemReducedMotion(query.matches);
    query.addEventListener('change', update);
    return () => query.removeEventListener('change', update);
  }, []);
  const animate = duration > 0 && state.isPlaying && !hidden && !state.reducedMotion && !systemReducedMotion;

  useLayoutEffect(() => {
    const current = motion.current;
    if (!animate || !fill.current) {
      current?.animation.cancel();
      motion.current = null;
      return;
    }
    // Follow the playback clock between host samples without a per-frame JS loop.
    const time = position * 1000;
    if (current?.title === title && current.duration === duration) {
      const { animation } = current;
      if (animation.playbackRate !== speed) animation.playbackRate = speed;
      if (Math.abs(Number(animation.currentTime) - time) > 300) {
        animation.currentTime = time;
        if (animation.playState === 'finished' && position < duration) animation.play();
      }
      return;
    }
    current?.animation.cancel();
    const animation = fill.current.animate([{ transform: 'scaleX(0)' }, { transform: 'scaleX(1)' }], {
      duration: duration * 1000, easing: 'linear', fill: 'both',
    });
    animation.playbackRate = speed;
    animation.currentTime = time;
    motion.current = { title, duration, animation };
  }, [animate, duration, position, speed, title]);
  useLayoutEffect(() => () => {
    motion.current?.animation.cancel();
    motion.current = null;
  }, []);

  return <div className="playback-progress">
    <div className="playback-progress-clip" aria-hidden="true">
      <span ref={fill} className="playback-progress-fill" style={{ transform: `scaleX(${duration > 0 ? position / duration : 0})` }} />
    </div>
    <Slider label={t('trackPosition')} className="small-slider" min={0} max={duration || 1} step="any"
      disabled={duration <= 0} value={position} valueText={state.positionLabel}
      aria-valuetext={t('timeDescription', { current: state.positionLabel, total: state.durationLabel })}
      onChange={event => {
        const value = Number(event.currentTarget.value);
        const animation = motion.current?.animation;
        if (animation) {
          animation.currentTime = value * 1000;
          if (value < duration && animation.playState !== 'running') animation.play();
        }
        send({ command: 'seek', value });
      }} />
  </div>;
}

function MuteButton({ state, send }: { state: PlayerState; send: Send }) {
  const t = useStrings();
  const silent = state.isMuted || state.volume === 0;
  return iconButton(silent ? t('unmute') : t('mute'), silent ? 'muted' : 'volume', () => send({ command: 'mute' }), 'mute-button', silent);
}

function Player({ state, hidden, send }: { state: PlayerState; hidden: boolean; send: Send }) {
  const t = useStrings();
  const choosingPane = useRef(false);
  const compact = state.design === 'Compact';
  const openPane = (pane: Pane) => { choosingPane.current = true; send({ command: 'pane', value: pane }); };
  const onMenuChange = (open: boolean) => {
    if (!open && choosingPane.current) { choosingPane.current = false; return; }
    send({ command: 'pane', value: open ? 'menu' : 'player' });
  };
  const options = <Menu label={t('options')} material="frosted" placement="end" open={state.pane === 'menu'} onOpenChange={onMenuChange}
    trigger={<IconButton aria-label={t('options')} data-tooltip={t('options')} variant="secondary" size="small" className="options-button"><Icon name="more" /></IconButton>}>
    <MenuItem onClick={() => openPane('settings')}><Icon name="settings" /><span>{t('appearance')}</span><Icon name="chevron" size={12} /></MenuItem>
    <MenuItem onClick={() => openPane('library')}><Icon name="library" /><span>{t('library')}</span><Icon name="chevron" size={12} /></MenuItem>
    <MenuItem onClick={() => { send({ command: 'next' }); }}><Icon name="next" /><span>{t('next')}</span></MenuItem>
    <MenuItem onClick={() => openPane('search')}><Icon name="plus" /><span>{t('addMusic')}</span></MenuItem>
    <MenuItem onClick={() => send({ command: 'hide' })}><Icon name="hide" /><span>{t('hideWidget')}</span></MenuItem>
  </Menu>;
  return <GlassSurface className={`player-shell ${compact ? 'player-compact' : 'player-mini'}`}>
    <div className={`track-row ${compact ? 'drag-handle' : ''}`} onPointerDown={event => { if (compact && event.button === 0 && !(event.target as HTMLElement).closest('button,input')) send({ command: 'drag' }); }}>
      {compact && <GlassSurface className="music-mark"><AudioBars active={state.isPlaying && !state.isMuted && state.volume > 0 && !state.reducedMotion} /></GlassSurface>}
      <div className="track-copy">
        {compact ? <div className="track-title" aria-label={state.title} data-tooltip={state.title}>{state.title}</div> : <MiniTrackTitle title={state.title} reducedMotion={state.reducedMotion} />}
        {compact && <div className="track-subtitle">{state.subtitle || t('localLibrary')}</div>}
      </div>
      {!compact && <>
        <MiniVolume state={state} send={send} />
        {iconButton(state.isPlaying ? t('pause') : t('play'), state.isPlaying ? 'pause' : 'play', () => send({ command: 'togglePlay' }), 'mini-play-button')}
        {iconButton(t('next'), 'next', () => send({ command: 'next' }), 'mini-next-button')}
      </>}
      {options}
    </div>
    {compact && <>
      <div className="track-meta"><TrackTime state={state} /></div>
      <Slider label={t('trackPosition')} className="seek-slider small-slider" min={0} max={Math.max(1, state.duration)} step={0.1} value={Math.min(state.position, state.duration)} valueText={state.positionLabel} onChange={event => send({ command: 'seek', value: Number(event.currentTarget.value) })} />
      <div className="transport">
        {iconButton(t('shuffle'), 'shuffle', () => send({ command: 'shuffle', value: !state.shuffle }), '', state.shuffle)}
        {iconButton(t('previous'), 'prev', () => send({ command: 'prev' }))}
        {iconButton(state.isPlaying ? t('pause') : t('play'), state.isPlaying ? 'pause' : 'play', () => send({ command: 'togglePlay' }), 'play-button')}
        {iconButton(t('next'), 'next', () => send({ command: 'next' }))}
        <div className="repeat-control">{iconButton([t('repeatList'), t('repeatTrack'), t('repeatOff')][state.repeatMode], 'repeat', () => send({ command: 'repeat', value: (state.repeatMode + 1) % 3 }), '', state.repeatMode !== 2)}{state.repeatMode === 1 && <span className="repeat-one">1</span>}</div>
      </div>
      <Volume state={state} send={send} />
      <div className="compact-footer"><Button variant="secondary" size="small" leadingIcon={<Icon name="library" size={13} />} onClick={() => send({ command: 'pane', value: 'library' })}>{t('library')}</Button><span>{state.speed}× {state.sleepMinutes > 0 ? '· ' + t('sleepRemaining', { minutes: state.sleepMinutes }) : ''}</span></div>
      <ResizeHandle state={state} send={send} />
    </>}
    {!compact && <MiniPlaybackProgress state={state} hidden={hidden} send={send} />}
  </GlassSurface>;
}

function MiniVolume({ state, send }: { state: PlayerState; send: Send }) {
  const t = useStrings();
  const silent = state.isMuted || state.volume === 0;
  return <Popover label={t('adjustVolume')} material="frosted" placement="center" open={state.pane === 'volume'}
    onOpenChange={open => send({ command: 'pane', value: open ? 'volume' : 'player' })}
    trigger={<IconButton variant="secondary" className="mini-volume-button" aria-label={t('adjustVolume')} aria-haspopup="dialog"><Icon name={silent ? 'muted' : 'volume'} /></IconButton>}>
    <div className="mini-volume-panel"><p>{t('volume')}</p><Volume state={state} send={send} focusSlider /></div>
  </Popover>;
}

function Volume({ state, send, focusSlider }: { state: PlayerState; send: Send; focusSlider?: boolean }) {
  const t = useStrings();
  return <div className="volume-control">
    <MuteButton state={state} send={send} />
    <Slider label={t('volume')} data-ogui-autofocus={focusSlider || undefined} className="volume-slider small-slider" min={0} max={100} step={1} value={state.volume} valueText={`${Math.round(state.volume)} %`} onChange={event => send({ command: 'volume', value: Number(event.currentTarget.value) })} />
    <output>{state.isMuted ? t('muted') : `${Math.round(state.volume)}%`}</output>
  </div>;
}

function ResizeHandle({ state, send }: { state: PlayerState; send: Send }) {
  const t = useStrings();
  const start = useRef<{ x: number; y: number; scale: number; dpi: number; sent: number } | null>(null);
  const resizeAt = (x: number, y: number, force = false) => {
    const point = start.current; if (!point) return;
    const scale = Math.max(.8, Math.min(1.5, point.scale + Math.max(x - point.x, y - point.y) / point.dpi / 400));
    if (force || performance.now() - point.sent > 45) { send({ command: 'resize', value: Math.round(scale * 100) / 100 }); point.sent = performance.now(); }
  };
  return <IconButton variant="secondary" className="resize-handle" aria-label={t('resize')} role="slider" aria-valuemin={80} aria-valuemax={150} aria-valuenow={Math.round(state.sizeScale * 100)} aria-valuetext={`${Math.round(state.sizeScale * 100)} %`}
    onPointerDown={event => { event.preventDefault(); event.stopPropagation(); event.currentTarget.setPointerCapture(event.pointerId); start.current = { x: event.screenX, y: event.screenY, scale: state.sizeScale, dpi: window.devicePixelRatio / state.sizeScale, sent: 0 }; }}
    onPointerMove={event => { if (start.current) resizeAt(event.screenX, event.screenY); }}
    onPointerUp={event => { resizeAt(event.screenX, event.screenY, true); start.current = null; event.currentTarget.releasePointerCapture(event.pointerId); }}
    onPointerCancel={() => { start.current = null; }}
    onKeyDown={event => { if (['ArrowLeft', 'ArrowDown', 'ArrowRight', 'ArrowUp'].includes(event.key)) { event.preventDefault(); event.stopPropagation(); send({ command: 'resize', value: Math.max(.8, Math.min(1.5, state.sizeScale + (['ArrowRight', 'ArrowUp'].includes(event.key) ? .05 : -.05))) }); } }}>
    <svg viewBox="0 0 16 16" width="13" height="13" aria-hidden="true"><path d="M5 12 12 5M9 12l3-3" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round" /></svg>
  </IconButton>;
}

function Settings({ state, send, auraColor, onAuraPreview }: { state: PlayerState; send: Send; auraColor: string; onAuraPreview: (color: string) => void }) {
  const t = useStrings();
  return <GlassSurface className="pane settings-pane">
    <PaneHeader title={t('settings')} send={send} />
    <div className="pane-scroll">
      <section><GlassChoice label={t('language')} value={state.languagePreference} options={[
        { value: 'auto', label: t('languageAuto') }, { value: 'fr', label: t('languageFr') }, { value: 'en', label: t('languageEn') },
      ]} onChange={value => send({ command: 'language', value })} /></section>
      <section><h2>{t('layout')}</h2><SegmentedControl aria-label={t('playerLayout')} items={[{ value: 'Minimalist', label: t('minimal') }, { value: 'Compact', label: t('compact') }]} value={state.design} onValueChange={value => send({ command: 'design', value })} /></section>
      {state.design === 'Compact' && <section><Slider label={t('playerSize')} min={80} max={150} step={5} value={Math.round(state.sizeScale * 100)} valueText={`${Math.round(state.sizeScale * 100)} %`} onChange={event => send({ command: 'resize', value: Number(event.currentTarget.value) / 100 })} /><p className="setting-note">{t('resizeHint')}</p></section>}
      <section className="aura-section"><div className="section-heading"><h2>{t('glassAura')}</h2><span className="aura-indicator" /></div>
        <SegmentedControl aria-label={t('auraMode')} items={[{ value: 'Static', label: t('fixedColor') }, { value: 'RGB', label: t('rgb') }]} value={state.auraMode} onValueChange={value => send({ command: 'auraMode', value })} />
        {state.auraMode === 'Static' ? <AuraPicker color={auraColor} onPreview={onAuraPreview} onCommit={value => send({ command: 'auraColor', value })} /> : <div className="rgb-preview"><span /><p>{t('rgbHint')}<small>{t('rgbDuration')}</small></p></div>}
      </section>
      {state.design === 'Minimalist' && <section><GlassChoice label={t('playerPosition')} value={state.placement} options={Object.entries(minimalPlacementLabels).map(([value, key]) => ({ value, label: t(key) }))} onChange={value => send({ command: 'placement', value })} fullWidth /></section>}
      <section className="setting-pair"><GlassChoice label={t('speed')} value={state.speed} options={[0.75, 1, 1.25, 1.5, 2].map(value => ({ value, label: `${value}×` }))} onChange={value => send({ command: 'speed', value })} /><GlassChoice label={t('sleep')} value={state.sleepMinutes} options={[{ value: 0, label: t('off') }, ...[15, 30, 60].map(value => ({ value, label: `${value} min` }))]} onChange={value => send({ command: 'sleep', value })} /></section>
      <section className="switch-settings">
        <Switch label={t('alwaysOnTop')} description={t('alwaysOnTopHint')} checked={state.topmost} onCheckedChange={value => send({ command: 'topmost', value })} />
        <Switch label={t('reducedMotion')} description={t('reducedMotionHint')} checked={state.reducedMotion} onCheckedChange={value => send({ command: 'motion', value })} />
        <Switch label={t('shuffle')} checked={state.shuffle} onCheckedChange={value => send({ command: 'shuffle', value })} />
      </section>
      <section><GlassChoice label={t('repeat')} value={state.repeatMode} options={[{ value: 0, label: t('repeatList') }, { value: 1, label: t('repeatTrack') }, { value: 2, label: t('off') }]} onChange={value => send({ command: 'repeat', value })} /></section>
      <ShortcutEditor state={state} send={send} />
    </div>
  </GlassSurface>;
}

function Library({ state, send }: { state: PlayerState; send: Send }) {
  const t = useStrings();
  const [query, setQuery] = useState('');
  const [deleting, setDeleting] = useState<number | null>(null);
  const [deletingPlaylist, setDeletingPlaylist] = useState(false);
  const dragging = useRef<{ name: string; playlist: string } | null>(null);
  const [dropTarget, setDropTarget] = useState<{ name: string; after: boolean } | null>(null);
  const tracks = state.library.filter(track => track.title.toLocaleLowerCase().includes(query.toLocaleLowerCase()));
  const selected = state.library.find(track => track.id === deleting);
  const canReorder = state.activePlaylist !== 'ALL' && !query.trim() && tracks.length > 1;
  const move = (value: TrackMove) => send({ command: 'moveTrack', name: state.activePlaylist, value });
  const endDrag = () => { dragging.current = null; setDropTarget(null); };
  const selectPlaylist = (name: string) => { endDrag(); send({ command: 'selectPlaylist', name }); };
  return <GlassSurface className="pane library-pane">
    <PaneHeader title={t('library')} subtitle={t(state.library.length === 1 ? 'trackOne' : 'trackOther', { count: state.library.length })} send={send} />
    <div className="library-actions"><Button variant="secondary" size="small" leadingIcon={<Icon name="plus" size={14} />} onClick={() => send({ command: 'pane', value: 'search' })}>{t('addMusic')}</Button><Button variant="secondary" size="small" onClick={() => send({ command: 'pane', value: 'createPlaylist' })}>{t('newPlaylist')}</Button>{state.activePlaylist !== 'ALL' && iconButton(t('playlistDeleteLabel', { name: state.activePlaylist }), 'trash', () => setDeletingPlaylist(true), 'delete-playlist')}</div>
    <div className="playlist-tabs"><SegmentedControl aria-label={t('activePlaylist')} value={state.activePlaylist} items={[{ value: 'ALL', label: t('all') }, ...state.playlists.map(playlist => ({ value: playlist.name, label: playlist.name }))]} onValueChange={selectPlaylist} /></div>
    <LocalizedSearchField label={t('findTrack')} value={query} onValueChange={value => { endDrag(); setQuery(value); }} placeholder={t('findInLibrary')} />
    <div className="track-list">
      {tracks.map((track, index) => <div className={`track-item ${track.id === state.currentIndex ? 'is-current' : ''}`} key={track.name}
        data-insert={dropTarget?.name === track.name ? (dropTarget.after ? 'after' : 'before') : undefined}
        onDragOver={event => {
          const source = dragging.current;
          if (!canReorder || source?.playlist !== state.activePlaylist || source.name === track.name) {
            if (source?.name === track.name) setDropTarget(null);
            return;
          }
          event.preventDefault();
          event.dataTransfer.dropEffect = 'move';
          const row = event.currentTarget.getBoundingClientRect();
          const after = event.clientY > row.top + row.height / 2;
          setDropTarget(current => current?.name === track.name && current.after === after ? current : { name: track.name, after });
        }}
        onDrop={event => {
          event.preventDefault();
          const source = dragging.current;
          if (canReorder && source?.playlist === state.activePlaylist && source.name !== track.name) {
            const row = event.currentTarget.getBoundingClientRect();
            move({ from: source.name, to: track.name, after: event.clientY > row.top + row.height / 2 });
          }
          endDrag();
        }}>
        {canReorder && <IconButton className="track-grip" aria-label={t('moveTrackLabel', { title: track.title })} data-tooltip={t('reorderHint')} variant="secondary" draggable
          onDragStart={event => {
            dragging.current = { name: track.name, playlist: state.activePlaylist };
            event.dataTransfer.effectAllowed = 'move';
            event.dataTransfer.setData('text/plain', track.title);
          }}
          onDragEnd={endDrag}
          onKeyDown={event => {
            if (event.key !== 'ArrowUp' && event.key !== 'ArrowDown') return;
            event.preventDefault();
            event.stopPropagation();
            const after = event.key === 'ArrowDown';
            const neighbor = tracks[index + (after ? 1 : -1)];
            if (neighbor) move({ from: track.name, to: neighbor.name, after });
          }}><Icon name="grip" size={14} /></IconButton>}
        <Button variant="secondary" className="track-select" onClick={() => send({ command: 'selectTrack', index: track.id })} leadingIcon={track.id === state.currentIndex ? <Icon name={state.isPlaying ? 'pause' : 'play'} size={13} /> : <span className="track-number">{index + 1}</span>}><span>{track.title}</span></Button>
        <Menu label={t('trackOptions', { title: track.title })} material="frosted" placement="end" trigger={<IconButton aria-label={t('trackOptions', { title: track.title })} variant="secondary" size="small"><Icon name="more" /></IconButton>}>
          {state.playlists.map(playlist => {
            if (!playlist.tracks) return null;
            const included = playlist.tracks.includes(track.name);
            const label = included ? t('removeFrom', { name: playlist.name }) : t('addTo', { name: playlist.name });
            return <MenuItem key={playlist.name} onClick={() => send({
              command: included ? 'removeFromPlaylist' : 'addToPlaylist', name: playlist.name, value: track.name,
            })}><Icon name={included ? 'hide' : 'plus'} size={13} /><span className="menu-label">{label}</span></MenuItem>;
          })}
          <MenuItem destructive onClick={() => setDeleting(track.id)}><Icon name="trash" size={13} />{t('deleteFile')}</MenuItem>
        </Menu>
      </div>)}
      {tracks.length === 0 && <div className="empty-library"><Icon name="note" size={30} />
        {state.activePlaylist !== 'ALL' && state.library.length === 0
          ? <Button variant="secondary" leadingIcon={<Icon name="plus" size={16} />} onClick={() => send({ command: 'pane', value: 'playlistTracks' })}>{t('addLibraryTracks')}</Button>
          : <p>{query ? t('noMatchingTrack') : t('firstTrack')}</p>}
      </div>}
    </div>
    {selected && <Dialog open onOpenChange={open => { if (!open) setDeleting(null); }} title={t('deleteTrackQuestion')} description={t('deleteTrackDescription')}>
      <p className="delete-track-name">{selected.title}</p><div className="dialog-actions"><Button variant="secondary" onClick={() => setDeleting(null)}>{t('cancel')}</Button><Button variant="secondary" className="danger-button" onClick={() => { send({ command: 'deleteTrack', index: selected.id }); setDeleting(null); }}>{t('delete')}</Button></div>
    </Dialog>}
    {deletingPlaylist && <Dialog open onOpenChange={setDeletingPlaylist} title={t('deletePlaylistQuestion')} description={t('deletePlaylistDescription')}>
      <p className="delete-track-name">{state.activePlaylist}</p><div className="dialog-actions"><Button variant="secondary" onClick={() => setDeletingPlaylist(false)}>{t('cancel')}</Button><Button variant="secondary" className="danger-button" onClick={() => { send({ command: 'deletePlaylist', name: state.activePlaylist }); setDeletingPlaylist(false); }}>{t('delete')}</Button></div>
    </Dialog>}
  </GlassSurface>;
}

function PlaylistTracks({ state, send }: { state: PlayerState; send: Send }) {
  const t = useStrings();
  const [query, setQuery] = useState('');
  const [selected, setSelected] = useState<Set<string>>(() => new Set());
  const members = state.playlists.find(playlist => playlist.name === state.activePlaylist)?.tracks;
  const included = useMemo(() => new Set(members), [members]);
  const search = query.trim().toLocaleLowerCase();
  const tracks = useMemo(() => state.library.filter(track => track.title.toLocaleLowerCase().includes(search)), [search, state.library]);
  const pending = useMemo(() => state.library.filter(track => selected.has(track.name) && !included.has(track.name)), [included, selected, state.library]);
  const toggle = (name: string) => setSelected(current => {
    const next = new Set(current);
    if (!next.delete(name)) next.add(name);
    return next;
  });
  return <GlassSurface className="pane library-pane playlist-picker">
    <PaneHeader title={t('addLibraryTracks')} subtitle={state.activePlaylist} send={send} backTo="library" />
    <LocalizedSearchField label={t('findTrack')} placeholder={t('findInLibrary')} value={query} onValueChange={setQuery} />
    <div className="track-list">
      {tracks.map(track => {
        const added = included.has(track.name);
        const picked = selected.has(track.name);
        return <GlassSurface className={`playlist-picker-row ${added || picked ? 'is-picked' : ''}`} key={track.name}>
          <Icon name="note" size={16} />
          <span className="playlist-picker-title">{track.title}</span>
          <IconButton className="playlist-picker-toggle" variant="secondary" aria-pressed={added || picked} disabled={added}
            aria-label={t(added ? 'trackAlreadyIncluded' : 'selectLibraryTrack', { title: track.title })}
            onClick={() => toggle(track.name)}><Icon name={added || picked ? 'check' : 'plus'} size={16} /></IconButton>
        </GlassSurface>;
      })}
      {!tracks.length && <div className="empty-library"><Icon name="note" size={30} />
        <p>{state.library.length ? t('noMatchingTrack') : t('firstTrack')}</p>
        {!state.library.length && <Button variant="secondary" leadingIcon={<Icon name="plus" size={16} />} onClick={() => send({ command: 'pane', value: 'search' })}>{t('addMusic')}</Button>}
      </div>}
    </div>
    <footer className="playlist-picker-footer">
      <span role="status">{t(pending.length === 1 ? 'selectedTrackOne' : 'selectedTrackOther', { count: pending.length })}</span>
      <Button variant="secondary" leadingIcon={<Icon name="plus" size={16} />} disabled={!pending.length}
        onClick={() => send({ command: 'addPlaylistTracks', name: state.activePlaylist, value: pending.map(track => track.name) })}>{t('confirmPlaylistTracks')}</Button>
    </footer>
  </GlassSurface>;
}

function DownloadFeedback({ download, language }: Pick<PlayerState, 'download' | 'language'>) {
  const t = useStrings();
  if (download.error) return <Alert tone="danger" title={t('addFailed')}>{download.error}</Alert>;
  if (!download.busy) return download.succeeded ? <p className="download-status" role="status">{download.status}</p> : null;
  const { received, total, status } = download;
  const formatBytes = (bytes: number) => new Intl.NumberFormat(language, {
    style: 'unit', unit: bytes < 1e6 ? 'kilobyte' : 'megabyte', maximumFractionDigits: 1,
  }).format(bytes / (bytes < 1e6 ? 1e3 : 1e6));
  return <GlassSurface className="download-feedback">
    {total > 0 ? <Progress label={status} value={Math.min(100, received / total * 100)} /> :
      <div className="ogui-progress"><span role="status">{status}</span><progress aria-label={status} /></div>}
    {(received > 0 || total > 0) && <p className="download-bytes">{total > 0
      ? t('downloadByteCount', { received: formatBytes(received), total: formatBytes(total) })
      : t('downloadReceived', { received: formatBytes(received) })}</p>}
  </GlassSurface>;
}

function AddMusic({ state, send }: { state: PlayerState; send: Send }) {
  const t = useStrings();
  const [query, setQuery] = useState(state.search.query);
  const [url, setUrl] = useState('');
  const tab = state.pane === 'import' ? 'import' : 'search';
  const [download, setDownload] = useState<{ url: string; title?: string } | null>(null);
  const [destination, setDestination] = useState('');
  const playlists = state.playlists.filter(playlist => playlist.name !== 'ALL');
  const selectedPlaylist = playlists.find(playlist => playlist.name === destination);
  const startDownload = (location: string, title?: string) => {
    if (state.download.busy) return;
    if (!playlists.length) { send({ command: 'download', url: location }); return; }
    setDestination('');
    setDownload({ url: location, title });
  };
  return <GlassSurface className="pane add-music-pane">
    <PaneHeader title={t('addMusic')} send={send} backTo="library" />
    <Tabs label={t('musicSources')} className="add-music-tabs" value={tab} onValueChange={value => send({ command: 'pane', value })} items={[
      { value: 'search', label: <><Icon name="search" size={15} />{t('search')}</>, content: <OnlineSearch state={state} send={send} query={query} onQueryChange={setQuery} onDownload={startDownload} /> },
      { value: 'import', label: <><Icon name="link" size={15} />{t('linkOrFile')}</>, content: <ImportMusic state={state} send={send} url={url} onUrlChange={setUrl} onDownload={startDownload} /> },
    ]} />
    <DownloadFeedback download={state.download} language={state.language} />
    {download && <Dialog open onOpenChange={open => { if (!open) setDownload(null); }} title={t('downloadDestination')} description={download.title || t('downloadDestinationDescription')}>
      <form onSubmit={event => {
        event.preventDefault();
        if (state.download.busy) return;
        send({ command: 'download', url: download.url, name: selectedPlaylist?.name });
        setDownload(null);
      }}>
        <div className="choice-field download-destination-field">
          <span className="field-label">{t('saveTo')}</span>
          <Menu label={t('saveTo')} material="frosted" trigger={
            <Button type="button" variant="secondary" className="choice-trigger" aria-describedby="download-destination-current"
              leadingIcon={<Icon name={selectedPlaylist ? 'note' : 'library'} />} trailingIcon={<Icon name="chevron" size={12} />}>
              <span id="download-destination-current">{selectedPlaylist?.name ?? t('all')}</span>
            </Button>
          }>
            {[{ value: '', label: t('all') }, ...playlists.map(({ name }) => ({ value: name, label: name }))].map(({ value, label }) => {
              const current = value === (selectedPlaylist?.name ?? '');
              return <MenuItem key={value} className="download-destination-option" aria-current={current || undefined}
                data-ogui-autofocus={current || undefined} onClick={() => setDestination(value)}>
                <Icon name={value ? 'note' : 'library'} /><span className="menu-label">{label}</span>{current && <Icon name="check" size={15} />}
              </MenuItem>;
            })}
          </Menu>
        </div>
        <div className="dialog-actions">
          <Button variant="secondary" type="button" onClick={() => setDownload(null)}>{t('cancel')}</Button>
          <Button variant="secondary" type="submit" leadingIcon={<Icon name="download" size={16} />} disabled={state.download.busy}>{t('downloadTrack')}</Button>
        </div>
      </form>
    </Dialog>}
  </GlassSurface>;
}

function ImportMusic({ state, send, url, onUrlChange, onDownload }: { state: PlayerState; send: Send; url: string; onUrlChange: (url: string) => void; onDownload: (url: string) => void }) {
  const t = useStrings();
  return <div className="import-pane">
    <Button className="import-local" variant="secondary" leadingIcon={<Icon name="upload" size={21} />} onClick={() => send({ command: 'importLocal' })}><span>{t('pickFiles')}<small>MP3, WAV, FLAC, M4A…</small></span></Button>
    <div className="separator-label"><span />{t('orFromLink')}<span /></div>
    <form noValidate onSubmit={event => { event.preventDefault(); if (url.trim()) onDownload(url.trim()); }}>
      <TextField label={t('youtubeSpotifyLink')} placeholder="https://…" value={url} onChange={event => onUrlChange(event.currentTarget.value)} type="url" required />
      <Button className="download-button" variant="secondary" type="submit" disabled={state.download.busy || !url.trim()} leadingIcon={<Icon name="link" />}>{state.download.busy ? t('adding') : t('addToLibrary')}</Button>
    </form>
  </div>;
}

function CreatePlaylist({ send }: { send: Send }) {
  const t = useStrings();
  const [name, setName] = useState('');
  return <GlassSurface className="pane"><PaneHeader title={t('newPlaylist')} send={send} backTo="library" />
    <form className="playlist-form" onSubmit={event => { event.preventDefault(); if (name.trim()) send({ command: 'createPlaylist', name: name.trim() }); }}>
      <TextField autoFocus label={t('namePlaylist')} placeholder={t('examplePlaylist')} value={name} maxLength={64} onChange={event => setName(event.currentTarget.value)} required />
      <div className="dialog-actions"><Button variant="secondary" onClick={() => send({ command: 'pane', value: 'library' })}>{t('cancel')}</Button><Button variant="secondary" type="submit" disabled={!name.trim() || name.toUpperCase() === 'ALL'}>{t('create')}</Button></div>
    </form>
  </GlassSurface>;
}

function GlassTheme({ state, color, hidden, children }: { state: PlayerState; color: string; hidden: boolean; children: ReactNode }) {
  const theme = useMemo(() => ({ appearance: 'dark', className: 'glass-system', theme: { preset: 'neutral', accent: '#dce4ef', radius: 'soft', glass: { tint: 'var(--music-aura)', blur: 0.8, opacity: 0.45, saturation: 1, rim: 1.2, highlight: 1.15, lensing: 1.2, lightAngle: 315, lightSpread: 90, shadow: 0.7 } } } as const), []);
  useLayoutEffect(() => {
    // Disclosure surfaces are portaled to body, outside the theme's DOM node.
    // Keep the tint at document scope so the actual library tokens inherit it.
    const root = document.documentElement;
    root.style.setProperty('--music-aura', color);
    root.classList.toggle('aura-rgb', state.auraMode === 'RGB');
  }, [state.auraMode, color]);
  useLayoutEffect(() => {
    const root = document.documentElement;
    root.classList.toggle('aura-paused', hidden || !state.interactive || state.reducedMotion);
  }, [hidden, state.interactive, state.reducedMotion]);
  return <GlassSystemProvider renderer="css" quality="low" motion={state.reducedMotion || hidden ? 'off' : 'system'} toasts={false} design="liquid"
    theme={theme}>{children}</GlassSystemProvider>;
}

export default function App() {
  const { state, send } = usePlayerBridge();
  const [hidden, setHidden] = useState(document.hidden);
  const [auraDraft, setAuraDraft] = useState<{ base: string; color: string } | null>(null);
  const auraColor = state.pane === 'settings' && auraDraft?.base === state.auraColor ? auraDraft.color : state.auraColor;
  const main = useRef<HTMLElement>(null);
  const playerOpen = ['player', 'menu', 'volume'].includes(state.pane);
  const surface = playerOpen ? 'player' : state.pane === 'search' || state.pane === 'import' ? 'addMusic' : state.pane;
  const playerInset = surface === 'player' ? state.playerInset : 0;
  const previousSurface = useRef(surface);
  useLayoutEffect(() => {
    const previous = previousSurface.current;
    previousSurface.current = surface;
    if (previous === surface || hidden || state.reducedMotion || matchMedia('(prefers-reduced-motion: reduce)').matches) return;
    const back = surface === 'player' || (surface === 'library' && ['createPlaylist', 'addMusic', 'playlistTracks'].includes(previous));
    const chrome = main.current?.firstElementChild;
    const animations = Array.from(chrome?.children ?? []).map(element => element.animate([
      { opacity: .48, translate: `${back ? -8 : 8}px 3px` },
      { opacity: 1, translate: '0 0' },
    ], { duration: 190, easing: 'cubic-bezier(.2,.75,.25,1)' }));
    return () => animations.forEach(animation => animation.cancel());
  }, [surface, hidden, state.reducedMotion]);
  useEffect(() => { if (state.pane !== 'settings') setAuraDraft(null); }, [state.pane]);
  useWindowShape(state, send);
  useEffect(() => {
    const onVisibility = () => setHidden(document.hidden);
    document.addEventListener('visibilitychange', onVisibility);
    return () => document.removeEventListener('visibilitychange', onVisibility);
  }, []);
  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.defaultPrevented) return;
      if (event.target instanceof Element && event.target.closest('input,textarea,select,[contenteditable],[role="slider"],[data-shortcut-capture]')) return;
      if (event.key === 'Escape' && !document.querySelector('[role="menu"], [role="dialog"]')) send({ command: 'pane', value: 'player' });
      else if (event.ctrlKey && event.key.toLowerCase() === 'l') { event.preventDefault(); send({ command: 'pane', value: 'library' }); }
      else if (event.key.toLowerCase() === 'm') { event.preventDefault(); send({ command: 'mute' }); }
      else if (event.key === ' ' && !(event.target as HTMLElement).closest('button')) { event.preventDefault(); send({ command: 'togglePlay' }); }
      else if (event.key === 'ArrowRight' || event.key === 'ArrowLeft') {
        event.preventDefault();
        send(event.ctrlKey ? { command: event.key === 'ArrowRight' ? 'next' : 'prev' } : { command: 'seekRelative', value: event.key === 'ArrowRight' ? 5 : -5 });
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [send]);
  let content: ReactNode;
  switch (state.pane) {
    case 'settings': content = <Settings state={state} send={send} auraColor={auraColor} onAuraPreview={color => setAuraDraft({ base: state.auraColor, color })} />; break;
    case 'library': content = <Library state={state} send={send} />; break;
    case 'playlistTracks': content = <PlaylistTracks key={state.activePlaylist} state={state} send={send} />; break;
    case 'search':
    case 'import': content = <AddMusic state={state} send={send} />; break;
    case 'createPlaylist': content = <CreatePlaylist send={send} />; break;
    default: content = <Player state={state} hidden={hidden} send={send} />;
  }
  return <LanguageProvider language={state.language}><GlassTheme state={state} color={auraColor} hidden={hidden}>
    <main ref={main} data-passive={hidden || !state.interactive || undefined} data-pane={state.pane} data-design={state.design} data-menu-above={playerInset > 0 || undefined} style={playerInset ? { paddingTop: playerInset } : undefined} className={isPreview ? 'preview' : ''} onContextMenu={event => { event.preventDefault(); if (state.pane === 'player') send({ command: 'pane', value: 'menu' }); }}>
      {content}
      {state.error && <GlassSurface className="native-error"><div role="alert">{state.error}</div></GlassSurface>}
    </main>
    <WidgetTooltip key={surface} enabled={!hidden && state.pane !== 'volume' && state.pane !== 'menu'} minimal={playerOpen && state.design === 'Minimalist'} />
  </GlassTheme></LanguageProvider>;
}
