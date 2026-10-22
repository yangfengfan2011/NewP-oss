import { useRef, type ThHTMLAttributes } from 'react';

interface ResizableHeaderCellProps extends ThHTMLAttributes<HTMLTableCellElement> {
  resizeLabel?: string;
  resizeWidth?: number;
  onResizeWidth?: (width: number | undefined, commit: boolean) => void;
}

/** 只处理列宽手势；预览不写个人偏好，提交与持久化仍由列表宿主拥有。 */
export function ResizableHeaderCell({ children, resizeLabel, resizeWidth, onResizeWidth, ...props }: ResizableHeaderCellProps) {
  const drag = useRef<{ pointerId: number; x: number; width: number }>();
  return <th {...props}>{children}{resizeLabel && onResizeWidth && <span className="np-column-resize-handle"
    role="separator" aria-orientation="vertical" aria-label={resizeLabel} aria-valuenow={resizeWidth} aria-valuemin={1} tabIndex={0}
    onClick={event => { event.preventDefault(); event.stopPropagation(); }}
    onPointerDown={event => {
      if (event.button !== 0) return;
      event.preventDefault(); event.stopPropagation();
      const width = event.currentTarget.closest('th')?.getBoundingClientRect().width ?? resizeWidth ?? 1;
      drag.current = { pointerId: event.pointerId, x: event.clientX, width };
      event.currentTarget.setPointerCapture?.(event.pointerId);
    }}
    onPointerMove={event => {
      if (drag.current?.pointerId !== event.pointerId) return;
      event.preventDefault(); event.stopPropagation();
      onResizeWidth(Math.max(1, Math.round(drag.current.width + event.clientX - drag.current.x)), false);
    }}
    onPointerUp={event => {
      if (drag.current?.pointerId !== event.pointerId) return;
      event.preventDefault(); event.stopPropagation();
      const width = Math.max(1, Math.round(drag.current.width + event.clientX - drag.current.x));
      drag.current = undefined;
      event.currentTarget.releasePointerCapture?.(event.pointerId);
      onResizeWidth(width, true);
    }}
    onLostPointerCapture={() => { if (drag.current) { drag.current = undefined; onResizeWidth(undefined, false); } }}
    onPointerCancel={() => { drag.current = undefined; onResizeWidth(undefined, false); }}
    onKeyDown={event => {
      if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') return;
      event.preventDefault(); event.stopPropagation();
      const width = event.currentTarget.closest('th')?.getBoundingClientRect().width ?? resizeWidth ?? 1;
      onResizeWidth(Math.max(1, Math.round(width + (event.key === 'ArrowLeft' ? -10 : 10))), true);
    }}
  />}</th>;
}
