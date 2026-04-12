import { useEffect, useState } from 'react';
import { Panel, StatusBadge } from '@icp-platform/ui';

export interface OrganizationTreeNode {
  key: string;
  label: string;
  meta: string;
  kind: 'company' | 'department';
  isEnabled: boolean;
  children?: OrganizationTreeNode[];
}

interface OrganizationTreePanelProps {
  nodes: OrganizationTreeNode[];
  selectedKey?: string;
  onSelect?: (node: OrganizationTreeNode) => void;
}

export default function OrganizationTreePanel({
  nodes,
  selectedKey,
  onSelect,
}: OrganizationTreePanelProps) {
  const [expandedKeys, setExpandedKeys] = useState<Set<string>>(() => new Set(nodes.map((node) => node.key)));

  useEffect(() => {
    setExpandedKeys(new Set(nodes.map((node) => node.key)));
  }, [nodes]);

  return (
    <Panel title="组织树" meta="公司 / 部门层级结构">
      <ul style={{ listStyle: 'none', margin: 0, padding: 0, display: 'grid', gap: 10 }}>
        {nodes.map((node) => (
          <OrganizationTreeItem
            key={node.key}
            node={node}
            selectedKey={selectedKey}
            expandedKeys={expandedKeys}
            onToggle={(key: string) => {
              setExpandedKeys((currentKeys) => {
                const nextKeys = new Set(currentKeys);
                if (nextKeys.has(key)) {
                  nextKeys.delete(key);
                } else {
                  nextKeys.add(key);
                }
                return nextKeys;
              });
            }}
            onSelect={onSelect}
          />
        ))}
      </ul>
    </Panel>
  );
}

interface OrganizationTreeItemProps {
  node: OrganizationTreeNode;
  selectedKey?: string;
  expandedKeys: Set<string>;
  onToggle: (key: string) => void;
  onSelect?: (node: OrganizationTreeNode) => void;
}

function OrganizationTreeItem({
  node,
  selectedKey,
  expandedKeys,
  onToggle,
  onSelect,
}: OrganizationTreeItemProps) {
  const hasChildren = (node.children?.length ?? 0) > 0;
  const isExpanded = expandedKeys.has(node.key);
  const isSelected = selectedKey === node.key;

  return (
    <li>
      <button
        type="button"
        onClick={() => onSelect?.(node)}
        style={{
          width: '100%',
          display: 'flex',
          alignItems: 'center',
          gap: 10,
          padding: '12px 14px',
          borderRadius: 14,
          border: isSelected ? '1px solid #2563eb' : '1px solid #dbe1ea',
          background: isSelected ? '#eff6ff' : '#ffffff',
          cursor: 'pointer',
          textAlign: 'left',
        }}
      >
        {hasChildren ? (
          <span
            onClick={(event) => {
              event.stopPropagation();
              onToggle(node.key);
            }}
            style={{
              display: 'inline-flex',
              width: 20,
              justifyContent: 'center',
              color: '#475569',
              fontSize: 12,
            }}
          >
            {isExpanded ? '▼' : '▶'}
          </span>
        ) : (
          <span style={{ width: 20 }} />
        )}

        <span style={{ display: 'inline-flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
          <strong>{node.label}</strong>
          <span style={{ color: '#64748b', fontSize: 12 }}>{node.meta}</span>
          <StatusBadge variant={node.isEnabled ? 'success' : 'warning'} label={node.isEnabled ? '启用' : '停用'} />
        </span>
      </button>

      {hasChildren && isExpanded ? (
        <ul style={{ listStyle: 'none', margin: '10px 0 0 30px', padding: 0, display: 'grid', gap: 10 }}>
          {node.children!.map((child) => (
            <OrganizationTreeItem
              key={child.key}
              node={child}
              selectedKey={selectedKey}
              expandedKeys={expandedKeys}
              onToggle={onToggle}
              onSelect={onSelect}
            />
          ))}
        </ul>
      ) : null}
    </li>
  );
}
