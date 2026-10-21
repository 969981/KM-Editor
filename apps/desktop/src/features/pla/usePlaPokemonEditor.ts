/* SPDX-License-Identifier: GPL-3.0-only */
import { useEffect, useRef, useState } from 'react';
import type { PlaKind, PlaPage, PlaRecord } from '../../bridge/plaProjectBridge';
import { usePublishCommonEditorError } from '../../components/CommonEditorDiagnostics';
import { useCoalescedTextInputState } from '../../components/useCoalescedTextInputState';
import { useLocalization } from '../../localization';
import type { PlaEditorProps } from './PlaEditorSection';
import { validPlaDraft } from './plaDraftValidation';

export const plaPokemonKinds = ['personal', 'learnsets', 'evolutions', 'compatibility', 'tutors'] as const;
export type PlaPokemonKind = typeof plaPokemonKinds[number];
type Pages = Partial<Record<PlaPokemonKind, PlaPage>>;

export function usePlaPokemonEditor(props: PlaEditorProps) {
  const { paths, session, bridge, drafts, setDrafts, beginLoad, cacheRevision, onDirtyChange, onStage } = props;
  const { t } = useLocalization();
  const [query, setQuery] = useCoalescedTextInputState();
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [tutorId, setTutorId] = useState<string | null>(null);
  const [pages, setPages] = useState<Pages>({});
  const [loadedScope, setLoadedScope] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [staging, setStaging] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [refresh, setRefresh] = useState(0);
  useEffect(() => {
    const target = props.navigationTarget;
    if (!target || target.section !== 'pokemon') return;
    if (target.kind === 'tutors') setTutorId(target.id);
    else { setSelectedId(target.id); setQuery(''); }
  }, [props.navigationTarget]);
  const epoch = useRef(0);
  const stageLock = useRef(false);
  const mounted = useRef(true);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; }; }, []);
  const scope = JSON.stringify(paths);
  const scopeRef = useRef(scope);
  scopeRef.current = scope;
  const draftKey = (kind: PlaKind, id: string) => `pokemon/${kind}/${paths.gameTextLanguage ?? 'English'}/${id}`;
  const dirty = Object.keys(drafts).some(key => key.startsWith('pokemon/'));
  useEffect(() => { onDirtyChange(dirty); }, [dirty, onDirtyChange]);
  const requestKey = JSON.stringify({ paths, session, refresh, cacheRevision });
  useEffect(() => {
    const run = ++epoch.current;
    const finish = beginLoad?.();
    let started = false;
    setLoading(true);
    const request = JSON.parse(requestKey) as { paths: typeof paths; session: typeof session };
    const timer = window.setTimeout(() => {
      started = true;
      void Promise.all(plaPokemonKinds.map(async kind => {
        const first = await bridge.loadPla({ paths: request.paths, session: request.session, kind });
        if (first.page.total > 4096) throw new Error(t('pla.failed'));
        const records = [...first.page.records];
        for (let offset = 100; offset < first.page.total; offset += 100) {
          if (epoch.current !== run) return first;
          const next = await bridge.loadPla({ paths: request.paths, session: request.session, kind, offset });
          if (next.page.revision !== first.page.revision) throw new Error(t('pla.failed'));
          records.push(...next.page.records);
        }
        return { page: { ...first.page, records } };
      }))
        .then(responses => {
          if (epoch.current !== run) return;
          const next = Object.fromEntries(responses.map(response => [response.page.kind, response.page])) as Pages;
          setPages(next);
          setLoadedScope(JSON.stringify(request.paths));
          setSelectedId(current => next.personal?.records.some(record => record.id === current) ? current : next.personal?.records[0]?.id ?? null);
          setTutorId(current => next.tutors?.records.some(record => record.id === current) ? current : next.tutors?.records[0]?.id ?? null);
        }).catch(reason => { if (epoch.current === run) { setPages({}); setError(String(reason)); } })
        .finally(() => { finish?.(); if (epoch.current === run) setLoading(false); });
    }, 180);
    return () => { clearTimeout(timer); if (!started) finish?.(); epoch.current++; };
  }, [bridge, requestKey, beginLoad]);

  const matchingRecords = (pages.personal?.records ?? []).filter(record => {
    const values = record.values;
    const typeLabels = ['type1', 'type2'].map(key => pages.personal?.fields.find(field => field.key === key)?.choices?.find(choice => choice.value === values[key])?.label ?? '');
    return [record.id, record.label, ...typeLabels].some(value => value.toLocaleLowerCase().includes(query.trim().toLocaleLowerCase()));
  });
  const effectiveSelectedId = matchingRecords.find(record => record.id === selectedId)?.id ?? matchingRecords[0]?.id ?? null;
  const recordFor = (kind: PlaPokemonKind) => pages[kind]?.records.find(record => record.id === (kind === 'tutors' ? tutorId : effectiveSelectedId)) ?? null;
  const valuesFor = (kind: PlaPokemonKind, record: PlaRecord) => ({ ...record.values, ...drafts[draftKey(kind, record.id)]?.values });
  const setValue = (kind: PlaPokemonKind, key: string, value: string) => {
    const record = recordFor(kind), page = pages[kind];
    if (!session || !record || !page || loadedScope !== scope) return;
    const id = draftKey(kind, record.id);
    setDrafts(current => {
      const values = { ...current[id]?.values, [key]: value };
      if (value === record.values[key]) delete values[key];
      const next = { ...current };
      if (!Object.keys(values).length) delete next[id];
      else next[id] = { revision: current[id]?.revision ?? page.revision, values, baseline: current[id]?.baseline ?? { ...record.values } };
      return next;
    });
    setError(null);
  };
  const currentKinds = plaPokemonKinds.filter(kind => kind !== 'tutors');
  const hasDraft = (kind: PlaPokemonKind, record: PlaRecord | null = recordFor(kind)) => Boolean(record && drafts[draftKey(kind, record.id)]);
  const changedCount = (kinds: readonly PlaPokemonKind[]) => kinds.reduce((sum, kind) => {
    const record = recordFor(kind);
    return sum + (record ? Object.keys(drafts[draftKey(kind, record.id)]?.values ?? {}).length : 0);
  }, 0);
  const invalidKinds = plaPokemonKinds.filter(kind => {
    const record = recordFor(kind), page = pages[kind];
    const draft = record && drafts[draftKey(kind, record.id)];
    return record && page && draft && !validPlaDraft(page, record, draft.values);
  });
  usePublishCommonEditorError({ domain: 'workflow.pokemon', field: 'pla', message: error ?? (invalidKinds.length ? t('pla.invalid') : null) });
  const stage = async (kinds: readonly PlaPokemonKind[], restoreBase = false) => {
    if (!session || loadedScope !== scope || loading || props.isBusy || stageLock.current) return;
    const submitted = kinds.flatMap(kind => {
      const record = recordFor(kind), page = pages[kind];
      if (!record || !page) return [];
      const key = draftKey(kind, record.id), draft = drafts[key];
      if (!restoreBase && !draft) return [];
      const values = restoreBase ? Object.fromEntries(page.fields.filter(field => !field.readOnly && !record.readOnlyFields.includes(field.key))
        .map(field => [field.key, record.values[field.key]])) : { ...draft.values };
      return [{ kind, record, page, key, draft, values }];
    });
    if (!submitted.length) return;
    if (!restoreBase && submitted.some(item => !validPlaDraft(item.page, item.record, item.values))) { setError(t('pla.invalid')); return; }
    stageLock.current = true; setStaging(true); setError(null);
    try {
      for (const item of submitted) {
        if (!mounted.current || scopeRef.current !== scope) return;
        const baselineIsCurrent = item.draft && Object.keys(item.values).every(key => item.draft.baseline[key] === item.record.values[key]);
        const accepted = await onStage({ kind: item.kind, id: item.record.id, values: item.values,
          revision: restoreBase || baselineIsCurrent ? item.page.revision : item.draft.revision, restoreBase });
        if (!mounted.current || scopeRef.current !== scope) return;
        if (!accepted) { setError(t('pla.failed')); return; }
        setDrafts(current => {
          if (current[item.key] !== item.draft) {
            const newer = current[item.key];
            return newer && !restoreBase ? { ...current, [item.key]: { ...newer, baseline: { ...newer.baseline, ...item.values } } } : current;
          }
          const next = { ...current }; delete next[item.key]; return next;
        });
      }
    } catch (reason) { if (mounted.current && scopeRef.current === scope) setError(String(reason)); }
    finally { stageLock.current = false; if (mounted.current) { setStaging(false); setRefresh(value => value + 1); } }
  };
  const discard = (kinds: readonly PlaPokemonKind[]) => setDrafts(current => {
    const next = { ...current };
    for (const kind of kinds) { const record = recordFor(kind); if (record) delete next[draftKey(kind, record.id)]; }
    return next;
  });
  const stageExperienceYield = async (restoreBase: boolean) => {
    if (!session || loadedScope !== scope || loading || props.isBusy || stageLock.current || dirty || !pages.personal) return;
    stageLock.current = true; setStaging(true); setError(null);
    try {
      const accepted = await onStage({ kind: 'personal', id: '*', values: { baseExperience: '0' }, revision: pages.personal.revision, restoreBase });
      if (mounted.current && scopeRef.current === scope && !accepted) setError(t('pla.failed'));
    } catch (reason) { if (mounted.current && scopeRef.current === scope) setError(String(reason)); }
    finally { stageLock.current = false; if (mounted.current) { setStaging(false); setRefresh(value => value + 1); } }
  };
  const outstandingIds = [...new Set(Object.keys(drafts).filter(key => currentKinds.some(kind => key.startsWith(`pokemon/${kind}/${paths.gameTextLanguage ?? 'English'}/`)))
    .map(key => key.slice(key.lastIndexOf('/') + 1)))];
  const nextId = outstandingIds.find(id => id !== effectiveSelectedId);
  const reviewNext = () => {
    if (!nextId) return;
    selectRecord('personal', nextId);
    if (!matchingRecords.some(record => record.id === nextId)) setQuery('');
  };
  const selectRecord = (kind: 'personal' | 'tutors', id: string | null) => {
    const commit = () => kind === 'personal' ? setSelectedId(id) : setTutorId(id);
    if (id && props.onNavigateRecord) props.onNavigateRecord(kind, id, pages[kind]?.records.find(record => record.id === id)?.label ?? id, commit);
    else commit();
  };
  return { pages, matchingRecords, selectedId: effectiveSelectedId, setSelectedId: (id: string | null) => selectRecord('personal', id),
    tutorId, setTutorId: (id: string) => selectRecord('tutors', id), query, onQuery: setQuery,
    loading, staging, isCurrentScope: loadedScope === scope, recordFor, valuesFor, setValue, hasDraft, draftKey, invalidKinds, currentKinds,
    changedCount, stage, stageExperienceYield, dirty, discard, nextId, reviewNext, outstandingIds, refresh: () => setRefresh(value => value + 1) };
}
