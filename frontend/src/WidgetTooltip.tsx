import { useEffect, useId, useLayoutEffect, useRef, useState } from 'react';

function tooltipAnchor(target: EventTarget | null) {
  return target instanceof Element ? target.closest<HTMLElement>('[data-tooltip]') : null;
}

export function WidgetTooltip({ enabled, minimal }: { enabled: boolean; minimal: boolean }) {
  const id = useId();
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const bubble = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!enabled) { setAnchor(null); return; }
    let pending: HTMLElement | null = null;
    let timer = 0;
    const dismiss = () => {
      window.clearTimeout(timer);
      pending = null;
      setAnchor(null);
    };
    const show = (element: HTMLElement, delay: number) => {
      if (element === pending) return;
      dismiss();
      pending = element;
      timer = window.setTimeout(() => { if (element.isConnected) setAnchor(element); }, delay);
    };
    const enter = (event: PointerEvent) => {
      const element = tooltipAnchor(event.target);
      if (event.pointerType !== 'touch' && element) show(element, 370);
    };
    const leave = (event: PointerEvent) => {
      const element = tooltipAnchor(event.target);
      if (element === pending && element && !(event.relatedTarget instanceof Node && element.contains(event.relatedTarget))) dismiss();
    };
    const focus = (event: FocusEvent) => {
      const element = tooltipAnchor(event.target);
      if (element?.matches(':focus-visible')) show(element, 220);
    };
    const escape = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && pending) {
        if (bubble.current) event.preventDefault();
        dismiss();
      }
    };
    document.addEventListener('pointerover', enter);
    document.addEventListener('pointerout', leave);
    document.addEventListener('focusin', focus);
    document.addEventListener('focusout', dismiss);
    document.addEventListener('pointerdown', dismiss);
    document.addEventListener('keydown', escape);
    document.addEventListener('scroll', dismiss, true);
    window.addEventListener('blur', dismiss);
    return () => {
      window.clearTimeout(timer);
      document.removeEventListener('pointerover', enter);
      document.removeEventListener('pointerout', leave);
      document.removeEventListener('focusin', focus);
      document.removeEventListener('focusout', dismiss);
      document.removeEventListener('pointerdown', dismiss);
      document.removeEventListener('keydown', escape);
      document.removeEventListener('scroll', dismiss, true);
      window.removeEventListener('blur', dismiss);
    };
  }, [enabled]);

  useLayoutEffect(() => {
    const tip = bubble.current;
    if (!anchor || !tip || !enabled) return;
    const position = () => {
      if (!anchor.isConnected) { setAnchor(null); return; }
      const rect = anchor.getBoundingClientRect();
      const { offsetWidth: width, offsetHeight: height } = tip;
      const viewportWidth = document.documentElement.clientWidth;
      const viewportHeight = document.documentElement.clientHeight;
      const left = Math.max(6, Math.min(viewportWidth - width - 6, rect.left + (rect.width - width) / 2));
      const player = minimal ? anchor.closest('.player-mini') : null;
      const progress = player?.querySelector('.playback-progress')?.getBoundingClientRect();
      const top = player
        ? (progress?.top ?? player.getBoundingClientRect().bottom) - height - 1
        : rect.bottom + height + 12 <= viewportHeight ? rect.bottom + 6 : Math.max(6, rect.top - height - 6);
      tip.style.left = left + 'px';
      tip.style.top = top + 'px';
    };
    position();
    const describedBy = anchor.getAttribute('aria-describedby');
    anchor.setAttribute('aria-describedby', [describedBy, id].filter(Boolean).join(' '));
    window.addEventListener('resize', position);
    return () => {
      window.removeEventListener('resize', position);
      if (describedBy) anchor.setAttribute('aria-describedby', describedBy);
      else anchor.removeAttribute('aria-describedby');
    };
  }, [anchor, enabled, id, minimal]);

  if (!enabled || !anchor?.isConnected) return null;
  return <div id={id} className="widget-tooltip" data-player-tooltip={minimal || undefined} ref={bubble} role="tooltip">{anchor.dataset.tooltip}</div>;
}
