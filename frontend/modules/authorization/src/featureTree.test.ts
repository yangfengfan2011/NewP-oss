import { describe, expect, it } from "vitest";
import { buildFeatureTree, changeBranch, featureDiff, type FeaturePoint } from "./featureTree";
const point = (position: string, parentPosition: string | null, title = position): FeaturePoint => ({
  position, parentPosition, title, moduleId: "Authorization", key: position, kind: parentPosition ? "action" : "navigation",
  behaviorKey: position, entryDependencies: parentPosition ? [parentPosition] : [], order: 0,
});
const catalog = [point("a", null), point("a/x", "a", "保存"), point("a/y", "a", "删除"), point("b", null), point("b/x", "b", "保存")];
describe("功能工作台树", () => {
  it("静态树类型展示分组不进入授权集合且相同操作保持独立", () => {
    const points = [point("tree", null),
      { ...point("tree/company/edit", "tree", "编辑"), staticScope: "company", staticScopeTitle: "公司" },
      { ...point("tree/person/edit", "tree", "编辑"), staticScope: "person", staticScopeTitle: "人员" }];
    const groups = buildFeatureTree(points, "", new Set(), false)[0].children!;
    expect(groups.map(group => [group.title, group.checkable, group.selectable])).toEqual([["公司", false, false], ["人员", false, false]]);
    expect(groups.flatMap(group => group.children!.map(child => child.key))).toEqual(["tree/company/edit", "tree/person/edit"]);
    expect(changeBranch(points, new Set(), "tree", true).size).toBe(3);
  });
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
