import React, { useState, useRef, useEffect, useCallback } from 'react';
import { Tree } from './Tree';
import type { TreeNode } from './Tree';

export interface TreeSelectProps {
  data: TreeNode[];
  value?: string;
  onChange?: (key: string, node: TreeNode) => void;
  placeholder?: string;
  label?: string;
}

export function TreeSelect({ data, value, onChange, placeholder = '请选择', label }: TreeSelectProps) {
  const [open, setOpen] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);

  const findLabel = useCallback(
    (nodes: TreeNode[], targetKey: string): string | undefined => {
      for (const node of nodes) {
        if (node.key === targetKey) return node.label;
        if (node.children) {
          const found = findLabel(node.children, targetKey);
          if (found) return found;
        }
      }
      return undefined;
    },
    [],
  );

  const displayText = value ? findLabel(data, value) ?? value : '';

  useEffect(() => {
    if (!open) return;
    const handleClickOutside = (e: MouseEvent) => {
      if (containerRef.current && !containerRef.current.contains(e.target as Node)) {
        setOpen(false);
      }
    };
    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, [open]);

  const handleSelect = (key: string, node: TreeNode) => {
    onChange?.(key, node);
    setOpen(false);
  };

  // Collect all ancestor keys of the selected value for default expansion
  const getAncestorKeys = useCallback(
    (nodes: TreeNode[], targetKey: string, path: string[] = []): string[] | null => {
      for (const node of nodes) {
        if (node.key === targetKey) return path;
        if (node.children) {
          const result = getAncestorKeys(node.children, targetKey, [...path, node.key]);
          if (result) return result;
        }
      }
      return null;
    },
    [],
  );

  const defaultExpandedKeys = value ? getAncestorKeys(data, value) ?? [] : [];

  return (
    <div className="sh-form-group sh-tree-select" ref={containerRef}>
      {label && <label className="sh-form-label">{label}</label>}
      <div
        className="sh-form-input sh-tree-select-trigger"
        onClick={() => setOpen((prev) => !prev)}
      >
        <span className={displayText ? undefined : 'sh-tree-select-placeholder'}>
          {displayText || placeholder}
        </span>
        <span className="sh-tree-select-arrow">▼</span>
      </div>
      {open && (
        <div className="sh-tree-select-dropdown">
          <Tree
            data={data}
            selectedKey={value}
            defaultExpandedKeys={defaultExpandedKeys}
            onSelect={handleSelect}
          />
        </div>
      )}
    </div>
  );
}
