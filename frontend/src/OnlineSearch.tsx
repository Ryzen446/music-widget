import { useLayoutEffect, useRef, useState } from 'react';
import { Alert, Button, IconButton, Skeleton } from 'open-glass-ui';
import { GlassSurface } from './GlassSurface';
import { LocalizedSearchField } from './LocalizedSearchField';
import { Icon } from './Icons';
import { useStrings } from './locale';
import type { Command, PlayerState } from './bridge';

function ResultArtwork({ id }: { id: string }) {
  const [failed, setFailed] = useState(false);
  if (failed || !/^[A-Za-z0-9_-]{11}$/.test(id)) return <Icon name="note" size={21} />;
  return <img src={`https://i.ytimg.com/vi/${id}/default.jpg`} alt="" width={120} height={90}
    loading="lazy" decoding="async" referrerPolicy="no-referrer" draggable={false} onError={() => setFailed(true)} />;
}

function SearchAudio({ url, generation, volume, muted, send }: { url: string; generation: number; volume: number; muted: boolean; send: (command: Command) => void }) {
  const audio = useRef<HTMLAudioElement>(null);
  const report = (event: 'started' | 'ended' | 'error') => send({ command: 'previewMedia', value: event, index: generation });
  useLayoutEffect(() => {
    const player = audio.current;
    if (!player) return;
    player.volume = volume / 100;
    player.muted = muted;
  }, [muted, volume]);
  useLayoutEffect(() => {
    const player = audio.current;
    if (!player) return;
    let active = true;
    player.src = url;
    void player.play().catch(() => { if (active) send({ command: 'previewMedia', value: 'error', index: generation }); });
    return () => {
      active = false;
      player.pause();
      player.removeAttribute('src');
      player.load();
    };
  }, [generation, send, url]);
  return <audio ref={audio} preload="none" onPlaying={() => report('started')} onEnded={() => report('ended')} onError={() => { if (audio.current?.getAttribute('src')) report('error'); }} />;
}

export function OnlineSearch({ state, send, query, onQueryChange, onDownload }: { state: PlayerState; send: (command: Command) => void; query: string; onQueryChange: (query: string) => void; onDownload: (url: string, title: string) => void }) {
  const t = useStrings();
  const search = state.search;
  const preview = state.trackPreview;
  const previewing = preview.phase === 'loading' || preview.phase === 'playing';
  const run = (page = 1) => { if (query.trim().length >= 2) send({ command: 'search', value: query.trim(), index: page }); };
  return <div className="online-pane">
    {previewing && preview.url && preview.generation && <SearchAudio key={preview.generation} url={preview.url} generation={preview.generation} volume={state.volume} muted={state.isMuted} send={send} />}
    <form className="online-form" onSubmit={event => { event.preventDefault(); run(); }}>
      <LocalizedSearchField label={t('trackOrArtist')} value={query} onValueChange={onQueryChange} placeholder={t('searchPlaceholder')} maxLength={200} />
      <Button variant="secondary" type="submit" leadingIcon={<Icon name="search" size={16} />} disabled={(search.busy && query.trim() === search.query) || query.trim().length < 2}>{search.busy && query.trim() === search.query ? t('searching') : t('search')}</Button>
    </form>
    <div className="search-summary" role="status"><span>{search.busy ? t('searchProgress') : search.query ? t('searchResults', { query: search.query }) : t('searchYouTube')}</span>{previewing && <span>{preview.phase === 'loading' ? t('previewLoading') : state.isMuted || state.volume === 0 ? t('previewMuted') : t('previewPlaying')}</span>}</div>
    {search.error && <Alert tone="danger" title={t('searchUnavailable')}>{search.error}</Alert>}
    {preview.error && <Alert tone="danger" title={t('previewUnavailable')}>{preview.error}</Alert>}
    <div className="online-results" aria-busy={search.busy}>
      {search.busy ? Array.from({ length: 4 }, (_, index) => <Skeleton key={index} className="search-skeleton" />) : search.items.map(track => {
        const selected = previewing && preview.id === track.id;
        return <GlassSurface className="search-result" key={track.id}>
          <div className="result-artwork"><ResultArtwork id={track.id} /><IconButton variant="secondary" className="result-preview" aria-pressed={selected} data-tooltip={selected ? t('stopPreview') : t('playPreview')} aria-label={t(selected ? 'stopPreviewOf' : 'playPreviewOf', { title: track.title })} onClick={() => send({ command: 'previewTrack', value: track.id })}><Icon name={selected ? 'stop' : 'play'} size={16} /></IconButton></div>
          <div className="result-copy"><h2>{track.title}</h2><p>{track.artist || t('unknownArtist')}{track.durationLabel ? ` · ${track.durationLabel}` : ''}</p></div>
          <IconButton variant="secondary" aria-label={t('addTrackToLibrary', { title: track.title })} data-tooltip={t('addToLibrary')} disabled={state.download.busy} onClick={() => onDownload(track.url, track.title)}><Icon name="download" size={17} /></IconButton>
        </GlassSurface>;
      })}
      {!search.busy && !search.error && search.items.length === 0 && <div className="search-empty"><Icon name={search.query ? 'note' : 'search'} size={32} /><p>{search.query ? t('noSearchResults') : t('searchStart')}</p></div>}
    </div>
    {search.query && !search.error && <nav className="search-pagination" aria-label={t('resultPages')}><Button variant="secondary" size="small" leadingIcon={<Icon name="back" size={13} />} disabled={search.busy || search.page <= 1} onClick={() => send({ command: 'search', value: search.query, index: search.page - 1 })}>{t('previousPage')}</Button><span>{t('pageNumber', { page: search.page })}</span><Button variant="secondary" size="small" trailingIcon={<Icon name="chevron" size={13} />} disabled={search.busy || !search.hasNext || search.page >= 10} onClick={() => send({ command: 'search', value: search.query, index: search.page + 1 })}>{t('nextPage')}</Button></nav>}
  </div>;
}
