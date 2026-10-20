import { useCallback, useEffect, useRef, useState } from 'react';

export function useLoad<T>(key: string, loader: (signal: AbortSignal) => Promise<T>) {
  const latest = useRef(loader);
  const controller = useRef<AbortController | null>(null);
  const [data, setData] = useState<T>();
  const [error, setError] = useState<unknown>();
  const [loading, setLoading] = useState(true);
  useEffect(() => { latest.current = loader; });
  const reload = useCallback(async () => {
    controller.current?.abort();
    const current = new AbortController(); controller.current = current;
    setLoading(true); setError(undefined);
    try { const value = await latest.current(current.signal); if (!current.signal.aborted) setData(value); }
    catch (failure) { if (!current.signal.aborted) setError(failure); }
    finally { if (!current.signal.aborted) setLoading(false); }
  }, []);
  useEffect(() => { setData(undefined); void reload(); return () => controller.current?.abort(); }, [key, reload]);
  return { data, error, loading, reload };
}
