import React from 'react';

export interface KeyValuePair {
  key: string;
  value: string;
}

export interface KeyValueEditorProps {
  items: KeyValuePair[];
  onChange: (items: KeyValuePair[]) => void;
  keyPlaceholder?: string;
  valuePlaceholder?: string;
  addLabel?: string;
}

export function KeyValueEditor({
  items,
  onChange,
  keyPlaceholder = '键',
  valuePlaceholder = '值',
  addLabel = '添加',
}: KeyValueEditorProps) {
  const handleKeyChange = (index: number, newKey: string) => {
    const next = [...items];
    next[index] = { ...next[index], key: newKey };
    onChange(next);
  };

  const handleValueChange = (index: number, newValue: string) => {
    const next = [...items];
    next[index] = { ...next[index], value: newValue };
    onChange(next);
  };

  const handleRemove = (index: number) => {
    onChange(items.filter((_, i) => i !== index));
  };

  const handleAdd = () => {
    onChange([...items, { key: '', value: '' }]);
  };

  return (
    <div className="sh-kv-editor">
      {items.map((item, index) => (
        <div key={index} className="sh-kv-row">
          <input
            className="sh-form-input"
            type="text"
            placeholder={keyPlaceholder}
            value={item.key}
            onChange={(e) => handleKeyChange(index, e.target.value)}
          />
          <input
            className="sh-form-input"
            type="text"
            placeholder={valuePlaceholder}
            value={item.value}
            onChange={(e) => handleValueChange(index, e.target.value)}
          />
          <button
            className="sh-kv-remove"
            type="button"
            onClick={() => handleRemove(index)}
            aria-label="删除"
          >
            ×
          </button>
        </div>
      ))}
      <button className="sh-btn sh-btn-sm sh-kv-add" type="button" onClick={handleAdd}>
        + {addLabel}
      </button>
    </div>
  );
}
