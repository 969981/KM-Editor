/* SPDX-License-Identifier: GPL-3.0-only */

import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { useLocalization } from '../../localization';
import type { EditorCategory } from './categoryLayout';
import type { CategoryDialogAction } from './CategoryDialogs';

export type CategoryMenuState = { category: EditorCategory; name: string; x: number; y: number; trigger: HTMLElement };

export function CategoryContextMenu({ menu, onClose, onDialog }: {
  menu: CategoryMenuState; onClose: () => void; onDialog: (action: CategoryDialogAction) => void;
}) {
  const { t } = useLocalization();
  const ref = useRef<HTMLDivElement>(null);
  const [position, setPosition] = useState({ left: menu.x, top: menu.y });
  useLayoutEffect(() => {
    const bounds = ref.current!.getBoundingClientRect();
    setPosition({ left: Math.max(8, Math.min(menu.x, innerWidth - bounds.width - 8)),
      top: Math.max(8, Math.min(menu.y, innerHeight - bounds.height - 8)) });
    ref.current?.querySelector('button')?.focus();
  }, [menu]);
  useEffect(() => {
    const dismiss = () => onClose();
    const outside = (event: PointerEvent) => { if (!ref.current?.contains(event.target as Node)) onClose(); };
    document.addEventListener('pointerdown', outside);
    window.addEventListener('resize', dismiss);
    window.addEventListener('blur', dismiss);
    window.addEventListener('scroll', dismiss, true);
    return () => {
      document.removeEventListener('pointerdown', outside);
      window.removeEventListener('resize', dismiss);
      window.removeEventListener('blur', dismiss);
      window.removeEventListener('scroll', dismiss, true);
    };
  }, [onClose]);
  return createPortal(<div aria-label={t('categories.menu')} className="category-context-menu" ref={ref}
    role="menu" style={position} onContextMenu={event => event.preventDefault()} onKeyDown={event => {
      if (event.key === 'Escape' || event.key === 'Tab') {
        event.preventDefault(); onClose(); menu.trigger.focus();
      }
      if (['ArrowUp', 'ArrowDown', 'Home', 'End'].includes(event.key)) {
        event.preventDefault();
        const items = Array.from(ref.current!.querySelectorAll('button'));
        const index = items.indexOf(document.activeElement as HTMLButtonElement);
        items[event.key === 'Home' ? 0 : event.key === 'End' ? items.length - 1
          : (index + (event.key === 'ArrowUp' ? -1 : 1) + items.length) % items.length]?.focus();
      }
    }}>
    {(['rename', ...(menu.category.custom ? ['delete'] as const : [])] as const).map(type =>
      <button key={type} role="menuitem" type="button" onClick={() => {
        menu.trigger.focus(); onClose(); onDialog({ type, id: menu.category.id, name: menu.name });
      }}>{t(`categories.${type}`)}</button>)}
  </div>, document.body);
}
