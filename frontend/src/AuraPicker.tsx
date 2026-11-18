import { Button, Slider, TextField } from 'open-glass-ui';
import { useEffect, useState, type CSSProperties, type PointerEvent } from 'react';
import { useStrings } from './locale';

type HSV = { h: number; s: number; v: number };
const clamp = (n: number) => Math.min(1, Math.max(0, n));
function toHex({ h, s, v }: HSV) {
  const f = (n: number) => { const k = (n + h / 60) % 6; return Math.round(255 * (v - v * s * Math.max(0, Math.min(k, 4 - k, 1)))).toString(16).padStart(2, '0'); };
  return `#${f(5)}${f(3)}${f(1)}`.toUpperCase();
}
function toHSV(hex: string): HSV {
  const rgb = [1, 3, 5].map(i => parseInt(hex.slice(i, i + 2), 16) / 255);
  const [r, g, b] = rgb; const max = Math.max(...rgb), min = Math.min(...rgb), d = max - min;
  const h = d === 0 ? 0 : max === r ? ((g - b) / d + 6) % 6 : max === g ? (b - r) / d + 2 : (r - g) / d + 4;
  return { h: h * 60, s: max === 0 ? 0 : d / max, v: max };
}
export function AuraPicker({ color, onPreview, onCommit }: { color: string; onPreview: (color: string) => void; onCommit: (color: string) => void }) {
  const t = useStrings();
  const [hsv, setHSV] = useState(() => toHSV(color));
  const [hex, setHex] = useState(color);
  useEffect(() => { setHex(color); setHSV(previous => { const next = toHSV(color); return { ...next, h: next.s === 0 ? previous.h : next.h }; }); }, [color]);
  const update = (next: HSV, commit = false) => { const value = toHex(next); setHSV(next); setHex(value); onPreview(value); if (commit) onCommit(value); };
  const pick = (event: PointerEvent<HTMLDivElement>, commit = false) => {
    const bounds = event.currentTarget.getBoundingClientRect();
    update({ ...hsv, s: clamp((event.clientX - bounds.left) / bounds.width), v: 1 - clamp((event.clientY - bounds.top) / bounds.height) }, commit);
  };
  const commitHex = () => { const value = hex.startsWith('#') ? hex : `#${hex}`; if (/^#[\da-f]{6}$/i.test(value)) { onPreview(value.toUpperCase()); onCommit(value.toUpperCase()); } else setHex(color); };
  return <div className="aura-picker">
    <div className="color-spectrum" role="slider" tabIndex={0} aria-label={t('saturationBrightness')} aria-valuemin={0} aria-valuemax={100} aria-valuenow={Math.round(hsv.s * 100)} aria-valuetext={t('saturationValue', { saturation: Math.round(hsv.s * 100), brightness: Math.round(hsv.v * 100) })} style={{ backgroundColor: `hsl(${hsv.h} 100% 50%)` }}
      onPointerDown={event => { event.preventDefault(); event.currentTarget.setPointerCapture(event.pointerId); pick(event); }}
      onPointerMove={event => { if (event.currentTarget.hasPointerCapture(event.pointerId)) pick(event); }}
      onPointerUp={event => { pick(event, true); event.currentTarget.releasePointerCapture(event.pointerId); }}
      onKeyDown={event => { if (['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key)) { event.preventDefault(); event.stopPropagation(); update({ ...hsv, s: clamp(hsv.s + (event.key === 'ArrowRight' ? .02 : event.key === 'ArrowLeft' ? -.02 : 0)), v: clamp(hsv.v + (event.key === 'ArrowUp' ? .02 : event.key === 'ArrowDown' ? -.02 : 0)) }, true); } }}>
      <span className="spectrum-handle" style={{ left: `${hsv.s * 100}%`, top: `${(1 - hsv.v) * 100}%` }} />
    </div>
    <Slider label={t('hue')} className="hue-slider" min={0} max={359} step={1} value={hsv.h} valueText={`${Math.round(hsv.h)}°`} onChange={event => update({ ...hsv, h: Number(event.currentTarget.value) })} onPointerUp={() => onCommit(toHex(hsv))} onKeyUp={() => onCommit(toHex(hsv))} />
    <div className="color-details"><span className="color-preview" style={{ backgroundColor: color }} aria-hidden="true" /><TextField label={t('hexColor')} value={hex} maxLength={7} spellCheck={false} onChange={event => setHex(event.currentTarget.value)} onBlur={commitHex} onKeyDown={event => { if (event.key === 'Enter') commitHex(); }} /></div>
    <div className="color-presets" aria-label={t('auraColors')}>
      {['#B9C7E8', '#8AA9FF', '#C499F2', '#F0A3B9', '#F2C59D', '#8DD4C5'].map(value => <Button key={value} variant="secondary" aria-label={t('auraValue', { color: value })} aria-pressed={color === value} className="color-preset" style={{ '--swatch': value } as CSSProperties} onClick={() => { onPreview(value); onCommit(value); }}><span /></Button>)}
    </div>
  </div>;
}
