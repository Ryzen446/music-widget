import { Glass } from 'open-glass-ui';
import type { ReactNode } from 'react';

type SurfaceProps = {
  children: ReactNode; className?: string;
};
const look = { blur: 0.6, opacity: 0.65, rim: 1.3, highlight: 1.2, lensing: 1.45, shadow: 0.65 };
export function GlassSurface(props: SurfaceProps) {
  return <Glass className={props.className} material="clear" look={look} interactive>{props.children}</Glass>;
}
