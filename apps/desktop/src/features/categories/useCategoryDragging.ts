/* SPDX-License-Identifier: GPL-3.0-only */

import { useEffect, useRef, useState, type DragEvent, type KeyboardEvent } from 'react';
import type { CategoryAction, EditorCategory } from './categoryLayout';
import { updateEditorCategories } from './categoryPreferences';

type Source = { kind: 'editor' | 'category'; id: string };
type Target = { key: string; edge: 'before' | 'after' | 'inside'; action: CategoryAction };

export function useCategoryDragging(groups: EditorCategory[], disabled: boolean,
  onExpand: (id: string) => void) {
  const [source, setSource] = useState<Source | null>(null);
  const [target, setTarget] = useState<Target | null>(null);
  const hover = useRef<{ id: string; timer: ReturnType<typeof setTimeout> } | null>(null);
  const cancelHover = () => { if (hover.current) clearTimeout(hover.current.timer); hover.current = null; };
  const clear = () => { setSource(null); setTarget(null); cancelHover(); };
  useEffect(() => {
    const cancel = (event: globalThis.KeyboardEvent) => { if (event.key === 'Escape') clear(); };
    window.addEventListener('keydown', cancel);
    window.addEventListener('blur', clear);
    return () => { window.removeEventListener('keydown', cancel); window.removeEventListener('blur', clear); cancelHover(); };
  }, []);
  useEffect(() => { if (disabled) clear(); }, [disabled]);

  function destination(event: DragEvent<HTMLElement>, category: EditorCategory, editorId?: string): Target | null {
    if (disabled || !source) return null;
    const bounds = event.currentTarget.getBoundingClientRect();
    const after = event.clientY > bounds.top + bounds.height / 2;
    if (source.kind === 'category') {
      if (editorId || category.id === source.id) return null;
      const beforeId = category.id === 'betaEditors' || !after ? category.id
        : groups[groups.indexOf(category) + 1]?.id ?? null;
      return { key: category.id, edge: category.id === 'betaEditors' || !after ? 'before' : 'after',
        action: { type: 'moveCategory', id: source.id, beforeId } };
    }
    if (category.id === 'betaEditors') return null;
    const beforeId = !editorId ? null : after
      ? category.sectionIds[(category.sectionIds as string[]).indexOf(editorId) + 1] ?? null : editorId;
    return { key: editorId ?? category.id, edge: !editorId ? 'inside' : after ? 'after' : 'before',
      action: { type: 'moveEditor', id: source.id, categoryId: category.id, beforeId } };
  }

  function keyboard(event: KeyboardEvent<HTMLElement>, category: EditorCategory, editorId?: string) {
    if (!event.altKey || disabled || category.id === 'betaEditors') return;
    const direction = event.key === 'ArrowUp' || event.key === 'ArrowLeft' ? -1 : 1;
    if (!['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight'].includes(event.key)) return;
    event.preventDefault();
    if (editorId && (event.key === 'ArrowLeft' || event.key === 'ArrowRight')) {
      const next = groups[groups.indexOf(category) + direction];
      if (next && next.id !== 'betaEditors' && updateEditorCategories({ type: 'moveEditor', id: editorId,
        categoryId: next.id, beforeId: null })) onExpand(next.id);
    } else if (event.key === 'ArrowUp' || event.key === 'ArrowDown') {
      const ids = editorId ? category.sectionIds : groups.filter(group => group.id !== 'betaEditors').map(group => group.id);
      const id = editorId ?? category.id;
      const index = (ids as string[]).indexOf(id);
      if (index + direction < 0 || index + direction >= ids.length) return;
      const beforeId = ids[index + (direction < 0 ? -1 : 2)] ?? null;
      updateEditorCategories(editorId ? { type: 'moveEditor', id, categoryId: category.id, beforeId }
        : { type: 'moveCategory', id, beforeId });
    }
    requestAnimationFrame(() => {
      const attribute = editorId ? 'data-category-editor' : 'data-category-id';
      document.querySelectorAll<HTMLElement>(`[${attribute}]`).forEach(element => {
        if (element.getAttribute(attribute) === (editorId ?? category.id)) element.focus();
      });
    });
  }

  function bindings(category: EditorCategory, editorId?: string) {
    const id = editorId ?? category.id;
    const movable = category.id !== 'betaEditors' && !disabled;
    return {
      draggable: movable,
      'data-movable': movable,
      'data-category-id': editorId ? undefined : id,
      'data-category-editor': editorId,
      onKeyDown: (event: KeyboardEvent<HTMLElement>) => keyboard(event, category, editorId),
      onDragStart: (event: DragEvent<HTMLElement>) => {
        if (!movable) { event.preventDefault(); return; }
        event.dataTransfer.effectAllowed = 'move';
        event.dataTransfer.setData('application/x-km-navigation', id);
        setSource({ kind: editorId ? 'editor' : 'category', id });
      },
      onDragOver: (event: DragEvent<HTMLElement>) => {
        const next = destination(event, category, editorId);
        if (!next) { setTarget(null); cancelHover(); return; }
        event.preventDefault();
        event.dataTransfer.dropEffect = 'move';
        setTarget(next);
        if (source?.kind === 'editor' && !editorId) {
          if (hover.current?.id !== category.id) {
            cancelHover();
            hover.current = { id: category.id, timer: setTimeout(() => onExpand(category.id), 600) };
          }
        } else cancelHover();
      },
      onDragLeave: () => { setTarget(null); cancelHover(); },
      onDrop: (event: DragEvent<HTMLElement>) => {
        const next = destination(event, category, editorId);
        if (next) {
          event.preventDefault();
          if (updateEditorCategories(next.action) && source?.kind === 'editor') onExpand(category.id);
        }
        clear();
      },
      onDragEnd: clear
    };
  }
  const className = (id: string) => `${source?.id === id ? ' category-dragging' : ''}${target?.key === id ? ` category-drop-${target.edge}` : ''}`;
  return { bindings, className };
}
