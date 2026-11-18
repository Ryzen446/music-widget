import { SearchField } from 'open-glass-ui';
import { useLayoutEffect, useRef, type ComponentProps } from 'react';
import { useStrings } from './locale';

export function LocalizedSearchField(props: ComponentProps<typeof SearchField>) {
  const t = useStrings();
  const input = useRef<HTMLInputElement>(null);
  const { value, label } = props;
  useLayoutEffect(() => {
    // Open Glass UI does not expose a label prop for its clear button.
    input.current?.closest('label')?.querySelector('button')?.setAttribute('aria-label', t('clearSearch'));
  }, [t, value, label]);
  return <SearchField {...props} ref={input} />;
}
