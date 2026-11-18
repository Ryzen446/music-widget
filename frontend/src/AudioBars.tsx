import { useStrings } from './locale';
import { Glass } from 'open-glass-ui';
import { useEffect, useRef } from 'react';

type AudioFrame = { type: 'visualizer'; levels: number[] };
const count = 5;

export function AudioBars({ active }: { active: boolean }) {
  const t = useStrings();
  const bars = useRef<(HTMLElement | null)[]>([]);
  useEffect(() => {
    const paint = (levels: number[]) => bars.current.forEach((bar, index) => {
      if (bar) bar.style.transform = `scaleY(${Math.max(.13, Math.min(1, Number.isFinite(levels[index]) ? levels[index] : 0))})`;
    });
    paint([]);
    if (!active) return;
    const host = window.chrome?.webview;
    const frame = (event: MessageEvent) => {
      const message = event.data as AudioFrame;
      if (message?.type === 'visualizer' && Array.isArray(message.levels)) paint(message.levels);
    };
    host?.addEventListener('message', frame);
    return () => { host?.removeEventListener('message', frame); paint([]); };
  }, [active]);
  return <div className="audio-visualizer" data-active={active} role="img" aria-label={t('audioVisualizer')}>
    {Array.from({ length: count }, (_, index) => <Glass key={index} ref={element => { bars.current[index] = element; }} className="audio-glass-bar" material="clear" look={{ opacity: .8, rim: 1.4, highlight: 1.4, lensing: 1.3 }} />)}
  </div>;
}
