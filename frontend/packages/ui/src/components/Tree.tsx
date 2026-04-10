import React, { useState, useCallback } from 'react';

export interface TreeNode {
  key: string;
  label: string;
  children?: TreeNode[];
}

export interface TreeProps {
  data: TreeNode[];
  selectedKey?: string;
  defaultExpandedKeys?: string[];
  onSelect?: (key: string, node: TreeNode) => void;
  renderLabel?: (node: TreeNode) => React.ReactNode;
}

export function Tree({ data, selectedKey, defaultExpandedKeys, onSelect, renderLabel }: TreeProps) {
  const [expandedKeys, setExpandedKeys] = useState<Set<string>>(
    () => new Set(defaultExpandedKeys ?? []),
  );

  const toggleExpand = useCallback((key: string) => {
    setExpandedKeys((prev) => {
      const next = new Set(prev);
      if (next.has(key)) {
        next.delete(key);
      } else {
        next.add(key);
      }
      return next;
    });
  }, []);

  return (
    <ul className="sh-tree" role="tree">
      {data.map((node) => (
        <TreeNodeItem
          key={node.key}
          node={node}
          selectedKey={selectedKey}
          expandedKeys={expandedKeys}
          onToggle={toggleExpand}
          onSelect={onSelect}
          renderLabel={renderLabel}
        />
      ))}
    </ul>
  );
}

interface TreeNodeItemProps {
  node: TreeNode;
  selectedKey?: string;
  expandedKeys: Set<string>;
  onToggle: (key: string) => void;
  onSelect?: (key: string, node: TreeNode) => void;
  renderLabel?: (node: TreeNode) => React.ReactNode;
}

function TreeNodeItem({
  node,
  selectedKey,
  expandedKeys,
  onToggle,
  onSelect,
  renderLabel,
}: TreeNodeItemProps) {
  const hasChildren = node.children && node.children.length > 0;
  const isExpanded = expandedKeys.has(node.key);
  const isSelected = selectedKey === node.key;

  const rowClass = `sh-tree-node-row${isSelected ? ' sh-tree-node-row-selected' : ''}`;

  return (
    <li className="sh-tree-node" role="treeitem" aria-expanded={hasChildren ? isExpanded : undefined}>
      <div
        className={rowClass}
        onClick={() => onSelect?.(node.key, node)}
      >
        {hasChildren ? (
          <button
            className={`sh-tree-toggle${isExpanded ? ' sh-tree-toggle-expanded' : ''}`}
            onClick={(e) => {
              e.stopPropagation();
              onToggle(node.key);
            }}
            aria-label={isExpanded ? '收起' : '展开'}
          >
            ▶
          </button>
        ) : (
          <span className="sh-tree-toggle-placeholder" />
        )}
        <span className="sh-tree-label">
          {renderLabel ? renderLabel(node) : node.label}
        </span>
      </div>
      {hasChildren && isExpanded && (
        <ul className="sh-tree-children" role="group">
          {node.children!.map((child) => (
            <TreeNodeItem
              key={child.key}
              node={child}
              selectedKey={selectedKey}
              expandedKeys={expandedKeys}
              onToggle={onToggle}
              onSelect={onSelect}
              renderLabel={renderLabel}
            />
          ))}
        </ul>
      )}
    </li>
  );
}
