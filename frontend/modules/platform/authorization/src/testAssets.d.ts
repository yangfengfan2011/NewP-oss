/** Vitest 使用 Vite 原始资产导入验证真实 authoring 词典和视图。 */
declare module '*.json?raw' {
  const content: string;
  export default content;
}
