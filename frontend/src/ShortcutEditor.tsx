import { Alert, Button, IconButton } from 'open-glass-ui';
import { useEffect, useRef, useState, type KeyboardEvent } from 'react';
import { Icon } from './Icons';
import { useStrings } from './locale';
import { formatShortcut, shortcutActions, shortcutFromKey, shortcutLabels, type Shortcut, type ShortcutAction } from './shortcuts';
import type { Command, PlayerState } from './bridge';

export function ShortcutEditor({ state, send }: { state: PlayerState; send: (command: Command) => void }) {
  const t = useStrings();
  const [recording, setRecording] = useState<ShortcutAction | null>(null);
  const [invalid, setInvalid] = useState(false);
  const captureWasReady = useRef(false);
  const pending = useRef<{ action: ShortcutAction; code: string; binding: Shortcut } | null>(null);
  useEffect(() => {
    if (state.shortcutCapturing) captureWasReady.current = true;
    else if (captureWasReady.current) {
      captureWasReady.current = false;
      pending.current = null;
      setRecording(null);
    }
  }, [state.shortcutCapturing]);

  const cancel = () => {
    pending.current = null;
    setRecording(null);
    setInvalid(false);
    send({ command: 'shortcutCapture', value: false });
  };
  const choose = (action: ShortcutAction) => {
    if (recording === action) { cancel(); return; }
    pending.current = null;
    setInvalid(false);
    setRecording(action);
    send({ command: 'shortcutCapture', value: true });
  };
  const record = (event: KeyboardEvent<HTMLButtonElement>, action: ShortcutAction) => {
    if (recording !== action) return;
    if (event.key === 'Tab') return;
    event.preventDefault();
    event.stopPropagation();
    if (event.key === 'Escape') { cancel(); return; }
    if (!state.shortcutCapturing || event.repeat || ['Control', 'Alt', 'Shift', 'Meta', 'AltGraph'].includes(event.key)) return;
    const binding = shortcutFromKey(event.nativeEvent);
    if (!binding) { pending.current = null; setInvalid(true); return; }
    pending.current = { action, code: event.code || event.key, binding };
    setInvalid(false);
  };
  const finish = (event: KeyboardEvent<HTMLButtonElement>) => {
    const next = pending.current;
    if (!next || next.action !== recording || next.code !== (event.code || event.key)) return;
    event.preventDefault();
    pending.current = null;
    setRecording(null);
    send({ command: 'shortcutSet', name: next.action, value: next.binding });
  };

  return <section className="shortcuts-settings" aria-labelledby="shortcuts-heading">
    <h2 id="shortcuts-heading">{t('shortcuts')}</h2>
    <p className="setting-note">{t('shortcutsHint')}</p>
    <div className="shortcut-list">
      {shortcutActions.map(action => {
        const label = t(shortcutLabels[action]);
        const binding = state.shortcuts[action];
        const active = recording === action;
        const inactive = state.shortcutInactive.includes(action);
        return <div className="shortcut-row" key={action}>
          <span>{label}{inactive && <small>{t('shortcutInactive')}</small>}</span>
          <Button variant="secondary" size="small" className="shortcut-record" aria-label={t('editShortcut', { action: label })}
            aria-pressed={active} data-shortcut-capture={active || undefined}
            data-tooltip={active ? undefined : t('editShortcut', { action: label })}
            onClick={() => choose(action)} onKeyDown={event => record(event, action)} onKeyUp={finish} onBlur={() => { if (active) cancel(); }}>
            {active ? t(state.shortcutCapturing ? 'shortcutPress' : 'shortcutPreparing') : formatShortcut(binding, t)}
          </Button>
          <IconButton variant="secondary" size="small" aria-label={t('clearShortcut', { action: label })}
            data-tooltip={t('clearShortcut', { action: label })} disabled={!binding}
            onClick={() => send({ command: 'shortcutSet', name: action, value: null })}><Icon name="close" size={13} /></IconButton>
        </div>;
      })}
    </div>
    {(invalid || state.shortcutError) && <Alert tone="danger" title={t(invalid ? 'shortcutInvalid' : state.shortcutError || 'shortcutUnavailable')} />}
  </section>;
}
