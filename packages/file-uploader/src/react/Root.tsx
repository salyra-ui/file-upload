import { useEffect, useMemo, useRef, type ReactNode } from "react";
import {
  createUploader,
  type UploaderOptions,
  type UploaderStore,
} from "../core";
import { UploaderContext } from "./context";
export interface RootProps {
  store?: UploaderStore;
  options?: UploaderOptions;
  children?: ReactNode;
  restore?: boolean;
}
export function Root({ store, options, children, restore = true }: RootProps) {
  const owned = useMemo(() => {
    if (store) return store;
    if (!options)
      throw new Error("FileUploader.Root requires a store or options");
    return createUploader(options);
  }, [store]);
  const value = useMemo(
    () => ({ store: owned, inputs: new Set<HTMLInputElement>() }),
    [owned],
  );
  const lifecycle = useRef(0);
  useEffect(() => {
    lifecycle.current++;
    if (restore) void owned.restore();
    return () => {
      const ticket = ++lifecycle.current;
      if (!store)
        queueMicrotask(() => {
          if (lifecycle.current === ticket) owned.destroy();
        });
    };
  }, [owned, store, restore]);
  return (
    <UploaderContext.Provider value={value}>
      {children}
    </UploaderContext.Provider>
  );
}
