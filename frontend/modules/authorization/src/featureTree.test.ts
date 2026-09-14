import { describe, expect, it } from "vitest";
import { buildFeatureTree, changeBranch, featureDiff, type FeaturePoint } from "./featureTree";
const point = (position: string, parentPosition: string | null, title = position): FeaturePoint => ({
  position, parentPosition, title, moduleId: "Authorization", key: position, kind: parentPosition ? "action" : "navigation",
  behaviorKey: position, entryDependencies: parentPosition ? [parentPosition] : [], order: 0,
});
const catalog = [point("a", null), point("a/x", "a", "保存"), point("a/y", "a", "删除"), point("b", null), point("b/x", "b", "保存")];
describe("功能工作台树", () => {
  it("搜索保留匹配路径，授权筛选不丢失隐藏分支的草稿", () => {
    expect(buildFeatureTree(catalog, "保存", new Set(["a/x"]), true).map(n => [n.key, n.children?.map(c => c.key)])).toEqual([["a", ["a/x"]]]);
    expect(changeBranch(catalog, new Set(["b", "b/x"]), "a", true)).toEqual(new Set(["b", "b/x", "a", "a/x", "a/y"]));
  });
  it("分支全选只作用于当次目录，清除不影响其他分支，差异独立计算", () => {
    const draft = changeBranch(catalog, new Set(["b"]), "a", true);
    expect(draft.has("a/future")).toBe(false);
    expect(changeBranch(catalog, draft, "a", false)).toEqual(new Set(["b"]));
    expect(featureDiff(new Set(["a", "b"]), new Set(["a", "a/x"]))).toEqual({ added: ["a/x"], removed: ["b"] });
  });
});
